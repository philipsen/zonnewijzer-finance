"""Insert-only GnuCash customer invoices via piecash."""

from __future__ import annotations

import logging
from datetime import datetime
from decimal import ROUND_HALF_UP, Decimal
from typing import TypeVar

from sqlalchemy import inspect as sa_inspect
from piecash import Address, open_book
from piecash.business import Customer
from piecash.business.invoice import Entry, Invoice
from piecash.business.person import PersonType
from piecash.business.tax import Taxtable

from app.schemas import InsertBody, ResultItem
from app.seq_log import seq_emit
from app.settings import get_settings

_log = logging.getLogger(__name__)


def _ensure_piecash_invoice_kvp_mapping() -> None:
    """GnuCash stores the invoice pointer on txn/lot under slot name ``invoice``; piecash only mapped ``invoice-guid``."""
    from piecash.kvp import SlotGUID

    if "invoice" in SlotGUID._mapping_name_class:
        return
    SlotGUID._mapping_name_class = {
        **dict(SlotGUID._mapping_name_class),
        "invoice": "piecash.business.invoice.Invoice",
    }


_ensure_piecash_invoice_kvp_mapping()

T = TypeVar("T")


def _piecash_new(cls: type[T]) -> T:
    """piecash blocks ``__init__`` on Invoice/Entry; use SQLAlchemy's supported factory."""
    return sa_inspect(cls).class_manager.new_instance()


def _find_account_by_fullname(book, fullname: str):
    for acc in book.accounts:
        if acc.fullname == fullname:
            return acc
    return None


def _find_taxtable(book, name: str) -> Taxtable | None:
    want = (name or "").strip()
    if not want:
        return None
    return book.session.query(Taxtable).filter_by(name=want).first()


def _entry_line_net(entry) -> Decimal:
    q = Decimal(str(entry.quantity))
    p = Decimal(str(entry.i_price))
    return (q * p).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)


def _payload_line_totals(inv_in) -> tuple[Decimal, Decimal]:
    """(sum lineAmount, sum rounded qty*unitPrice) from JSON."""
    s_la = Decimal(0)
    s_qp = Decimal(0)
    for line in inv_in.lines:
        la = Decimal(str(line.line_amount or 0)).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
        q = Decimal(str(line.quantity))
        p = Decimal(str(line.unit_price))
        s_la += la
        s_qp += (q * p).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
    return s_la, s_qp


def _verify_saved_unposted_line_amounts(
    uri: str,
    invoice_id: str,
    expected_lines: int,
    payload_sum_line_amount: Decimal,
    payload_sum_qty_price: Decimal,
) -> tuple[bool, str, Decimal]:
    """Reload invoice from DB; ensure line nets match payload (detect doubled storage)."""
    with open_book(uri_conn=uri, readonly=True, open_if_lock=True, do_backup=False) as book:
        inv = book.session.query(Invoice).filter_by(id=invoice_id).first()
        if inv is None:
            return False, "invoice missing after save", Decimal(0)
        if inv.post_txn is not None:
            return False, "expected unposted invoice but post_txn is set", Decimal(0)
        entries = list(inv.entries)
        if len(entries) != expected_lines:
            return (
                False,
                f"entry count {len(entries)} != payload lines {expected_lines}",
                Decimal(0),
            )
        db_net = sum((_entry_line_net(e) for e in entries), Decimal(0))
        db_net = db_net.quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)

    ref = payload_sum_line_amount
    if ref == 0:
        ref = payload_sum_qty_price
    if ref == 0:
        return True, "", db_net

    diff_la = abs(db_net - payload_sum_line_amount)
    diff_qp = abs(db_net - payload_sum_qty_price)
    cent = Decimal("0.02")
    if diff_la <= cent or diff_qp <= cent:
        return True, "", db_net

    if db_net >= ref * 2 - cent and ref > 0:
        return (
            False,
            f"stored line net {db_net} is ~2x reference {ref} (doubled? lineAmount_sum={payload_sum_line_amount} q*price_sum={payload_sum_qty_price})",
            db_net,
        )

    return (
        False,
        f"stored line net {db_net} vs payload lineAmount_sum={payload_sum_line_amount} q*price_sum={payload_sum_qty_price}",
        db_net,
    )


def _insert_single_invoice(uri: str, income_account: str, inv_in, dry_run: bool) -> ResultItem:
    """Create customer invoice and line entries only; GnuCash must post the invoice in the UI."""
    iid = inv_in.invoice_id
    with open_book(
        uri_conn=uri,
        readonly=False,
        open_if_lock=True,
        do_backup=False,  # piecash only auto-backs up SQLite; use pg_dump for Postgres
    ) as book:
        existing = book.session.query(Invoice).filter_by(id=iid).first()
        if existing is not None:
            return ResultItem(invoiceId=iid, status="skipped_already_exists", detail="invoice id already in book")

        try:
            eur = book.currencies(mnemonic=inv_in.currency)
        except Exception:  # noqa: BLE001
            eur = book.default_currency

        inc = _find_account_by_fullname(book, income_account)
        if inc is None:
            return ResultItem(
                invoiceId=iid,
                status="error",
                detail=f"Income account not found (create in GnuCash): {income_account}",
            )

        settings = get_settings()
        tax_name = (settings.invoice_tax_table_name or "").strip()
        taxtable = _find_taxtable(book, tax_name) if tax_name else None
        if tax_name and taxtable is None:
            return ResultItem(
                invoiceId=iid,
                status="error",
                detail=f"Tax table not found in GnuCash (create or rename): {tax_name}",
            )

        customer = book.session.query(Customer).filter_by(id=inv_in.customer_id).first()
        if customer is None:
            addr1 = " ".join(x for x in [inv_in.street or "", inv_in.house_number or ""] if x).strip() or inv_in.customer_name
            customer = Customer(
                name=inv_in.customer_name,
                currency=eur,
                id=inv_in.customer_id,
                notes="Created by gnucash-sync",
                address=Address(addr1=addr1, email=inv_in.email or ""),
            )
            book.add(customer)
            book.flush()

        opened = datetime.fromisoformat(inv_in.invoice_date_opened[:10])
        note = f"{inv_in.year}-{inv_in.month:02d} app; MolliePaid={inv_in.mollie_paid}"

        inv = _piecash_new(Invoice)
        inv.id = iid
        inv.notes = note
        inv.active = 1
        inv.currency = eur
        inv.date_opened = opened
        inv.owner_type = PersonType[Customer]
        inv.owner_guid = customer.guid
        book.add(inv)
        book.flush()

        tax_incl = bool(settings.invoice_tax_included)
        for line in inv_in.lines:
            entry = _piecash_new(Entry)
            entry.date = opened
            entry.description = (line.description or "")[:2046]
            entry.invoice = inv
            entry.i_acct = inc.guid
            entry.quantity = Decimal(str(line.quantity))
            entry.i_price = Decimal(str(line.unit_price))
            entry.i_discount = Decimal(0)
            entry.i_disc_type = "PERCENT"
            entry.i_disc_how = "PRETAX"
            if taxtable is not None:
                entry.i_taxable = 1
                entry.i_taxincluded = 1 if tax_incl else 0
                entry.i_taxtable = taxtable.guid
            else:
                entry.i_taxable = 0
                entry.i_taxincluded = 0
            inv.entries.append(entry)

        book.flush()

        sum_la, sum_qp = _payload_line_totals(inv_in)
        if not dry_run:
            _log.info(
                "Invoice %s pre-save amounts: lines=%s payload sum(lineAmount)=%s sum(qty*unitPrice)=%s",
                iid,
                len(inv_in.lines),
                sum_la,
                sum_qp,
            )

        if dry_run:
            book.cancel()
            return ResultItem(invoiceId=iid, status="dry_run_would_create", detail="rolled back, not saved")

        book.save()

        ok, vmsg, db_net = _verify_saved_unposted_line_amounts(
            uri,
            iid,
            len(inv_in.lines),
            sum_la,
            sum_qp,
        )
        if ok:
            _log.info(
                "Invoice %s post-save amount check OK: db_line_net_sum=%s (matches payload within 0.02), unposted",
                iid,
                db_net,
            )
        else:
            _log.error("Invoice %s post-save amount check FAILED: %s", iid, vmsg)
            seq_emit(
                "Error",
                "Invoice amount verify failed for {InvoiceId}: {Message}",
                InvoiceId=iid,
                Message=vmsg[:400],
                Application=settings.app_name,
            )
            return ResultItem(
                invoiceId=iid,
                status="amount_verify_failed",
                detail=vmsg[:500],
            )

        return ResultItem(
            invoiceId=iid,
            status="created",
            detail="Saved as unposted invoice; post from GnuCash when ready.",
        )


def run_insert(body: InsertBody, dry_run: bool) -> list[ResultItem]:
    settings = get_settings()
    uri = (settings.gnucash_postgres_url or "").strip()
    if not uri:
        raise ValueError("GNUCASH_POSTGRES_URL is not set")

    # GnuCash full names use ":" (UI may show "Income / Charging"); accept "/" in env.
    income = (settings.income_account or "").strip().replace("/", ":")
    if not income:
        raise ValueError("INCOME_ACCOUNT is not set")

    results: list[ResultItem] = []
    for inv in body.invoices:
        try:
            r = _insert_single_invoice(uri, income, inv, dry_run)
            results.append(r)
            seq_emit(
                "Information",
                "Invoice insert outcome {Status} for {InvoiceId}",
                Status=r.status,
                InvoiceId=r.invoiceId,
                Application=settings.app_name,
            )
        except Exception as ex:  # noqa: BLE001
            _log.exception("Insert failed for %s", inv.invoice_id)
            results.append(ResultItem(invoiceId=inv.invoice_id, status="error", detail=str(ex)[:500]))
            seq_emit(
                "Error",
                "Invoice insert error for {InvoiceId}",
                InvoiceId=inv.invoice_id,
                exception=str(ex),
                Application=settings.app_name,
            )
    return results

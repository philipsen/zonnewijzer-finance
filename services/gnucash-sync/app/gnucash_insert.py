"""Insert-only GnuCash customer invoices via piecash."""

from __future__ import annotations

import logging
from collections import defaultdict
from datetime import datetime
from decimal import ROUND_HALF_UP, Decimal
from typing import TypeVar

from sqlalchemy import inspect as sa_inspect
from piecash import Address, open_book
from piecash.business import Customer
from piecash.business.invoice import Entry, Invoice
from piecash.business.person import PersonType
from piecash.business.tax import Taxtable
from piecash.core.transaction import Lot, Split, Transaction

from app.schemas import InsertBody, ResultItem
from app.seq_log import seq_emit
from app.settings import get_settings

_log = logging.getLogger(__name__)

T = TypeVar("T")


def _piecash_new(cls: type[T]) -> T:
    """piecash blocks ``__init__`` on Invoice/Entry; use SQLAlchemy's supported factory."""
    return sa_inspect(cls).class_manager.new_instance()


def _find_account_by_fullname(book, fullname: str):
    for acc in book.accounts:
        if acc.fullname == fullname:
            return acc
    return None


def _find_first_receivable(book):
    for acc in book.accounts:
        if acc.type == "RECEIVABLE" and not acc.placeholder:
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


def _tax_amounts_for_net(net: Decimal, taxtable: Taxtable) -> list[tuple[Decimal, object]]:
    """Per tax-table row: (tax amount, liability account). Net is ex-VAT base."""
    out: list[tuple[Decimal, object]] = []
    for te in taxtable.entries:
        acc = te.account
        if te.type == "percentage":
            rate = Decimal(str(te.amount))
            amt = (net * (rate / Decimal(100))).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
        else:
            amt = Decimal(str(te.amount)).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
        if amt != 0:
            out.append((amt, acc))
    return out


def _post_invoice(
    book,
    inv: Invoice,
    customer: Customer,
    inc,
    opened: datetime,
    note: str,
    taxtable: Taxtable | None,
    ar,
) -> None:
    """Create posted transaction + lot (customer invoice → receivable)."""
    eur = inv.currency
    total_net = Decimal(0)
    for entry in inv.entries:
        total_net += _entry_line_net(entry)

    tax_by_acc: defaultdict[str, Decimal] = defaultdict(Decimal)
    tax_accounts: dict[str, object] = {}
    if taxtable is not None:
        tax_rows = _tax_amounts_for_net(total_net, taxtable)
        for amt, acc in tax_rows:
            tax_by_acc[acc.guid] += amt
            tax_accounts[acc.guid] = acc

    total_tax = sum(tax_by_acc.values(), Decimal(0))
    total_gross = (total_net + total_tax).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)

    memo = (note or "")[:2046]
    post_date = opened.date() if hasattr(opened, "date") else opened

    lot = Lot(title=f"Invoice {inv.id}", account=ar)
    book.add(lot)
    # Link lot ↔ invoice like gncInvoiceAttachToLot (GnuCash expects this for business/AR views).
    try:
        lot["invoice"] = inv
    except (TypeError, ValueError, KeyError):
        _log.warning("Could not set invoice slot on lot for %s", inv.id, exc_info=True)

    splits: list[Split] = [
        Split(
            account=ar,
            value=total_gross,
            memo=memo,
            action="",
            lot=lot,
        ),
        Split(
            account=inc,
            value=-total_net,
            memo=memo,
            action="",
        ),
    ]
    for guid, amt in tax_by_acc.items():
        if amt == 0:
            continue
        acc = tax_accounts[guid]
        label = f"Tax ({taxtable.name})" if taxtable is not None else "Tax"
        splits.append(Split(account=acc, value=-amt, memo=label, action=""))

    txn = Transaction(
        currency=eur,
        description=customer.name or inv.id,
        post_date=post_date,
        num=inv.id,
        splits=splits,
    )
    book.add(txn)
    # Match gncInvoiceAttachToTxn + xaccTransSetTxnType: type "?" postings are not treated as
    # invoice posts and can double-count with business logic in AR / income.
    try:
        txn["trans-txn-type"] = "I"
        txn["invoice"] = inv
        txn["trans-read-only"] = "Generated from an invoice. Try unposting the invoice."
    except (TypeError, ValueError, KeyError):
        _log.warning("Could not set invoice posting slots on transaction for %s", inv.id, exc_info=True)

    inv.post_txn = txn
    inv.post_acc = ar
    inv.post_lot = lot
    inv.date_posted = opened


def _insert_single_invoice(uri: str, income_account: str, inv_in, dry_run: bool) -> ResultItem:
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

        post_full = (settings.invoice_post_account or "").strip()
        ar = _find_account_by_fullname(book, post_full) if post_full else _find_first_receivable(book)
        if ar is None:
            return ResultItem(
                invoiceId=iid,
                status="error",
                detail="No receivable account: set INVOICE_POST_ACCOUNT to a full account name "
                "(e.g. Activa:Debiteuren) or create a non-placeholder RECEIVABLE account.",
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

        if not dry_run:
            try:
                _post_invoice(book, inv, customer, inc, opened, note, taxtable, ar)
            except Exception as ex:  # noqa: BLE001
                return ResultItem(
                    invoiceId=iid,
                    status="error",
                    detail=f"Posting invoice failed: {ex}"[:500],
                )

        if dry_run:
            book.cancel()
            return ResultItem(invoiceId=iid, status="dry_run_would_create", detail="rolled back, not saved")

        book.save()
        return ResultItem(invoiceId=iid, status="created", detail=None)


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

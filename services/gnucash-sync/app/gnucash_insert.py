"""Insert-only GnuCash customer invoices via piecash."""

from __future__ import annotations

import logging
from datetime import datetime
from decimal import Decimal
from typing import TypeVar

from sqlalchemy import inspect as sa_inspect
from piecash import Address, open_book
from piecash.business import Customer
from piecash.business.invoice import Entry, Invoice
from piecash.business.person import PersonType

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

        for line in inv_in.lines:
            entry = _piecash_new(Entry)
            entry.date = opened
            entry.description = (line.description or "")[:2046]
            entry.invoice = inv
            entry.i_acct = inc.guid
            entry.quantity = Decimal(str(line.quantity))
            entry.i_price = Decimal(str(line.unit_price))
            inv.entries.append(entry)

        book.flush()

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
                InvoiceId=r.invoice_id,
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

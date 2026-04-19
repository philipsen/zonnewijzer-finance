"""Read-only: which invoice ids already exist in the GnuCash book."""

from __future__ import annotations

from piecash import open_book
from piecash.business.invoice import Invoice


def run_invoice_ids_exist(uri: str, invoice_ids: list[str]) -> list[str]:
    """Return the subset of ``invoice_ids`` that exist as ``invoices.id`` in the book."""
    if not invoice_ids:
        return []
    uri = (uri or "").strip()
    if not uri:
        return []
    uniq = list(dict.fromkeys(i.strip() for i in invoice_ids if i and i.strip()))
    if not uniq:
        return []
    with open_book(uri_conn=uri, readonly=True, open_if_lock=True, do_backup=False) as book:
        rows = book.session.query(Invoice.id).filter(Invoice.id.in_(uniq)).all()
        return [r[0] for r in rows]

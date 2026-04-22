"""Remove app-created GnuCash business objects (INV-* invoices, CU-* customers)."""

from __future__ import annotations

import logging

from piecash import open_book
from piecash.business import Customer
from piecash.business.invoice import Invoice

_log = logging.getLogger(__name__)

_APP_INVOICE_PREFIX = "INV-"
_APP_CUSTOMER_PREFIX = "CU-"


def run_wipe_app_business(uri: str, dry_run: bool) -> tuple[int, int]:
    """
    Delete invoices whose id starts with INV- (posted or not), then customers whose id starts with CU-.

    Posted invoices: clear links, remove posting transaction (splits), remove posted lot, then remove invoice
    (entries cascade). Non-app invoices/customers are left untouched.
    """
    invoices_removed = 0
    customers_removed = 0

    with open_book(
        uri_conn=uri,
        readonly=False,
        open_if_lock=True,
        do_backup=False,
    ) as book:
        inv_candidates = [
            inv
            for inv in book.session.query(Invoice).all()
            if (inv.id or "").startswith(_APP_INVOICE_PREFIX)
        ]

        for inv in inv_candidates:
            txn = inv.post_txn
            lot = inv.post_lot
            inv.post_txn = None
            inv.post_acc = None
            inv.post_lot = None
            inv.date_posted = None
            if txn is not None:
                book.session.delete(txn)
            if lot is not None:
                book.session.delete(lot)
            book.session.delete(inv)
            invoices_removed += 1

        book.session.flush()

        cust_candidates = [
            c
            for c in book.session.query(Customer).all()
            if (c.id or "").startswith(_APP_CUSTOMER_PREFIX)
        ]

        for cust in cust_candidates:
            book.session.delete(cust)
            customers_removed += 1

        if dry_run:
            book.cancel()
        else:
            book.save()

    return invoices_removed, customers_removed

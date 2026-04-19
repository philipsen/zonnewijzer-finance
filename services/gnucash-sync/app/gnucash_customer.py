"""Ensure a GnuCash customer exists (matches invoice customer ids, e.g. CU-42)."""

from __future__ import annotations

from piecash import Address, open_book
from piecash.business import Customer

from app.schemas import CustomerEnsureIn, CustomerEnsureResult


def run_ensure_customer(uri: str, body: CustomerEnsureIn, dry_run: bool) -> CustomerEnsureResult:
    cid = body.customer_id.strip()
    if not cid:
        return CustomerEnsureResult(customerId="", status="error", detail="customerId is required")

    with open_book(
        uri_conn=uri,
        readonly=False,
        open_if_lock=True,
        do_backup=False,
    ) as book:
        existing = book.session.query(Customer).filter_by(id=cid).first()
        if existing is not None:
            return CustomerEnsureResult(customerId=cid, status="already_exists")

        try:
            eur = book.currencies(mnemonic=body.currency)
        except Exception:  # noqa: BLE001
            eur = book.default_currency

        addr1 = " ".join(
            x for x in [body.street or "", body.house_number or ""] if x
        ).strip() or body.customer_name

        customer = Customer(
            name=body.customer_name,
            currency=eur,
            id=cid,
            notes="Created by gnucash-sync (ensure customer)",
            address=Address(
                addr1=addr1,
                email=body.email or "",
            ),
        )
        book.add(customer)
        book.flush()

        if dry_run:
            book.cancel()
            return CustomerEnsureResult(
                customerId=cid,
                status="dry_run_would_create",
                detail="rolled back, not saved",
            )

        book.save()
        return CustomerEnsureResult(customerId=cid, status="created", detail=None)

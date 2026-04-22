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
from piecash.business.tax import Taxtable, TaxtableEntry
from piecash.core.transaction import Lot, Split, Transaction

from app.schemas import InsertBody, ResultItem
from app.seq_log import seq_emit
from app.settings import get_settings

_log = logging.getLogger(__name__)

# gncInvoicePostAddSplit uses gnc_set_num_action with gncInvoiceGetTypeString (English UI default).
_INVOICE_SPLIT_ACTION = "Invoice"


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


def _ensure_tax_table_child_for_posting(book, parent: Taxtable) -> Taxtable:
    """Match ``gncTaxTableReturnChild`` (gncInvoice.c): posted lines reference a child snapshot."""
    if parent.parent is not None:
        return parent
    if parent.children:
        return parent.children[0]
    child = Taxtable(parent.name)
    book.add(child)
    child.parent = parent
    for te in list(parent.entries):
        child.entries.append(TaxtableEntry(te.type, te.amount, te.account, taxtable=child))
    book.flush()
    _log.info(
        "Created tax table child snapshot guid=%s for template %r",
        child.guid,
        parent.name,
    )
    return child


def _entry_line_net(entry) -> Decimal:
    q = Decimal(str(entry.quantity))
    p = Decimal(str(entry.i_price))
    return (q * p).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)


def _percentage_tax_specs(taxtable: Taxtable) -> list[tuple[Decimal, object]]:
    specs: list[tuple[Decimal, object]] = []
    seen_pairs: set[tuple[str, Decimal]] = set()
    for te in taxtable.entries:
        if te.type != "percentage":
            continue
        rate = Decimal(str(te.amount))
        acc = te.account
        key = (acc.guid, rate)
        if key in seen_pairs:
            _log.warning(
                "Tax table %r: skipping duplicate percentage row account=%s rate=%s%%",
                getattr(taxtable, "name", "?"),
                getattr(acc, "fullname", acc.guid),
                rate,
            )
            continue
        seen_pairs.add(key)
        specs.append((rate, acc))
    return specs


def _tax_amounts_for_net(net: Decimal, taxtable: Taxtable) -> list[tuple[Decimal, object]]:
    """One combined VAT base for all percentage rows; split tax by rate weight (avoids 2× from 10.5+10.5)."""
    out: list[tuple[Decimal, object]] = []
    pct_specs = _percentage_tax_specs(taxtable)
    combined_pct = sum((r for r, _ in pct_specs), Decimal(0))

    if combined_pct > 0 and pct_specs:
        total_pct_tax = (net * combined_pct / Decimal(100)).quantize(
            Decimal("0.01"), rounding=ROUND_HALF_UP
        )
        if combined_pct > Decimal("30"):
            _log.warning(
                "Tax table %r: combined percentage is %s%% (check tax table rows)",
                getattr(taxtable, "name", "?"),
                combined_pct,
            )
        remaining = total_pct_tax
        for i, (rate, acc) in enumerate(pct_specs):
            if i == len(pct_specs) - 1:
                part = remaining.quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
            else:
                part = (total_pct_tax * rate / combined_pct).quantize(
                    Decimal("0.01"), rounding=ROUND_HALF_UP
                )
                remaining -= part
            if part != 0:
                out.append((part, acc))

    for te in taxtable.entries:
        if te.type == "percentage":
            continue
        acc = te.account
        amt = Decimal(str(te.amount)).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
        if amt != 0:
            out.append((amt, acc))
    return out


def _combined_vat_rate_percent(taxtable: Taxtable) -> Decimal:
    return sum((r for r, _ in _percentage_tax_specs(taxtable)), Decimal(0))


def _posting_totals(
    inv: Invoice,
    taxtable: Taxtable | None,
    *,
    tax_included: bool = False,
) -> tuple[Decimal, Decimal, Decimal, dict[str, Decimal], dict[str, object]]:
    sum_lines = sum(_entry_line_net(entry) for entry in inv.entries)
    sum_lines = sum_lines.quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)

    if taxtable is None:
        return sum_lines, Decimal(0), sum_lines, {}, {}

    rate_pct = _combined_vat_rate_percent(taxtable)
    if tax_included and rate_pct > 0:
        total_gross = sum_lines
        total_net = (total_gross / (1 + rate_pct / Decimal(100))).quantize(
            Decimal("0.01"), rounding=ROUND_HALF_UP
        )
    else:
        total_net = sum_lines
        total_gross = Decimal(0)

    tax_by_acc: dict[str, Decimal] = {}
    tax_accounts: dict[str, object] = {}
    for amt, acc in _tax_amounts_for_net(total_net, taxtable):
        g = acc.guid
        tax_by_acc[g] = tax_by_acc.get(g, Decimal(0)) + amt
        tax_accounts[g] = acc

    total_tax = sum(tax_by_acc.values(), Decimal(0))
    if not (tax_included and rate_pct > 0):
        total_gross = (total_net + total_tax).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)

    return total_net, total_tax, total_gross, tax_by_acc, tax_accounts


def _d(v) -> Decimal:
    return Decimal(str(v))


def _verify_saved_posting(
    uri: str,
    invoice_id: str,
    income_guid: str,
    ar_guid: str,
    expected_income_leg: Decimal,
    expected_gross: Decimal,
    tax_by_acc: dict[str, Decimal],
) -> tuple[bool, str]:
    with open_book(uri_conn=uri, readonly=True, open_if_lock=True, do_backup=False) as book:
        inv = book.session.query(Invoice).filter_by(id=invoice_id).first()
        if inv is None:
            return False, "invoice not found after save"
        txn = inv.post_txn
        if txn is None:
            return False, "post_txn is null after save"

        try:
            kvp_inv = txn.get("invoice")
        except Exception as ex:  # noqa: BLE001
            return False, f"post_txn lacks readable invoice KVP ({type(ex).__name__}: {ex})"
        if kvp_inv is None:
            return False, "post_txn has no invoice KVP (txn.get('invoice'))"
        if kvp_inv.guid != inv.guid:
            return False, (
                f"post_txn invoice KVP points to guid={kvp_inv.guid!r} id={kvp_inv.id!r}, "
                f"expected guid={inv.guid!r} id={invoice_id!r}"
            )

        splits = list(txn.splits)
        balance = sum(_d(s.value) for s in splits)
        if balance != 0:
            return False, f"post_txn not balanced: sum(values)={balance} (splits={len(splits)})"

        by_acc: dict[str, list[Decimal]] = defaultdict(list)
        for sp in splits:
            by_acc[sp.account.guid].append(_d(sp.value))

        ar_vals = by_acc.get(ar_guid, [])
        if len(ar_vals) != 1:
            return False, f"receivable: expected 1 split, got {len(ar_vals)} values={ar_vals}"
        if ar_vals[0] != expected_gross:
            return False, f"receivable split value={ar_vals[0]} expected={expected_gross}"

        inc_vals = by_acc.get(income_guid, [])
        inc_sum = sum(inc_vals, Decimal(0))
        expected_inc = -expected_income_leg
        if len(inc_vals) != 1:
            return (
                False,
                f"income: expected 1 split, got {len(inc_vals)} sum={inc_sum} expected={expected_inc} "
                f"per-split={inc_vals}",
            )
        if inc_sum != expected_inc:
            return False, f"income split value={inc_sum} expected={expected_inc}"

        for guid, tax_amt in tax_by_acc.items():
            if tax_amt == 0:
                continue
            got_list = by_acc.get(guid, [])
            got = sum(got_list, Decimal(0))
            want = -tax_amt
            if len(got_list) != 1 or got != want:
                return False, f"tax account {guid[:8]}...: splits={got_list} sum={got} expected={want}"

        tax_guids = {g for g, a in tax_by_acc.items() if a != 0}
        allowed = {ar_guid, income_guid, *tax_guids}
        for sp in splits:
            g = sp.account.guid
            if g not in allowed:
                return False, (
                    f"unexpected split on account {getattr(sp.account, 'fullname', g)} value={_d(sp.value)}"
                )

    return True, ""


def _post_invoice(
    book,
    inv: Invoice,
    customer: Customer,
    inc,
    opened: datetime,
    note: str,
    taxtable: Taxtable | None,
    ar,
    *,
    tax_included: bool = False,
) -> None:
    eur = inv.currency
    total_net, total_tax, total_gross, tax_by_acc, tax_accounts = _posting_totals(
        inv, taxtable, tax_included=tax_included
    )
    income_leg = (total_gross - total_tax).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)

    memo = (note or "")[:2046]
    post_date = opened.date() if hasattr(opened, "date") else opened

    lot = Lot(title=f"Invoice {inv.id}", account=ar)
    book.add(lot)
    try:
        lot["invoice"] = inv
    except (TypeError, ValueError, KeyError):
        _log.warning("Could not set invoice slot on lot for %s", inv.id, exc_info=True)

    splits: list[Split] = [
        Split(
            account=ar,
            value=total_gross,
            memo=memo,
            action=_INVOICE_SPLIT_ACTION,
            lot=lot,
        ),
        Split(
            account=inc,
            value=-income_leg,
            memo=memo,
            action=_INVOICE_SPLIT_ACTION,
        ),
    ]
    for guid, amt in tax_by_acc.items():
        if amt == 0:
            continue
        acc = tax_accounts[guid]
        label = f"Tax ({taxtable.name})" if taxtable is not None else "Tax"
        splits.append(Split(account=acc, value=-amt, memo=label, action=_INVOICE_SPLIT_ACTION))

    _log.info(
        "Invoice %s posting: tax_included=%s net=%s tax=%s gross=%s income_leg=%s splits [%s]",
        inv.id,
        tax_included,
        total_net,
        total_tax,
        total_gross,
        income_leg,
        "; ".join(f"{sp.account.fullname}={sp.value}" for sp in splits),
    )

    txn = Transaction(
        currency=eur,
        description=customer.name or inv.id,
        post_date=post_date,
        num=inv.id,
        splits=splits,
    )
    book.add(txn)
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
    try:
        inv.charge_amt = total_gross
    except Exception:  # noqa: BLE001
        _log.debug("Could not set charge_amt on invoice %s", inv.id, exc_info=True)


def _payload_line_totals(inv_in) -> tuple[Decimal, Decimal]:
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
            f"stored line net {db_net} is ~2x reference {ref} (doubled? lineAmount={payload_sum_line_amount} q*p={payload_sum_qty_price})",
            db_net,
        )

    return (
        False,
        f"stored line net {db_net} vs payload lineAmount={payload_sum_line_amount} q*p={payload_sum_qty_price}",
        db_net,
    )


def _insert_single_invoice(uri: str, income_account: str, inv_in, dry_run: bool) -> ResultItem:
    iid = inv_in.invoice_id
    with open_book(
        uri_conn=uri,
        readonly=False,
        open_if_lock=True,
        do_backup=False,
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
        auto_post = bool(settings.invoice_auto_post)
        tax_name = (settings.invoice_tax_table_name or "").strip()
        taxtable = _find_taxtable(book, tax_name) if tax_name else None
        if tax_name and taxtable is None:
            return ResultItem(
                invoiceId=iid,
                status="error",
                detail=f"Tax table not found in GnuCash (create or rename): {tax_name}",
            )

        if auto_post and taxtable is not None:
            taxtable = _ensure_tax_table_child_for_posting(book, taxtable)

        ar = None
        if auto_post:
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

        book.flush()

        sum_la, sum_qp = _payload_line_totals(inv_in)
        if not dry_run:
            _log.info(
                "Invoice %s pre-save: auto_post=%s lines=%s payload sum(lineAmount)=%s sum(qty*unitPrice)=%s",
                iid,
                auto_post,
                len(inv_in.lines),
                sum_la,
                sum_qp,
            )

        if dry_run:
            book.cancel()
            return ResultItem(invoiceId=iid, status="dry_run_would_create", detail="rolled back, not saved")

        if auto_post:
            try:
                _post_invoice(
                    book,
                    inv,
                    customer,
                    inc,
                    opened,
                    note,
                    taxtable,
                    ar,
                    tax_included=tax_incl,
                )
            except Exception as ex:  # noqa: BLE001
                return ResultItem(
                    invoiceId=iid,
                    status="error",
                    detail=f"Posting invoice failed: {ex}"[:500],
                )

        book.save()

        if auto_post:
            t_net, t_tax, t_gross, t_tax_by, _ = _posting_totals(inv, taxtable, tax_included=tax_incl)
            income_leg = (t_gross - t_tax).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
            vok, vmsg = _verify_saved_posting(
                uri,
                iid,
                inc.guid,
                ar.guid,
                income_leg,
                t_gross,
                dict(t_tax_by),
            )
            if not vok:
                _log.error("Post-save posting verify failed for %s: %s", iid, vmsg)
                seq_emit(
                    "Error",
                    "Invoice post_verify failed for {InvoiceId}: {Message}",
                    InvoiceId=iid,
                    Message=vmsg[:400],
                    Application=settings.app_name,
                )
                return ResultItem(invoiceId=iid, status="post_verify_failed", detail=vmsg[:500])
            _log.info(
                "Invoice %s posted and verified (gross=%s income_leg=%s)",
                iid,
                t_gross,
                income_leg,
            )
            return ResultItem(
                invoiceId=iid,
                status="created",
                detail="Invoice created and posted to receivable.",
            )

        ok, vmsg, db_net = _verify_saved_unposted_line_amounts(
            uri,
            iid,
            len(inv_in.lines),
            sum_la,
            sum_qp,
        )
        if ok:
            _log.info(
                "Invoice %s post-save line check OK: db_line_net_sum=%s (unposted)",
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
            return ResultItem(invoiceId=iid, status="amount_verify_failed", detail=vmsg[:500])

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

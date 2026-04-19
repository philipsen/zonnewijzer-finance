"""GnuCash insert API — run with project .venv (see README)."""

from __future__ import annotations

import logging
import sys
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, Header, HTTPException, Query, Request
from app.gnucash_customer import run_ensure_customer
from app.gnucash_insert import run_insert
from app.gnucash_wipe import run_wipe_app_business
from app.schemas import CustomerEnsureIn, CustomerEnsureResult, GnucashWipeAppResponse, InsertBody, InsertResponse
from app.seq_log import seq_emit
from app.settings import Settings, get_settings
from app.startup_db import verify_gnucash_database_or_die

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(name)s %(message)s",
    stream=sys.stdout,
)
_log = logging.getLogger("gnucash_sync")


def get_app_settings() -> Settings:
    return get_settings()


@asynccontextmanager
async def _lifespan(_app: FastAPI) -> AsyncIterator[None]:
    verify_gnucash_database_or_die()
    yield


app = FastAPI(title="gnucash-sync", version="0.1.0", lifespan=_lifespan)


@app.middleware("http")
async def correlation_middleware(request: Request, call_next):
    cid = request.headers.get("x-correlation-id") or request.headers.get("X-Correlation-Id")
    if cid:
        request.state.correlation_id = cid
    response = await call_next(request)
    if cid:
        response.headers["X-Correlation-Id"] = cid
    return response


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}


@app.post("/invoices/insert", response_model=InsertResponse)
def insert_invoices(
    body: InsertBody,
    request: Request,
    dry_run: bool = Query(False),
    x_gnucash_sync_key: str | None = Header(None, alias="X-GnuCash-Sync-Key"),
    settings: Settings = Depends(get_app_settings),
) -> InsertResponse:
    if settings.sync_api_key and (x_gnucash_sync_key or "") != settings.sync_api_key:
        raise HTTPException(status_code=401, detail="Invalid or missing X-GnuCash-Sync-Key")

    cid = getattr(request.state, "correlation_id", None)
    seq_emit(
        "Information",
        "GnuCash insert batch started ({Count} invoices, dry_run={DryRun})",
        Count=len(body.invoices),
        DryRun=dry_run,
        CorrelationId=cid,
        Application=settings.app_name,
    )

    try:
        results = run_insert(body, dry_run)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e)) from e
    except Exception as e:  # noqa: BLE001
        _log.exception("Batch insert failed")
        seq_emit("Error", "GnuCash insert batch failed", exception=str(e), CorrelationId=cid, Application=settings.app_name)
        raise HTTPException(status_code=500, detail=str(e)) from e

    seq_emit(
        "Information",
        "GnuCash insert batch finished",
        CorrelationId=cid,
        Application=settings.app_name,
    )
    return InsertResponse(results=results)


@app.post("/customers/ensure", response_model=CustomerEnsureResult)
def ensure_customer(
    body: CustomerEnsureIn,
    request: Request,
    dry_run: bool = Query(False),
    x_gnucash_sync_key: str | None = Header(None, alias="X-GnuCash-Sync-Key"),
    settings: Settings = Depends(get_app_settings),
) -> CustomerEnsureResult:
    if settings.sync_api_key and (x_gnucash_sync_key or "") != settings.sync_api_key:
        raise HTTPException(status_code=401, detail="Invalid or missing X-GnuCash-Sync-Key")

    cid = getattr(request.state, "correlation_id", None)
    seq_emit(
        "Information",
        "GnuCash ensure customer {CustomerId} (dry_run={DryRun})",
        CustomerId=body.customer_id,
        DryRun=dry_run,
        CorrelationId=cid,
        Application=settings.app_name,
    )

    uri = (settings.gnucash_postgres_url or "").strip()
    if not uri:
        raise HTTPException(status_code=400, detail="GNUCASH_POSTGRES_URL is not set")

    try:
        result = run_ensure_customer(uri, body, dry_run)
    except Exception as e:  # noqa: BLE001
        _log.exception("Ensure customer failed for %s", body.customer_id)
        seq_emit(
            "Error",
            "GnuCash ensure customer failed",
            exception=str(e),
            CorrelationId=cid,
            Application=settings.app_name,
        )
        raise HTTPException(status_code=500, detail=str(e)) from e

    if result.status == "error":
        raise HTTPException(status_code=400, detail=result.detail or "error")

    seq_emit(
        "Information",
        "GnuCash ensure customer finished {Status} for {CustomerId}",
        Status=result.status,
        CustomerId=result.customerId,
        CorrelationId=cid,
        Application=settings.app_name,
    )
    return result


@app.post("/gnucash/wipe-app", response_model=GnucashWipeAppResponse)
def wipe_app_business(
    request: Request,
    dry_run: bool = Query(False),
    x_gnucash_sync_key: str | None = Header(None, alias="X-GnuCash-Sync-Key"),
    settings: Settings = Depends(get_app_settings),
) -> GnucashWipeAppResponse:
    """Delete INV-* invoices and CU-* customers (dev / reset)."""
    if settings.sync_api_key and (x_gnucash_sync_key or "") != settings.sync_api_key:
        raise HTTPException(status_code=401, detail="Invalid or missing X-GnuCash-Sync-Key")

    cid = getattr(request.state, "correlation_id", None)
    uri = (settings.gnucash_postgres_url or "").strip()
    if not uri:
        raise HTTPException(status_code=400, detail="GNUCASH_POSTGRES_URL is not set")

    seq_emit(
        "Warning",
        "GnuCash wipe-app started (dry_run={DryRun})",
        DryRun=dry_run,
        CorrelationId=cid,
        Application=settings.app_name,
    )
    try:
        inv_n, cust_n = run_wipe_app_business(uri, dry_run)
    except Exception as e:  # noqa: BLE001
        _log.exception("GnuCash wipe-app failed")
        seq_emit(
            "Error",
            "GnuCash wipe-app failed",
            exception=str(e),
            CorrelationId=cid,
            Application=settings.app_name,
        )
        raise HTTPException(status_code=500, detail=str(e)) from e

    seq_emit(
        "Warning",
        "GnuCash wipe-app finished: {Invoices} invoices, {Customers} customers (dry_run={DryRun})",
        Invoices=inv_n,
        Customers=cust_n,
        DryRun=dry_run,
        CorrelationId=cid,
        Application=settings.app_name,
    )
    return GnucashWipeAppResponse(dryRun=dry_run, invoicesRemoved=inv_n, customersRemoved=cust_n)

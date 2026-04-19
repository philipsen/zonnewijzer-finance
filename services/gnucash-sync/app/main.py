"""GnuCash insert API — run with project .venv (see README)."""

from __future__ import annotations

import logging
import sys

from fastapi import Depends, FastAPI, Header, HTTPException, Query, Request
from app.gnucash_insert import run_insert
from app.schemas import InsertBody, InsertResponse
from app.seq_log import seq_emit
from app.settings import Settings, get_settings

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(name)s %(message)s",
    stream=sys.stdout,
)
_log = logging.getLogger("gnucash_sync")


def get_app_settings() -> Settings:
    return get_settings()


app = FastAPI(title="gnucash-sync", version="0.1.0")


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

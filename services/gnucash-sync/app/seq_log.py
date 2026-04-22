"""Optional Seq ingestion (CLEF-style JSON lines)."""

from __future__ import annotations

import json
import logging
from datetime import datetime, timezone
from typing import Any

import httpx

from app.settings import get_settings

_log = logging.getLogger("gnucash_sync")


def _clef_event(level: str, message_template: str, **properties: Any) -> dict[str, Any]:
    exc = properties.get("exception")
    base: dict[str, Any] = {
        "@t": datetime.now(timezone.utc).isoformat(),
        "@l": level,
        "@mt": message_template,
        **{k: v for k, v in properties.items() if k != "exception"},
    }
    if exc is not None:
        base["@x"] = exc if isinstance(exc, str) else str(exc)
    return base


def seq_emit(level: str, message: str, **properties: Any) -> None:
    """If SEQ_SERVER_URL is set, POST one CLEF event; always log to root logger."""
    extra = {k: v for k, v in properties.items() if v is not None}
    _log.log(getattr(logging, level, logging.INFO), message, extra=extra)
    settings = get_settings()
    base = (settings.seq_server_url or "").strip().rstrip("/")
    if not base:
        return
    url = f"{base}/ingest/clef"
    payload = json.dumps(_clef_event(level, message, **properties)) + "\n"
    headers = {"Content-Type": "application/vnd.serilog.clef"}
    if settings.seq_api_key:
        headers["X-Seq-ApiKey"] = settings.seq_api_key
    try:
        with httpx.Client(timeout=5.0) as client:
            r = client.post(url, content=payload.encode("utf-8"), headers=headers)
            if r.status_code >= 300:
                _log.warning("Seq POST failed: %s %s", r.status_code, r.text[:200])
    except Exception as ex:  # noqa: BLE001
        _log.warning("Seq emit error: %s", ex)

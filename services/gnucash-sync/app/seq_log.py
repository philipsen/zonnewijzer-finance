"""Optional Seq ingestion (CLEF-style JSON lines)."""

from __future__ import annotations

import json
import logging
import time
from datetime import datetime, timezone
from typing import Any

import httpx

from app.settings import get_settings

_log = logging.getLogger("gnucash_sync")


def _clef_event(level: str, message_template: str, **properties: Any) -> dict[str, Any]:
    return {
        "@t": datetime.now(timezone.utc).isoformat(),
        "@l": level,
        "@mt": message_template,
        "@x": properties.get("exception"),
        **{k: v for k, v in properties.items() if k != "exception"},
    }


def seq_emit(level: str, message: str, **properties: Any) -> None:
    """If SEQ_SERVER_URL is set, POST one CLEF event; always log to root logger."""
    extra = {k: v for k, v in properties.items() if v is not None}
    _log.log(getattr(logging, level, logging.INFO), message, extra=extra)
    settings = get_settings()
    base = (settings.seq_server_url or "").strip().rstrip("/")
    if not base:
        return
    url = f"{base}/api/events/raw"
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

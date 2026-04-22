"""Startup validation for GnuCash PostgreSQL (same existence check piecash uses)."""

from __future__ import annotations

import logging
from urllib.parse import urlparse, urlunparse

from sqlalchemy_utils import database_exists

from app.settings import get_settings

_log = logging.getLogger("gnucash_sync")


def _redact_postgres_url(uri: str) -> str:
    p = urlparse(uri)
    if not p.password:
        return uri
    user = p.username or ""
    host = p.hostname or ""
    port = f":{p.port}" if p.port else ""
    auth = f"{user}:***" if user else "***"
    netloc = f"{auth}@{host}{port}"
    return urlunparse(p._replace(netloc=netloc))


def verify_gnucash_database_or_die() -> None:
    settings = get_settings()
    uri = (settings.gnucash_postgres_url or "").strip()
    if not uri:
        msg = "GNUCASH_POSTGRES_URL is not set"
        _log.error(msg)
        raise RuntimeError(msg)
    safe = _redact_postgres_url(uri)
    try:
        ok = database_exists(uri)
    except Exception as ex:
        _log.exception("Cannot reach PostgreSQL for GnuCash (%s)", safe)
        raise RuntimeError(f"Cannot reach PostgreSQL for GnuCash ({safe}): {ex}") from ex
    if not ok:
        msg = (
            "GnuCash PostgreSQL database does not exist or is not reachable "
            f"({safe}). Check host, port, database name (e.g. gnucash_dev), and restore a GnuCash book if needed."
        )
        _log.error(msg)
        raise RuntimeError(msg)
    _log.info("GnuCash PostgreSQL database check OK (%s)", safe)

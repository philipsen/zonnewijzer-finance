"""GnuCash insert API — run from project venv (see README)."""

from fastapi import FastAPI

app = FastAPI(title="gnucash-sync", version="0.1.0")


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}

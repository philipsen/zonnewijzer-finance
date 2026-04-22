# GnuCash integration (development)

This app inserts **customer invoices** into a **PostgreSQL-backed GnuCash book** via a small Python service (`services/gnucash-sync`) using **piecash**. GnuCash remains the **source of truth** after insert; the API is **insert-only** (existing invoice IDs are skipped).

## 1. Start local Postgres for GnuCash

From the repository root:

```bash
docker compose -f docker-compose.gnucash-dev.yml up -d
```

Or run the VS Code task **Docker: GnuCash Postgres (dev)** (same command, detached).

Default connection (matches `services/gnucash-sync/sample.env`):

- Host: `127.0.0.1`, port **5433**
- User / password: `gnucash` / `gnucash`
- Database: `gnucash_dev`

## 2. Create or restore a GnuCash book in that database

GnuCash expects its full SQL schema. Typical options:

1. **GnuCash desktop:** File → Save As → select **PostgreSQL**, enter the connection details above, and save a new book. Create at least:
   - **Income** account whose **full name** matches `INCOME_ACCOUNT` (default in sample env: `Income:Charging`).
   - **EUR** as currency (default for this integration).
2. **Restore** a `pg_dump` of a dev book into `gnucash_dev` (do not commit dumps or production data).

Until a valid book exists, inserts will fail (often with “account not found” or schema errors).

## 3. Python service (`gnucash-sync`)

```bash
cd services/gnucash-sync
python3 -m venv .venv
source .venv/bin/activate   # Windows: .venv\Scripts\activate
pip install -r requirements.txt
cp sample.env .env
# Edit .env: GNUCASH_POSTGRES_URL, SYNC_API_KEY, INCOME_ACCOUNT (full path in GnuCash)
```

Run:

```bash
python -m uvicorn app.main:app --reload --host 127.0.0.1 --port 8787
```

Or use the VS Code tasks **Setup GnuCash API venv** and **Start GnuCash API**.

### Environment variables (`.env`)

| Variable | Purpose |
|-----------|---------|
| `GNUCASH_POSTGRES_URL` | SQLAlchemy URI, e.g. `postgresql://gnucash:gnucash@127.0.0.1:5433/gnucash_dev` |
| `SYNC_API_KEY` | Shared secret; must match backend `GnuCashSync:ApiKey` |
| `INCOME_ACCOUNT` | Full GnuCash account path for invoice lines (e.g. `Income:Charging`) |
| `SEQ_SERVER_URL` | Optional; e.g. `http://localhost:5341` |
| `SEQ_API_KEY` | Optional Seq API key |

### Auth

Requests must send header **`X-GnuCash-Sync-Key`** when `SYNC_API_KEY` is set.

### Dry run

`POST /invoices/insert?dry_run=true` validates and simulates inserts, then **rolls back** (no `save()`). Use this before a real run.

## 4. Backend (finance API)

In `appsettings.Development.json` (or secrets / environment):

```json
"GnuCashSync": {
  "BaseUrl": "http://127.0.0.1:8787",
  "ApiKey": "<same as SYNC_API_KEY>"
}
```

Admin endpoint:

- **`POST /api/invoices/gnucash/insert`**
- Body: `{ "year": 2025, "month": 3, "chargingUserId": null, "dryRun": false }`
  - Omit `chargingUserId` or set `null` to insert **all** users with sessions that month.
  - Set `chargingUserId` to a single charging user id for one invoice.
  - Set **`dryRun`: true** to call the Python service with `?dry_run=true` (no commits in GnuCash).

Requires admin JWT (same as invoice grid).

## 5. Admin UI

On **Invoices** (invoice grid), use **GnuCash** controls: pick year/month, optional **Dry run**, then **Insert month** or **Insert row** for a single user/month.

## 6. Operational notes

- Prefer **not** running GnuCash desktop against the same book while the Python service is writing.
- Pin **GnuCash** and **piecash** versions; major GnuCash upgrades can change the SQL schema.
- Customer IDs from the app look like **`CU-{chargingUserId}`**; invoice IDs match **`INV-{yyyy}-{mm}-{userId}`**. Re-sends with the same invoice id are **skipped**.

## 7. VS Code

- **Docker: GnuCash Postgres** — starts `docker-compose.gnucash-dev.yml`.
- **Start full stack (Backend + Frontend + GnuCash API)** — runs all three dev processes (after venv setup).

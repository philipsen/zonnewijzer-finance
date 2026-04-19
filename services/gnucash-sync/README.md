# gnucash-sync (Python)

Use a **local virtual environment** (do not install dependencies globally).

## One-time setup

From this directory (`services/gnucash-sync/`):

```bash
python3 -m venv .venv
source .venv/bin/activate   # Windows: .venv\Scripts\activate
pip install -r requirements.txt
```

Copy `sample.env` to `.env` and adjust values. The repo root `.gitignore` ignores `.env`.

## Run the API

With the venv activated:

```bash
python -m uvicorn app.main:app --reload --host 127.0.0.1 --port 8787
```

Or use the VS Code task **Start GnuCash API**, which calls `.venv/bin/python` directly (create the venv first with **Setup GnuCash API venv**).

## Windows

Use `.venv\Scripts\python.exe -m uvicorn app.main:app --reload --host 127.0.0.1 --port 8787` or the Windows variant of the VS Code task.

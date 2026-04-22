#!/usr/bin/env bash
# Dev server: watch only ./app so reload is reliable (default cwd watch can miss or over-trigger on some setups).
set -euo pipefail
cd "$(dirname "$0")"
exec .venv/bin/python -m uvicorn app.main:app \
  --reload \
  --reload-dir app \
  --host 127.0.0.1 \
  --port 8787

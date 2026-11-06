#!/usr/bin/env bash
# Forward SMTP from the WireGuard address to the mailbox provider.
# DigitalOcean blocks outbound 587, and mail.warmoestuin.nl no longer accepts 2525.
# The droplet connects to this Mac over WireGuard; socat opens 587 from the Mac.
set -euo pipefail

BIND_ADDR="${SMTP_RELAY_BIND:-10.0.0.2}"
LISTEN_PORT="${SMTP_RELAY_PORT:-2525}"
UPSTREAM_HOST="${SMTP_UPSTREAM_HOST:-mail.warmoestuin.nl}"
UPSTREAM_PORT="${SMTP_UPSTREAM_PORT:-587}"

if ! command -v socat >/dev/null 2>&1; then
  echo "socat is not installed. Install it with: brew install socat" >&2
  exit 1
fi

if ! ifconfig | grep -q "inet ${BIND_ADDR} "; then
  echo "WireGuard address ${BIND_ADDR} is not on this Mac. Connect the tunnel first." >&2
  exit 1
fi

echo "SMTP relay ${BIND_ADDR}:${LISTEN_PORT} -> ${UPSTREAM_HOST}:${UPSTREAM_PORT}"
echo "Leave this running. The Mac must stay awake and WireGuard must stay connected."
exec socat \
  "TCP-LISTEN:${LISTEN_PORT},bind=${BIND_ADDR},fork,reuseaddr" \
  "TCP:${UPSTREAM_HOST}:${UPSTREAM_PORT}"

#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_CADDYFILE="${SCRIPT_DIR}/Caddyfile"
TARGET_CADDYFILE="/etc/caddy/Caddyfile"

if [[ ! -f "${SOURCE_CADDYFILE}" ]]; then
  echo "Missing ${SOURCE_CADDYFILE}"
  exit 1
fi

echo "Installing relay Caddyfile to ${TARGET_CADDYFILE}..."
sudo cp "${SOURCE_CADDYFILE}" "${TARGET_CADDYFILE}"
sudo chown root:root "${TARGET_CADDYFILE}"
sudo chmod 644 "${TARGET_CADDYFILE}"

echo "Validating Caddy configuration..."
sudo caddy validate --config "${TARGET_CADDYFILE}"

echo "Restarting Caddy..."
sudo systemctl restart caddy
sudo systemctl --no-pager --full status caddy

echo "Done. Relay should now listen on 10.0.0.3:18080."

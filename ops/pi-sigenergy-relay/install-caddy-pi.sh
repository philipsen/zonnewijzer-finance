#!/usr/bin/env bash
set -euo pipefail

echo "Installing Caddy on Raspberry Pi (Debian/Ubuntu)..."
sudo apt update
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https curl gpg
curl -1sLf "https://dl.cloudsmith.io/public/caddy/stable/gpg.key" | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf "https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt" | sudo tee /etc/apt/sources.list.d/caddy-stable.list >/dev/null
sudo apt update
sudo apt install -y caddy
echo "Caddy installed."

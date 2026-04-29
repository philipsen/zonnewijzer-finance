# Pi Sigenergy Relay

This folder contains files to run a Caddy relay on the Raspberry Pi (`10.0.0.3`) so the server (`10.0.0.1`) can reach Sigenergy through the Pi egress IP.

## Files

- `Caddyfile`: reverse proxy to `https://api-eu.sigencloud.com`, only allows source `10.0.0.1`
- `install-caddy-pi.sh`: installs Caddy on Debian/Ubuntu-based Pi
- `apply-relay.sh`: copies Caddyfile to `/etc/caddy/Caddyfile`, validates, and restarts Caddy
- `server-env.example`: env value to set on the server

## On the Pi (10.0.0.3)

```bash
chmod +x install-caddy-pi.sh apply-relay.sh
./install-caddy-pi.sh
./apply-relay.sh
```

## On the server (10.0.0.1)

Set:

```bash
SIGENERGY_BASE_URL=http://10.0.0.3:18080
```

Then restart your `integration-daemon` service/container.

## Test from server

```bash
curl -sS -X POST "http://10.0.0.3:18080/openapi/auth/login/password" \
  -H "Content-Type: application/json" \
  -H "User-Agent: vscode-restclient" \
  -d '{"username":"<SIGENERGY_USERNAME>","password":"<SIGENERGY_PASSWORD>"}'
```

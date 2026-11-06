# Mac SMTP relay

DigitalOcean blocks outbound port 587. The mailbox provider closed port 2525. This Mac can still reach `mail.warmoestuin.nl:587`, and the droplet can reach the Mac over WireGuard (`utun`, address `10.0.0.2`).

`smtp-relay.sh` runs socat on the Mac. It listens on the WireGuard address and forwards the TCP connection to the mailbox provider. STARTTLS still runs end to end between the backend and `mail.warmoestuin.nl`; socat only forwards bytes. The mail connection leaves the Mac through its normal network, not back through the tunnel.

```
finance-backend  --WireGuard-->  Mac 10.0.0.2:2525  --socat-->  mail.warmoestuin.nl:587
```

## On the Mac

WireGuard must be connected (`10.0.0.2` on `utun`). Install socat once:

```bash
brew install socat
```

Start the relay and leave the terminal open. The Mac must stay awake.

```bash
chmod +x ops/mac-smtp-relay/smtp-relay.sh
./ops/mac-smtp-relay/smtp-relay.sh
```

Optional overrides:

| Variable | Default | Meaning |
|----------|---------|---------|
| `SMTP_RELAY_BIND` | `10.0.0.2` | WireGuard address to listen on |
| `SMTP_RELAY_PORT` | `2525` | Port the droplet connects to |
| `SMTP_UPSTREAM_HOST` | `mail.warmoestuin.nl` | Mailbox provider |
| `SMTP_UPSTREAM_PORT` | `587` | Submission port, reachable from the Mac |

## On the server (`10.0.0.1`)

In `compose/zonnewijzer-finance/.env`:

```bash
SMTP_HOST=10.0.0.2
SMTP_PORT=2525
```

Recreate the backend so Compose picks up the env change. `docker restart` keeps the previous host.

```bash
cd compose/zonnewijzer-finance
docker compose -f docker-compose.production.yml up -d backend
docker exec finance-backend nc -zv -w 5 10.0.0.2 2525
```

The backend container must reach `10.0.0.2`. Docker rewrites container traffic to the droplet's WireGuard address (`10.0.0.1`) before it enters the tunnel. If the host connects and the container does not, the container is sending from its bridge address and WireGuard is dropping it.

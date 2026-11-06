# Finance App - Docker Deployment Guide

This guide explains how to deploy the Finance App using Docker images from Docker Hub.

## Prerequisites

- Docker and Docker Compose installed
- 2GB+ available RAM
- 1GB+ available disk space

## Quick Start (Simple Deployment)

1. **Clone or download the docker-compose files**

2. **Create required directories**
   ```bash
   # For simple deployment
   mkdir -p database logs
   ```

3. **Deploy with simple configuration**
   ```bash
   docker-compose -f docker-compose.simple.yml up -d
   ```

4. **Access the application**
   - App + API: http://localhost:5000

## Production Deployment (Single Backend Container)

1. **Create required directories**
   ```bash
   # For production deployment  
   mkdir -p data logs nginx/ssl
   ```

2. **Deploy with production configuration**
   ```bash
   docker-compose -f docker-compose.production.yml up -d
   ```

3. **Access the application**
   - App + API: http://localhost:5000

## Configuration

### Environment Variables

You can customize the deployment by creating a `.env` file:

```env
# Database
DATABASE_PATH=/app/database/finance.db

# Backend
ASPNETCORE_ENVIRONMENT=Production
BACKEND_PORT=5001

# App
APP_PORT=5000

# SMTP — DigitalOcean blocks outbound 587, and the provider closed 2525.
# Production uses the socat relay on the Mac (WireGuard). See ops/mac-smtp-relay/README.md.
SMTP_PASSWORD=your-warmoestuin-mailbox-password
SMTP_HOST=10.0.0.2
SMTP_PORT=2525
```

### Production secrets (server `.env`)

Host, port, username, from address, invoice BCC, and certificate skip are in `Backend/appsettings.Production.json` (and `docker-compose.production.yml` for skip). **Update these on the deploy host** when changing providers:

| Variable | Used for | Action for Warmoestuin SMTP |
|----------|----------|-----------------------------|
| `SMTP_PASSWORD` | `Smtp__Password` | Warmoestuin mailbox password for `zonnewijzer@warmoestuin.nl` |
| `SMTP_HOST` | `Smtp__Host` | **`10.0.0.2`**, the Mac's WireGuard address running socat |
| `SMTP_PORT` | `Smtp__Port` | **`2525`** on the Mac. socat forwards that to `mail.warmoestuin.nl:587` |

Production does not open 587 itself. DigitalOcean blocks that port, and `mail.warmoestuin.nl:2525` is closed. The backend connects to socat on the Mac over WireGuard. socat connects onward to port 587. STARTTLS still runs end to end (`SkipCertificateValidation=true` for the bb-host cert mismatch). Local dev uses **587** in `appsettings.Development.json` and does not need the relay.

Start the relay on the Mac before sending mail. Setup and the script are in [ops/mac-smtp-relay/README.md](ops/mac-smtp-relay/README.md).

```bash
./ops/mac-smtp-relay/smtp-relay.sh
```

After updating secrets, recreate the backend. `docker restart` does not reload `.env`.

```bash
docker compose -f docker-compose.production.yml up -d backend
```

Test from the backend container:

```bash
docker exec finance-backend nc -zv -w 5 10.0.0.2 2525
```

Other backend secrets (unchanged by this SMTP move):

| Variable | Used for |
|----------|----------|
| `JWT_KEY` | JWT signing |
| `GITHUB_CLIENT_ID` / `GITHUB_CLIENT_SECRET` | GitHub OAuth |
| `MOLLIE_API_KEY` | Payments |
| `DAEMON_API_KEY` | Backend ↔ integration daemon |
| `BOOTSTRAP_ADMIN_PASSWORD` | Optional first admin seed |
| Smappee / Sigenergy vars on daemon | Integration daemon `.env` |

After updating `SMTP_PASSWORD`, redeploy or restart the backend container:

```bash
docker compose -f docker-compose.production.yml up -d backend
```

### Google OAuth Setup

To enable Google authentication, you need to:

1. Create a Google Cloud project and OAuth credentials
2. Update the backend configuration with your Google Client ID
3. Update the frontend with your Google Client ID

### SSL/HTTPS (Optional)

For HTTPS support:

1. Place your SSL certificates in `nginx/ssl/`:
   - `cert.pem` (certificate)
   - `key.pem` (private key)

2. Uncomment the HTTPS server block in `nginx/nginx.conf`

## Data Persistence

### Directory Structure
- **Simple deployment**: Uses `./database/` for database storage
- **Production deployment**: Uses `./data/` for database storage  
- **Both deployments**: Use `./logs/` for application logs

Storage locations:
- **Database**: `./data/finance.db` (production) or `./database/finance.db` (simple)
- **Logs**: `./logs/`

## Management Commands

### View logs
```bash
# All services
docker-compose logs -f

# Specific service
docker-compose logs -f backend
```

### Stop services
```bash
docker-compose down
```

### Update images
```bash
docker-compose pull
docker-compose up -d
```

### Backup database
```bash
cp data/finance.db data/finance.db.backup.$(date +%Y%m%d)
```

## Troubleshooting

### Common Issues

1. **Port conflicts**: If port 5000 is in use, modify the port mapping in docker-compose

2. **Database permissions**: Ensure the `data` directory is writable
   ```bash
   chmod 755 data
   ```

3. **Container health checks failing**: Check logs with `docker-compose logs`

4. **Invoice email returns 504 or times out**: The backend sends through the Mac socat relay at **`10.0.0.2:2525`**. DigitalOcean blocks outbound **587**, and the provider no longer accepts **2525**. Confirm WireGuard is up and `./ops/mac-smtp-relay/smtp-relay.sh` is running on the Mac.

   ```bash
   docker exec finance-backend nc -zv -w 5 10.0.0.2 2525
   docker compose -f docker-compose.production.yml logs backend --tail 50
   ```

### Reset Application

To completely reset the application:
```bash
docker-compose down -v
rm -rf data/* logs/*
docker-compose up -d
```

## Service URLs

- **App + Backend**: ASP.NET Core service that serves both API and Vue frontend assets
- **Database**: SQLite database file for persistence

## Support

For issues or questions, check the application logs or contact the development team.

## Version Information

- Backend Image: `philipsen/finance-backend:latest`
- Last Updated: March 2026
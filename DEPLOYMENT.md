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

# SMTP — production uses mail.warmoestuin.nl:2525 (587 times out from the container)
SMTP_PASSWORD=your-warmoestuin-mailbox-password
# SMTP_HOST=mail.warmoestuin.nl
# SMTP_PORT=2525
```

### Production secrets (server `.env`)

Host, port, username, from address, invoice BCC, and certificate skip are in `Backend/appsettings.Production.json` (and `docker-compose.production.yml` for skip). **Update these on the deploy host** when changing providers:

| Variable | Used for | Action for Warmoestuin SMTP |
|----------|----------|-----------------------------|
| `SMTP_PASSWORD` | `Smtp__Password` | Warmoestuin mailbox password for `zonnewijzer@warmoestuin.nl` |
| `SMTP_HOST` | `Smtp__Host` | Default **`mail.warmoestuin.nl`** |
| `SMTP_PORT` | `Smtp__Port` | Default **`2525`** (port 587 times out from the container; 2525 is reachable) |

Production sends directly to Warmoestuin mail on **port 2525** with STARTTLS (`SkipCertificateValidation=true` for the bb-host cert mismatch). Local dev uses **587** in `appsettings.Development.json`.

After updating secrets, redeploy or restart:

```bash
docker compose -f docker-compose.production.yml up -d backend
```

Test from the backend container:

```bash
docker exec finance-backend nc -zv -w 5 mail.warmoestuin.nl 2525
```

Other backend secrets (unchanged by this SMTP move):

| Variable | Used for |
|----------|----------|
| `JWT_KEY` | JWT signing |
| `GITHUB_CLIENT_ID` / `GITHUB_CLIENT_SECRET` | GitHub OAuth |
| `MOLLIE_API_KEY` | Payments |
| `DAEMON_API_KEY` | Backend ↔ integration daemon |
| `GNUCASH_SYNC_API_KEY` | GnuCash insert API |
| `BOOTSTRAP_ADMIN_PASSWORD` | Optional first admin seed |
| `GNUCASH_POSTGRES_URL` | GnuCash sync database |
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

4. **Invoice email returns 504 or times out**: Check connectivity from the backend container to **`mail.warmoestuin.nl:2525`** (production default). Port **587** often times out from Docker.

   ```bash
   docker exec finance-backend nc -zv -w 5 mail.warmoestuin.nl 2525
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
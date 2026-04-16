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

# SMTP (used for password reset emails, via Backend -> SmtpEmailService)
SMTP_HOST=mail-eu.smtp2go.com
SMTP_PORT=587
SMTP_ENABLESSL=true
SMTP_USERNAME=your-smtp-username
SMTP_PASSWORD=your-smtp-password
SMTP_FROM_EMAIL=your-from-email
SMTP_FROM_NAME=Zonnewijzer Finance
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
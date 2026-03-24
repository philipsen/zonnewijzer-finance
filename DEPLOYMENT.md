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
   - Frontend: http://localhost:3000
   - Backend API: http://localhost:5001

## Production Deployment (with Nginx)

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
   - Frontend: http://localhost:8080
   - Backend API: http://localhost:8080/api/

## Configuration

### Environment Variables

You can customize the deployment by creating a `.env` file:

```env
# Database
DATABASE_PATH=/app/database/finance.db

# Backend
ASPNETCORE_ENVIRONMENT=Production
BACKEND_PORT=5001

# Frontend  
FRONTEND_PORT=3000

# Nginx (production only)
HTTP_PORT=80
HTTPS_PORT=443
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

1. **Port conflicts**: If ports 8080, 3000, or 5000 are in use, modify the ports in the docker-compose file

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

- **Frontend**: Vue.js application with invoice management interface
- **Backend**: ASP.NET Core API with Google OAuth and SQLite database
- **Database**: SQLite database file for persistence

## Support

For issues or questions, check the application logs or contact the development team.

## Version Information

- Backend Image: `philipsen/finance-backend:latest`
- Frontend Image: `philipsen/finance-frontend:latest`
- Last Updated: March 2026
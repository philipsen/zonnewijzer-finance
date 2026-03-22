# Docker Setup for Finance App

This Docker setup allows you to run the entire Finance application (backend + frontend) in containers.

## Prerequisites

- Docker
- Docker Compose

## Quick Start

1. **Build and run the application:**
   ```bash
   docker-compose up --build
   ```

2. **Access the application:**
   - Open your web browser and go to: `http://localhost:5000`
   - The frontend will be served by nginx and API calls will be proxied to the backend

3. **Stop the application:**
   ```bash
   docker-compose down
   ```

## Architecture

- **Frontend**: Vue.js app served by nginx on port 80 (exposed as port 5000)
- **Backend**: ASP.NET Core API running on port 5000 (internal)
- **Database**: SQLite database mounted from local `./database` directory
- **Logs**: Backend logs are also mounted to `./Backend/logs`

## Database Persistence

The SQLite database is mounted from the local `./database` directory to `/app/data` in the backend container. This ensures that your data persists between container restarts.

## Configuration

- Backend configuration: `Backend/appsettings.Production.json`
- Frontend nginx configuration: `Frontend/nginx.conf`
- Docker Compose configuration: `docker-compose.yml`

## Development vs Production

This Docker setup is configured for production use. For development, continue using:
- Backend: `dotnet run --project Backend/Backend.csproj`
- Frontend: `cd Frontend && npm run dev`

## Useful Commands

```bash
# Build only (without starting)
docker-compose build

# Run in background (detached mode)
docker-compose up -d

# View logs
docker-compose logs

# View logs for a specific service
docker-compose logs backend
docker-compose logs frontend

# Rebuild and restart
docker-compose up --build --force-recreate
```

## Troubleshooting

1. **Port conflicts**: If port 5000 is already in use, change it in docker-compose.yml
2. **Database issues**: Check that the `./database` directory has proper permissions
3. **Build issues**: Try running `docker-compose build --no-cache` for a clean rebuild
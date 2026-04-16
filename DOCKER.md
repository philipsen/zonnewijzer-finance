# Docker Setup for Finance App

This Docker setup runs the entire Finance application in a single backend container.

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
   - The backend serves both SPA assets and API endpoints

3. **Stop the application:**
   ```bash
   docker-compose down
   ```

## Architecture

- **Backend**: ASP.NET Core app running on port 5000, serving both API and built frontend assets
- **Database**: SQLite database mounted from local `./database` directory
- **Logs**: Backend logs are also mounted to `./Backend/logs`

## Database Persistence

The SQLite database is mounted from the local `./database` directory to `/app/data` in the backend container. This ensures that your data persists between container restarts.

## Configuration

- Backend configuration: `Backend/appsettings.Production.json`
- Frontend build source: `Frontend/`
- Docker Compose configuration: `docker-compose.yml`

## Development vs Production

This Docker setup is configured for production use. For development, continue using:
- Backend: `dotnet run --project Backend/Backend.csproj`
- Frontend: `cd Frontend && npm run dev`
- In development, backend proxies non-`/api` requests to Vite at `http://localhost:3000`

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

# Rebuild and restart
docker-compose up --build --force-recreate
```

## Troubleshooting

1. **Port conflicts**: If port 5000 is already in use, change it in `docker-compose.yml`
2. **Database issues**: Check that the `./database` directory has proper permissions
3. **Build issues**: Try running `docker-compose build --no-cache` for a clean rebuild
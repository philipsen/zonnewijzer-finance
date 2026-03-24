#!/bin/bash
# update-and-restart.sh - Script to pull latest images and restart services

echo "🔄 Pulling latest images from Docker Hub..."
docker compose -f docker-compose.production.yml pull

echo "🚀 Restarting services with updated images..."
docker compose -f docker-compose.production.yml up -d

echo "✅ Update complete! Checking service status..."
docker compose -f docker-compose.production.yml ps
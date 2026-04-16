#!/bin/bash
set -e

echo "Building backend image..."
docker build -f Backend/Dockerfile -t philipsen/finance-backend:latest .

echo "Pushing backend image..."
docker push philipsen/finance-backend:latest

echo "Done! Backend single image built and pushed."

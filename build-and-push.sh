#!/bin/bash
set -e

echo "Building backend image..."
docker build -t philipsen/finance-backend:latest ./Backend

echo "Building frontend image..."
docker build -t philipsen/finance-frontend:latest ./Frontend

echo "Pushing backend image..."
docker push philipsen/finance-backend:latest

echo "Pushing frontend image..."
docker push philipsen/finance-frontend:latest

echo "Done! Both images built and pushed."

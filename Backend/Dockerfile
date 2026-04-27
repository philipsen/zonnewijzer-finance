FROM node:22-alpine AS frontend-build
WORKDIR /src
COPY Frontend/package*.json ./Frontend/
RUN cd Frontend && npm ci
COPY Frontend ./Frontend
RUN cd Frontend && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY Backend/Backend.csproj ./Backend/
COPY IntegrationCore/IntegrationCore.csproj ./IntegrationCore/
RUN dotnet restore "Backend/Backend.csproj"
COPY Backend ./Backend
COPY IntegrationCore ./IntegrationCore
RUN dotnet publish "Backend/Backend.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/* && mkdir -p /app/logs

COPY --from=backend-build /app/publish .
COPY --from=frontend-build /src/Frontend/dist ./wwwroot

EXPOSE 5000
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5000

ENTRYPOINT ["dotnet", "Backend.dll"]
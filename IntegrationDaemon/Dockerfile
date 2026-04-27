FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Finance.sln ./
COPY Backend/Backend.csproj Backend/
COPY IntegrationCore/IntegrationCore.csproj IntegrationCore/
COPY IntegrationDaemon/IntegrationDaemon.csproj IntegrationDaemon/
RUN dotnet restore IntegrationDaemon/IntegrationDaemon.csproj
COPY . .
RUN dotnet publish IntegrationDaemon/IntegrationDaemon.csproj -c Release -o /app/publish /p:UseAppHost=false /p:ErrorOnDuplicatePublishOutputFiles=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish ./
ENV DOTNET_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "IntegrationDaemon.dll"]

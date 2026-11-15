FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 6555

FROM node:22-bookworm-slim AS frontend
WORKDIR /source/src/OpenCRM.Frontend
COPY src/OpenCRM.Frontend/ ./
RUN npm ci --no-audit --no-fund
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /source
COPY services/notifications/Directory.Build.props services/notifications/Directory.Build.props
COPY services/payments/Directory.Build.props services/payments/Directory.Build.props
COPY ["services/payments/src/Payments.Contracts/Payments.Contracts.csproj", "services/payments/src/Payments.Contracts/"]
COPY services/notifications/src/Notifications.Contracts/ services/notifications/src/Notifications.Contracts/
COPY ["src/OpenCRM.WebApp/OpenCRM.WebApp.csproj", "src/OpenCRM.WebApp/"]
COPY ["src/OpenCRM.Infrastructure.Auth/OpenCRM.Infrastructure.Auth.csproj", "src/OpenCRM.Infrastructure.Auth/"]
COPY ["src/OpenCRM.Infrastructure.Admin/OpenCRM.Infrastructure.Admin.csproj", "src/OpenCRM.Infrastructure.Admin/"]
COPY ["src/OpenCRM.Infrastructure.FileStorage/OpenCRM.Infrastructure.FileStorage.csproj", "src/OpenCRM.Infrastructure.FileStorage/"]
COPY ["src/OpenCRM.Infrastructure.Payments/OpenCRM.Infrastructure.Payments.csproj", "src/OpenCRM.Infrastructure.Payments/"]
COPY ["src/OpenCRM.Modules.Documents/OpenCRM.Modules.Documents.csproj", "src/OpenCRM.Modules.Documents/"]
COPY ["src/OpenCRM.Modules.Vehicles/OpenCRM.Modules.Vehicles.csproj", "src/OpenCRM.Modules.Vehicles/"]
COPY ["src/OpenCRM.Modules.Vehicles.Contracts/OpenCRM.Modules.Vehicles.Contracts.csproj", "src/OpenCRM.Modules.Vehicles.Contracts/"]
COPY ["src/OpenCRM.Modules.Inventory/OpenCRM.Modules.Inventory.csproj", "src/OpenCRM.Modules.Inventory/"]
COPY ["src/OpenCRM.Modules.Inventory.Contracts/OpenCRM.Modules.Inventory.Contracts.csproj", "src/OpenCRM.Modules.Inventory.Contracts/"]
COPY ["src/OpenCRM.Modules.Maintenance/OpenCRM.Modules.Maintenance.csproj", "src/OpenCRM.Modules.Maintenance/"]
COPY ["src/OpenCRM.Modules.Maintenance.Contracts/OpenCRM.Modules.Maintenance.Contracts.csproj", "src/OpenCRM.Modules.Maintenance.Contracts/"]
COPY ["src/OpenCRM.Infrastructure.Common/OpenCRM.Infrastructure.Common.csproj", "src/OpenCRM.Infrastructure.Common/"]
COPY ["src/OpenCRM.Modules.Support/OpenCRM.Modules.Support.csproj", "src/OpenCRM.Modules.Support/"]
COPY ["src/OpenCRM.Domain/OpenCRM.Domain.csproj", "src/OpenCRM.Domain/"]
COPY ["src/OpenCRM.Services.Interfaces/OpenCRM.Services.Interfaces.csproj", "src/OpenCRM.Services.Interfaces/"]
COPY ["src/OpenCRM.Domain.Interfaces/OpenCRM.Domain.Interfaces.csproj", "src/OpenCRM.Domain.Interfaces/"]
COPY ["src/OpenCRM.Infrastructure.Data/OpenCRM.Infrastructure.Data.csproj", "src/OpenCRM.Infrastructure.Data/"]
COPY ["src/OpenCRM.Infrastructure.Migrations/OpenCRM.Infrastructure.Migrations.csproj", "src/OpenCRM.Infrastructure.Migrations/"]
COPY ["src/Helpers/Helpers.csproj", "src/Helpers/"]
COPY ["src/OpenCRM.Modules.Clients/OpenCRM.Modules.Clients.csproj", "src/OpenCRM.Modules.Clients/"]
COPY ["src/OpenCRM.Modules.Clients.Contracts/OpenCRM.Modules.Clients.Contracts.csproj", "src/OpenCRM.Modules.Clients.Contracts/"]
COPY ["src/OpenCRM.Modules.Communications.Contracts/OpenCRM.Modules.Communications.Contracts.csproj", "src/OpenCRM.Modules.Communications.Contracts/"]
COPY ["src/OpenCRM.Modules.Communications/OpenCRM.Modules.Communications.csproj", "src/OpenCRM.Modules.Communications/"]
COPY ["src/OpenCRM.Modules.Organizations/OpenCRM.Modules.Organizations.csproj", "src/OpenCRM.Modules.Organizations/"]
COPY ["src/OpenCRM.Modules.Organizations.Contracts/OpenCRM.Modules.Organizations.Contracts.csproj", "src/OpenCRM.Modules.Organizations.Contracts/"]
COPY ["src/OpenCRM.Modules.Catalog/OpenCRM.Modules.Catalog.csproj", "src/OpenCRM.Modules.Catalog/"]
COPY ["src/OpenCRM.Modules.Catalog.Contracts/OpenCRM.Modules.Catalog.Contracts.csproj", "src/OpenCRM.Modules.Catalog.Contracts/"]
COPY ["src/OpenCRM.Modules.Planning/OpenCRM.Modules.Planning.csproj", "src/OpenCRM.Modules.Planning/"]
COPY ["src/OpenCRM.Modules.Planning.Contracts/OpenCRM.Modules.Planning.Contracts.csproj", "src/OpenCRM.Modules.Planning.Contracts/"]
COPY ["src/OpenCRM.Modules.Billing.Contracts/OpenCRM.Modules.Billing.Contracts.csproj", "src/OpenCRM.Modules.Billing.Contracts/"]
COPY ["src/OpenCRM.Modules.Billing/OpenCRM.Modules.Billing.csproj", "src/OpenCRM.Modules.Billing/"]
COPY ["src/OpenCRM.Modules.Promotions.Contracts/OpenCRM.Modules.Promotions.Contracts.csproj", "src/OpenCRM.Modules.Promotions.Contracts/"]
COPY ["src/OpenCRM.Modules.Promotions/OpenCRM.Modules.Promotions.csproj", "src/OpenCRM.Modules.Promotions/"]
COPY ["src/OpenCRM.Modules.Payments.Contracts/OpenCRM.Modules.Payments.Contracts.csproj", "src/OpenCRM.Modules.Payments.Contracts/"]
COPY ["src/OpenCRM.Modules.Workspace/OpenCRM.Modules.Workspace.csproj", "src/OpenCRM.Modules.Workspace/"]
COPY ["src/OpenCRM.Modules.Accounts/OpenCRM.Modules.Accounts.csproj", "src/OpenCRM.Modules.Accounts/"]
COPY ["src/OpenCRM.Modules.Notifications/OpenCRM.Modules.Notifications.csproj", "src/OpenCRM.Modules.Notifications/"]
COPY ["src/OpenCRM.Modules.Imports/OpenCRM.Modules.Imports.csproj", "src/OpenCRM.Modules.Imports/"]
RUN dotnet restore "./src/OpenCRM.WebApp/OpenCRM.WebApp.csproj"
COPY . .
COPY --from=frontend /source/src/OpenCRM.WebApp/wwwroot/ /source/src/OpenCRM.WebApp/wwwroot/
WORKDIR /source/src/OpenCRM.WebApp
RUN dotnet build "./OpenCRM.WebApp.csproj" -c $BUILD_CONFIGURATION --no-restore -o /app/build -p:BuildScenarioClientsOnBuild=false

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./OpenCRM.WebApp.csproj" -c $BUILD_CONFIGURATION --no-restore -o /app/publish /p:UseAppHost=false -p:BuildScenarioClientsOnBuild=false -p:BuildScenarioClientsOnPublish=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OpenCRM.WebApp.dll"]

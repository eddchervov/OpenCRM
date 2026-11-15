FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source/services/notifications
COPY ["services/notifications/Directory.Build.props", "."]
COPY ["services/notifications/src/Notifications.Contracts/Notifications.Contracts.csproj", "src/Notifications.Contracts/"]
COPY ["services/notifications/src/Notifications.Domain/Notifications.Domain.csproj", "src/Notifications.Domain/"]
COPY ["services/notifications/src/Notifications.Application/Notifications.Application.csproj", "src/Notifications.Application/"]
COPY ["services/notifications/src/Notifications.Infrastructure/Notifications.Infrastructure.csproj", "src/Notifications.Infrastructure/"]
COPY ["services/notifications/src/Notifications.Worker/Notifications.Worker.csproj", "src/Notifications.Worker/"]
RUN dotnet restore "src/Notifications.Worker/Notifications.Worker.csproj"
COPY services/notifications/ ./
COPY configuration/brand.json /source/configuration/brand.json
COPY src/Helpers/ProjectBrand.cs /source/src/Helpers/ProjectBrand.cs
RUN dotnet publish "src/Notifications.Worker/Notifications.Worker.csproj" --configuration Release --output /app/publish --no-restore /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
USER $APP_UID
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Notifications.Worker.dll"]

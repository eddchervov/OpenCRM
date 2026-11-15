FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source/services/payments
COPY services/payments/Directory.Build.props ./
COPY services/payments/src/Payments.Contracts/Payments.Contracts.csproj src/Payments.Contracts/
COPY services/payments/src/Payments.Service/Payments.Service.csproj src/Payments.Service/
RUN dotnet restore src/Payments.Service/Payments.Service.csproj
COPY services/payments/src/ src/
COPY configuration/brand.json /source/configuration/brand.json
COPY src/Helpers/ProjectBrand.cs /source/src/Helpers/ProjectBrand.cs
RUN dotnet publish src/Payments.Service/Payments.Service.csproj --no-restore -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Payments.Service.dll"]

# Local setup

**English** | [Русский](local-run.ru.md)

This guide runs the basic OpenCRM web application with your own PostgreSQL instance. SMTP, RabbitMQ, the notification service, AI, and payments are not required for registration or basic CRM work.

## Requirements

- .NET SDK 10.
- Git.
- Node.js `^20.19.0` or `>=22.12.0` and npm with workspace support. This requirement comes from Vite 7 in `src/OpenCRM.Frontend/package-lock.json`. The root `package.json` has no separate `engines` field.
- PostgreSQL 17. Earlier supported versions may work, but a clean project installation has been verified on PostgreSQL 17.
- A local .NET development certificate for HTTPS.

Docker is unnecessary if PostgreSQL is already installed or available separately.

## Source code and frontend dependencies

Run these terminal commands from your working directory.

**Linux (Bash):**

```bash
git clone https://github.com/eddchervov/OpenCRM.git
cd OpenCRM/src/OpenCRM.Frontend
npm ci
cd ../..
```

After `npm ci`, the last command returns to the root of the `OpenCRM` clone.

**Windows (PowerShell):**

```powershell
git clone https://github.com/eddchervov/OpenCRM.git
Set-Location OpenCRM/src/OpenCRM.Frontend
npm ci
Set-Location ../..
```

## Create a local database

Choose one option. The example password is a placeholder: replace it with your own value and use the same value in the connection string.

### Option A: PostgreSQL in Docker

For a new local installation with a persistent named volume, run this single command (works in Bash and PowerShell):

```text
docker run --name opencrm-local-db --detach --publish 127.0.0.1:5432:5432 --volume opencrm-local-data:/var/lib/postgresql/data --env POSTGRES_DB=opencrm_local --env POSTGRES_USER=opencrm_local --env POSTGRES_PASSWORD=replace-with-your-local-password postgres:17-alpine
```

The command creates the `opencrm_local` role and database. Before applying migrations, wait until the TCP check inside the container reports `accepting connections`:

```text
docker exec opencrm-local-db pg_isready -h 127.0.0.1 -U opencrm_local -d opencrm_local
```

If PostgreSQL is still starting, repeat the check after a few seconds. If port `5432` is occupied, choose another published port and use it in the connection string. Restart an existing container without deleting its volume or changing the existing database password. Then continue to “Connection and migrations.”

### Option B: An installed PostgreSQL instance

The commands below require the `psql` client in PATH. You can also execute the SQL in your preferred PostgreSQL administration client.

Create a separate PostgreSQL role and an empty database owned by that role. Execute the SQL using a PostgreSQL administrator account; replace the example password with your own random local value and do not save it in Git.

**Linux (Bash):**

```bash
psql -h localhost -U postgres -d postgres -c "CREATE ROLE opencrm_local LOGIN PASSWORD 'replace-with-your-local-password';"
psql -h localhost -U postgres -d postgres -c "CREATE DATABASE opencrm_local OWNER opencrm_local;"
```

**Windows (PowerShell):**

```powershell
psql -h localhost -U postgres -d postgres -c "CREATE ROLE opencrm_local LOGIN PASSWORD 'replace-with-your-local-password';"
psql -h localhost -U postgres -d postgres -c "CREATE DATABASE opencrm_local OWNER opencrm_local;"
```

If PostgreSQL is already configured, you can use your own role name, password, host, port, and new database name. Do not target an existing production database with migration commands.

## Connection and migrations

Set `ConnectionStrings__OpenCRMConnection` in the current terminal. This example targets the new local database. Use your own password.

**Linux (Bash):**

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__OpenCRMConnection='Host=localhost;Port=5432;Database=opencrm_local;Username=opencrm_local;Password=replace-with-your-local-password'
```

**Windows (PowerShell):**

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__OpenCRMConnection = 'Host=localhost;Port=5432;Database=opencrm_local;Username=opencrm_local;Password=replace-with-your-local-password'
```

Restore the local EF CLI from the repository manifest. Version `10.0.7` matches the migrations project; there is no need to change global tools used by other projects:

```text
dotnet tool restore
```

Then apply migrations to your new database from the clone root:

```text
dotnet ef database update --project src/OpenCRM.Infrastructure.Migrations --startup-project src/OpenCRM.Infrastructure.Migrations
```

The migrations project reads the same `ConnectionStrings__OpenCRMConnection` variable. Applying migrations is a separate step; the web application does not create the schema at startup.

## Start the application and sign in

On Windows and macOS, create and trust a local HTTPS certificate:

```text
dotnet dev-certs https --trust
```

On Linux, run `dotnet dev-certs https --check`. If the certificate has not been created, create it with `dotnet dev-certs https`; then add it to your system and browser trust stores using your distribution's tools.

Start the application from the clone root:

```text
dotnet run --project src/OpenCRM.WebApp/OpenCRM.WebApp.csproj --launch-profile https
```

Open `https://localhost:7142`. Application and database readiness is available at `/api/health/ready`.

Register a user at `/auth/register`, sign in, and create an organization. Initial registration does not require an email service: `Notifications:Transport=Disabled` and `EmailConfirmation:Required=false`. The application does not send a registration email or automatically mark the address as verified. If you cannot configure trusted HTTPS locally, the `http` profile is available at `http://localhost:5134` to check startup and readiness. Use HTTPS for sign-in: the application's session cookie is marked Secure.

There is no default administrator account or password. Administrative access requires the appropriate role. See the [administration guide](administration.md) to assign the first administrator in your own new database and launch both admin interfaces.

## Build the frontend and solution

After installing npm packages, you can build the frontend from `src/OpenCRM.Frontend`:

```text
npm run build
```

To run the web CRM on any supported server OS, build the web project from the clone root:

```text
dotnet restore src/OpenCRM.WebApp/OpenCRM.WebApp.csproj
dotnet build src/OpenCRM.WebApp/OpenCRM.WebApp.csproj
```

The full solution also includes the Windows WPF Launcher. Use Windows to build all applications, including the launcher:

```text
dotnet restore OpenCRM.sln
dotnet build OpenCRM.sln
```

## Further configuration

- [Application configuration](configuration.md) describes value sources, main sections, and optional integrations.
- [Shared brand file](../configuration/README.md) describes `configuration/brand.json` and server-side brand overrides.
- [Application architecture](architecture-current.md) helps locate existing modules and product customization points.

Restart the affected processes after changing configuration.

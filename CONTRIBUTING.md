# Developing OpenCRM

**English** | [Русский](CONTRIBUTING.ru.md)

Develop improvements to the shared CRM foundation in the core; keep features specific to a website or product in your own customization layer. The [extension guide](docs/extending-opencrm.md) describes the separation and update workflow.

## Preparation

1. Complete the [local setup](docs/local-run.md).
2. Read the [architecture](docs/architecture-current.md), the nearest `AGENTS.md`, and the `.editorconfig` for the component you will change.
3. Find an existing module, contract, UI component, or adapter with the appropriate responsibility.

Integration tests require Docker: they use isolated PostgreSQL and RabbitMQ instances. They do not require secrets or real credentials.

## Where to make changes

| Change | Area |
| --- | --- |
| CRM business rules and operations | `src/OpenCRM.Modules.*` |
| Cross-module APIs and DTOs | The corresponding `*.Contracts` projects |
| Storage, databases, providers | `src/OpenCRM.Infrastructure.*` |
| HTTP layer, pages, module registration | `src/OpenCRM.WebApp` |
| Client screens | `src/OpenCRM.Frontend/Scenarios` |
| Shared interface | `src/OpenCRM.Frontend/packages/ui`, `packages/workspace` |
| Administration applications | `src/OpenCRM.Frontend/Admin` |
| Microservices | `services/notifications`, `services/payments` |

Reuse contracts and components. Preserve roles, permission checks, organization isolation, and existing data. Do not enable external integrations or commercial restrictions in the base mode without the corresponding configuration.

## Validation

The full solution includes the Windows WPF Launcher. Run these solution-wide commands on Windows from the repository root:

```bash
dotnet tool restore
dotnet restore OpenCRM.sln
dotnet build OpenCRM.sln
dotnet test OpenCRM.sln --no-restore -p:BuildScenarioClientsOnBuild=false
```

The main solution includes backend test projects, including the services. A normal build also builds the client applications; first run `npm ci` in `src/OpenCRM.Frontend`, as described in the setup guide.

For backend development on Linux/macOS, build the web project and run the individual module test projects; validate the Windows Launcher on Windows. For example:

```bash
dotnet restore src/OpenCRM.WebApp/OpenCRM.WebApp.csproj
dotnet build src/OpenCRM.WebApp/OpenCRM.WebApp.csproj
```

When changing an individual module, you can run its test project:

```bash
dotnet test tests/OpenCRM.Modules.Clients.Tests/OpenCRM.Modules.Clients.Tests.csproj -p:BuildScenarioClientsOnBuild=false
```

Backend checks in module projects are separated into `Unit` and `Integration`. A full run must include both groups. Unit tests alone do not replace checks of HTTP, databases, the broker, and document rendering.

Frontend, Bash:

```bash
cd src/OpenCRM.Frontend
node --test tests/*.test.mjs
npm run build
cd ../..
```

Frontend, PowerShell:

```powershell
Push-Location src/OpenCRM.Frontend
node --test (Get-ChildItem tests/*.test.mjs | ForEach-Object FullName)
npm run build
Pop-Location
```

To check backend boundaries, run the following in PowerShell 7:

```powershell
./scripts/assert-backend-boundaries.ps1
```

Choose additional checks based on the risks of your change. For UI changes, check keyboard access, narrow screens, and light/dark themes; for access control, test the owner, a member without the required permission, and an unrelated user.

## Modules, projects, and data

- Include each new backend test project in `OpenCRM.sln`, the `Tests` solution folder, and both build configurations.
- Keep a module's tests in its own project and shared fixtures in the shared testing library, without references between test projects.
- Accompany database model changes with a consistent migration. Check metadata, the main file, designer, and snapshot against neighboring migrations; build and validate on a separate database.
- Plan backups, production database updates, and rollback separately from application compilation.
- Test data must be fictitious, valid, and distinguishable. Accidentally clearing passwords, tokens, or unique phone numbers can invalidate the purpose of tests.
- Preserve the contract under test when fixing a test. Do not replace meaningful assertions without a reason or conceal a problem by adding a delay.

## Documentation languages

English is the default language for project documentation. Each guide has a Russian version next to it: `guide.md` and `guide.ru.md`. Keep both versions synchronized when changing behavior, configuration, or instructions.

- Retain the `English | Русский` links below the document title.
- Link to the matching language version of a guide and update heading anchors when translating titles.
- Share screenshots and other binary assets across both versions; translate captions rather than duplicating images.
- Preserve commands, API identifiers, configuration keys, migration IDs, and executable examples. Russian UI labels or voice examples may remain quoted with an English explanation.
- Translate diagram labels while preserving identifiers and relationships; standalone PlantUML sources use paired `.puml` and `.ru.puml` files.
- Do not translate or change third-party license texts or operational `AGENTS.md` instructions as part of guide translation.

## Before submitting a change

Describe the problem and resulting behavior. List the checks you ran and what remains unverified. Update the relevant module documentation when you change a contract, configuration, or startup procedure.

Preserve encoding and line endings. Write comments in newly added code in Russian. Do not include secrets, settings from your own running installation, build output, or temporary reports in your changes.

Retain the [license](LICENSE), copyright notice, and visible “Based on [OpenCRM](https://github.com/eddchervov/OpenCRM)” attribution. The interface does not need the author's name or profile link; author information remains in the core README and license.

With Python 3 installed, validate local documentation links, heading anchors, language pairs, and screenshot paths from the repository root:

```powershell
python scripts/check-documentation.py
```

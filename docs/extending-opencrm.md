# Build your product on OpenCRM

**English** | [Русский](extending-opencrm.ru.md)

OpenCRM is a shared CRM foundation. Start with the available settings, then add your product's differences while keeping shared fixes in the core.

## Choose the level of change

| Requirement | Where to change it |
| --- | --- |
| Name, installation URL, connections, providers | [Configuration](configuration.md), [branding](../configuration/README.md) |
| Notifications, payments, and background operations | [Separate microservices](microservices.md) |
| Your own help articles | Implement `IOrganizationWikiContentProvider` and register it in the host |
| A custom screen or scenario | Client application and shared `packages/ui`, `packages/workspace` |
| A shared business capability or fix | The relevant `OpenCRM.Modules.*` module and its tests |
| A completely new module | Project, contracts, explicit host registration, client route, and tests |

## What is already supported

Installation settings are separate from application code. Business modules, contracts, infrastructure implementations, and shared UI components reside in separate projects and packages. Standard dependency injection lets you replace supported implementations when composing the host.

The reusable [OpenCRM.Hosting](../src/OpenCRM.Hosting/OpenCRM.Hosting.csproj) Razor library compiles the shared controllers, services, and views from the existing WebApp source tree. The neutral [Program.cs](../src/OpenCRM.WebApp/Program.cs) delegates to `OpenCRMApplication.ConfigureBuilder(builder, args)` and `ConfigureApplication(app)`. A product host can call the same composition and register its own services before `builder.Build()`. Automatic plugin discovery and a separately published NuGet extension SDK are not provided.

## A product host and one source tree

A product application references the [shared hosting library](../src/OpenCRM.Hosting/OpenCRM.Hosting.csproj), not the neutral executable. The [product-host MSBuild import](../build/OpenCRM.ProductHost.targets) accepts one `OpenCRMRoot`, adds the shared project and static resources, and builds all CRM clients from that source tree. The consumer supplies its own `brand.json`, `appsettings.json`, and entry point. Client files are rebuilt before the .NET SDK discovers static assets. `dotnet publish --no-build` uses the previously built assets and does not rebuild frontend; build the product first after frontend changes.

The import copies core defaults as `opencrm.defaults.json`. Load that file before the product's settings, then reapply environment variables and command-line arguments. Register product services after the common builder configuration; apply the common pipeline to the built application. The neutral application remains independently runnable.

For local development, `OpenCRMRoot` may select a neighboring checkout. For reproducible CI and releases, use a pinned source revision inside the product repository. Backend and frontend must use the same root; do not maintain a second editable copy of shared CRM code.

`OPENCRM_FRONTEND_OUTPUT_ROOT` is an absolute path to the consumer's `wwwroot`. The import passes it to Vite, preserving the public `/clients/*`, `/admin-panel`, and `/app` URLs. Without it, `npm run build` retains the neutral application's existing output paths. Each host needs its own physical client files because Razor reads their manifests from `WebRootPath`.

A product replacing the neutral home controller must explicitly exclude that controller from MVC discovery and add its own application part. A new controller with the same name alone can create ambiguous routes. Test compiled Razor views, CSS/JS, and both administration entry points from the published output.

When running two hosts on localhost, use distinct cookie names and key directories; different ports alone do not isolate cookies. `DataProtection:ApplicationName` is configurable and defaults to `OpenCRM.WebApp`. Preserve an existing installation's discriminator and keyring when upgrading. Keep build outputs separate and avoid concurrent writes to shared intermediate files.

## 1. Start with configuration

Configure your own values without changing shared behavior:

- [`configuration/brand.json`](../configuration/brand.json) defines branding and public URLs.
- Supply connection strings and secrets to the relevant process through environment variables or a secret store.
- Enable email, AI, and payment infrastructure as needed.
- `PlatformPolicy__Commercial__Enabled` controls the optional commercial mode.

The server supports `Brand__DisplayName` and `Brand__PublicBaseUrl`; desktop applications use the brand file. Keep settings consistent across all processes in your distribution. Changing branding does not remove the required OpenCRM core attribution.

See [configuration](configuration.md) for details and configuration source precedence.

## 2. Separate your version from upstream

Create your own fork/repository for a separate product. In its clone, `origin` should point to your product and `upstream` to OpenCRM.

```bash
git remote add upstream https://github.com/eddchervov/OpenCRM.git
git remote -v
```

If `upstream` is already configured, do not add it again. These commands apply to your product's clone.

Keep changes small and separate:

- Deployment configuration and secrets belong outside the shared repository.
- Put product-specific classes in their own files/namespaces and, when appropriate, projects.
- Build your screens as product components reusing shared UI packages.
- Make shared fixes in the relevant OpenCRM modules, avoiding a second manual fix of the same logic in the product.

Updating from upstream is the current practical mechanism for receiving shared logic. A separately packaged, ready-made NuGet core SDK is not provided.

## 3. Use existing extension points

### Help content

The [`IOrganizationWikiContentProvider`](../src/OpenCRM.WebApp/Models/Organizations/IOrganizationWikiContentProvider.cs) contract describes content availability, search, and article retrieval. The base distribution registers an empty provider; the help item is hidden when no content is available.

The contract and article models keep their `OpenCRM.WebApp.Models.Organizations` namespaces and are compiled into `OpenCRM.Hosting`. Implement the provider in the product host or a product library referencing Hosting. The shared library must not reference the product back; this keeps dependency direction one way.

For an implemented `ProductWikiContentProvider`, replace the registration after `OpenCRMApplication.ConfigureBuilder(builder, args)` and before `builder.Build()` in the product entry point:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenCRM.WebApp.Models.Organizations;

builder.Services.Replace(ServiceDescriptor.Singleton<
    IOrganizationWikiContentProvider,
    ProductWikiContentProvider>());
```

This illustrates connecting a class in the existing host. You must implement the class itself; placing a DLL in the application directory without registration does not connect it. Account for the singleton lifetime and thread safety.

### Interface

- [`packages/ui`](../src/OpenCRM.Frontend/packages/ui) contains shared UI elements.
- [`packages/workspace`](../src/OpenCRM.Frontend/packages/workspace) contains the workspace and CRM components.
- [`Scenarios`](../src/OpenCRM.Frontend/Scenarios) contains client applications.
- [`Admin`](../src/OpenCRM.Frontend/Admin) contains administration applications.

Reuse existing components, theme styles, and permission handling. Use the shared `UiDatePicker` and `UiTimePicker` for dates and times. After changing client components, build the workspaces and check the relevant scenarios, narrow screens, and both themes.

### Commercial mode

Use the supported configuration to enable the standard mode, then configure your own plans, prices, and integrations. Part of the business logic uses [`IPlatformCommercialPolicy`](../src/OpenCRM.Modules.Billing.Contracts/IPlatformCommercialPolicy.cs). Other components, including HTTP filters, payment registration, and background jobs, also read `PlatformPolicy:Commercial:Enabled` directly. Replacing the interface alone therefore does not switch the entire installation: use the standard setting and verify consistency in both modes.

Preserve membership, roles, permission checks, and technical limits regardless of commercial settings.

### Providers and your own business logic

First check whether an existing integration supports the protocol and settings you need. A new protocol requires an adapter implementation and explicit DI registration. Infrastructure registrations reside in `src/OpenCRM.Infrastructure.*` and `*ServiceCollectionExtensions.cs` files.

When adding a module, define its responsibility, public contracts, registration, and tests. Interact through neighboring modules' contracts. If new tables are needed, design data ownership and migrations; arbitrary entities are not discovered automatically.

## 4. Receive core updates

In your product's clone, save your working changes and create a separate update branch:

```bash
git fetch upstream
git switch -c update/opencrm-core
git merge upstream/main
```

After merging:

1. Resolve conflicts while preserving product settings and core checks.
2. Restore dependencies, build, and run appropriate [tests](../CONTRIBUTING.md#validation).
3. Validate migrations on a separate database and check your installation's main scenarios.
4. Only then include the update in your product's release.

Conflicts are possible, especially when the same shared files have changed. Small product changes in separate components simplify updates. Fix a shared bug in OpenCRM, then bring the fix into your product through upstream.

## Attribution

You may change your product's name, appearance, and functionality. Retain the [license terms](../LICENSE) and visible “Based on [OpenCRM](https://github.com/eddchervov/OpenCRM)” attribution on the website/web interface, including commercial and closed-source customizations.

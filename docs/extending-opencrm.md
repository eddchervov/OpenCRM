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

The application is currently explicitly composed in [Program.cs](../src/OpenCRM.WebApp/Program.cs) and [service registrations](../src/OpenCRM.WebApp/Extensions/ServiceCollectionExtensions.cs). Add a new extension to the source/projects, register it, and build it. Automatic plugin loading, a standalone extension SDK, and a ready-made separate product host have not yet been implemented.

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

The contract and article model currently reside in WebApp. A practical approach is to implement the provider in a product-specific folder/namespace within WebApp and replace its registration after the host's standard service configuration. Moving this class directly into a project that references WebApp and is simultaneously referenced by WebApp creates a circular dependency; first change the contract/composition boundaries separately if you need this arrangement.

For a `ProductWikiContentProvider` already implemented within the host, replace the provider registration in `ConfigureBuilder` in `Program.cs`, after the standard presentation service registration:

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

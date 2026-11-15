# Accounts module: technical design

**English** | [Русский](design.ru.md)

> Historical document for a completed extraction. Old stages, commands, and intermediate statuses are not current assignments. Open work is listed in the [index](../README.md); current boundaries are described in the current architecture.

Model clarification: an account has no mode; personal vehicles are available to everyone, while type and permissions belong to the selected organization. Checks and automatic writes of `AccountMode` have been removed from user workflows. Legacy profile fields and the v1 API form remain for compatibility and do not determine access; obsolete profile PUT and mode-selection POST requests no longer change state. The organization type catalog remains active. The design below records the original module extraction, not the former separation of users into modes.

Decision for extracting `OpenCRM.Modules.Accounts`. It follows the specification (`spec.md`): the module owns its implementation and registers its own services; contracts remain shared. There is exactly one separate architectural choice here: replacing the static registration dependency on notifications, described in section 3.

## 1. Projects

| Project | Path | Solution folder | Purpose |
| --- | --- | --- | --- |
| `OpenCRM.Modules.Accounts` | `src/OpenCRM.Modules.Accounts/OpenCRM.Modules.Accounts.csproj` | Backend → Application | Account profile and scenario implementations |
| `OpenCRM.Modules.Accounts.Tests` | `tests/OpenCRM.Modules.Accounts.Tests/OpenCRM.Modules.Accounts.Tests.csproj` | Tests | Unit/integration module checks |

No new `*.Contracts` project is created. The module project targets `net10.0`, with `ImplicitUsings` and `Nullable`, as in `OpenCRM.Modules.Workspace`.

The implementation structure follows the domain folders of the original BL:

```
src/OpenCRM.Modules.Accounts/
  AccountProfiles/AccountProfileService.cs
  AccountProfiles/AccountProfileAdminService.cs
  Users/UsersService.cs
  Users/AccountRegistrationService.cs
  Users/AccountDeletionService.cs
  Users/EmailCodeLoginService.cs
  AccountsServiceCollectionExtensions.cs
```

Namespaces are `OpenCRM.Modules.Accounts.AccountProfiles` and `OpenCRM.Modules.Accounts.Users`, matching their folders as in other modules. Public implementation types remain public, as in `OpenCRM.Modules.Workspace`; private nested types such as `EmailLoginCodeCacheEntry` remain private. Method bodies and message text do not need changes.

## 2. Dependencies and registration

Project references for the module: `OpenCRM.Domain`, `OpenCRM.Infrastructure.Data`, and `OpenCRM.Services.Interfaces` (`Helpers` only if a confirmed need arises). Declare packages explicitly based on the files actually moved, using versions from the 10.0.x family compatible with the already resolved graph: `Microsoft.Extensions.Identity.Core` 10.0.7 (`UserManager<T>`, `IdentityResult`), `Microsoft.EntityFrameworkCore` (`Include`/`AsNoTracking`/`ToListAsync`), `Microsoft.Extensions.Caching.Memory` (`IMemoryCache`), and `Microsoft.Extensions.Logging.Abstractions` (`ILogger<T>`). A transitive package reference does not replace an explicit declaration; do not add unused packages.

References to `OpenCRM.Infrastructure.BL`, another module's implementation, and `WebApp` are prohibited.

`AccountsServiceCollectionExtensions.AddOpenCRMAccounts` registers exactly six services with their previous lifetimes:

```
AddTransient<IUsersService, UsersService>();
AddScoped<IAccountRegistrationService, AccountRegistrationService>();
AddScoped<IAccountDeletionService, AccountDeletionService>();
AddScoped<IEmailCodeLoginService, EmailCodeLoginService>();
AddScoped<IAccountProfileService, AccountProfileService>();
AddScoped<IAccountProfileAdminService, AccountProfileAdminService>();
```

Remove these six registrations from `BusinessServiceCollectionExtensions.AddOpenCRMBusinessServices` (`Infrastructure.BL`); do not affect other BL registrations. `IApplicationUnitOfWork`, `IPromoCodeService`, `IEmailNotificationService`, `ISiteNotificationPublisher`, `IMemoryCache`, and `ILogger<T>` enter the module as external dependencies owned by existing components.

WebApp calls `AddOpenCRMAccounts()` alongside the other module registrations in `Extensions/ServiceCollectionExtensions.AddOpenCRMInfrastructure`.

## 3. Registration notifications: narrow contract instead of static call

Before: `AccountRegistrationService` calls `OpenCRM.Infrastructure.BL.Notifications.SiteNotificationPublisher.AddRegistrationNotificationsAsync(context, user)` inside the registration transaction.

After:

- `OpenCRM.Services.Interfaces.Notifications.ISiteNotificationPublisher` gains `Task AddRegistrationNotificationsAsync(ApplicationUser user)`. `ApplicationUser` is available because the contracts project already references `OpenCRM.Domain`. The entity parameter is deliberate: it matches the current static-call argument, so the owner's algorithm is not rewritten and values (`Id`, `NormalizedEmail`) are not fetched again.
- The notification owner retains the implementation: `SiteNotificationPublisherAdapter` (BL) implements the method by delegating to the existing `SiteNotificationPublisher.AddRegistrationNotificationsAsync(context, user)`. The welcome and pending-invitation notification algorithm, event keys `welcome:{userId}` and `invitation:{id}:{token}`, text, field truncation, and internal `SaveChangesAsync` remain in place.
- The module receives `ISiteNotificationPublisher` through its constructor and invokes it at the same step as the previous algorithm: after profile `SaveAsync` and before applying a promo code.

Transaction boundaries: the adapter receives the same scoped `OpenCRMContext` (one DI container per request), works inside the already-open `IApplicationUnitOfWork` transaction, and does not open its own transaction. No messages or emails are sent before commit; no hidden `SaveChanges` calls are added beyond the existing one. Direct writes to `TelegramNotificationOutbox` through `OpenCRM.Infrastructure.Data.Notifications.TelegramAdminNotificationPublisher` remain; this is an allowed dependency on shared infrastructure and does not change behavior.

Adding a method to the interface also requires updating the existing `DatabaseSiteNotificationPublisher` stub in `tests/OpenCRM.Modules.Organizations.Tests/Integration/OrganizationsDatabaseFixture.cs`; otherwise the Organizations test project will no longer compile.

## 4. Integration points

- `OpenCRM.sln`: add the new module under Backend → Application and the test project under Tests, with all 12 Debug/Release × Any CPU/x64/x86 configurations as in `OpenCRM.Modules.Workspace`.
- `src/Dockerfile`: add a `COPY` for the new module csproj before restore; publishing obtains its assembly through the WebApp `ProjectReference`.
- `src/OpenCRM.WebApp/OpenCRM.WebApp.csproj`: add a project reference to `OpenCRM.Modules.Accounts` (without it, `AddOpenCRMAccounts` does not compile), as for other modules.
- `tests/Accounts.runsettings`: include the `[OpenCRM.Modules.Accounts]*` package and parallelism as in `Workspace.runsettings`.
- `scripts/assert-backend-boundaries.ps1`: existing checks already cover the new projects for “no cycles”, “module does not reference BL/another implementation”, and “tests reside in Tests and in both builds”; run it unchanged and require it to pass on the new graph.

Intermediate build state: while module implementations are moved but dependent checks in `OpenCRM.Infrastructure.Tests` still reference old BL namespaces, the full solution build is red. This is expected during extraction, not a defect. Build affected projects as files move; `dotnet build OpenCRM.sln` must pass after the tests are migrated.

## 5. Module tests

`OpenCRM.Modules.Accounts.Tests`: target `net10.0`, `IsPackable=false`, packages matching `OpenCRM.Modules.Workspace.Tests` (Testcontainers.PostgreSql, Microsoft.AspNetCore.TestHost, Moq, Microsoft.NET.Test.Sdk, coverlet.collector, xunit, xunit.runner.visualstudio); references to `OpenCRM.Modules.Accounts`, `OpenCRM.WebApp`, `OpenCRM.Testing`, `OpenCRM.Infrastructure.Migrations`, and `tests/xunit.runner.json`. No references to other test projects. Physical `Unit` and `Integration` folders use matching namespaces.

Move checks for the module implementation:

- `tests/OpenCRM.Modules.Accounts.Tests/Integration/AccountProfileRemovalTests.cs` → `Integration` (uses real `AccountProfileService`, TestServer, and PostgreSQL);
- `tests/OpenCRM.Modules.Accounts.Tests/Integration/RegistrationErrorTests.cs` → `Integration` (uses real `AccountRegistrationService` and the registration form).

Keep checks unrelated to the module implementation in place: `RegistrationTransactionTests`, `RegistrationAttributionTests` (WebApp/`RegistrationAttribution`), `AccountProfileModeTests` (Domain), `AuthMvcAcceptanceTests`, `AdminAuthenticationApiTests`, and tests in other modules that substitute `Services.Interfaces` interfaces. Contracts do not move, so their stub registrations remain.

Use coverage to find untested areas; no percentage is required. Meaningful scenarios (minimum):

- registration: creating a user with and without a password, consents and their timestamps, profile mode and organization type, promo code, attribution, welcome and pending-invitation notifications, Telegram outbox row; Identity rejection, transaction rollback, and no notifications after rollback;
- profile: reading and saving mode, organization-type fallback, recommended and enabled capabilities, reset with an empty mode, catalog cache and cache reset;
- admin profiles and capabilities: create/update/delete, unique codes, errors with existing text, result ordering;
- email-code sign-in: invalid, expired, and reused code, attempt limit, one-minute anti-spam delay, email-send failure, consent for registration, account creation, email confirmation, deleted account (by email and `OriginalEmail`);
- deletion: masking email and UserName, `OriginalEmail`, `IsDeleted`/`DeletedAt`, removing external logins, return value, Telegram outbox row;
- user records and statistics: sign-in/visit, counters, personal area, non-deleted records, finding deleted original email, exclusion for an unknown user;
- HTTP/DI: account-profile routes through TestServer, `AddOpenCRMAccounts` registrations (scoped and transient, uniqueness), no project-level dependency on BL.

Integration checks use real Identity, PostgreSQL (Testcontainers), and TestServer, substituting only external email delivery. They do not connect to working or production databases.

## 6. Checks and reproduction

```powershell
dotnet build OpenCRM.sln -m:1 -p:BuildScenarioClientsOnBuild=false
dotnet test tests/OpenCRM.Modules.Accounts.Tests/OpenCRM.Modules.Accounts.Tests.csproj -m:1 -p:BuildScenarioClientsOnBuild=false -p:BuildScenarioClientsOnPublish=false --settings tests/Accounts.runsettings --collect "XPlat Code Coverage" --logger trx
./scripts/assert-module-coverage.ps1 -ModuleName Accounts -ReportPath <coverage.cobertura.xml>
./scripts/assert-backend-boundaries.ps1
dotnet sln OpenCRM.sln list
```

Run targeted suites during implementation and the full backend regression after the final changes. Put reports and TRX files in an isolated directory (the `--artifacts-path` argument) and disable client builds. Controller checks use argv commands without a shell, so use the solution build and targeted module suites within the time limit.

## 7. Risks and decision boundaries

The extraction does not change the database schema, migrations, HTTP contracts, authorization rules, or transaction boundaries. The only new connection is calling `ISiteNotificationPublisher` instead of the owner's static method; the same scoped context and transaction preserve registration atomicity. If implementation shows that preserving behavior requires changing public contracts, the database schema, access rules, or opening separate transactions, stop and raise a separate decision: that scenario is outside this stage.

## Final implementation clarifications

Do not add unused `InternalsVisibleTo`: tests use public contracts and the module has no internal types. Email-code login accepts an optional `TimeProvider` with `System` as the default so expiry and resend timing can be tested deterministically. Integration uses a local `AccountsDatabaseFixture` with PostgreSQL and real migrations only; RabbitMQ and EF InMemory are not needed for these checks. Detailed results are in the [guide](../README.md).

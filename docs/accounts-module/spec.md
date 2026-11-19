# Accounts module: extraction specification

**English** | [Русский](spec.ru.md) | [简体中文](spec.zh-CN.md)

> **Model change.** The extraction described below is a historical specification. Account scenarios, modes, recommendations, and capabilities, along with their API and storage, have been removed. Accounts owns registration, sign-in, user deletion, and personal records. The organization type catalog belongs to Organizations. Former requirements for saving account profiles and registering their services are no longer current. The current model, database preparation, and recovery process are in [profile removal](profile-removal.md).

## 1. Goal

Extract `OpenCRM.Modules.Accounts` as a separate application module from `OpenCRM.Infrastructure.BL`. It owns the account profile and account application scenarios and registers its own services. The module does not depend on `OpenCRM.Infrastructure.BL` or other module implementations. Public contracts and HTTP behavior remain unchanged.

## 2. Extraction scope

Move these six implementations out of `OpenCRM.Infrastructure.BL`:

| File | Implements | Lifetime |
| --- | --- | --- |
| `AccountProfiles/AccountProfileService.cs` | `IAccountProfileService` | Scoped |
| `AccountProfiles/AccountProfileAdminService.cs` | `IAccountProfileAdminService` | Scoped |
| `Users/UsersService.cs` | `IUsersService` | Transient |
| `Users/AccountRegistrationService.cs` | `IAccountRegistrationService` | Scoped |
| `Users/AccountDeletionService.cs` | `IAccountDeletionService` | Scoped |
| `Users/EmailCodeLoginService.cs` | `IEmailCodeLoginService` | Scoped |

These components are not moved and remain with their owners:

- `Admin/AdminUsersService.cs` and administrative user queries;
- billing, subscriptions, and promo codes (`IPromoCodeService`);
- email sending (`IEmailNotificationService`), notification delivery, and queue handlers;
- unit of work `IApplicationUnitOfWork`/`ApplicationUnitOfWork`;
- Identity adapters, cookies, CSRF, Views, callback URLs, and redirects in `WebApp`.

The module uses their existing interfaces as external ports.

## 3. Contracts

- Public contracts and DTOs remain in `OpenCRM.Services.Interfaces` (`Users`, `AccountProfiles`).
- Do not create a separate `OpenCRM.Modules.Accounts.Contracts`: there is no consumer of those contracts outside `Services.Interfaces`, and existing consumers already depend on that project.
- Keep contract namespaces, DTOs, and JSON fields unchanged so consumers (WebApp, `Auth`, modules, and tests) continue using the same interfaces.

## 4. Module dependency boundaries

Allowed project references: `OpenCRM.Domain`, `OpenCRM.Infrastructure.Data`, and `OpenCRM.Services.Interfaces`; use `Helpers` only if there is a confirmed actual need. Include only packages actually used; `UserManager<ApplicationUser>`/`IdentityResult` may use the Identity package (version family 10.0.7, as in neighboring projects). Do not add unused references or packages.

Prohibited: a reference to `OpenCRM.Infrastructure.BL`, an implementation in another module, or `OpenCRM.WebApp`. No `*.Contracts` references are needed for this task.

Service registration belongs to the module through public `AddOpenCRMAccounts(this IServiceCollection)`. Remove the six former registrations from `BusinessServiceCollectionExtensions`, preserving their lifetimes (see the table above). WebApp calls `AddOpenCRMAccounts` with the other module registrations.

## 5. Removing the notification dependency

`AccountRegistrationService` directly calls the static `OpenCRM.Infrastructure.BL.Notifications.SiteNotificationPublisher.AddRegistrationNotificationsAsync(context, user)`. This dependency cannot move as-is: the module cannot reference BL, and copying the welcome and invitation notification algorithm into Accounts is prohibited.

Use a narrow contract owned by notifications:

- Extend the existing `ISiteNotificationPublisher` (`OpenCRM.Services.Interfaces.Notifications`) with a method to add notifications for a registered user (`AddRegistrationNotificationsAsync(ApplicationUser user)`). Use the same argument as the static method so its algorithm and data do not need rewriting.
- Keep the implementation with the notification owner: the existing BL adapter `SiteNotificationPublisherAdapter` delegates to the previous `SiteNotificationPublisher` algorithm. Do not copy the algorithm into Accounts.
- The module calls only the interface, receiving the same scoped `OpenCRMContext` through DI.

Preserve the write order (welcome notification followed by pending-invitation notifications), event keys, text, title/body truncation, the algorithm's internal `SaveChangesAsync`, and participation in the current registration transaction. Do not introduce new transactions, separate contexts, deferred sends, or sends before commit.

The direct write to `TelegramNotificationOutbox` through `OpenCRM.Infrastructure.Data.Notifications.TelegramAdminNotificationPublisher` (a static method over the shared `OpenCRMContext`) remains: `OpenCRM.Infrastructure.Data` is shared infrastructure permitted for the module, and changing this call is outside scope and does not alter behavior.

## 6. Behavior to preserve

Required behavior, checked against source code and tests:

- **Registration.** Atomic execution in one `IApplicationUnitOfWork.ExecuteInTransactionAsync` transaction: `UserManager.CreateAsync` (with or without a password), personal-data and advertising consent with timestamps, attribution, `IAccountProfileService.SaveAsync` (mode and organization type), welcome and invitation notifications, Telegram outbox row, promo-code application through `RedeemForRegistrationAsync`; on Identity rejection, rollback and no notifications; `AccountCreationResult(User, Errors)` with existing error text. **The source specification is truncated at `EmailConfirmed = request.Password =`; consult the Russian source and implementation for that condition.**
- **Profile mode and organization type.** `personal`/`organization`, `ServiceStation` for organization registration, `personal` for personal mode, selection of an available type (requested → current → Service Station → first available), recommended capabilities, clearing the profile with an empty mode, one-day catalog cache and `ClearCatalogCache`.
- **Administrative profiles and capabilities.** Unique codes, `InvalidOperationException` with existing messages, DTO order and composition, cache reset after changes.
- **Email-code sign-in.** Code length 8, limit of 5 attempts, one-hour expiry, cache key `auth:email-code:{EMAIL}`, one-minute anti-spam window, SHA-256 of `{EMAIL}|{code}|OpenCRM.EmailLoginCode.v1`, constant-time comparison, deletion of the entry after email-send failure or exhausted attempts, existing error text and `ErrorField`, `RequiresRegistrationConsent`, account creation for a valid registration code, and email confirmation for an existing user.
- **Account deletion.** Mask email and UserName as `deleted_{yyyyMMddHHmmss}_{Id}@deleted.local`, preserve `OriginalEmail`, set `IsDeleted`/`DeletedAt`, remove external logins, write a Telegram outbox row, return `originalEmail`, participate in one transaction.
- **User records.** `DateTime.Now.ToUniversalTime()`, visit and sign-in counters, personal statistics (personal area and non-deleted records), exception text “Не найден пользователь с id=…” (“User with id=… was not found”), and lookup by a previously deleted original email.

## 7. Compatibility

- Do not change HTTP routes, parameters, JSON, status codes, messages, or public contracts.
- Keep `UserManager`/`SignInManager`, cookies, CSRF, Views, callback URLs, and redirects in WebApp; do not rewrite `AuthController` to prohibit Identity in the controller.
- Do not change the database schema, migrations, or product rules.

## 8. Checks and acceptance criteria

1. The six implementations reside in `OpenCRM.Modules.Accounts`; their former files and registrations are removed from `Infrastructure.BL`.
2. `AddOpenCRMAccounts` registers the same lifetimes and is called by WebApp; there is no Accounts → BL project dependency; contracts remain in `OpenCRM.Services.Interfaces`.
3. The static registration dependency on `Infrastructure.BL.Notifications.SiteNotificationPublisher` is removed through a narrow contract and owner adapter; the same scoped `OpenCRMContext` and transaction are used, with no separate transaction or pre-commit send.
4. Routes, JSON, results, error messages, and public contracts are preserved; consent, attribution, promo-code, profile-mode, organization-type, capability, email-code expiry/limit, deleted-account masking, `OriginalEmail`, and external-login rules remain unchanged.
5. `OpenCRM.Modules.Accounts.Tests` is created in the solution's `Tests` folder and added to `OpenCRM.sln` for Debug/Release, with physical `Unit`/`Integration` folders and no references between test projects. Appropriate existing tests are moved; shared fixtures remain in `OpenCRM.Testing`.
6. The module is added to `OpenCRM.sln`, `src/Dockerfile` (restore/publish), the module coverage CI matrix, and `scripts/assert-backend-boundaries.ps1`; Debug/Release solution builds pass.
7. Integration checks use real Identity/PostgreSQL/TestServer and substitute external email sending: registration rejection and rollback, no notifications after rollback, deleted account, invalid/expired/reused code, profiles, permissions, and data isolation.

## 9. Known limitations

- Local Docker/PostgreSQL may be unavailable. In that case, container checks must be recorded as not run rather than reported as successful.
- The Telegram outbox row remains a direct module dependency on shared `OpenCRM.Infrastructure.Data` (an allowed modular-monolith boundary with shared `OpenCRMContext` and database).
- Real email and message delivery, working and production databases, browser pilot, and server release are out of scope.

## Last-visit information

`identity.AspNetUsers` stores one updatable snapshot of the last visit: `LastAccessAt` (UTC), IP, User-Agent, Accept-Language, Sec-CH-UA, platform, mobile-client flag, and authentication scheme. Missing HTTP headers are stored as NULL; previous values are not carried over. Client headers are length-limited and are not treated as verified device attributes. Geolocation, cookie contents, tokens, and URL parameters are not included in this snapshot.

The snapshot is written after successful authentication, after successfully opening an MVC page with a saved session, and after successful API-session renewal with a refresh token. Background requests, static files, failed sign-ins, and administrator impersonation do not update it. Opening a page and refreshing a token do not increment `CountLogins` or change `LastLoginDate`; `LastLoginMethod` separately describes the last explicit authentication. Returning focus to an already-open tab without navigation does not count as another visit.

The IP is read from `Connection.RemoteIpAddress` after existing trusted-proxy handling. A failure to write a visit is logged and does not interrupt rendering the page or returning refreshed API tokens. Existing accounts are not backfilled retroactively.

Migration `20261021161413_AddUserLastAccessInfo` adds nullable columns only. DbContext/Migration metadata is in Designer in the project's EF Core 10.0.7 format; the snapshot is consistent. Down removes new visit and sign-in method information while retaining users and existing authentication fields. Save the new information first if it is needed before rollback. Check Up/Down on a separate PostgreSQL instance, not on a working database.

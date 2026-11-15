# OpenCRM Admin — Avalonia

**English** | [Русский](README.ru.md)

The AI assistant administration section controls availability, channels, models, budgets, organization access, audit, spending, and reconciliation of unknown costs. Configuration and spending permissions are checked separately; a settings version conflict requires the administrator to review again. See [assistant documentation](../../../../docs/ai-assistant.md).

Organization records support forced archiving/restoring of any undeleted organization with `Admin.Users.Manage`, a reason, and explicit confirmation. The existing 30-day archive cleanup period applies; after a lost response, new actions are blocked until status reconciliation. See [server contract and restrictions](../../../../docs/admin-organizations.md). These actions require a server API update independently of desktop deployment.

A standalone .NET 10/Avalonia desktop client for Windows and macOS: email/password login, read-only administrator profile, and dashboard. It uses a black theme with white text regardless of OS theme.

## Start

From the repository root:

```powershell
dotnet run --project src/OpenCRM.Frontend/Admin/OpenCRM.Admin.App
```

The API base URL is read from the shared `configuration/brand.json` copied beside the application. `OPENCRM_ADMIN_API_URL` overrides it. HTTPS is required except for loopback HTTP during local development:

```powershell
$env:OPENCRM_ADMIN_API_URL = 'https://localhost:7142/'
dotnet run --project src/OpenCRM.Frontend/Admin/OpenCRM.Admin.App
```

HTTP is allowed only for loopback addresses. HTTPS certificate validation is never disabled. The client uses HTTP/1.1. Server API settings, JWT signing keys, and database access are not passed to the desktop application.

The client uses the server authentication and administration APIs. The server remains responsible for authentication, roles, permissions, and data access.

## Sign-in and permissions

- `POST /api/v1/auth/login` issues a token pair after validating email/password.
- `GET /api/v1/admin/current-user` checks `Admin.Access` or a current `System` role on the server. On success, it opens the dashboard if allowed, otherwise the profile.
- The server checks administrative roles/permissions against the database on every request; removing a role or deleting a user blocks access with an old JWT.
- The client reloads profile/permissions every minute and through “Обновить” (Refresh) in the profile. Profile 401/403 closes the session; dashboard 403 closes the page and refreshes permissions.
- On Windows, the refresh token is protected with current-user DPAPI in `%LOCALAPPDATA%/OpenCRM/Admin/Sessions`, separately for each API URL. Passwords and access tokens are not saved. At startup, the client refreshes tokens and checks server profile/permissions. Moving the portable app to another computer or running as another Windows user requires signing in again. Session persistence on macOS is not yet supported.
- Expiring access tokens refresh through `POST /api/v1/auth/refresh`. After a 401, a profile/report read is retried once.
- Closing the window normally preserves sign-in. “Выйти” (Sign out) deletes the saved session and calls `POST /api/v1/auth/revoke`. An expired access token is refreshed before revocation. Without connectivity, server revocation is not guaranteed; the local session is deleted. Refresh-token expiry or authorization rejection deletes saved sign-in; a temporary network failure preserves it.
- Windows allows one desktop instance per user so parallel windows do not share a rotating refresh token.

The menu contains only sections permitted by the current account. Items marked “позже” (later) are placeholders and do not open working screens. The profile has no forms for changing email, roles, or permissions.

## Menu counters

User, request, and email history badges show new records since the corresponding section was last successfully viewed. First use counts records from the start of the current local day; afterward unread records persist across midnight and application restarts. Read markers are stored in `admin.NavigationReadStates` per account/section.

The payments badge shows the all-time queue of paid payments with unfinished processing or no uploaded receipt. Processing is complete at `completed`; a receipt exists when a file or URL is saved. While either condition is unmet, the payment remains queued. Unpaid, canceled, and fully refunded payments are excluded. Viewing the section neither changes the queue nor saves a read marker for it.

When loading users, requests, or email history, the client obtains a server time boundary through `GET /api/v1/admin/navigation/counts?unread=true`. Only after successfully loading the current screen does it acknowledge that boundary through `POST /api/v1/admin/navigation/sections/{section}/viewed`. A load error or cancellation does not clear the badge; events after the boundary remain new. Concurrent acknowledgments can only advance the marker. Counters refresh every minute and immediately after acknowledgment. The catalog proposal queue shows every pending moderation request regardless of viewing.

`/navigation/counts` without `unread=true` retains daily statistics for older clients. Unread events require an updated server and `AddAdminNavigationReadStates` migration; a server without `AsOfUtc` receives no viewed acknowledgments from the new client.

## Users

Requires `Admin.Users.View` or `System`. The table fills the remaining workspace, with fixed headers/pagination; default page size is 50, with 25/50/100 available. Email/phone search applies on “Найти” (Find) or Enter; page size and deleted-user switches apply immediately. “Сбросить” (Reset) clears search and restores initial sorting. There is no separate UTM filter.

Server sorting is triggered by user, vehicle, maintenance, organization, last login, registration, and UTM source headers; another click reverses direction. A header arrow shows the current order. Vehicle, maintenance, and organization counts appear separately immediately after the user, followed by last login and registration. UTM source is first-acquisition attribution, not an authentication method. Plans belong to organizations; user rows do not have a personal-plan column. Clicking the organization count opens their tab in the record, where each has its own role, status, and plan. The list does not load an overview per row.

Open a record through the row button, double-click, or Enter on the selected user. Returning preserves the page, selection, and search. Selected/hovered rows use dark backgrounds with contrasting text.

The record includes overview, organizations, subscriptions, payments, vehicles/maintenance, support requests, activity history, and individual features. Its header shows the user, status, login/registration dates, and login/organization counts. Tabs use high-contrast buttons; the overview has two columns on wide screens, other records use lists, and fields use compact columns or name/value rows.  The organizations tab shows only current membership/ownership, without separate invitation history cards. Local status selection is active by default, archived, or deleted and survives refresh. Organization entries show join dates; invitation date/author appear only if an accepted invitation matches the organization, user email, and join date. If the API supplies no matching invitation, other records are not substituted. When the commercial module is enabled, the subscriptions tab shows only organization subscriptions with active (default), completed, or all selected locally; selection survives refresh. Old personal plans/history are hidden. Personal vehicles and maintenance exclude vehicles with `OrganizationId` and show maintenance only for known personal vehicles. The API caps the overall selection at 30 vehicles and 20 maintenance records; the interface explicitly marks reaching a limit, and unknown ownership is not presented as personal. The timeline is collapsed. Fields can be selected/copied; timestamps use the local time zone. Financial tabs, payments, subscriptions, and plans are available only when commercial mode is enabled and the user has `Admin.Billing.View`. Record data loads only on opening or refresh, not a timer. Sign-out/access revocation clears data; navigation cancels requests. Refresh preserves the selected tab.

Uses existing `GET /api/v1/admin/users` and `/users/{userId}/overview`. The server limits some collections to recent records; tabs explicitly disclose when limits are reached. This section is read-only; it has no controls for editing users or impersonating them.

API limitation: `overview` requires `Admin.Users.View` and may include financial fields regardless of `Admin.Billing.View`. The desktop hides commercial data when access is unavailable, but the UI does not replace server-side authorization.

## Dashboard

Dashboard access requires `Admin.Dashboard.View` or the `System` role. The dashboard shows CRM data for the selected period (today, 7 days, or month): payments, new registrations and organizations, and maintenance activity. Financial cards, payment alerts, and payment totals are shown only to administrators with `Admin.Billing.View` or the `System` role. Public-site visits, search-provider reports, and advertising-source analytics are not shown in the OpenCRM dashboard.

The page refreshes automatically at the selected interval (5, 10, 30, or 60 seconds; default 60 seconds), or manually. The interval is stored in `%LOCALAPPDATA%/OpenCRM/Admin/dashboard-refresh.txt`. A separate “Load statistics” action loads overall CRM statistics. User request logging is controlled separately by `UserActivity:Enabled` and is disabled in the standard configuration. Requests stop when the dashboard is inactive or the application window is minimized; navigation and sign-out cancel pending requests.

The desktop client reads `GET /api/v1/admin/dashboard` and, when commercial billing is enabled, billing summary endpoints. It displays an update-required message if a required API contract is not available; it does not substitute all-time data for a period-specific result.

## Structure

- `OpenCRM.Admin.App`: XAML, ViewModel, shell, settings, and window lifecycle.
- `OpenCRM.Admin.Api`: HTTP, session, and small client DTOs without Domain, EF, or server service dependencies.
- `OpenCRM.Admin.App.Tests`: ViewModel and actual XAML screen tests through Avalonia Headless.
- `OpenCRM.Admin.Api.Tests`: Session, token refresh, rejection, and HTTP contract tests.

New projects reside in the `Frontend/Admin` solution folder. The old Chrome launcher `OpenCRM.Admin.Launcher` remains separate.

## Validation

```powershell
dotnet test tests/OpenCRM.Admin.Api.Tests
dotnet test tests/OpenCRM.Admin.App.Tests
dotnet test tests/OpenCRM.Infrastructure.Tests -p:BuildScenarioClientsOnBuild=false --filter 'FullyQualifiedName~AdminAuthenticationApiTests|FullyQualifiedName~AdminAuthorizationHttpTests'
```

The last command uses Docker, PostgreSQL, and RabbitMQ through the existing fixture. It tests real sign-in/profile controllers, Identity, JWT, ordinary-user rejection, administrative policies, token refresh, and revocation.

To capture actual XAML screens with test data:

```powershell
$env:OPENCRM_ADMIN_SCREENSHOTS = "$PWD/output/admin-desktop"
dotnet test tests/OpenCRM.Admin.App.Tests
```

These images show a test profile, not a production-server session.

## Payments and subscriptions

This section is available only when `PlatformPolicy:Commercial:Enabled=true` and the administrator has `Admin.Billing.View`. The commercial module is disabled by default; CRM use does not require a subscription. Payment and organization-subscription tabs use server pagination with 25/50/100 entries, search, statuses, and header sorting. Payments support today, 7/30 days, or all-time filtering. The summary's 300-payment cap does not apply to these lists. Future subscriptions have a separate Not started status.

Open, double-click, or Enter displays details/change history. Returning preserves the page; sign-out/access revocation clears data. Lists require new server endpoints `GET /api/v1/admin/billing/payments` and `GET /api/v1/admin/billing/subscriptions/organizations`. With an older server, the client shows an update-required message.

## OS builds

```powershell
dotnet publish src/OpenCRM.Frontend/Admin/OpenCRM.Admin.App -c Release -r win-x64 --self-contained true -o output/AdminApp/win-x64
dotnet publish src/OpenCRM.Frontend/Admin/OpenCRM.Admin.App -c Release -r osx-arm64 --self-contained true -o output/AdminApp/osx-arm64
```

Use `osx-x64` for Intel Macs. Building for macOS does not replace testing on a MacBook. The commands publish self-contained binaries. Signing, installers, and automatic updates are not included.

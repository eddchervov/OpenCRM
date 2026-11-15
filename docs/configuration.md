# Configuration

**English** | [Русский](configuration.ru.md)

OpenCRM reads standard .NET configuration: JSON sections use `Parent:Child`, while environment variables replace the colon with a double underscore, for example `ConnectionStrings__OpenCRMConnection`.

The web application loads the standard `appsettings.json` and `appsettings.{Environment}.json`, then the shared `brand.json`, environment variables, and command-line arguments. When keys overlap, a later source overrides an earlier one. Supply local values and secrets through the environment or development secrets rather than committing them to repository files.

## Main sections

| Section or key | Purpose and default behavior |
| --- | --- |
| `ConnectionStrings:OpenCRMConnection` | Connects the main CRM to PostgreSQL. Set `ConnectionStrings__OpenCRMConnection`; it must be configured for migrations and WebApp startup. |
| `Brand:DisplayName`, `Brand:PublicBaseUrl` | Application name and public URL. Server processes accept the corresponding `Brand__DisplayName` and `Brand__PublicBaseUrl` variables. Other brand fields are set in the shared JSON. See [configuration/README.md](../configuration/README.md). |
| `Administration:Sections` | Optional product links shared by both admin panels. Configure each link's `Title`, local absolute `Url`, and existing `Permission` in `brand.json`; the UI filters links by permissions, while the destination must still enforce authorization. |
| `AdminDesktop:*` | Optional absolute paths in `brand.json` for the desktop Chrome profile, pending payment actions, protected session, and dashboard refresh preferences. Missing values use built-in per-user locations. |
| `UserTracking:Enabled` | Defaults to `false`. Enables additional visit/login counters and client access snapshots. Basic last-login date and method and the separate activity audit are not controlled by this switch. |
| `Export1C:ProcessingDownloadUrl` | Optional local URL advertised with 1C export capabilities. External or invalid URLs are ignored. |
| `EmailConfirmation:Required` | Defaults to `false`. Registration in the base mode does not require email verification. If you enable verification, configure and connect email delivery separately. |
| `Notifications:Transport` | Defaults to `Disabled`. Basic sign-in and CRM work then require neither a broker nor the Worker. Delivery uses `RabbitMq` with a separate Notification Worker. `InMemory` is intended for isolated tests, not persistent delivery. |
| `RabbitMq:*` | Broker settings and ApplicationId for exchanging messages with the Worker. Broker settings alone are insufficient: the Worker must be running and configured for the same infrastructure. |
| `PlatformPolicy:Commercial:Enabled` | Defaults to `false`: the CRM runs without plan restrictions. `true` enables the commercial module and initialization of built-in plans. Review and configure prices, quotas, and terms for your installation; real purchases require separate payment infrastructure. |
| `Assistant:*`, `AssistantTelegram:*` | AI assistant and Telegram interaction settings. The assistant is disabled by default; enabling it requires your own provider and channel configuration. Do not commit provider keys in tracked JSON. |
| `PaymentsService:*`, `YooKassa:*` | Subscription integration with the payment service and YooKassa settings. The payment service is disabled by default; enabling it requires your own keys, return URLs, and a corresponding payment service. |
| `S3:*` | Object storage parameters. Credentials are configured separately and are unnecessary for basic local setup. |
| `IdentitySettings:*` | Password, sign-in, and account lockout policies. Values come from the corresponding appsettings section. |
| `Security:AllowedCorsOrigins` | Allowed browser request origins. For your deployment, specify the addresses of its client applications. |
| `DataProtection:ApplicationName`, `DataProtection:KeysDirectory` | Stable isolation identity and keyring. ApplicationName defaults to `OpenCRM.WebApp`; preserve both when updating an existing installation. |
| `ReverseProxy:KnownProxy` | The trusted reverse proxy IP is required in Production; this value is unnecessary for the local development profile. |

### Desktop administration paths

The desktop launcher and admin app read these optional values from the root `AdminDesktop` object in the same `brand.json` copied beside the applications:

| Key | Purpose |
| --- | --- |
| `ChromeProfileDirectory` | Chrome profile used by the desktop launcher. |
| `PendingActionsDirectory` | Local storage for a pending payment action when the server response is unknown. |
| `SessionDirectory` | Directory for the saved desktop admin session. On Windows, the session file is protected with DPAPI for the current user. |
| `DashboardPreferencesFile` | File that stores the dashboard refresh interval. |

Each non-empty path must be absolute after environment-variable expansion. If a value or the `AdminDesktop` section is missing, the application uses its built-in per-user path. These paths are local storage locations, not secret configuration; keep the session directory accessible only to the intended OS user.

`Administration:Sections` is also read from `brand.json`. Each entry has a display `Title`, a local absolute `Url` beginning with one `/`, and a `Permission` code, for example `Admin.Dashboard.View`. Query strings and fragments are not accepted. Both admin panels filter the list by the signed-in profile's permission or system-admin status. The web panel adds the request `PathBase`; the desktop panel opens the local route in the system browser and preserves the server base path. The browser performs its ordinary sign-in flow; no JWT or other credential is appended to the URL. This navigation metadata does not protect the destination: product endpoints must enforce their own authorization. See [building a product](extending-opencrm.md#product-links-in-both-admin-panels).

Provide environment variables to the process that uses each setting. WebApp settings do not replace the separate Worker's or payment service's settings.

## Secrets and local values

Keep connection strings, the RabbitMQ password, AI and payment provider keys, SMTP credentials, and object storage keys in the environment's secret store or local configuration excluded from Git. For development, use the current session's environment variables. `dotnet user-secrets` applies only to a project with a configured `UserSecretsId`; WebApp, the migrations project, and the services do not share a common ID. Supply secrets to the application that reads them.

`configuration/brand.json` is not a secret store. Server processes support `Brand__DisplayName` and `Brand__PublicBaseUrl`; desktop applications use JSON. Keep the brand file and URL consistent across processes in a shared deployment.

## Optional integrations

### Notifications and email

Normal registration works with `Notifications:Transport=Disabled` and `EmailConfirmation:Required=false`. The `Smtp:*` settings belong to the separate Notification Worker, rather than independently enabling sending from WebApp. See the [notification service documentation](../services/notifications/README.md) for the broker/Worker connection, SMTP, environment variables, and templates.

Verify the complete email delivery chain before requiring verification. Setting `EmailConfirmation:Required=true` does not configure an email server.

### Commercial module and payments

To enable commercial mode in WebApp, set `PlatformPolicy__Commercial__Enabled=true` and restart the application. Startup initializes the built-in commercial plans. Review and configure prices, quotas, and terms through administration before offering purchases to users. Enabling the mode does not connect payments. For real purchases, configure the payment service, return URLs, and secrets separately using the [payment integration documentation](payments-service-integration.md). Do not enable commercial mode merely to use the basic CRM.

Initial commercial quotas can be set with `PlatformPolicy:Commercial:Defaults`: `FreeVehicleLimit`, `FreeClientLimit`, `FreeMembersLimit`, `WorkspaceMembersLimit`, `PersonalVehicleLimit`, and `DemoVehicleLimit`. Defaults are 0 (unlimited); values must be non-negative. They initialize new plans and missing assignments, and supply commercial fallbacks. Existing plan prices and saved quota assignments are preserved; the disabled commercial mode remains unlimited.

Set the same application ID in WebApp (`PaymentsService:ApplicationId`) and the payment service (`Payments:ApplicationId`). The default is `opencrm`. IDs use 1–64 lowercase ASCII letters, digits, or hyphens, starting with a letter or digit. Command and event queues are `payments-{ApplicationId}-commands` and `payments-{ApplicationId}-events`. Preserve an existing installation's ID and stored command payloads when updating; changing the display name is not a payment identity migration.

### AI assistant

The `Assistant` section contains the `Enabled` switch, provider URL and allowed addresses, models, provider secret key `Assistant__RouterApiKey` (legacy alternative: `AiPilot__ApiKey`), limits, and processing parameters. `Enabled=false` by default. The AI assistant is an optional server-side integration; enabling it requires a configured provider and environment secrets. See the [AI assistant document](ai-assistant.md) for server-side execution and security.

## Where to inspect available settings

- WebApp defaults and sections: `src/OpenCRM.WebApp/appsettings.json` and `src/OpenCRM.WebApp/appsettings.Development.json`.
- Configuration source order: `src/OpenCRM.Hosting/OpenCRMApplication.cs`.
- PostgreSQL, Identity, and Hangfire registration: `src/OpenCRM.WebApp/Extensions/ServiceCollectionExtensions.cs`.
- Assistant settings: `src/OpenCRM.WebApp/Infrastructure/Assistant/AssistantOptions.cs`.
- Shared brand settings: `configuration/brand.json` and `configuration/README.md`.
- Separate notification settings: `services/notifications/src/Notifications.Worker/appsettings.json` and `services/notifications/README.md`.
- Payment service settings: `services/payments/README.md` and the respective projects' `appsettings.json` files.

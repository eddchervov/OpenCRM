# NotificationService

**English** | [Русский](README.ru.md)

A notification microservice for OpenCRM and other applications. One Worker process receives commands through RabbitMQ, stores state in PostgreSQL, sends email/SMS, and returns results to the application's queue. Commands are accepted only through RabbitMQ. An email provider callback webhook may run on internal port 8080; there is no HTTP API for sending notifications.

## Message flow

```text
OpenCRM → notifications-opencrm-commands → Worker → PostgreSQL + outbox
                                                   ↓
                                         notifications-dispatch
                                                   ↓
                                          Worker → SMTP / SMS.ru
                                                   ↓
                                      PostgreSQL + outbox результата
                                                   ↓
OpenCRM ← notifications-opencrm-results ← RabbitMQ
```

Command acceptance and delivery use different handlers in the same process. The internal queue is retained for compatibility with previously accepted jobs and deferred sends.

## Features

- Addressed commands for notification creation, template updates, and status requests.
- Separate command and result queues for each application.
- Idempotency by ApplicationId and normalized IdempotencyKey.
- Transactional inbox/outbox for incoming commands; bus outbox for delivery results.
- Deferred delivery, four attempts, and lease recovery after a Worker crash.
- queued, processing, retrying, provider-accepted, simulated, failed, and not-found states.
- LogOnly by default: no real emails or SMS are sent.
- PostgreSQL, RabbitMQ, JSON logs, Docker Compose, and CI.

`provider-accepted` means acceptance by a provider, not confirmed recipient delivery. `simulated` separately indicates LogOnly. Provider delivery acknowledgments are not yet implemented.

Sources reside in `services/notifications` in the OpenCRM repository. All service projects are also included in the root `OpenCRM.sln`. Commands in this document run from the service directory; `NotificationService.slnx` allows a separate build and test run. Worker deployment and its database migrations remain independent of OpenCRM.

## Structure

```text
src/
  Notifications.Contracts       Публичные команды, результаты и адреса очередей
  Notifications.Domain          Уведомления, шаблоны и попытки
  Notifications.Application     Регистрация, запрос состояния и доставка
  Notifications.Infrastructure  RabbitMQ, EF Core, outbox, SMTP и SMS.ru
  Notifications.Worker          Запуск обработчиков и планировщика
tests/
  Notifications.Tests           Проверки сервиса уведомлений
    Unit                        Изолированная логика без Docker
    Integration                 БД, брокер, Razor и регистрация DI
```

## Local setup

Run this section's commands from `services/notifications`.

1. Copy `.env.example` to `.env` and supply your own values for the empty `POSTGRES_PASSWORD` and `RABBITMQ_PASSWORD`.
2. Run `docker compose up --build -d`.
3. Inspect `docker compose logs --tail=100 worker` and consumers in the RabbitMQ UI at `http://localhost:15672`.

For example commands and result handlers, see [OpenCRM integration](docs/opencrm-integration.md). Add an application using `RabbitMq__Applications__1=catalog` in the Worker's environment. IDs consist of lowercase Latin letters, digits, and hyphens and are limited to 100 characters. Each application must have a separate RabbitMQ user with access only to its own queues. The Applications list creates routes but does not replace broker permissions.

## Production

The Worker uses standard .NET configuration keys without the `NOTIFICATIONS_` prefix. WebApplication.CreateBuilder loads appsettings and, in Development, user-secrets for `src/Notifications.Worker`; Program then adds the shared `brand.json`, unprefixed environment variables, and command-line arguments. For direct startup, set values such as `ConnectionStrings__NotificationsDb`, `RabbitMq__Host`, `RabbitMq__Password`, and `Delivery__LogOnly`.

For Production and direct Worker startup, use unprefixed standard keys: `ConnectionStrings__NotificationsDb`, `RabbitMq__Host`, `RabbitMq__Username`, `RabbitMq__Password`, and `Delivery__LogOnly`. Keep secrets in an external store and provide them as Worker environment variables; tracked appsettings contain only parameter names and empty secret values.

The EF design-time factory determines the environment from `DOTNET_ENVIRONMENT`, then `ASPNETCORE_ENVIRONMENT`. In Development, it loads User Secrets with the same UserSecretsId as the Worker, then all unprefixed environment variables; the environment takes precedence. Migrations need only `ConnectionStrings__NotificationsDb`.

## Integration settings

Addresses, ports, SMS.ru timeout, sender display name, application list, and modes are configured in src/Notifications.Worker/appsettings.json and overridden through the environment. Options classes do not supply working default addresses, ports, names, or LogOnly mode. Empty strings and collections mean a setting has not been filled in.

The Worker validates required parameters before starting consumers. Delivery:LogOnly must be explicit; an absent value does not enable real sending. Email:Enabled, Sms:Enabled, Telegram:Enabled, and TelegramClient:Enabled enable channels independently. With LogOnly=true, external provider secrets are unnecessary.

For local compose, set only `POSTGRES_PASSWORD` and `RABBITMQ_PASSWORD` in `.env`: compose supplies the remaining required values, including `Delivery:LogOnly=true`, enabled email/SMS channels, SMTP port, and SMS.ru parameters. In `LogOnly`, SMTP and SMS.ru secrets are not validated and nothing is actually sent.

For real email-only sending, set DELIVERY_LOG_ONLY=false, EMAIL_ENABLED=true, SMS_ENABLED=false, and fill in SMTP_HOST, SMTP_PORT, SMTP_FROM_ADDRESS. If SMTP_USERNAME is set, SMTP_PASSWORD is required; an unauthenticated relay is allowed. An empty `SMTP_FROM_NAME` in `.env.example` lets `Smtp:FromName` use the shared name from `brand.json`; an explicit nonempty value overrides branding.

For real SMS, enable SMS_ENABLED=true and set SMSRU_API_KEY.

For owner messages, enable TELEGRAM_ENABLED=true and set TELEGRAM_BOT_TOKEN and TELEGRAM_OWNER_CHAT_ID. The bot sends only to recipient `owner`. If direct Bot API connectivity is unavailable, use the gateway in `telegram-relay`. After deployment, set TELEGRAM_RELAY_URL (an HTTPS URL ending in `/sendMessage`) and TELEGRAM_RELAY_KEY. In this mode, provide the bot token and chat ID as gateway Worker secrets `TELEGRAM_BOT_TOKEN`, `TELEGRAM_OWNER_CHAT_ID`; `RELAY_KEY` must match TELEGRAM_RELAY_KEY in NotificationService. Direct mode remains active if the gateway URL is empty. Real sending still requires TELEGRAM_ENABLED=true and DELIVERY_LOG_ONLY=false. OpenCRM separately enables message queuing through TelegramAdminNotifications__Enabled=true.

Deploy the gateway from `telegram-relay` with `npx wrangler deploy`, then `npx wrangler secret put RELAY_KEY`, `npx wrangler secret put TELEGRAM_BOT_TOKEN`, and `npx wrangler secret put TELEGRAM_OWNER_CHAT_ID`. Set TELEGRAM_RELAY_URL to the assigned Worker's HTTPS URL with `/sendMessage`. RELAY_KEY must match TELEGRAM_RELAY_KEY; never store secrets in `wrangler.jsonc`.

For customer Telegram, use the separate `telegram-client` channel: enable TELEGRAM_CLIENT_ENABLED=true and set TELEGRAM_CLIENT_CREDENTIAL_BASE_URL (the OpenCRM HTTPS URL), TELEGRAM_CLIENT_CREDENTIAL_KEY, TELEGRAM_CLIENT_RELAY_URL (Worker with `/sendOrganizationMessage`), and TELEGRAM_CLIENT_RELAY_KEY. The OpenCRM key must match TelegramOrganizationNotifications__ServiceKey; the relay key must match the gateway Worker's RELAY_KEY secret. The customer recipient is `<organizationGuidN>:<chatId>`. Before sending, the Worker retrieves the token through GET `/api/internal/notification-bots/telegram/{organizationId:N}` with X-Notification-Service-Key and X-Notification-Key headers. The response contains `botToken`; the token never enters the notification queue or NotificationService database.

The gateway Worker also exposes protected POST `/getOrganizationBot` and `/setOrganizationWebhook` endpoints to validate tokens and register webhooks. The latter sets `/updates/{organizationId}` on the gateway Worker itself. It forwards incoming updates only to the fixed OpenCRM address from `OPENCRM_BASE_URL`, at `/api/internal/notification-bots/telegram/{organizationId}/updates`, preserving `X-Telegram-Bot-Api-Secret-Token` for OpenCRM validation. Set `npx wrangler secret put OPENCRM_BASE_URL` during gateway setup. After the first deployment, set the gateway Worker's own URL using `npx wrangler secret put TELEGRAM_WEBHOOK_BASE_URL`; it uses this fixed base, not the Host header, for webhook URLs.

For customer MAX messages, enable MAX_ENABLED=true and set MAX_CREDENTIAL_BASE_URL (OpenCRM HTTPS URL) and MAX_CREDENTIAL_KEY. This key must match MaxNotifications__ServiceKey in OpenCRM, where delivery is enabled with MaxNotifications__Enabled=true. Organization bot tokens remain in OpenCRM: NotificationService retrieves the required token before sending and does not store it in a queue. Invalid URLs, ports, and test values in place of keys prevent startup.

The EF design-time factory does not insert a local database or password: a connection string is required. In Development it comes from Worker user-secrets or `ConnectionStrings__NotificationsDb`; in Production it comes from that environment variable. The --migrate mode validates only the database and needs neither RabbitMQ nor providers.

## Channels and providers

Business logic and commands work with email/sms/telegram channels through INotificationChannelSender. Adapter selection is separate from its connection parameters:

```json
{
  "Sms": { "Enabled": true, "Provider": "smsru" },
  "Email": { "Enabled": true, "Provider": "smtp" },
  "Telegram": { "Enabled": false, "Provider": "telegram" },
  "TelegramClient": { "Enabled": false, "Provider": "telegram" }
}
```

SmsRu parameters (BaseUrl, ApiKey, and others) belong only to the SMS.ru adapter; Smtp contains SMTP adapter settings; Telegram contains BotToken, OwnerChatId, and RequestTimeoutSeconds. They are not shared channel settings. In the environment, selection uses SMS_PROVIDER, EMAIL_PROVIDER, and TELEGRAM_PROVIDER. A disabled channel's settings are not validated and its adapter is not registered.

Current implementations are smsru for SMS, smtp for email, telegram for administrative messages, and telegram-client for customer messages on an organization's behalf. An unknown provider name prevents startup rather than implicitly falling back to SMS.ru. To support another service, add an INotificationChannelSender implementation, registration, and parameter validation in Infrastructure, then select it in configuration. OpenCRM, RabbitMQ messages, NotificationDispatcher, and the database model remain shared across providers. Changing BaseUrl alone is insufficient: providers use different protocols and response formats.

## Email templates

System emails reside in Infrastructure's Templates/OpenCRM/V1 as .cshtml with a shared layout and typed models. OpenCRM passes TemplateKey, TemplateVersion, and Data; system templates do not need registration through the broker. The service validates parameters and stores the rendered email before queued. Deferred delivery and retries use the same content. SystemTemplates:Applications determines appearance and access: each application profile has BrandName and a Templates list. Adding a client requires no code changes. Editable campaigns and SMS retain a separate database-template mechanism.

[Templates, models, and versions](docs/templates.md) · [Parameter examples](examples/system-emails)

## Migrations

Tables and EF history use the `notifications` schema. Migration 20260909054007_VersionedTemplateSnapshots adds template versions and email snapshots. The initial migration is retained. See the [template guide](docs/templates.md) for update order and rollback restrictions.

Automatic migrations are disabled in production. Run them separately from the Worker image:

```bash
docker run --rm --env-file .env образ-worker --migrate
```

This mode applies migrations and exits without starting consumers or sending notifications. For EF commands with the SDK, use Infrastructure as the startup project with its design-time factory:

```bash
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/Notifications.Infrastructure --startup-project src/Notifications.Infrastructure
```

In Development, EF reads the connection string from Worker user-secrets and then environment variables; in Production, only the environment. It does not load `.env` itself. The [schema consolidation SQL](scripts/maintenance/consolidate-schema.sql) is retained as a manual maintenance script; it does not need another run.

## Validation

```bash
dotnet build NotificationService.slnx -c Release
dotnet test NotificationService.slnx -c Release
```

Container integration tests require running Docker and create/remove their own isolated PostgreSQL 15 and RabbitMQ 4. They use a test sender without external email/SMS. Razor and DI registration checks work without Docker. Unit tests are available without Docker:

```bash
dotnet test NotificationService.slnx -c Release --filter "FullyQualifiedName~.Unit."
```

See [architecture](docs/architecture.md) for boundaries, guarantees, and limitations.

On Windows, when specifying the Docker Desktop endpoint manually, use `$env:DOCKER_HOST = "npipe://./pipe/dockerDesktopLinuxEngine"` for the current PowerShell process. The Docker CLI string with four slashes after `npipe:` is incompatible with the Testcontainers version in use.

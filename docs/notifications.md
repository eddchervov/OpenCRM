# OpenCRM notifications through RabbitMQ

**English** | [Русский](notifications.ru.md)

Within the main application, `OpenCRM.Modules.Notifications` owns emails, website notifications, outbox/result handlers, and MAX/Telegram settings. It registers services through `AddOpenCRMNotifications`; account and subscription scenarios use narrow contracts. Domain events and intents are stored in OpenCRM's shared database.

The separate NotificationService in `services/notifications` renders Razor emails and calls providers. Its Worker, database, and image remain independent; communication uses `Notifications.Contracts` and RabbitMQ. The main application does not use SMTP, SMS.ru, or Unisender keys. The module is tested in `OpenCRM.Modules.Notifications.Tests`, and the delivery service in `Notifications.Tests`.

Both applications obtain server secrets from their own server environment configuration; development uses separate user-secrets. See the [configuration description](../configuration/README.md) for the current arrangement.

## Why both a database and RabbitMQ are needed

1. OpenCRM saves the business change and notification job in its database. For example, a support request and its email are recorded in one transaction.
2. An OpenCRM background handler reads its EmailMessages table and sends the job to RabbitMQ.
3. The Worker receives the command, saves it in its own database, and sends the email.
4. The result returns through RabbitMQ and updates the OpenCRM log.

The database record is a persisted intent; RabbitMQ transports it between applications. If the broker is unavailable, the intent remains in the database for later sending. If the Worker is stopped, the queue retains the command until it starts. The Worker needs neither access to OpenCRM's database nor knowledge of its tables. Other applications use the same contracts.

This is an outbox pattern with repeated command delivery. A stable NotificationKey lets the Worker recognize duplicates. A single physical SMTP send is not guaranteed: failure after provider acceptance but before result persistence may cause repetition.

## Connection

Development: RabbitMq:Host=192.0.2.1, ApplicationId=opencrm-development.
Deployment: the operator supplies the broker address and network; ApplicationId=opencrm.

Shared parameters: port 5672, VirtualHost=/, Username=

Commands use MassTransit Send to notifications-<applicationId>-commands; results go to notifications-<applicationId>-results. Each results queue corresponds to one application database. A local database must not compete for production results; opencrm-development is reserved for it.

## Emails and log

| Scenario | Razor template |
| --- | --- |
| Email confirmation | system/email-confirmation |
| Eight-digit login code | system/login-code |
| Browser password reset | system/password-reset |
| Mobile application password reset | system/password-reset-mobile |
| Support request | system/support-message |
| Paid subscription, demo, demo availability | system/subscription-notice |
| Invite a new user to an organization | system/custom-email within the branded layout |
| Editor email and bulk campaign | system/custom-email |

OpenCRM passes the key and data. When inviting an address without an account, the rendered email is queued in the same transaction as the invitation. The link leads to email-code sign-in and then the invitation list; acceptance requires email verification. The Worker uses compiled version 1 cshtml and the shared layout. For system/custom-email, editor content is supplied as htmlBody; it is inserted as HTML, not executed as Razor. Temporary HTML template registration is unnecessary.

## Owner notifications in Telegram

On account creation, self-deletion, and confirmed subscription payment, OpenCRM saves a TelegramNotificationOutbox record. For payment, this occurs in the subscription activation transaction, so repeated webhooks do not add another notification. Registration covers the website, email-code sign-in, and API. Messages contain the email, user ID, and available event details; passwords, login codes, and payment credentials are not transmitted.

A background handler sends a command with channel `telegram` and logical recipient `owner` to NotificationService. Command retries use the same event key. Delivery statuses return to TelegramNotificationOutbox. For direct delivery, configure the bot token and chat ID in NotificationService (`Telegram__BotToken`, `Telegram__OwnerChatId`); when using a Cloudflare Worker, they are gateway secrets and NotificationService receives only its URL and access key. Real sending requires `Telegram__Enabled=true` and `Delivery__LogOnly=false`. While `TelegramAdminNotifications__Enabled=false`, the Telegram handler is not registered and jobs remain in OpenCRM's database. After preparing NotificationService, set `TelegramAdminNotifications__Enabled=true` in OpenCRM and restart it. The owner must start a conversation with the bot for actual delivery. Telegram failure does not undo the business operation; the Worker retries. A failure after Telegram accepts a message may cause a duplicate.

## Organization customer notifications in Telegram

Each organization has its own Telegram bot. The owner creates it through BotFather and enters its token in organization settings; OpenCRM stores the token securely. A customer receives a one-time link from their record and starts the bot themselves. Consent, chat, and history are separate from MAX and the administrative Telegram bot. Initial events are appointment creation, rescheduling, cancellation, and vehicle readiness for handover. A separate notification is created per channel, allowing MAX, Telegram, or both. The scenario works in detailing, repair shops, and fleets for maintenance records whose vehicle is linked to an organization customer; internal fleet vehicles without a customer record do not receive customer messages.

OpenCRM queues customer messages with channel `telegram-client`. NotificationService retrieves a token only for a valid message, verified customer, matching chat, and active organization, then sends through the Cloudflare Worker. The Worker also verifies bot tokens, registers webhooks, and forwards incoming events to OpenCRM; OpenCRM validates a separate bot secret. Message text/status appear in the customer and maintenance records. Enable with `TelegramOrganizationNotifications:Enabled`, `ServiceKey`, `RelayBaseUrl`, and `RelayKey` in OpenCRM and the `TelegramClient` settings in NotificationService. `TelegramClient:CredentialKey` equals `ServiceKey`, `TelegramClient:RelayKey` equals `RelayKey`, and gateway `RELAY_KEY` equals `RelayKey`. The gateway needs `OPENCRM_BASE_URL` and `TELEGRAM_WEBHOOK_BASE_URL`. Administrative `/sendMessage` retains its fixed bot and chat.

## Organization customer notifications in MAX

Each organization connects its own approved MAX bot in organization settings. The owner enters the token; OpenCRM verifies the bot and automatically registers an HTTPS webhook. ASP.NET Data Protection encrypts the token and webhook secret; preserve keys in `DataProtection:KeysDirectory` across upgrades. The webhook uses `AppSettings:BaseUrl`.

An employee issues a one-time link from the customer record. The customer starts the bot, and the webhook links their MAX ID to the record. Without that link, messages are not queued. Stopping the bot disables sending. Initial events are appointment creation, rescheduling, cancellation, and vehicle readiness. This works in detailing, repair shops, and fleets when the vehicle is linked to an organization customer. OpenCRM checks appointment validity and customer consent, then passes the notification through RabbitMQ to NotificationService. Before sending, the microservice retrieves the token through a protected HTTPS request to OpenCRM; the token never enters the queue. Text and status appear in customer and maintenance records. Enable with `MaxNotifications__Enabled=true` and `MaxNotifications__ServiceKey` in OpenCRM and `MAX_ENABLED=true`, `MAX_CREDENTIAL_BASE_URL`, and `MAX_CREDENTIAL_KEY` in NotificationService. An unknown MAX response may result in a duplicate on retry.

NotificationStatusChanged.Content returns final Subject, Body, IsHtml, and TextBody to the owning application's queue. OpenCRM saves them for email viewing in administration. Before the first Worker response, the log has a preliminary subject and original parameters in TemplateDataJson.

Mobile emails preserve opencrm:// and a fallback web link. Login codes still expire after one hour; queuing does not extend validity.

## Reliability and statuses

EmailMessages is both the log and durable outbox. NextDispatchAt determines the next dispatch or status check. FOR UPDATE SKIP LOCKED lets several OpenCRM instances process the queue.

When no result arrives, OpenCRM sends GetNotificationStatus. A not-found response requeues the original command with the same key. Repeated terminal results do not increase counters. Older states do not replace newer ones.

- queued — job saved.
- retrying — the Worker is retrying.
- sent / provider-accepted — the provider accepted the message; recipient delivery remains unconfirmed.
- simulated — LogOnly; nothing was actually sent.
- failed — command rejected or attempts exhausted.

Campaign SentCount and reminder SentAt update after provider-accepted. SimulatedCount is tracked separately. Pausing/canceling a campaign stops queuing further recipients; already dispatched jobs are not recalled. Terminal failures remain in the log without automatically creating new jobs.

## Provider events

Unisender sends an HTTPS webhook to /webhooks/unisender-go. A reverse proxy forwards it to notifications-worker:8080. The Worker verifies the signature, saves the event with its outbox, and returns NotificationDeliveryReported through RabbitMQ. OpenCRM stores EventId and updates the log in one transaction: duplicates do not increase open/click counts. Unsubscribe or complaint events revoke advertising campaign consent.

The webhook is a provider callback channel. There is no HTTP API for sending notifications from OpenCRM.

Every new email requires application_id and notification_key. Signed webhooks lacking these fields are ignored.

## Deployment

1. Stop the old Worker and update its image. Apply all NotificationService migrations, including VersionedTemplateSnapshots and 20260909092759_ProviderWebhookEvents, then start the Worker. Updated configuration allows both opencrm/opencrm-development profiles and all listed templates.
2. For Unisender, set EMAIL_PROVIDER=unisender-go, SMTP settings, UNISENDER_WEBHOOK_ENABLED=true, and UNISENDER_WEBHOOK_KEY in the server's .env.
3. Configure your own HTTPS `/webhooks/unisender-go` route to the Worker. Deployment determines the URL, TLS, and proxy rules; the service verifies the webhook secret.
4. Stop the old OpenCRM. Apply 20260909083524_AddNotificationOutbox and 20260909092118_CompleteNotificationIntegration to its database.
5. Set EmailPreferences__UnsubscribeTokenSecret. To retain existing unsubscribe links, transfer the previous effective EmailSettings:UnsubscribeTokenSecret or, if absent, EmailSettings:Password. This separately signs links; future SMTP password changes do not affect it.
6. Set Support__NotificationEmail and RabbitMq__Password, connect the application to the broker's network, and start OpenCRM.
7. Verify commands, results, and the log. Disable Delivery:LogOnly only when intentionally enabling real sending.

Local development secrets are supplied through user-secrets.

Example OpenCRM migration from the repository root:

~~~powershell
dotnet ef database update --project src/OpenCRM.Infrastructure.Migrations --startup-project src/OpenCRM.Infrastructure.Migrations
~~~

The design-time factory reads appsettings and environment variables. Set ConnectionStrings__OpenCRMConnection for the intended database.

Down for AddNotificationOutbox is forbidden with unprocessed jobs. Rollback removes service fields and simulated statistics. Down for CompleteNotificationIntegration removes deduplication IDs and restores the 300-character subject limit; PostgreSQL rejects narrowing when longer subjects exist rather than truncating content. Restoring TemplateRegisteredAt does not restore its previous values.

## Build and validation

NotificationService resides in `services/notifications`; its projects are included in `OpenCRM.sln`. Production code depends only on Notifications.Contracts. The test project references the Worker implementation for end-to-end checks. The service retains its own build settings, Docker image, Compose, and database; it can be built and deployed separately. Run the following from the repository root.

~~~powershell
docker build -f src/Dockerfile -t opencrm .
dotnet test tests/OpenCRM.Notifications.Tests/OpenCRM.Notifications.Tests.csproj
dotnet test services/notifications/NotificationService.slnx
docker build -f services/notifications/src/Notifications.Worker/Dockerfile -t notification-service-worker services/notifications
~~~

Tests start separate PostgreSQL 15 and RabbitMQ instances in Docker. The end-to-end test uses the real Worker, Razor, both OpenCRM handlers, and an HTTP webhook; LogOnly prevents real emails. Production databases and queues are not used.

## SMTP: outbound Worker network

Email delivery uses an outbound TLS connection from the Worker's network. The platform does not prescribe MTU, network addresses, or names: the deployment environment determines them. Some providers and networks may encounter TLS timeouts with MTU 1500 and packet fragmentation; reduce the Worker network MTU through your installation's tools and restart only the Worker container if needed. Do not clear RabbitMQ queues or databases for this change. TLS certificate validation is mandatory and is never disabled; a completed email send indicates correct configuration.

## Emails from administration

In “История email” (Email history), “Написать письмо” (Compose email) opens the shared editor. Recipients may be any email address (no account required), a selected user, an organization type, or all users. User search covers the entire database and returns up to 30 matches. Review addresses and preview the text before sending.

Plain text is escaped and preserves paragraphs; HTML is a separate mode. All emails use a campaign, the OpenCRM outbox, and the microservice's system/custom-email template. The Worker adds the branded layout; the editor need not supply a header/footer. Results remain in email history. Verification and consent filters are unavailable for an arbitrary address.

The shared Templates/OpenCRM/V1/_Layout.cshtml in NotificationService defines the appearance of all system and administrative emails. Branding comes from configuration/brand.json (ProjectBrand) and fills SystemTemplates profiles plus Email:FromName and Smtp:FromName as defaults: explicit environment/application settings take precedence. The email mark is the name's first letter; applications may specify their own through SystemTemplates:Applications:<applicationId>:BrandMark. The sender address is configured separately with Smtp:FromAddress.

# Connect OpenCRM through RabbitMQ

**English** | [Русский](opencrm-integration.ru.md)

OpenCRM already uses the EmailMessages outbox and result handlers. Running applications depend only on Notifications.Contracts. In this solution, that is a ProjectReference to services/notifications/src/Notifications.Contracts within the shared repository. CI builds applications and the Worker from one checkout; keep contract and consumer versions consistent when releasing.

## Addresses

Production: notifications-opencrm-commands and notifications-opencrm-results.
Development: notifications-opencrm-development-commands and notifications-opencrm-development-results.
The deployment operator supplies the broker address and network; the default port is 5672.
Local OpenCRM is configured with 192.0.2.1:5672.
RabbitMq:Applications in the Worker contains allowed application IDs.
The client application's name is set through RabbitMq:ApplicationId.
The Worker creates customer conversation queues (`TelegramChat` and `ConversationDelivery`) only for `Conversations:ApplicationId`; that ID must belong to `RabbitMq:Applications` and match the OpenCRM application's `RabbitMq:ApplicationId`.
With multiple applications, `Conversations:ApplicationId` is mandatory. Automatic selection is allowed only when `RabbitMq:Applications` has one item. Do not connect production and development conversations to the same Worker instance: it shares one API URL and service-to-service key.

A results queue belongs to one application database. Do not connect a local database to the production queue. Commands use MassTransit Send with the standard envelope, not Publish. NotificationDispatchRequested is the Worker's internal contract.

## Commands and results

SendNotification accepts CreateNotificationRequest: IdempotencyKey, Channel, Recipient, TemplateKey, Data, and TemplateVersion. Do not supply Subject/Body for Razor emails. Retried jobs use the same IdempotencyKey. GetNotificationStatus returns NotificationStatusChanged or not-found to the permanent results queue.

queued/processing/retrying/provider-accepted/simulated/failed describe processing. Provider-accepted does not yet mean recipient delivery; simulated excludes a real send. Content contains final Subject, Body, IsHtml, and TextBody for the application log.

Administrative messages use channel `telegram` and recipient `owner`. Customer messages sent on an organization's behalf use the separate `telegram-client` channel and recipient `<organizationGuidN>:<chatId>`. The Worker retrieves the token through `GET https://<opencrm-host>/api/internal/notification-bots/telegram/{organizationId:N}` with `X-Notification-Service-Key`, `X-Notification-Key`, and `X-Notification-Recipient` headers (the chat ID from the recipient); a successful response is `{"botToken":"..."}`. The token is immediately forwarded to the HTTPS gateway and never stored in the notification queue or database.

The organization owner verifies the token through the protected Worker endpoint `POST /getOrganizationBot` with `{"bot_token":"..."}`. Set the webhook through `POST /setOrganizationWebhook` with `{"bot_token":"...","organization_id":"<uuid>","secret":"<telegram-secret>"}`. Telegram calls `POST https://<worker-host>/updates/{organizationId}`; the Worker forwards the body and `X-Telegram-Bot-Api-Secret-Token` to the fixed `https://<opencrm-host>/api/internal/notification-bots/telegram/{organizationId}/updates` address. OpenCRM must verify the supplied secret against that organization's secret.

OpenCRM registers NotificationResultConsumer for NotificationStatusChanged and NotificationCommandRejected, and NotificationDeliveryConsumer for NotificationDeliveryReported. The endpoint uses ConfigureConsumeTopology=false. State and counters update in a transaction; repeated delivery events are ignored by EventId.

Other clients may register immutable database template versions through UpsertNotificationTemplate and must then also consume NotificationTemplateUpdated. OpenCRM does not use that command: editor emails use system/custom-email.

## Version 1 Razor templates

- system/email-confirmation: name, confirmationUrl.
- system/password-reset: name, resetUrl.
- system/login-code: code — eight digits.
- system/password-reset-mobile: userId, code, resetUrl.
- system/support-message: messageId, typeName, name, email, phone, topic, text.
- system/subscription-notice: kind (upcoming, paid-expired, demo-expired, demo-available), organizationName, planName, endDate, daysRemaining, durationDays, actionUrl.
- system/custom-email: subject, htmlBody — trusted HTML from the application editor.
- system/subscription-expiring: name, subscriptionName, endDate, renewalUrl.

Keys are allowed in SystemTemplates:Applications:<applicationId>:Templates. Profiles opencrm and opencrm-development are included in the base appsettings. Templates and models reside in Templates/OpenCRM/V1. Data is not executed as Razor; custom-email inserts HTML without escaping, so its source must be trusted. See [rules and versions](templates.md).

## Provider events

With Email:Provider=unisender-go, the adapter adds application_id and notification_key to global_metadata in the X-UNISENDER-GO header. UnisenderGo:TrackLinks and TrackRead control tracking. The Worker verifies the webhook signature and sends NotificationDeliveryReported to the application's addressed queue. OpenCRM does not know the HTTP request format or provider key.

Events without application_id or notification_key are ignored. There is no compatibility with the old protocol. Ordinary smtp does not add Unisender headers.

For migrations and integration settings, see [../README.md](../README.md), the integration settings and migrations sections; nginx configurations are in the same `docs` directory. Upgrade the Worker build with templates and migrations first, then OpenCRM and the HTTPS webhook route. An old running Worker does not support the new contracts.

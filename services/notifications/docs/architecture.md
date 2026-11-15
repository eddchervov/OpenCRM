# Architecture

**English** | [Русский](architecture.ru.md)

NotificationService has one executable project, Notifications.Worker. OpenCRM and other applications determine why a notification is needed; the service owns templates, delivery, retries, and history. It does not share applications' databases or domain models.

## External boundary

Public contracts reside in Notifications.Contracts:

| Command | Purpose | Result |
| --- | --- | --- |
| SendNotification | Create a notification with an idempotency key | NotificationStatusChanged |
| GetNotificationStatus | Retrieve state by the original key | NotificationStatusChanged |
| UpsertNotificationTemplate | Store a template | NotificationTemplateUpdated |

Invalid data produces NotificationCommandRejected with the original command's MessageId. Malformed envelopes and infrastructure failures that exhaust transport retries go to the `*_error` queue; business acknowledgment is not guaranteed for them.

Commands use MassTransit Send to `notifications-{applicationId}-commands`. ConfigureConsumeTopology is disabled: publishing the command type globally does not bind it to every application. ApplicationId is derived from the incoming queue and need not be passed in the command body. The service also selects the response address; a user-supplied ResponseAddress is not used for business results.

Results are sent directly to `notifications-{applicationId}-results`, without global Publish. RabbitMQ users and permissions limit application authority. Do not give an application access to other applications' queues, the internal notifications-dispatch queue, or a shared administrator account.

## Transactions and retries

The external consumer uses EF consumer outbox/inbox. Command processing and outgoing messages commit in one transaction. The inbox eliminates duplicate MessageIds; a unique (ApplicationId, IdempotencyKey) index protects against repeated commands with a new MessageId. The key is trimmed before lookup and persistence. On repetition, the first notification remains authoritative: new data does not replace its content.

The internal NotificationDispatchRequested retains its original contract and queue. The sending consumer creates a separate dependency scope with a bus outbox. It can persist the attempt, state, and addressed event in one transaction without holding the inbox transaction during an SMTP/HTTP call. Delivery retries are separate from registration.

Every 10 seconds, the scheduler publishes due deferred jobs and releases processing leases older than 10 minutes. Releasing a lease resets the previous publication marker. An atomic UPDATE claims the notification; terminal records are not resent. After four started attempts, ordinary provider errors result in failed. Unhandled infrastructure failures remain in the error queue and require diagnosis.

This provides at-least-once message delivery. If the provider accepted a message but the process exited before persisting the result, an external send may be repeated. There is no absolute exactly-once sending guarantee without provider idempotency support. A persistence failure after a successful call is not classified as provider rejection.

## Telegram customer conversations

Outgoing conversation messages are stored in OpenCRM and published as `ConversationDeliveryWakeup(ProviderCode, MessageId, RequestId)` to a separate channel queue: `notifications-{applicationId}-conversation-{provider}`. The contract recognizes only Telegram and MAX; each connected adapter gets its own queue and worker, keeping provider concurrency and resilience independent. The signal carries identifiers only; tokens and attachment bytes are transferred through a protected HTTPS API after acquiring a delivery lease. RabbitMQ is a wake-up mechanism; final delivery state is stored in OpenCRM and NotificationsService.

The Worker declares a separate durable queue with `x-overflow=reject-publish`; default limits are 500 messages and 8 MiB, with bounded prefetch and concurrency. It also predeclares a bounded error queue of 500 messages and 8 MiB that rejects new publications at capacity. OpenCRM publishes to the main queue's exchange with mandatory routing and waits for a publisher acknowledgment. This prevents a publisher from creating a queue with different arguments and retains the command in the outbox if the broker rejects publication. Increase limits only after evaluating memory and processing throughput.

The Rabbit consumer only performs `INSERT ... ON CONFLICT DO NOTHING` into the durable inbox and acknowledges the message after the row commits. It does not call OpenCRM or Telegram. A bounded number of workers claim due records with `FOR UPDATE SKIP LOCKED`, persisting the lease and next attempt time; core API errors and 409 responses produce durable backoff. Repeated wake-ups with the same IDs neither create a second row nor call the provider concurrently. Unprocessed records older than 24 hours become `expired` and appear in the periodic queue snapshot.

Inbox persistence errors get three bounded MassTransit retries. Afterward, the original message enters the bounded `*_error` queue. There is no automatic bulk replay; a complete requeue could repeat an external API call whose outcome is unknown. Once the cause is resolved, investigate messages individually. State by `LeaseId` prevents blindly resending an already started attempt: its outcome remains uncertain. After inbox persistence, Rabbit retries are unnecessary: the database worker performs recovery, and after service unavailability OpenCRM republishes the wake-up with the same IDs.

The mechanism is based on the [MassTransit outbox documentation](https://masstransit.io/documentation/patterns/transactional-outbox). The project retains MassTransit version 8.5.7.

## States

| Result status | Meaning |
| --- | --- |
| queued | Notification saved, including deferred sends |
| processing | Processing started; returned when requesting a snapshot |
| retrying | An attempt failed; retries will follow |
| provider-accepted | Provider accepted the message |
| simulated | LogOnly applied; no real send occurred |
| failed | Attempt limit exhausted |
| not-found | The application has no record with that key |

The existing database still stores a successful attempt with Sent status. Simulation is identified by a persisted provider ID prefixed with `log:`, so the snapshot remains correct after restart or a LogOnly change.

Events may be duplicated or delayed. Clients deduplicate by EventId, match ApplicationId/IdempotencyKey, consider AttemptCount, and never replace terminal state with an earlier one. GetNotificationStatus restores a snapshot. Old notifications do not receive retroactive events; their state can be requested by key.

Templates have an immutable Version in the (ApplicationId, Key, Channel, Version) key. Registering the same content again is acknowledged; overwriting a version is rejected. For database templates, the client waits for NotificationTemplateUpdated with the required version. System emails under system/* compile from Razor .cshtml with the Worker. SystemTemplates:Applications determines access and application name. They use typed models and a layout belonging to their own version. They cannot be registered or replaced by a broker command. Command acceptance renders and persists the email snapshot with the outbox. Delivery and retries do not reread the template; old jobs get a version 1 snapshot before their first send. See [templates](templates.md) for details and migration order.

There is no HTTP notification-sending API or HTTP health endpoints. Check process state through Docker/logs and consumers. With UnisenderGo:WebhookEnabled, the Worker accepts signed events at /webhooks/unisender-go and forwards the shared NotificationDeliveryReported contract to the application queue. ProviderWebhookEvents and the bus outbox commit in one transaction. Stable EventId determines duplicates. A successful SMTP call means provider acceptance; delivered arrives as a separate event. NotificationStatusChanged.Content contains the rendered email snapshot only for its owning application.

## Switching providers

NotificationDispatcher depends on INotificationChannelSender and selects an implementation by channel. SMS.ru is not part of the domain model or RabbitMQ contracts. Email:Provider, Sms:Provider, and Telegram:Provider select a specific Infrastructure adapter; Smtp, SmsRu, and Telegram sections contain only their respective integration parameters. A new protocol requires an adapter, registration, and configuration validation; business logic and service clients remain unchanged.

Concrete adapters and settings reside in separate Infrastructure directories: Providers/SmsRu contains SmsRuNotificationSender and SmsRuOptions; Providers/Smtp contains EmailNotificationSender and SmtpOptions. Namespaces match directories. Providers/Telegram contains TelegramNotificationSender and TelegramOptions. It accepts only the logical `owner` address; its configuration stores the bot token and owner's chat_id. Shared delivery settings and dependency registration remain in Infrastructure.

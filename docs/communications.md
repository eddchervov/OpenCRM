# Customer conversations

**English** | [Русский](communications.ru.md) | [简体中文](communications.zh-CN.md)

## Customers and channels

An organization connects its own bot. The persistent bot identifier is stored separately from its name and encrypted token. Connections, verified external customer accounts, conversations, messages, attachments, and read receipts are stored in separate related tables.

The conversation list is grouped by customer. An unknown sender remains a separate contact: matching names or phone numbers do not automatically link them to a customer record. Disabled connections retain their history for viewing.

The customer starts communicating with the bot or follows an invitation. The CRM does not create a private conversation from a phone number alone. Connection processing uses the external account identity and event timestamp so an outdated event cannot undo newer state.

Disconnecting atomically revokes links, closes conversations for sending, clears the automatic notification address, and cancels unsent messages. If sending has already started, the operation returns `409` until the outcome is known. An incoming message alone does not restore a revoked link. Restarting the bot resumes only an existing verified connection.

Moving a conversation and manually unlinking it also use a single transaction. Old links remain revoked, previous notification addresses are cleared, and pending messages are canceled. A manual link does not enable automatic notifications for the new customer. The MAX delivery address is stored separately from the chat ID.

## Permissions and templates

The server checks `messages.view`, `messages.send`, `messages.manage`, and `messages.templates.manage` together with user membership, organization type, and workspace access. Without `clients.view`, customer record data is hidden; maintenance fields require `maintenance.view`. Read state is tracked separately for each employee.

Groups and templates belong to the Communications module. They support ordering, versions, and archiving; editing an outdated version returns a conflict. Allowed substitutions: `organization.name`, `client.name`, `maintenance.date`, `maintenance.time`, `maintenance.vehicle`. Arbitrary expressions and scripts are not executed.

A preview uses the saved template version, conversation, and optional maintenance record. The employee can edit the text and send it manually. The message stores the final text, `MessageTemplateId`, `MessageTemplateVersion`, and `MaintenanceId`; later template changes do not alter history. Repeating a request with the same `RequestId` returns the existing message.

If a maintenance record specifies a buyer, its context is available in that buyer's conversations. The vehicle owner is used only for older records without a buyer.

## Delivery and recovery

The message and delivery intent are written in one transaction in the primary database. RabbitMQ receives the provider and message/request IDs; message text, tokens, and file bytes are not placed on the queue. MAX and Telegram queues are separate, with bounded queue sizes and concurrency.

The notification service first writes the command to a durable inbox, then acknowledges RabbitMQ. Bounded handlers lease jobs from the database; pending attempts remain there instead of being requeued indefinitely. Deduplication accounts for the provider, message, and `RequestId`.

For multiple applications, `Conversations:ApplicationId` selects the conversation application and must match the corresponding client's `RabbitMq:ApplicationId`. Automatic selection is allowed only when there is one application.

The start of an attempt is recorded before the external call. Recovery uses the previous `LeaseId`: a confirmed result is read from the database, while a result lost after the external call began is marked unconfirmed and is not automatically resent. The employee verifies receipt before sending again. Exactly-once delivery guarantees depend on the messenger's idempotency support.

Earlier pending or sending messages block subsequent messages in the same conversation. With an active lease or deferred attempt, the API returns `409` with `Retry-After`; the waiting period is limited to 24 hours.

## Files and log

Outgoing files are limited to 20 MB and encrypted with Data Protection before storage in S3. Content access goes through an authorized API without a public link to the decrypted file. Incomplete uploads are isolated by employee and conversation and deleted after one day.

Incoming files are downloaded through a verified provider connection and protected API. Arbitrary URLs are not accepted; the MAX URL is validated before contacting the CDN, and the bot token is not forwarded to the CDN. Incoming file availability depends on the provider retaining the message and the connection remaining valid; text and metadata are stored in the system.

The notification log shows the event, final text, channel, time, and result. Viewing the log does not trigger another send.

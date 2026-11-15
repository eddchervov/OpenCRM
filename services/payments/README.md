# OpenCRM subscription payment service

**English** | [Русский](README.ru.md)

Standalone Payments on .NET 10 with its own PostgreSQL and RabbitMQ. All three projects are included in `OpenCRM.sln`: `Payments.Contracts` and `Payments.Service` are in `Services/Payments`, and `Payments.Tests` is in `Tests`. They do not reference OpenCRM. Checkout and Billing connect through a separate financial transport; see the [integration and recovery guide](../../docs/payments-service-integration.md). Production requirements are listed in [Server setup](../../docs/payments-service-integration.md#server-setup).

This is a separate stage of the [architecture design](../../docs/payments-service-design.md): command intake, operation persistence, test/live YooKassa adapters, webhook inbox, reconciliation, refunds, and reliable state delivery. Subscription grants, promotional codes, and current receipt generation remain in the main application.

## Modes and boundaries

- `Disabled` is the default mode. A receipt and `payments_disabled` rejection are stored; no executable monetary operation is created. Enabling a mode does not execute previously rejected commands.
- `YooKassaTest` explicitly uses a test store. It requires a shop ID and a secret prefixed with `test_`; the response must have `test=true`.
- `YooKassaLive` uses a live store with explicit `IsTest=false`, a numeric shop ID, and a `live_` key. The intent, stored payment, store, and provider response modes must match.
- Single-stage, one-off subscription payment with `capture=true` is supported. Automatic payments, saving cards, and acquiring for studios are not enabled. `Purpose=subscription`, `InitiationMode=customer`; other values are rejected.
- An unknown outcome is not a failure. Retries preserve the original body/key; after 23 hours from the first possible send, a new POST is blocked and the operation requires investigation. GET for a known object remains available after this period.
- Full and partial refunds have separate commands, operations, keys, and amount reservations. Monetary state is confirmed through the provider API. Billing records the refund and requires manual entitlement review; it does not automatically shorten a subscription.

## Start a separate environment

Run from the repository root in PowerShell 7. Preserve the same passwords on subsequent starts when reusing existing volumes. This example generates random values for the first start. Save them in your own protected store and reuse the same values for later starts with these volumes:

```powershell
$env:PAYMENTS_POSTGRES_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$env:PAYMENTS_RABBITMQ_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$env:PAYMENTS_SERVICE_KEY = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
docker compose -f services/payments/compose.yaml up -d --build
```

The `payments-service` stack uses its own volumes, the `payments` database and user, and the `payments` RabbitMQ virtual host. The migrations container finishes before the service starts. Normal application startup does not automatically apply migrations. The application container runs without root.

Local addresses: HTTP `http://localhost:5087`, PostgreSQL `localhost:5434`, AMQP `localhost:5674`, RabbitMQ management `http://localhost:15674`. Ports bind only to loopback. External webhooks require a separate HTTPS reverse proxy and test store; this Compose setup is not a production deployment.

To make YooKassa test requests, set `PAYMENTS_MODE=YooKassaTest`, `PAYMENTS_SHOP_ID`, and `PAYMENTS_TEST_SECRET_KEY` in a protected environment and recreate only the Payments service. Do not take these values from the main application's configuration. Switching already accepted operations between merchant accounts is unsupported: the account is fixed in the intent, and a mismatch blocks execution.

For startup without Docker, the application accepts `ConnectionStrings__PaymentsDb`, `Payments__ServiceKey`, `RabbitMq__Host`, `RabbitMq__Port`, `RabbitMq__VirtualHost`, `RabbitMq__Username`, and `RabbitMq__Password`. Values in `appsettings.json` contain no passwords. Apply migrations in a separate invocation:

```powershell
dotnet run --project services/payments/src/Payments.Service -- --migrate
dotnet run --project services/payments/src/Payments.Service
```

`/health/live` checks the process; `/health/ready` checks Payments schema availability and MassTransit transport. A healthy service in `Disabled` mode does not mean charging is possible.

## Contracts

Send commands directly through MassTransit `Send` to `queue:payments-opencrm-commands`. There are no HTTP payment-creation commands. Grant RabbitMQ vhost and queue access only to the trusted application; `ApplicationId` does not replace transport authorization.

| Contract | Main fields |
| --- | --- |
| `CreatePayment` | `CommandId`, `ApplicationId=opencrm`, `PurchaseId`, `AmountMinor` in kopecks, `Currency=RUB`, description, `ReturnUrl`, initiator/buyer `BuyerId`, entitlement recipient `BeneficiaryId`. |
| `RequestRefund` | `CommandId`, `ApplicationId`, `PurchaseId`, amount in kopecks, reason, and request author. |
| `PaymentStateChanged` | `EventId`, `PurchaseId`, intent ID, increasing `AggregateVersion`, creation/payment state, amounts, provider ID, URL, test flag. |
| `PaymentCommandRejected` | Command/purchase ID and rejection code before a new monetary operation is created. |
| `PaymentOperationStateChanged` | Operation/command ID, Kind/Status, safe error code, including canceled/rejected refunds. |

A new client references only `Payments.Contracts`. Example command from a separate test sender:

```csharp
var purchaseId = Guid.NewGuid();
var issuedAt = DateTimeOffset.UtcNow;
var endpoint = await bus.GetSendEndpoint(new Uri("queue:payments-opencrm-commands"));
await endpoint.Send(new CreatePayment(
    Guid.NewGuid(),
    "opencrm",
    purchaseId,
    125_000,
    "RUB",
    "Подписка opencrm.example.invalid",
    $"https://opencrm.example.invalid/payment/complete?purchaseId={purchaseId:D}",
    "test-buyer",
    "test-organization",
    SchemaVersion: 2,
    IssuedAt: issuedAt,
    NotAfter: issuedAt.AddHours(23),
    MerchantAccountId: "<shopId тестового магазина>"));
```

Results go to the separate durable `payments-opencrm-events` queue. It retains messages while no consumer is connected. Broker acknowledgment does not mean a subscription has been granted. Billing stores a durable inbox and the latest version; entitlement grants and AppliedAt are atomic. Acknowledgment of entitlement application back to Payments is not yet implemented; application state is stored and checked in OpenCRM.

Repeating `CommandId` with identical content creates nothing; different content is rejected. Repeating `PurchaseId` with an unchanged intent and a new `CommandId` does not create a second payment. The price, currency, or beneficiary of an accepted intent cannot change. Billing reserves the beneficiary's unfinished purchase; incompatible purchases under different PurchaseIds are blocked until the outcome is known.

`ReturnUrl` must exactly match a base URL from `Payments:AllowedReturnUrls` plus `?purchaseId={PurchaseId:D}`; by default, the list contains only the current HTTPS payment completion route. Explicitly configure the allowed URL when enabling a standalone test interface. Amounts are limited to positive RUB payments up to 1,000,000 rubles.

## HTTP and notification processing

- `GET /internal/payments/opencrm/{purchaseId}` requires `X-Payments-Key`. It returns verified state and operations without request bodies, provider keys, or card data. This is an internal server API: never give its key to a browser.
- `POST /webhooks/yookassa` accepts bounded JSON, stores an unverified signal, and returns HTTP 200 after persistence. A duplicate creates no new record. If the database is unavailable, no success acknowledgment is returned. Invalid JSON/event types receive 400; exceeding limits triggers limiter rejection.
- The Worker rechecks the object through the YooKassa API, verifying the amount, RUB, merchant account, `test`, and internal intent ID. A webhook alone cannot confirm payment. Unknown objects remain queued for rechecking: a notification may arrive before the creation response.
- Stored webhooks and operations are processed after restart. An expired lease allows another Worker to take over; the lease token protects the record from an old executor. Received money is not rolled back because another operation failed.

Other applications cannot obtain results through the HTTP API. There is one service key for the single trusted application of the initial stage; multi-application authorization requires separate configuration.

## Database and migration

EF Core 10, Npgsql, the `payments` schema, and a separate migration history table. The initial migration creates intents, operations with foreign keys, durable command deduplication, a webhook inbox, and MassTransit EF inbox/outbox. `CommandId` is preserved independently of transport inbox cleanup. Amounts are integer kopecks; database constraints prohibit negative refunds, refunds exceeding the payment, and duplicate provider IDs.

`InitialPayments.Down` drops the service tables and all payment data. It is a technical schema rollback for an empty environment, not a safe rollback of active payments. Once used, recovery requires backups and provider reconciliation; no automatic database rollback is performed. Before production use, establish a verified backup/restore process, provider registers, and financial incident handling described in the architecture design.

## Validation

From the repository root:

```powershell
dotnet sln OpenCRM.sln list
dotnet build services/payments/tests/Payments.Tests/Payments.Tests.csproj
dotnet test services/payments/tests/Payments.Tests/Payments.Tests.csproj --filter FullyQualifiedName~.Unit.
dotnet test services/payments/tests/Payments.Tests/Payments.Tests.csproj --filter FullyQualifiedName~.Integration.
dotnet build OpenCRM.sln
```

Integration tests require Docker and start their own temporary PostgreSQL/RabbitMQ instances on random ports. The HTTP adapter is checked using a controlled simulator; automated tests do not perform real charges or call the real YooKassa API. These checks need neither the main database nor a running OpenCRM instance.

Current limitations: reconciliation of external registers, payments unknown locally, and operator notifications require separate acceptance testing. Historical payments/receipts remain in OpenCRM; history is not automatically transferred to the new database. Studio acquiring and automatic charges remain future extensions. Automated tests have not used the real YooKassa API or live charges. Live mode and external webhook delivery have not been verified.

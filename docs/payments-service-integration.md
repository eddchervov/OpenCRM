# Connecting OpenCRM to Payments

**English** | [Русский](payments-service-integration.ru.md)

One-off purchases and renewals of personal and organization subscriptions for OpenCRM are integrated. Studio work order acquiring, automatic payments, and entitlement changes after refunds are outside this scope. A refund is recorded as a monetary fact; an administrator changes already granted access manually.

## Source of state

Payments owns YooKassa requests, store keys, monetary facts, refunds, and its database. OpenCRM owns the purchase terms snapshot, beneficiary entitlements, and access grants. Applications exchange contracts through a separate RabbitMQ vhost; neither accesses the other's database directly.

OpenCRM saves the purchase, payment, and immutable command in one transaction before network access. The client receives HTTP 202 and PurchaseId. GET /api/v1/payments/purchases/{purchaseId} is available only to the purchase owner; the result page uses that exact ID. A URL or payment does not mean the subscription is already activated.

The outbox repeats original IDs and bodies; the inbox verifies EventId and version. A beneficiary reservation blocks simultaneous incompatible purchases. Plan terms and features are snapshotted at creation; for renewal, the snapshot applies from GrantedFrom. Subscription grants and AppliedAt commit together while locking the beneficiary and purchase. A database failure after payment retries entitlement application, not the charge.

Historical payments retain PaymentProviderCode=yookassa and the previous route. New purchases use payments-service. Old handlers, reconciliation, and manual monetary status changes do not modify these purchases. A purchase accepted through the microservice never falls back to the old gateway on a network failure or configuration change.

## Test connection configuration

PaymentsService:Enabled=false by default. Supply these settings through a protected environment; never commit real values:

| OpenCRM setting | Purpose |
| --- | --- |
| PaymentsService__Enabled=true | Connect the separate bus, consumer, dispatch, and existing purchase recovery. |
| PaymentsService__AcceptNewPurchases=false | Stop new purchases while retaining Enabled=true: old purchases, events, and access grants continue processing. |
| PaymentsService__MerchantAccountId | Your selected test or live shop ID; matches Payments:MerchantAccountId and is fixed in the command. |
| PaymentsService__ExpectedIsTest | Must match the provider mode: `true` for `YooKassaTest`, `false` for `YooKassaLive`. Default: `true`. |
| PaymentsService__BaseUrl | Private Payments URL; HTTPS, or HTTP for loopback/an explicitly configured exact `InternalHttpHost`. |
| PaymentsService__ServiceKey | Secret shared with Payments:ServiceKey, at least 32 characters. |
| PaymentsService__Host / Port / VirtualHost | Separate RabbitMQ vhost, default payments; not /. |
| PaymentsService__Username / Password | Account restricted to the required financial transport. |
| PaymentsService__ReturnUrl | HTTPS /payment/complete address accounting for external PathBase. |
| PaymentsService__AllowedReturnUrls__0 | Allowed base HTTPS return URL without query/fragment. |
| PaymentsService__AllowedConfirmationHosts__0 | Trusted YooKassa confirmation domain: exact domain and its subdomains. |

Payments uses the same base URLs through Payments:AllowedReturnUrls. The command adds only ?purchaseId={ID:D}. Do not allowlist arbitrary client URLs. ServiceKey is not a shop key; YooKassa keys reside only in Payments. Do not reuse the old gateway's live shop/secret to enable the new path automatically.

Payments supports `Disabled`, `YooKassaTest`, and `YooKassaLive`. `Disabled` stores a rejection receipt and `payments_disabled` result without an executable operation. Active modes require consistent merchant ID, test/live flag, and secret-key prefix. Previously created operations without a fixed mode are migrated to `ManualReview`; enabling a mode or changing merchant must not revive unknown old intents. Live mode is available in code but requires your own provider configuration and operational acceptance.

CreatePayment/RequestRefund use schema 2: immutable MerchantAccountId, IssuedAt, and NotAfter. The deadline for a new POST is fixed at command creation and limited to 23 hours; restoring an old backup does not restart it. Old operations without PostBefore allow only reconciliation of a known object and block new charges. Order/operation IDs and provider keys are reproduced from the original command; retries after database restoration use the same parameters. Timeouts cover response headers and body. Notifications and monetary events remain schema 1.

## Refunds and manual review

A full refund before access is granted closes the purchase without activation. Any partial refund before granting blocks automatic activation and requires review. A full or partial refund after granting does not shorten the subscription; the purchase receives refund_requires_access_review. Failed or unknown refunds persist as separate events with CommandId, OperationId, Status, and a safe error code, without replacing confirmed monetary amounts.

OpenCRM receives refund confirmations and failures. Authorized administrators can request a refund through the protected actions described in [Administrator actions](#administrator-actions); `RequestRefund` is dispatched through the financial outbox. A manually entered status is not a provider-confirmed refund. Authorization, amount confirmation, idempotency, and provider reconciliation remain mandatory.

## Journal and recovery

In OpenCRM: SubscriptionPurchases stores application state, SubscriptionPaymentCommands stores outgoing intents/leases/attempts, and SubscriptionPaymentEvents is the durable inbox with stages/codes. In Payments: ProviderOperations stores body/key/mode/lease; CommandReceipts handles deduplication; PaymentAuditEntries is a journal without UPDATE/DELETE; WebhookNotifications stores incoming signals. Raw payloads, keys, Authorization, headers, payment credentials, and error response bodies are never written to diagnostic ILogger output.

Each exception in the new flow receives Stage, identifiers, exception type, and a safe code; ILogger remains available when the database is unavailable. Financial event persistence and application are atomic. A webhook gets 200 only after its signal is saved; monetary facts are rechecked with provider GET. A webhook signature alone grants no access.

| Failure | Behavior and operator action |
| --- | --- |
| RabbitMQ unavailable or ACK lost | Command remains in outbox/lease; identical ID/body are retried with backoff up to 5 minutes. Check broker/vhost permissions; do not create a new checkout. |
| Duplicate/reordered event | Inbox and increasing version prevent duplicate grants or monetary rollback. Conflicting contents under one EventId are logged as Critical. |
| Webhook/event lost | Payments polls the provider; OpenCRM reads a private snapshot roughly once a minute before activation and every 15 minutes afterward; errors defer with journal entries. Refund operation results are also recovered. |
| GET 401/403/another owner's purchase | Log error and reconciliation delay. Previously persisted verified succeeded retains eligibility for activation; otherwise NeedsAttention. Only a fully matching snapshot can clear a temporary connection error; refunds or financial conflicts are not hidden. |
| Payments returns 404 after dispatch | With neither ACK nor order ID, only the original schema 2 command may resume, with intact hash, matching IDs/amount/currency/shop, and unexpired NotAfter. Preserve an active lease. Known orders, a saved ACK, or failed checks require quarantine/review; SentAt is not reset automatically. |
| Provider timeout/5xx after POST | OutcomeUnknown with original body/key; after 23 hours a new POST is forbidden, but GET may check a known object. A timeout is not rejection. |
| Amount/currency/merchant/test/metadata mismatch | ManualReview/NeedsAttention without an access grant; compare the original purchase and provider object. |
| Database unavailable/access grant failure | Transaction rolls back; payment remains confirmed and access application retries after recovery. Inspect ILogger, then the inbox/application journal. |
| Client retries/reloads the page | Preserve operation key and PurchaseId; check the existing owned purchase instead of starting another payment. |
| Message exhausts retries | Inspect payments-opencrm-commands_error, payments-opencrm-events_error, and *_skipped. After fixing the cause, restore delivery of the original message with the same domain IDs. |

Before enabling monetary processing, provide external log storage/retention and alerts for error queues, outbox age, activation_pending, needs_attention, and register discrepancies. The application does not create infrastructure alerts automatically. A lack of log errors does not replace reconciliation with the provider register.

## Migrations and rollback

OpenCRM adds 20261025071834_AddPaymentsServicePurchases; Payments adds 20261025140000_HardenPaymentExecutionAndAudit and 20261025180000_AddProviderOperationPostBefore. Validate the main file, Designer with DbContext/Migration, snapshot, and HasPendingModelChanges. Monetary snapshots/commands are protected from modification. Down for the new OpenCRM schema is forbidden when purchases/commands/events exist. Payments hardening Down is forbidden with financial audit or operations of unknown mode; PostBefore Down is forbidden with accepted operations carrying a deadline. An empty schema permits technical rollback; never automatically roll back an active database.

To stop sales, use AcceptNewPurchases=false with Enabled=true and restart a compatible OpenCRM version. The end-to-end test covers pending purchase → restart with new purchases blocked → new purchase rejected → previous purchase succeeded → one access grant. Enabled=false disables all financial transport and is appropriate for an empty/unconnected environment; startup with Enabled=false is blocked when any microservice purchases exist, including applied ones, because late refunds still need monitoring.

Application rollback must not disable consumption for accepted purchases or enable direct charging for them. Retain the recovery service or stop new purchases until a compatible version is available. Back up both databases and RabbitMQ under your own release process. Restoring an old backup of one database requires reconciling the other database, provider, and register; automatic discovery of all lost purchases in registers is not yet implemented.

## Validation and remaining boundaries

Automated tests use separate PostgreSQL/RabbitMQ containers and a controlled provider, with no real charges or real YooKassa API calls. End-to-end validation connects two processes, two databases, a separate bus, private HTTP, repeated commands/events, and consumer restart.

Before production integration, validate external HTTPS webhook/network access, register reconciliation/backup recovery, alerts, and live mode. Fake providers and readiness do not replace these checks. Contract sources: [YooKassa API format](https://yookassa.ru/developers/using-api/interaction-format), [webhooks](https://yookassa.ru/developers/using-api/webhooks).

## Validation with the real YooKassa test API

Keep the test shop credentials in your own secret store and supply them only to the test process. For a separately deployed service, configure `Payments__Mode=YooKassaTest`, its test merchant ID and `test_` secret; provider responses with `test=false` are rejected.

The optional [manual browser harness](../tests/OpenCRM.Infrastructure.Tests/Integration/PaymentsBrowserHarnessTests.cs) starts a temporary Payments service, PostgreSQL, and RabbitMQ. It requires Docker and a local HTTPS certificate. It does not start WebApp or automatically complete the browser acceptance scenarios.

| Harness environment variable | Meaning |
| --- | --- |
| `PAYMENTS_BROWSER_HARNESS=1` | Explicitly enables this manual test; otherwise it is skipped. |
| `PAYMENTS_BROWSER_PROVIDER=YooKassaTest` | Selects the real test API; the default is the controlled `Fake` provider. This is not a deployed service setting. |
| `PAYMENTS_BROWSER_SHOP_ID` | Numeric ID of your test shop. |
| `PAYMENTS_BROWSER_TEST_KEY` | Its `test_` secret, supplied through your protected process environment, never committed. |
| `PAYMENTS_BROWSER_MAIN_CONNECTION` | Optional connection string to a disposable main CRM database. When supplied, the harness seeds test scenario data into it. Never target a working installation. |
| `PAYMENTS_BROWSER_ARTIFACTS` | Optional private output directory; defaults to ignored `artifacts/payments-browser-qa`. Its temporary configuration contains local service credentials. |

After setting these variables in the test process environment, run from the repository root:

```text
dotnet test tests/OpenCRM.Infrastructure.Tests/OpenCRM.Infrastructure.Tests.csproj --filter FullyQualifiedName~PaymentsBrowserHarnessTests
```

The service listens at `https://localhost:7245`; the harness assumes the separately started test WebApp returns to `https://localhost:7244/payment/complete`. It waits for an operator-created `stop` file in the artifact directory or ends after 60 minutes. Its containers are temporary and disposed on shutdown. Retain any required payment facts before stopping it.

Operator acceptance with a real test shop must cover authentication, a successful test payment and single entitlement grant, partial/full refunds and manual access review, a repeated original idempotent command, and a rejected test-card attempt. These checks contact the external test API without charging real money; automated suites use controlled providers. External webhook delivery, register reconciliation, and backup recovery require separate acceptance. Local callback URLs are not publicly reachable, and this harness cannot enable live mode.

## Payment record in administration

The microservice record separates confirmed money, refunds, and the historical granted access period. Each monetary fact shows the administrator's next action. The YooKassa ID is separate from the internal microservice ID. The journal is collapsed by default; events have clear labels, codes expand separately, and there is no nested scrolling. Manual microservice status replacement is unavailable. An unknown purchase ID displays no result rather than substituting the last successful payment; canceled purchases do not activate access. Repeating a POST with the original key/body creates no second payment object, and a GET creates no object at all. Navigation to subscriptions opens the correct beneficiary's list, including stopped subscriptions.

## Administrator actions

Web and desktop clients share GET/POST `/api/v1/admin/billing/payments/{id}/service-actions`. Reading requires ViewBilling; execution requires ManageBilling; the author comes from the current session. POST persists a relational administrative operation before network access. The client retains the UUID and request body until a definitive response; repeating the same UUID returns the original operation, while altered body/author/payment is rejected. Monetary facts cannot be edited manually.

- “Проверить платёж” (Check payment) requests an immediate YooKassa read through the microservice. Request acceptance differs from actual confirmation of a new LastReconciledAt. Refunds and grant retries require a check within the last 5 minutes matching current facts.
- “Вернуть деньги” (Refund money) accepts the full available remainder or a partial kopeck-precision amount, reason, and confirmation. RequestRefund is written to the existing outbox. A pending/unknown refund reserves the purchase and blocks subsequent attempts and automatic access grants. Confirming the refund operation without a new monetary snapshot does not release the reservation.
- “Повторить выдачу доступа” (Retry access grant) revalidates confirmed payment, snapshot freshness, absence of refunds/conflicts, and the previously granted period. It uses the existing idempotent applicator and does not create a charge.
- “Завершить разбор” (Complete review) saves the decision, author, and comment independently of monetary facts. A new material conflict or refund requires review again. This neither removes technical restrictions nor changes access. Subscription renewal/stopping remain separate administrative actions.

The full reason, up to 500 characters, and author are retained in the local audit. The microservice contract reason is limited to 128 characters without splitting a surrogate pair; a long author ID is replaced with a stable SHA256 representation. After a definitive rejection without a monetary operation, another refund is allowed only after a fresh check and proof of safe rejection from related immutable journals. An unknown outcome does not qualify for this exception.

Migrations `20261025134829_AddSubscriptionPaymentAdminActions` and `20261025135503_AddVerifiedAdminPaymentSnapshot` were created with EF Core 10.0.7 and have consistent Designer/DbContext/Migration metadata and snapshot. Empty Down/Up is validated; both migrations prohibit rollback of a nonempty administrative financial journal.

## Server setup

The base installation keeps commercial mode and the payment transport disabled. To sell platform subscriptions, configure your own plans, enable `PlatformPolicy:Commercial:Enabled`, and connect Payments separately. Work-order payment tracking does not require this integration.

1. Provision a separate service database with a nonsuperuser role, a private RabbitMQ vhost, and restricted service accounts. Supply secrets through protected configuration outside the repository. Back up both application databases and preserve Data Protection keys before upgrades.
2. Start with `YooKassaTest`. Match the merchant ID, `Payments:IsTest`, and OpenCRM's `PaymentsService:ExpectedIsTest`. Live mode requires `YooKassaLive`, `IsTest=false`, a matching live key, and `ExpectedIsTest=false`; changing one flag alone is insufficient.
3. Keep the internal Payments API private. Use HTTPS or explicitly allow only the exact internal hostname through `PaymentsService:InternalHttpHost`; require the shared service key. Expose only the necessary provider callback, `/webhooks/yookassa`, through your own HTTPS proxy. Do not reuse a previous installation's routes or network topology without checking them.
4. Apply both applications' migrations and start their consumers before accepting purchases. Validate readiness, broker permissions, command/event delivery, and recovery after a service interruption. Use `AcceptNewPurchases=false` to stop new purchases while retaining processing of already accepted operations.
5. Arrange your own log retention, readiness checks, error/skipped-queue alerts, unknown-operation and reconciliation-delay monitoring, and operator notifications. No installation-specific monitoring script or schedule is supplied by OpenCRM.
6. Complete test-shop payment/refund, duplicate-message, access-grant, and backup-recovery acceptance before enabling live sales. Provider credentials, public TLS/DNS, and outbound provider access must be verified from the service environment. Readiness alone does not prove monetary processing.

Retain recovery capability for all accepted purchases, including late refunds. Do not discard monetary history, restore one database without reconciling its counterpart, or automatically roll back an active financial database. Backup retention and release acceptance belong to the operator of each installation.

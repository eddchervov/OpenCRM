# Payment service boundaries

**English** | [Русский](payments-service-design.ru.md) | [简体中文](payments-service-design.zh-CN.md)

The payment service implements technical communication with a payment provider. The web application owns organization permissions and commercial policy; the payment service does not assign roles or independently grant CRM access.

The base installation has `PlatformPolicy:Commercial:Enabled=false`: a subscription purchase is not required for core CRM work. The installation owner supplies seller details, available products, prices, receipt rules, provider keys, and allowed return URLs. No working seller configuration is included.

## Processing invariants

- A command contains a stable operation ID. Repeating the same command does not create a duplicate charge.
- Changed amounts, currency, or purchased items must not replace previously accepted operation terms under the same ID.
- External notifications are verified, persisted in an inbox, and processed with duplicate protection.
- Changes to monetary state are persisted separately from granting business entitlements. States reach consumers through a reliable outbox.
- An unknown request outcome requires provider reconciliation. Automatically repeating a potentially completed charge is unacceptable.
- A refund is a monetary fact. The application's commercial layer decides what happens to previously granted entitlements.
- Operation history, reasons for administrative changes, and provider IDs are retained for reconciliation; secrets are excluded from user responses and logs.

## Integration and validation

The service includes provider adapters; their presence does not enable payments or mean a particular seller is ready to accept money. See the [service README](../services/payments/README.md) and [application integration](payments-service-integration.md) for contracts and configuration.

Before connecting, validate idempotency, changed terms in a repeated command, duplicate webhooks, unknown outcomes, reconciliation, refunds, and repeated state delivery. Local automated checks do not replace acceptance testing of a configured provider. Manual [work order payment tracking](maintenance-services-payments.md) remains a separate mechanism and does not itself charge money through the payment service.

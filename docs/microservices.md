# OpenCRM microservices

**English** | [Русский](microservices.ru.md)

OpenCRM combines a modular CRM core with separate notification and payment services. The application CRM runs in an ASP.NET Core host; background integration tasks run in their own applications.

## Why they are separate

- **Independent deployment.** Update and run a service independently of the CRM web interface.
- **Own settings and data.** Connections, secrets, and the service database belong to that service.
- **Separate workers.** Queue processing and provider interaction happen outside the user's HTTP request.
- **Optional integration.** These services are not required for local registration and basic CRM work.

See [architecture](architecture-current.md) for web host composition and business module boundaries.

## Component map

| Component | Responsibility | Documentation |
| --- | --- | --- |
| WebApp and modules | Users, organizations, permissions, CRM operational data, documents, work order payment tracking | [Local setup](local-run.md) |
| Notifications | Delivery queues, templates, connected channel adapters, background attempts and statuses | [Service README](../services/notifications/README.md), [core integration](notifications.md) |
| Payments | Payment operations and statuses, provider integration, background reconciliation | [Service README](../services/payments/README.md), [core integration](payments-service-integration.md) |

## Notifications

Source: [`services/notifications`](../services/notifications); solution: [`NotificationService.slnx`](../services/notifications/NotificationService.slnx). The service contains an API and Worker. Enable specific channels and external providers through your own configuration.

The basic CRM uses `Notifications:Transport=Disabled`. To connect real delivery, prepare the service, RabbitMQ, its database, and the required channel parameters, then set the WebApp transport to `RabbitMq` with matching broker settings. Lack of SMTP does not block password registration or internal support requests.

Do not confuse disabled delivery with the test-only `InMemory` transport: it does not provide production email delivery.

For step-by-step parameters and commands, see [Notifications README](../services/notifications/README.md). The local container environment is described in [compose.yaml](../services/notifications/compose.yaml).

## Payments

Source: [`services/payments`](../services/payments). This service has its own API, storage, and provider parameters. Integration requires your own addresses, keys, allowed return URLs, and matching service settings.

OpenCRM commercial subscriptions are disabled by default. Enabling commercial mode and connecting a payment provider are separate actions. Tracking payments for customers' work orders in the CRM does not require selling subscriptions to the platform itself.

See [Payments README](../services/payments/README.md) for startup, parameters, and limitations; [payment service integration](payments-service-integration.md) for the connection to the core; and [compose.yaml](../services/payments/compose.yaml) for the container environment.

## Configure your installation

1. Start and validate the [basic CRM](local-run.md) first.
2. Choose the integration you need and read its service README.
3. Create separate service databases and supply secrets to the corresponding processes.
4. Apply the service's migrations and start its API/Worker according to its guide.
5. Configure matching addresses and service keys on both sides of the connection.
6. Check health endpoints, a test operation, and failure/retry behavior before connecting production providers.

Environment variable names and prefixes depend on the application. WebApp's environment does not automatically configure Notifications and Payments. See [configuration](configuration.md) and each service's README.

Preserve data and coordinate core/service contracts when upgrading. A PostgreSQL version change or moving its data directory requires separate preparation; do not replace existing databases or volumes with empty ones merely to restart.

# Service readiness checks

**English** | [Русский](service-health-monitoring.ru.md) | [简体中文](service-health-monitoring.zh-CN.md)

The payment and notification services expose internal HTTP endpoints:

| Endpoint | Purpose |
| --- | --- |
| `/health/live` | The process responds; dependencies are not checked. |
| `/health/ready` | Checks the service's configured dependencies, including PostgreSQL and the message bus. |

A successful check returns HTTP 200. Degraded and Unhealthy states return HTTP 503. Readiness does not confirm delivery of a particular email, payment processing, or every business scenario; validate these separately.

The web application exposes `/api/health/ready` to check its database connection. The services' `--migrate` mode applies migrations and exits before starting HTTP handlers.

The deployment owner determines installation URLs, proxies, request restrictions, monitoring, and alert recipients. Expose only the checks you need and do not disclose connection strings, secrets, or exception details externally. The core does not include a ready-made configuration for a particular server.

# OpenCRM overview

**English** | [Русский](project-overview.ru.md) | [简体中文](project-overview.zh-CN.md)

OpenCRM manages customers and the related operational workflows of organizations.

## Solution contents

- `src/OpenCRM.WebApp` — server web host, pages, and API.
- `src/OpenCRM.Frontend` — client interfaces and shared components.
- `src/OpenCRM.Modules.*` — business logic.
- `src/OpenCRM.Infrastructure.*` — storage and external component integrations.
- `services/notifications` and `services/payments` — separate background services.

Build the main set of projects using `OpenCRM.sln`. For configuration and local setup, see the root [README](../README.md); architectural boundaries are described in the [technical documentation](architecture-current.md).

# Application architecture

**English** | [Русский](architecture-current.ru.md) | [简体中文](architecture-current.zh-CN.md)

The solution separates the web host, business modules, client applications, and background services.

## Main components

- `src/OpenCRM.WebApp` — the neutral executable, configuration, and resources.
- `src/OpenCRM.Hosting` — shared web composition; compiles the common controllers and Razor views from the WebApp source tree for neutral and product hosts.
- `src/OpenCRM.Modules.*` — business modules and their contracts.
- `src/OpenCRM.Infrastructure.*` — access to the database, external services, and environment.
- `src/OpenCRM.Frontend` — client applications, administration interface, and shared UI packages.
- `services/notifications` and `services/payments` — separate services with their own solutions and configuration.
- `tests` and `services/*/tests` — test projects.

The main .NET solution is `OpenCRM.sln`. Each separate service may have its own solution, startup instructions, and configuration parameters.

## Changing components

Before changing cross-module interaction, inspect public contracts in `*.Contracts` and the existing dependency registrations. Supply connection settings through local configuration or environment variables; do not hard-code secrets or deployment-specific data in source files.

Module responsibilities, actual relationships, and configuration parameters are defined by the code and project files. UML diagrams in the neighboring directory are illustrative and should be updated alongside the corresponding architecture changes.

# Notification service configuration

**English** | [Русский](README.ru.md) | [简体中文](README.zh-CN.md)

Configure individual notification service processes through environment variables and local configuration excluded from the repository.

## Branding

The display name used in templates, email subjects, and `FromName` comes from the shared `Helpers.ProjectBrand`: it reads `configuration/brand.json`, linked into the output directory as `brand.json`. The `Brand__DisplayName` and `Brand__PublicBaseUrl` environment variables override values in that process.

After changing the shared `brand.json`, rebuild the service or install a consistent file version in the Worker's launch directory.

## Providers and sender

Configure real providers (SMTP and messengers), access keys, and the email sender address separately through environment variables. Email is disabled by default and requires explicit configuration to enable it. Do not put secrets in `brand.json` or the repository; use environment variables for local development. The notification project's own `dotnet user-secrets` is not linked to a shared WebApp identifier.

Restart the affected processes after changing settings.

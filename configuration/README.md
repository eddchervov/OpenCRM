# configuration

**English** | [Русский](README.ru.md) | [简体中文](README.zh-CN.md)

This directory contains the shared `brand.json` file used by every application in the solution.

## brand.json

File structure:

```json
{
  "Brand": {
    "DisplayName": "OpenCRM",
    "PublicBaseUrl": "https://localhost:7142",
    "TelegramUrl": "",
    "VkUrl": "",
    "InstagramUrl": ""
  }
}
```

- `DisplayName` — the project name in the interface, documents, and notification templates.
- `PublicBaseUrl` — the public HTTPS URL including the base path; it is used to construct canonical URLs, robots, sitemap, and payment return URLs.
- `TelegramUrl`, `VkUrl`, `InstagramUrl` — community links, when configured.

This file contains no secrets and must never contain them: provider keys, connection strings, and the email sender address are configured separately in each application. Environment variables are recommended for local secrets. Each project's `dotnet user-secrets` remains separate: there is no shared UserSecretsId for WebApp and the EF projects.

## How the file is used

The shared `Helpers.ProjectBrand` class reads the file. WebApp, notifications, payments, and desktop include it as a linked file named `brand.json` in the output directory. Server processes support the `Brand__DisplayName` and `Brand__PublicBaseUrl` environment variables; desktop applications read the shared JSON file.

After changing `brand.json`, rebuild the required applications or distribute the same file version to every process's launch directory.

Restart the processes that use the settings after changing them.

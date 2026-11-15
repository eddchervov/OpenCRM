# OpenCRM.Admin.Launcher

**English** | [Русский](README.ru.md)

This Windows application opens the existing Vue admin interface through Google Chrome in application mode, without tabs or an address bar. Forms, APIs, authentication, and business logic remain shared with the web application.

The panel URL comes from the shared `brand.json` copied to the application directory; the `OPENCRM_ADMIN_API_URL` environment variable overrides it. The default is the public URL from `brand.json`. For local development, set the base URL:

```powershell
$env:OPENCRM_ADMIN_API_URL = 'https://localhost:7142/'
```

Cookies are stored in `%LOCALAPPDATA%\Администрирование\AdminDesktop\Chrome`, so signing in again at every launch is unnecessary. The server continues to enforce roles and permissions on every request.

## Browser behavior

Chrome starts with `--disable-http2` and `--disable-quic`. These browser flags do not change server protocol settings.
Chrome reuses an existing process for its separate profile. After changing launch parameters, close only the Chrome instance using `%LOCALAPPDATA%\Администрирование\AdminDesktop\Chrome` and restart the application. Do not close the user's ordinary Chrome instance.

Validate startup by the page actually loading and a window title containing the project display name or the word “Администрирование” (Administration), rather than the mere presence of a Chrome process or window. The taskbar window name is `<display name> — Администрирование`. The application log is `%LOCALAPPDATA%\OpenCRM\AdminDesktop\Logs\application.log`.

## Build for a product installation

The same desktop sources can use a consumer's brand file without changing the core configuration. Pass an absolute `ProjectBrandFile` path to build/publish; it is copied to the output as `brand.json`. Without the property, the shared `configuration/brand.json` is used. The corresponding public base URL determines the API address; `OPENCRM_ADMIN_API_URL` can override the address at runtime.

```powershell
dotnet publish --configuration Release -p:ProjectBrandFile="C:/work/Product/brand.json"
```

Run from this project's directory and replace the example path. Keep the resulting configuration consistent with the consumer's web host. This changes configuration packaging; it does not duplicate the administrative UI or disable server-side access checks.

# Administer your installation

**English** | [Русский](administration.ru.md)

OpenCRM includes a web admin interface and a separate desktop administration application. Both use one server's administrative APIs and enforce its roles and permissions.

## First administrator

The base distribution provides no ready-made administrator account, shared password, public role assignment form, or automatic bootstrap. The owner of a new installation first registers an ordinary account, then separately grants it administrative access in their own database.

The following manual procedure is for **your own new database after migrations**. The `System` role grants full administrative access. Assign it only to a verified owner account; organization membership and organization roles remain separate.

### 1. Create your account

Start the CRM using the [setup guide](local-run.md) and register at `/auth/register` with your own password. Basic password sign-in requires neither SMTP nor email verification.

### 2. Find the exact user ID

Connect to your database through `psql` using an account with database administration privileges. For the guide's example local database:

```text
psql -h localhost -U opencrm_local -d opencrm_local
```

Replace the address in this query with your registered email:

```sql
SELECT "Id", "Email"
FROM identity."AspNetUsers"
WHERE "NormalizedEmail" = upper('owner@example.invalid');
```

Continue only if you receive exactly one row and have verified that it is your account. Copy its `Id`.

### 3. Assign the role in a transaction

Run the following example in `psql`; `\set` is a client command. Replace `REPLACE_WITH_OWNER_ID` with the verified `Id`.

```sql
\set owner_id 'REPLACE_WITH_OWNER_ID'

BEGIN;

INSERT INTO identity."AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
VALUES (gen_random_uuid()::text, 'System', 'SYSTEM', gen_random_uuid()::text)
ON CONFLICT DO NOTHING;

INSERT INTO identity."AspNetUserRoles" ("UserId", "RoleId")
SELECT u."Id", r."Id"
FROM identity."AspNetUsers" AS u
JOIN identity."AspNetRoles" AS r ON r."NormalizedName" = 'SYSTEM'
WHERE u."Id" = :'owner_id'
ON CONFLICT DO NOTHING;

SELECT u."Id", u."Email", r."Name" AS "Role"
FROM identity."AspNetUsers" AS u
JOIN identity."AspNetUserRoles" AS ur ON ur."UserId" = u."Id"
JOIN identity."AspNetRoles" AS r ON r."Id" = ur."RoleId"
WHERE u."Id" = :'owner_id' AND r."NormalizedName" = 'SYSTEM';
```

Check that the final query returns your single selected account and the `System` role. If everything is correct, complete the transaction:

```sql
COMMIT;
```

If the result differs from what you expected or the account is not found, cancel the transaction instead of confirming:

```sql
ROLLBACK;
```

This example does not create users, change passwords, or grant privileges to all registered accounts. An ordinary role named `Admin`, without configured administrative permissions, does not replace this first bootstrap.

### 4. Sign in again

Sign out of the current session and sign in again to refresh roles in the cookie/token. Then open **https://localhost:7142/admin-panel/index.html**.

Configure subsequent administrative roles and permissions through the admin interface. Do not manually grant `System` in bulk to ordinary members.

## Web admin interface

The web client is built with the frontend. Its address is `/admin-panel/index.html` or `/admin-panel/` on your installation's server. Guests are redirected to sign-in, and opening the URL directly does not bypass insufficient permissions.

## Desktop application

Start WebApp. From the repository root in Windows PowerShell:

```powershell
$env:OPENCRM_ADMIN_API_URL = 'https://localhost:7142/'
dotnet run --project src/OpenCRM.Frontend/Admin/OpenCRM.Admin.App
```

Use your own administrator account. The brand file specifies the display name and default URL; `OPENCRM_ADMIN_API_URL` overrides the server URL for the current process. HTTP is allowed only for loopback; configure trusted HTTPS for your installation.

Details: [desktop administration README](../src/OpenCRM.Frontend/Admin/OpenCRM.Admin.App/README.md).

A separate Windows Chrome launcher is also provided: the [`OpenCRM.Admin.Launcher`](../src/OpenCRM.Frontend/Admin/OpenCRM.Admin.Launcher) project. It opens the web admin interface in Chrome application mode. This is another way to launch the administrative interface, not a separate user or permissions system.

## Commercial mode

Commercial sections are hidden in both the web and desktop admin interfaces by default. Enable them by setting `PlatformPolicy__Commercial__Enabled=true` on the server and restarting the affected processes. Plans, prices, the payment provider, and secrets belong to your installation.

This mode does not bypass administrative permissions. See [configuration](configuration.md) and [microservices](microservices.md).

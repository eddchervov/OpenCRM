# Администрирование своей установки

[English](administration.md) | **Русский** | [简体中文](administration.zh-CN.md)

В OpenCRM есть веб-админка и отдельное настольное административное приложение. Они используют административные API одного сервера и проверяют его роли и разрешения.

## Первый администратор

Готового административного аккаунта, общего пароля, публичной формы назначения роли и автоматического bootstrap в базовой поставке нет. Владелец новой установки сначала регистрирует свой обычный аккаунт, затем отдельно назначает ему административный доступ в собственной БД.

Ниже — ручной порядок для **своей новой базы после миграций**. Роль `System` даёт полный административный доступ. Назначайте её только проверенной учётной записи владельца; членство и роли внутри организаций остаются отдельными.

### 1. Создайте свою учётную запись

Запустите CRM по [инструкции](local-run.ru.md), зарегистрируйтесь на `/auth/register` со своим паролем. Для базового парольного входа SMTP и подтверждение email не требуются.

### 2. Найдите точный идентификатор пользователя

Подключитесь через `psql` к своей БД учётной записью с правом её администрирования. Для примера локальной базы из инструкции:

```text
psql -h localhost -U opencrm_local -d opencrm_local
```

Замените адрес в запросе своим зарегистрированным email:

```sql
SELECT "Id", "Email"
FROM identity."AspNetUsers"
WHERE "NormalizedEmail" = upper('owner@example.invalid');
```

Продолжайте только если получили ровно одну строку и проверили, что это ваш аккаунт. Скопируйте его `Id`.

### 3. Назначьте роль в транзакции

Следующий пример выполняется в `psql`; `\set` — команда клиента. Вставьте проверенный `Id` вместо `REPLACE_WITH_OWNER_ID`.

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

Проверьте, что последний запрос вернул ваш единственный выбранный аккаунт и роль `System`. Если всё верно, завершите транзакцию:

```sql
COMMIT;
```

Если результат отличается от ожидаемого или аккаунт не найден, отмените транзакцию вместо подтверждения:

```sql
ROLLBACK;
```

Этот пример не создаёт пользователей, не меняет пароли и не назначает права всем зарегистрированным аккаунтам. Обычная роль с названием `Admin` без настроенных административных разрешений не заменяет этот первый bootstrap.

### 4. Войдите повторно

Выйдите из текущей сессии и войдите заново, чтобы обновились роли в cookie/токене. После этого откройте **https://localhost:7142/admin-panel/index.html**.

Дальнейшие административные роли и разрешения настраивайте средствами админки. Не используйте ручное массовое назначение `System` для обычных участников.

## Веб-админка

Веб-клиент собирается вместе с frontend. Его адрес — `/admin-panel/index.html` либо `/admin-panel/` на сервере вашей установки. Гость направляется на вход, а недостаточные права не обходятся прямым открытием URL.

## Настольное приложение

Запустите WebApp. В Windows PowerShell из корня репозитория:

```powershell
$env:OPENCRM_ADMIN_API_URL = 'https://localhost:7142/'
dotnet run --project src/OpenCRM.Frontend/Admin/OpenCRM.Admin.App
```

Используйте свою административную учётную запись. Файл бренда задаёт отображаемое название и адрес по умолчанию; `OPENCRM_ADMIN_API_URL` переопределяет адрес сервера для конкретного процесса. HTTP разрешён только для loopback; для своей установки настройте доверенный HTTPS.

Подробности: [README настольной админки](../src/OpenCRM.Frontend/Admin/OpenCRM.Admin.App/README.ru.md).

Отдельно поставляется Windows Chrome launcher: проект [`OpenCRM.Admin.Launcher`](../src/OpenCRM.Frontend/Admin/OpenCRM.Admin.Launcher). Он открывает веб-админку в режиме приложения Chrome. Это другой способ запуска административного интерфейса, не отдельная система пользователей и прав.

## Коммерческий режим

По умолчанию коммерческие разделы скрыты в веб- и настольной админках. Для их включения задайте `PlatformPolicy__Commercial__Enabled=true` на сервере и перезапустите соответствующие процессы. Планы, цены, провайдер платежей и секреты относятся к вашей установке.

Режим не отменяет административные права. Подробности: [конфигурация](configuration.ru.md), [микросервисы](microservices.ru.md).

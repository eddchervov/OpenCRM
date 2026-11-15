# NotificationService

[English](README.md) | **Русский**

Микросервис уведомлений для OpenCRM и других приложений. Один процесс Worker принимает
команды через RabbitMQ, хранит состояние в PostgreSQL, отправляет email/SMS и возвращает
результаты в очередь приложения. Команды принимаются только через RabbitMQ.
На внутреннем порту 8080 может работать обратный вебхук почтового провайдера;
HTTP API для отправки уведомлений отсутствует.

## Поток сообщений

```text
OpenCRM → notifications-opencrm-commands → Worker → PostgreSQL + outbox
                                                   ↓
                                         notifications-dispatch
                                                   ↓
                                          Worker → SMTP / SMS.ru
                                                   ↓
                                      PostgreSQL + outbox результата
                                                   ↓
OpenCRM ← notifications-opencrm-results ← RabbitMQ
```

Приём команд и доставка выполняются разными обработчиками в одном процессе. Внутренняя
очередь сохранена для совместимости с ранее принятыми заданиями и отложенной отправкой.

## Возможности

- Адресные команды создания уведомлений, обновления шаблонов и запроса состояния.
- Отдельные очереди команд и результатов для каждого приложения.
- Идемпотентность по ApplicationId и нормализованному IdempotencyKey.
- Transactional inbox/outbox для входящих команд; bus outbox для результатов доставки.
- Отложенная отправка, четыре попытки и восстановление аренды после падения Worker.
- Состояния queued, processing, retrying, provider-accepted, simulated, failed, not-found.
- LogOnly по умолчанию: настоящие письма и SMS не отправляются.
- PostgreSQL, RabbitMQ, JSON-логи, Docker Compose и CI.

`provider-accepted` означает принятие провайдером, а не подтверждённую доставку получателю.
`simulated` отдельно обозначает LogOnly. При настроенных callback Unisender подписанные события провайдера могут отдельно подтвердить доставку; принятие SMTP само по себе её не подтверждает.

Исходники находятся в `services/notifications` репозитория OpenCRM. Все проекты сервиса также включены в корневой `OpenCRM.sln`. Команды в этом документе выполняются из папки сервиса; `NotificationService.slnx` позволяет собирать и проверять его отдельно. Публикация Worker и миграции его базы остаются независимыми от OpenCRM.

## Структура

```text
src/
  Notifications.Contracts       Публичные команды, результаты и адреса очередей
  Notifications.Domain          Уведомления, шаблоны и попытки
  Notifications.Application     Регистрация, запрос состояния и доставка
  Notifications.Infrastructure  RabbitMQ, EF Core, outbox, SMTP и SMS.ru
  Notifications.Worker          Запуск обработчиков и планировщика
tests/
  Notifications.Tests           Проверки сервиса уведомлений
    Unit                        Изолированная логика без Docker
    Integration                 БД, брокер, Razor и регистрация DI
```

## Локальный запуск

Команды этого раздела выполняются из каталога `services/notifications`.

1. Скопировать `.env.example` в `.env` и задать собственные значения для пустых `POSTGRES_PASSWORD` и `RABBITMQ_PASSWORD`.
2. Выполнить `docker compose up --build -d`.
3. Проверить `docker compose logs --tail=100 worker` и consumers в RabbitMQ UI на `http://localhost:15672`.

Пример команд и обработчиков результатов: [интеграция OpenCRM](docs/opencrm-integration.ru.md).
Добавление приложения: `RabbitMq__Applications__1=catalog` в environment Worker.
Идентификаторы состоят из строчных латинских букв, цифр и дефиса, не длиннее 100 символов.
У каждого приложения должен быть отдельный пользователь RabbitMQ с доступом только
к собственным очередям. Список Applications создаёт маршруты, но не заменяет права брокера.

## Production

Worker использует стандартные ключи конфигурации .NET без префикса `NOTIFICATIONS_`. WebApplication.CreateBuilder загружает appsettings, а в Development — user-secrets проекта `src/Notifications.Worker`; затем Program добавляет общий `brand.json`, переменные окружения без префикса и аргументы командной строки. Для прямого запуска задавайте, например, `ConnectionStrings__NotificationsDb`, `RabbitMq__Host`, `RabbitMq__Password` и `Delivery__LogOnly`.

Для Production и прямого запуска Worker задавайте стандартные ключи без префикса: `ConnectionStrings__NotificationsDb`, `RabbitMq__Host`, `RabbitMq__Username`, `RabbitMq__Password` и `Delivery__LogOnly`. Храните секреты во внешнем хранилище и передавайте процессу Worker через переменные окружения; отслеживаемые appsettings содержат только имена параметров и пустые значения секретов.

EF design-time фабрика определяет окружение по `DOTNET_ENVIRONMENT`, затем по `ASPNETCORE_ENVIRONMENT`. В Development она загружает User Secrets с тем же UserSecretsId, что и Worker, затем — все переменные окружения без префикса; окружение имеет приоритет. Для миграций достаточно передать `ConnectionStrings__NotificationsDb`.

## Настройки интеграций

Адреса, порты, тайм-аут SMS.ru, отображаемое имя отправителя, список приложений и режимы
задаются в src/Notifications.Worker/appsettings.json и переопределяются окружением.
В классах Options нет рабочих адресов, портов, имён или режима LogOnly по умолчанию.
Пустые строки и коллекции в свойствах означают незаполненную настройку.

Worker проверяет обязательные параметры до запуска обработчиков. Delivery:LogOnly должен
быть задан явно; отсутствие значения не включает реальную отправку.
Email:Enabled, Sms:Enabled, Telegram:Enabled и TelegramClient:Enabled позволяют включить нужные каналы независимо.
При LogOnly=true секреты внешних провайдеров не требуются.

Для локального compose достаточно задать только `POSTGRES_PASSWORD` и `RABBITMQ_PASSWORD` в `.env`: compose передаёт остальные обязательные значения, включая `Delivery:LogOnly=true`, включённые email/SMS-каналы, SMTP-порт и параметры SMS.ru. В режиме `LogOnly` секреты SMTP и SMS.ru не проверяются и реальная отправка не выполняется.

Для реальной отправки только email установите DELIVERY_LOG_ONLY=false, EMAIL_ENABLED=true,
SMS_ENABLED=false и заполните SMTP_HOST, SMTP_PORT, SMTP_FROM_ADDRESS.
При заданном SMTP_USERNAME обязателен SMTP_PASSWORD; relay без аутентификации допустим.
Пустой `SMTP_FROM_NAME` в `.env.example` оставляет `Smtp:FromName` общему названию из `brand.json`; явное непустое значение переопределяет бренд.
Для реальных SMS включите SMS_ENABLED=true и задайте SMSRU_API_KEY.
Для сообщений владельцу включите TELEGRAM_ENABLED=true, задайте TELEGRAM_BOT_TOKEN и
TELEGRAM_OWNER_CHAT_ID. Бот отправляет сообщения только получателю `owner`.
Если прямое соединение с Bot API недоступно, можно использовать шлюз в `telegram-relay`.
После его публикации задайте TELEGRAM_RELAY_URL (HTTPS-адрес с `/sendMessage`) и
TELEGRAM_RELAY_KEY. При таком режиме токен бота и ID чата задаются как секреты Worker
`TELEGRAM_BOT_TOKEN`, `TELEGRAM_OWNER_CHAT_ID`, а `RELAY_KEY` должен совпадать с
TELEGRAM_RELAY_KEY в NotificationService. Прямой режим сохраняется, если адрес шлюза пуст.
Для реальной отправки по-прежнему нужны TELEGRAM_ENABLED=true и DELIVERY_LOG_ONLY=false.
OpenCRM отдельно включает постановку сообщений через TelegramAdminNotifications__Enabled=true.
Шлюз публикуется из каталога `telegram-relay`: `npx wrangler deploy`, затем
`npx wrangler secret put RELAY_KEY`, `npx wrangler secret put TELEGRAM_BOT_TOKEN`
и `npx wrangler secret put TELEGRAM_OWNER_CHAT_ID`. После этого укажите выданный
HTTPS-адрес Worker с `/sendMessage` в TELEGRAM_RELAY_URL. RELAY_KEY должен совпадать
с TELEGRAM_RELAY_KEY; секреты не записывайте в `wrangler.jsonc`.
Для клиентского Telegram используйте отдельный канал `telegram-client`: включите
TELEGRAM_CLIENT_ENABLED=true, задайте TELEGRAM_CLIENT_CREDENTIAL_BASE_URL (HTTPS-адрес OpenCRM),
TELEGRAM_CLIENT_CREDENTIAL_KEY, TELEGRAM_CLIENT_RELAY_URL (Worker с `/sendOrganizationMessage`)
и TELEGRAM_CLIENT_RELAY_KEY. Ключ OpenCRM должен совпадать с
TelegramOrganizationNotifications__ServiceKey, а relay-ключ — с секретом Worker RELAY_KEY.
Клиентский получатель передаётся как `<organizationGuidN>:<chatId>`. Для отправки Worker
запрашивает токен через GET `/api/internal/notification-bots/telegram/{organizationId:N}`
с заголовками X-Notification-Service-Key и X-Notification-Key. Ответ содержит `botToken`;
токен не попадает в очередь уведомлений или базу NotificationService.
Worker также предоставляет защищённые POST `/getOrganizationBot` и
`/setOrganizationWebhook` для проверки токена и регистрации webhook. Последний задаёт
адрес `/updates/{organizationId}` на самом Worker. Входящие обновления Worker пересылает
только на фиксированный адрес OpenCRM из `OPENCRM_BASE_URL`, в
`/api/internal/notification-bots/telegram/{organizationId}/updates`, сохраняя
`X-Telegram-Bot-Api-Secret-Token` для проверки в OpenCRM. Укажите
`npx wrangler secret put OPENCRM_BASE_URL` при настройке Worker. После первого deploy
задайте URL самого Worker командой `npx wrangler secret put TELEGRAM_WEBHOOK_BASE_URL`;
Worker использует эту фиксированную основу для адреса webhook, а не заголовок Host.
Для клиентских сообщений MAX включите MAX_ENABLED=true и задайте MAX_CREDENTIAL_BASE_URL
(HTTPS-адрес OpenCRM) и MAX_CREDENTIAL_KEY. Этот ключ должен совпадать с
MaxNotifications__ServiceKey в OpenCRM; там доставка включается через
MaxNotifications__Enabled=true. Токены ботов организаций остаются в OpenCRM:
NotificationService запрашивает нужный токен перед отправкой и не хранит его в очереди.
Неверные URL, порты и тестовые значения вместо ключей останавливают запуск.

Design-time фабрика EF не подставляет локальную базу или пароль: строка подключения обязательна, в Development она берётся из user-secrets Worker или `ConnectionStrings__NotificationsDb`, в Production — из этой переменной окружения.
Режим --migrate проверяет только базу и не требует RabbitMQ или провайдеров.

## Каналы и провайдеры

Бизнес-логика и команды работают с каналами email/sms/telegram через INotificationChannelSender.
Выбор адаптера задаётся отдельно от его параметров подключения:

```json
{
  "Sms": { "Enabled": true, "Provider": "smsru" },
  "Email": { "Enabled": true, "Provider": "smtp" },
  "Telegram": { "Enabled": false, "Provider": "telegram" },
  "TelegramClient": { "Enabled": false, "Provider": "telegram" }
}
```

Параметры SmsRu (BaseUrl, ApiKey и другие) относятся только к адаптеру SMS.ru;
Smtp содержит настройки SMTP-адаптера, Telegram — BotToken, OwnerChatId и
RequestTimeoutSeconds. Эти параметры не являются общими настройками канала.
В окружении выбор задаётся через SMS_PROVIDER, EMAIL_PROVIDER и TELEGRAM_PROVIDER.
Настройки выключенного канала не проверяются и его адаптер не регистрируется.

Сейчас реализованы smsru для SMS, smtp для email, telegram для админских сообщений и
telegram-client для клиентских сообщений от имени организации.
Неизвестное имя провайдера останавливает
запуск: оно не заменяется неявно на SMS.ru. Для другого сервиса добавляется реализация
INotificationChannelSender, её регистрация и проверка параметров в Infrastructure.
После этого адаптер выбирается в конфигурации. OpenCRM, сообщения RabbitMQ,
NotificationDispatcher и модель базы остаются общими для всех провайдеров.
Одной замены BaseUrl недостаточно: протокол и формат ответа у поставщиков различаются.

## Шаблоны писем

Системные письма находятся в Templates/OpenCRM/V1 проекта Infrastructure как .cshtml
с общим макетом и типизированными моделями. OpenCRM передаёт TemplateKey, TemplateVersion
и Data; регистрировать системные шаблоны через брокер не требуется.
Сервис проверяет параметры и сохраняет готовое письмо до queued.
Отложенная отправка и повторы используют одно и то же содержимое.
Оформление и доступ задаёт SystemTemplates:Applications: у профиля приложения есть
BrandName и список Templates. Добавление клиента не требует правок кода.
Редактируемые рассылки и SMS сохраняют отдельный механизм шаблонов из БД.

[Шаблоны, модели и версии](docs/templates.ru.md) ·
[Примеры параметров](examples/system-emails)

## Миграции

Таблицы и история EF используют схему `notifications`. Версии шаблонов и снимки писем
добавляет миграция 20260909054007_VersionedTemplateSnapshots. Начальная миграция сохранена.
Порядок обновления и ограничения отката описаны в [инструкции шаблонов](docs/templates.ru.md).

В production автоматические миграции отключены. Отдельный запуск из образа Worker:

```bash
docker run --rm --env ConnectionStrings__NotificationsDb notification-service-worker --migrate
```

Перед этой командой задайте `ConnectionStrings__NotificationsDb` в окружении запуска и используйте собранный вами образ. Имена подстановки из Compose `.env` не заменяют ключи конфигурации .NET Worker.

Этот режим применяет миграции и завершает процесс без запуска consumers и отправки уведомлений.
Для команд EF с SDK используйте Infrastructure как startup project с design-time фабрикой:

```bash
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/Notifications.Infrastructure --startup-project src/Notifications.Infrastructure
```

Команда EF в Development читает строку подключения из user-secrets Worker, затем переменные окружения; в Production — только из окружения. Сам `.env` она не загружает.
[SQL объединения схем](scripts/maintenance/consolidate-schema.sql) сохранён как ручной
скрипт обслуживания, повторный запуск не нужен.

## Проверка

```bash
dotnet build NotificationService.slnx -c Release
dotnet test NotificationService.slnx -c Release
```

Контейнерные интеграционные тесты требуют работающий Docker и сами создают/удаляют
изолированные PostgreSQL 15 и RabbitMQ 4. Они используют тестового отправителя без
внешних писем/SMS. Проверки Razor и регистрации DI работают без Docker.
Без Docker доступны модульные проверки:

```bash
dotnet test NotificationService.slnx -c Release --filter "FullyQualifiedName~.Unit."
```

Подробнее о границах, гарантиях и ограничениях: [архитектура](docs/architecture.ru.md).

На Windows при ручном указании Docker Desktop endpoint используйте формат
`$env:DOCKER_HOST = "npipe://./pipe/dockerDesktopLinuxEngine"` для текущего процесса PowerShell.
Строка Docker CLI с четырьмя слешами после `npipe:` не подходит используемой версии Testcontainers.

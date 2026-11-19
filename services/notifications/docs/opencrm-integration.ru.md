# Подключение OpenCRM через RabbitMQ

[English](opencrm-integration.md) | **Русский** | [简体中文](opencrm-integration.zh-CN.md)

OpenCRM уже использует исходящую очередь EmailMessages и обработчики результатов.
Рабочие приложения зависят только от Notifications.Contracts. В текущем решении это
ProjectReference на services/notifications/src/Notifications.Contracts в общем репозитории.
В CI приложения и Worker собираются из одного checkout; при выпуске сохраняется согласованность версий контрактов и потребителей.

## Адреса

Production: notifications-opencrm-commands и notifications-opencrm-results.
Development: notifications-opencrm-development-commands и notifications-opencrm-development-results.
Брокер: адрес и сеть задаёт оператор развёртывания, порт по умолчанию 5672.
Локальный OpenCRM подключается к 192.0.2.1:5672.
RabbitMq:Applications в Worker содержит разрешённые идентификаторы приложений.
Имя приложения у клиента задаётся конфигурацией RabbitMq:ApplicationId.
Очереди клиентских переписок (`TelegramChat` и `ConversationDelivery`) Worker создаёт
только для `Conversations:ApplicationId`; этот идентификатор должен входить в
`RabbitMq:Applications` и совпадать с `RabbitMq:ApplicationId` приложения OpenCRM.
Если разрешено несколько приложений, `Conversations:ApplicationId` обязателен.
Автовыбор разрешён только при одном элементе в `RabbitMq:Applications`. Не подключайте
production и development переписки к одному экземпляру Worker: у него общий адрес API
и ключ service-to-service.

Очередь результатов относится к одной базе приложения. Не подключайте к production-очереди
локальную базу. Команды передаются MassTransit Send со стандартным envelope, а не Publish.
NotificationDispatchRequested — внутренний контракт Worker.

## Команды и результаты

SendNotification принимает CreateNotificationRequest: IdempotencyKey, Channel, Recipient,
TemplateKey, Data и TemplateVersion. Для Razor-писем Subject/Body не задаются.
Повтор задания использует прежний IdempotencyKey. GetNotificationStatus возвращает
NotificationStatusChanged или not-found в постоянную очередь результатов.

Состояния queued/processing/retrying/provider-accepted/simulated/failed описывают обработку.
Provider-accepted ещё не означает доставки адресату; simulated исключает реальную отправку.
Content содержит готовые Subject, Body, IsHtml и TextBody для журнала приложения.

Админские сообщения используют канал `telegram` и получателя `owner`.
Сообщения клиентов от имени организации используют отдельный канал `telegram-client`
и получателя `<organizationGuidN>:<chatId>`. Worker получает токен через
`GET https://<opencrm-host>/api/internal/notification-bots/telegram/{organizationId:N}`
с заголовками `X-Notification-Service-Key`, `X-Notification-Key` и `X-Notification-Recipient` (ID чата из получателя); успешный ответ:
`{"botToken":"..."}`. Токен сразу передаётся HTTPS-шлюзу и не сохраняется
в очереди или базе уведомлений.

Владелец организации проверяет токен через защищённый Worker-маршрут
`POST /getOrganizationBot` с телом `{"bot_token":"..."}`. Установка webhook выполняется
через `POST /setOrganizationWebhook` с телом `{"bot_token":"...","organization_id":"<uuid>","secret":"<telegram-secret>"}`.
Telegram вызывает `POST https://<worker-host>/updates/{organizationId}`; Worker
пересылает тело и `X-Telegram-Bot-Api-Secret-Token` на фиксированный адрес
`https://<opencrm-host>/api/internal/notification-bots/telegram/{organizationId}/updates`.
OpenCRM должен сверить переданный секрет с секретом этой организации.

OpenCRM регистрирует NotificationResultConsumer для NotificationStatusChanged и
NotificationCommandRejected, а NotificationDeliveryConsumer — для NotificationDeliveryReported.
Endpoint использует ConfigureConsumeTopology=false. Состояния и счётчики обновляются
в транзакции; повторные события доставки пропускаются по EventId.

Другие клиенты могут регистрировать неизменяемые версии шаблонов БД через
UpsertNotificationTemplate. Тогда они также должны принимать NotificationTemplateUpdated.
OpenCRM эту команду не использует: письма из редактора оформляет system/custom-email.

## Razor-шаблоны версии 1

- system/email-confirmation: name, confirmationUrl.
- system/password-reset: name, resetUrl.
- system/login-code: code — восемь цифр.
- system/password-reset-mobile: userId, code, resetUrl.
- system/support-message: messageId, typeName, name, email, phone, topic, text.
- system/subscription-notice: kind (upcoming, paid-expired, demo-expired, demo-available),
  organizationName, planName, endDate, daysRemaining, durationDays, actionUrl.
- system/custom-email: subject, htmlBody — доверенный HTML из редактора приложения.
- system/subscription-expiring: name, subscriptionName, endDate, renewalUrl.

Ключи разрешаются в SystemTemplates:Applications:<applicationId>:Templates.
Профили opencrm и opencrm-development включены в базовый appsettings.
Шаблоны и модели находятся в Templates/OpenCRM/V1. Данные не исполняются как Razor;
в custom-email HTML вставляется без экранирования, поэтому его источник должен быть доверенным.
[Правила и версии](templates.ru.md).

## События провайдера

При Email:Provider=unisender-go адаптер добавляет application_id и notification_key
в global_metadata заголовка X-UNISENDER-GO. UnisenderGo:TrackLinks и TrackRead управляют
отслеживанием. Worker проверяет подпись вебхука и передаёт NotificationDeliveryReported
в адресную очередь приложения. OpenCRM не знает формат HTTP-запроса или ключ провайдера.

События без application_id или notification_key игнорируются. Совместимости со старым протоколом нет.
Обычный smtp не добавляет заголовки Unisender.

О миграциях и настройках интеграций см. [../README.md](../README.ru.md) — разделы
«Настройки интеграций» и «Миграции»; конфигурации nginx находятся в этом же каталоге `docs`.
При обновлении сначала требуется новая сборка Worker с шаблонами и миграциями,
затем OpenCRM и маршрут HTTPS-вебхука. Запущенный старый Worker новых контрактов не поддерживает.

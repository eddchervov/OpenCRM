# Сервис оплаты подписок opencrm.example.invalid

[English](README.md) | **Русский**

Автономный Payments на .NET 10 со своей PostgreSQL и RabbitMQ. Все три проекта включены в `OpenCRM.sln`: `Payments.Contracts` и `Payments.Service` находятся в `Services/Payments`, `Payments.Tests` — в `Tests`. Проекты не ссылаются на OpenCRM. Checkout и Billing подключены через отдельный финансовый транспорт; [инструкция интеграции и восстановления](../../docs/payments-service-integration.ru.md), порядок production описан в разделе «Серверная установка» этого документа.

Это отдельный этап [проекта архитектуры](../../docs/payments-service-design.ru.md): приём команд, сохранение операций, адаптер тестовой и рабочей ЮKassa, webhook inbox, сверка, возвраты и надёжная доставка состояния. Выдача подписки, промокоды и текущее оформление чеков остаются в основном приложении.

## Режимы и границы

- `Disabled` — режим по умолчанию. Сохраняются receipt и отказ `payments_disabled`; исполняемая денежная операция не создаётся. Включение режима не запускает отклонённые команды.
- `YooKassaTest` — явный режим тестового магазина. Нужны shop ID и секрет с префиксом `test_`; проверяется `test=true` в ответе.
- `YooKassaLive` — рабочий магазин с явным `IsTest=false`, числовым shop ID и ключом `live_`. Режим поручения, сохранённого платежа, магазина и ответа провайдера должны совпадать.
- Поддерживается одностадийная разовая оплата подписки с `capture=true`. Автоплатежи, сохранение карт и эквайринг студий не включены. `Purpose=subscription`, `InitiationMode=customer`; другие значения отвергаются.
- Неопределённый исход не означает отказ. Повтор сохраняет исходные тело/ключ; после 23 часов от первой возможной отправки новый POST блокируется и операция требует разбора. GET известного объекта остаётся доступен после этого срока.
- Полный и частичный возвраты имеют отдельные команды, операции, ключи и резерв сумм. Состояние денег подтверждается API провайдера. Billing фиксирует возврат и требует ручного разбора доступа; автоматическое сокращение подписки не выполняется.

## Запуск отдельного контура

Выполнить в PowerShell 7 из корня репозитория. Для каждого запуска конфигурации пароли должны сохраняться прежними, если используются существующие volumes. Пример создаёт новые случайные значения для первого запуска. Сохраните их в собственном защищённом хранилище и используйте те же значения при последующих запусках с этими volumes:

```powershell
$env:PAYMENTS_POSTGRES_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$env:PAYMENTS_RABBITMQ_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$env:PAYMENTS_SERVICE_KEY = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
docker compose -f services/payments/compose.yaml up -d --build
```

Стек `payments-service` использует собственные volumes, БД `payments`, пользователя `payments` и RabbitMQ virtual host `payments`. Контейнер миграций завершается до старта сервиса. При обычном запуске приложение не применяет миграции автоматически. Контейнер приложения работает без root.

Локальные адреса: HTTP `http://localhost:5087`, PostgreSQL `localhost:5434`, AMQP `localhost:5674`, RabbitMQ management `http://localhost:15674`. Порты привязаны только к loopback. Для внешнего webhook нужны отдельный HTTPS reverse proxy и тестовый магазин; этот Compose не является боевым развёртыванием.

Для выполнения тестовых запросов ЮKassa задать `PAYMENTS_MODE=YooKassaTest`, `PAYMENTS_SHOP_ID` и `PAYMENTS_TEST_SECRET_KEY` в защищённом окружении и пересоздать только сервис Payments. Эти значения не должны браться из настроек основного приложения. Переключение уже принятых операций между merchant account не поддерживается: аккаунт фиксируется в поручении, несовпадение блокирует исполнение.

Для запуска без Docker приложение принимает `ConnectionStrings__PaymentsDb`, `Payments__ServiceKey`, `RabbitMq__Host`, `RabbitMq__Port`, `RabbitMq__VirtualHost`, `RabbitMq__Username`, `RabbitMq__Password`. Значения из `appsettings.json` не содержат паролей. Миграции выполняются отдельным запуском:

```powershell
dotnet run --project services/payments/src/Payments.Service -- --migrate
dotnet run --project services/payments/src/Payments.Service
```

`/health/live` проверяет процесс, `/health/ready` — доступность схемы Payments и транспорта MassTransit. Здоровый сервис в режиме `Disabled` не означает возможность списания.

## Контракты

Команды посылаются адресно через MassTransit `Send` в `queue:payments-opencrm-commands`. HTTP-команды создания оплаты отсутствуют. Доступ к RabbitMQ vhost и очередям выдаётся только доверенному приложению; поле `ApplicationId` не заменяет транспортную авторизацию.

| Контракт | Основные поля |
| --- | --- |
| `CreatePayment` | `CommandId`, `ApplicationId=opencrm`, `PurchaseId`, `AmountMinor` в копейках, `Currency=RUB`, описание, `ReturnUrl`, инициатор/покупатель `BuyerId`, получатель доступа `BeneficiaryId`. |
| `RequestRefund` | `CommandId`, `ApplicationId`, `PurchaseId`, сумма в копейках, причина и автор запроса. |
| `PaymentStateChanged` | `EventId`, `PurchaseId`, ID поручения, возрастающая `AggregateVersion`, состояние создания и платежа, суммы, provider ID, ссылка, признак теста. |
| `PaymentCommandRejected` | ID команды/покупки и код отказа до создания новой денежной операции. |
| `PaymentOperationStateChanged` | ID операции/команды, Kind/Status, безопасный код ошибки; в том числе отменённый/отклонённый возврат. |

Новый клиент ссылается только на `Payments.Contracts`. Пример команды из отдельного тестового отправителя:

```csharp
var purchaseId = Guid.NewGuid();
var issuedAt = DateTimeOffset.UtcNow;
var endpoint = await bus.GetSendEndpoint(new Uri("queue:payments-opencrm-commands"));
await endpoint.Send(new CreatePayment(
    Guid.NewGuid(),
    "opencrm",
    purchaseId,
    125_000,
    "RUB",
    "Подписка opencrm.example.invalid",
    $"https://opencrm.example.invalid/payment/complete?purchaseId={purchaseId:D}",
    "test-buyer",
    "test-organization",
    SchemaVersion: 2,
    IssuedAt: issuedAt,
    NotAfter: issuedAt.AddHours(23),
    MerchantAccountId: "<shopId тестового магазина>"));
```

Результаты отправляются в отдельную durable очередь `payments-opencrm-events`. Она накапливает сообщения, пока потребитель не подключён. Подтверждение брокера не означает выдачу подписки. Billing хранит долговечный inbox и последнюю версию; выдача доступа и AppliedAt атомарны. Подтверждение выдачи доступа обратно в Payments пока не реализовано; состояние применения хранится и проверяется в OpenCRM.

Повтор `CommandId` с тем же содержимым ничего не создаёт, с другим содержимым получает отказ. Повтор `PurchaseId` с неизменным поручением и новым `CommandId` не создаёт второй платёж. Нельзя менять цену, валюту или получателя уже принятого поручения. Billing резервирует незавершённую покупку получателя; несовместимые покупки под разными PurchaseId блокируются до известного результата.

`ReturnUrl` должен точно совпасть с базовым адресом из `Payments:AllowedReturnUrls` плюс `?purchaseId={PurchaseId:D}`; список по умолчанию содержит только текущий HTTPS-маршрут завершения оплаты. При включении самостоятельного тестового интерфейса его разрешённый URL задаётся явно. Передача суммы ограничена положительным RUB-платежом до 1 000 000 рублей.

## HTTP и обработка уведомлений

- `GET /internal/payments/opencrm/{purchaseId}` требует `X-Payments-Key`. Возвращает проверенное состояние и операции без тела запроса, провайдерских ключей и данных банковской карты. Это внутренний серверный API: его ключ нельзя выдавать браузеру.
- `POST /webhooks/yookassa` принимает ограниченный JSON, сохраняет непроверенный сигнал и отвечает HTTP 200 после записи. Повтор не создаёт новую запись. БД недоступна — успешного подтверждения нет. Неверный JSON/тип события получает 400, превышение лимита — отказ ограничителя.
- Worker перепроверяет объект через API ЮKassa, сверяет сумму, RUB, merchant account, `test` и внутренний ID поручения. Webhook не может самостоятельно подтвердить оплату. Неизвестный объект остаётся в очереди повторной проверки: уведомление может опередить ответ создания.
- Сохранённые вебхуки и операции обрабатываются после перезапуска. Просроченная аренда допускает новый Worker; токен аренды защищает запись от старого исполнителя. Полученные деньги не откатываются из-за ошибки другой операции.

Чужие приложения не получают результаты через HTTP API. Сервисный ключ один для единственного доверенного приложения первого этапа; многоприложенная авторизация потребует отдельной конфигурации.

## База и миграция

EF Core 10, Npgsql, схема `payments`, отдельная таблица истории миграций. Начальная миграция создаёт поручения, операции с FK, долговечную дедупликацию команд, webhook inbox и EF inbox/outbox MassTransit. `CommandId` сохраняется независимо от очистки транспортного inbox. Суммы — целые копейки; ограничения БД запрещают отрицательные/превышающие оплату возвраты и дубли provider ID.

`InitialPayments.Down` удаляет таблицы сервиса вместе со всеми платёжными данными. Это технический откат схемы пустого контура, а не безопасный откат работающих оплат. После начала использования восстановление требует резервирования и сверки провайдера; автоматический откат БД не выполняется. Перед боевым применением нужны проверенный процесс резервирования/восстановления, реестры провайдера и разбор финансовых инцидентов из проекта архитектуры.

## Проверки

Из корня репозитория:

```powershell
dotnet sln OpenCRM.sln list
dotnet build services/payments/tests/Payments.Tests/Payments.Tests.csproj
dotnet test services/payments/tests/Payments.Tests/Payments.Tests.csproj --filter FullyQualifiedName~.Unit.
dotnet test services/payments/tests/Payments.Tests/Payments.Tests.csproj --filter FullyQualifiedName~.Integration.
dotnet build OpenCRM.sln
```

Integration требует Docker и запускает собственные временные PostgreSQL/RabbitMQ со случайными портами. HTTP-адаптер проверяется управляемым имитатором; реальные списания и настоящее API ЮKassa в автоматических тестах не используются. Основная БД и запущенный OpenCRM для этих проверок не нужны.

Ограничения текущего состояния: сверка внешних реестров, неизвестных локально платежей и операторские уведомления требуют отдельной приёмки. Исторические оплаты/чеки сохранены в OpenCRM; автоматический перенос истории в новую БД не выполняется. Эквайринг студий и автоматические списания остаются будущими расширениями. Настоящее API ЮKassa и боевые списания в автотестах не использовались. Боевой режим и внешняя доставка webhook не проверялись.

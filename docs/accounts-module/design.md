# Модуль аккаунтов: технический дизайн

> Исторический документ завершённого переноса. Старые этапы, команды и промежуточные статусы не являются действующими поручениями. Открытая работа перечислена в [указателе](../README.md); текущие границы — в актуальной архитектуре.

Уточнение модели: у аккаунта нет режима; личная техника доступна всем, а тип и права относятся к выбранной организации. Проверки и автоматическая запись `AccountMode` удалены из пользовательских сценариев. Старые поля профиля и форма v1 API сохранены для совместимости и не определяют доступ; устаревшие PUT профиля и POST выбора режима больше не меняют состояние. Каталог типов организаций остаётся действующим. Нижеследующий дизайн фиксирует исходный перенос модуля, а не прежнее разделение пользователей на режимы.

Решение по переносу `OpenCRM.Modules.Accounts`. Обосновано спецификацией (`spec.md`): модуль владеет своей реализацией, сам регистрирует сервисы, контракты
остаются общими. Отдельный архитектурный выбор здесь ровно один — замена статической зависимости
регистрации от уведомлений; он приведён в разделе 3.

## 1. Проекты

| Проект | Путь | Папка решения | Назначение |
| --- | --- | --- | --- |
| `OpenCRM.Modules.Accounts` | `src/OpenCRM.Modules.Accounts/OpenCRM.Modules.Accounts.csproj` | Backend → Application | Реализации профилей и сценариев аккаунта |
| `OpenCRM.Modules.Accounts.Tests` | `tests/OpenCRM.Modules.Accounts.Tests/OpenCRM.Modules.Accounts.Tests.csproj` | Tests | Unit/Integration-проверки модуля |

Новый `*.Contracts` не создаётся. Проект модуля: `net10.0`, `ImplicitUsings`, `Nullable`,
как у `OpenCRM.Modules.Workspace`.

Структура реализации повторяет предметные папки исходного BL:

```
src/OpenCRM.Modules.Accounts/
  AccountProfiles/AccountProfileService.cs
  AccountProfiles/AccountProfileAdminService.cs
  Users/UsersService.cs
  Users/AccountRegistrationService.cs
  Users/AccountDeletionService.cs
  Users/EmailCodeLoginService.cs
  AccountsServiceCollectionExtensions.cs
```

Пространства имён: `OpenCRM.Modules.Accounts.AccountProfiles`, `OpenCRM.Modules.Accounts.Users`
(соответствуют папкам, как в остальных модулях). Публичные типы реализации остаются публичными, как
в `OpenCRM.Modules.Workspace`; закрытые вложенные типы (`EmailLoginCodeCacheEntry`) остаются
приватными. Телам методов и текстам сообщений изменения не нужны.

## 2. Зависимости и регистрация

Проектные ссылки модуля: `OpenCRM.Domain`, `OpenCRM.Infrastructure.Data`,
`OpenCRM.Services.Interfaces` (+ `Helpers` только при подтверждённой необходимости).
Пакеты объявляются явно по фактическому использованию перенесённых файлов, версиями семейства 10.0.x, совпадающими с уже разрешённым графом: `Microsoft.Extensions.Identity.Core` 10.0.7 (`UserManager<T>`, `IdentityResult`),
`Microsoft.EntityFrameworkCore` (Include/AsNoTracking/ToListAsync), `Microsoft.Extensions.Caching.Memory` (`IMemoryCache`), `Microsoft.Extensions.Logging.Abstractions` (`ILogger<T>`). Транзитивная доступность пакета не заменяет явное объявление; неиспользуемые пакеты не добавляются.
Запрещены ссылки на `OpenCRM.Infrastructure.BL`, на реализацию другого модуля и на `WebApp`.

`AccountsServiceCollectionExtensions.AddOpenCRMAccounts` регистрирует ровно шесть сервисов с
прежними временами жизни:

```
AddTransient<IUsersService, UsersService>();
AddScoped<IAccountRegistrationService, AccountRegistrationService>();
AddScoped<IAccountDeletionService, AccountDeletionService>();
AddScoped<IEmailCodeLoginService, EmailCodeLoginService>();
AddScoped<IAccountProfileService, AccountProfileService>();
AddScoped<IAccountProfileAdminService, AccountProfileAdminService>();
```

Эти шесть строк удаляются из `BusinessServiceCollectionExtensions.AddOpenCRMBusinessServices`
(`Infrastructure.BL`); остальные регистрации BL не затрагиваются. `IApplicationUnitOfWork`,
`IPromoCodeService`, `IEmailNotificationService`, `ISiteNotificationPublisher`,
`IMemoryCache` и `ILogger<T>` приходят в модуль как внешние зависимости существующих владельцев.

WebApp вызывает `AddOpenCRMAccounts()` рядом с остальными регистраторами модулей в
`Extensions/ServiceCollectionExtensions.AddOpenCRMInfrastructure`.

## 3. Уведомления о регистрации: узкий контракт вместо статического вызова

Было: `AccountRegistrationService` вызывает
`OpenCRM.Infrastructure.BL.Notifications.SiteNotificationPublisher.AddRegistrationNotificationsAsync(context, user)`
внутри транзакции регистрации.

Становится:

- `OpenCRM.Services.Interfaces.Notifications.ISiteNotificationPublisher` получает метод
  `Task AddRegistrationNotificationsAsync(ApplicationUser user)`. `ApplicationUser` доступен: проект
  контрактов уже ссылается на `OpenCRM.Domain`. Параметр-сущность выбран сознательно: он повторяет
  текущий аргумент статического вызова, поэтому алгоритм владельца не переписывается и данные
  (`Id`, `NormalizedEmail`) не извлекаются повторно.
- Реализация остаётся у владельца уведомлений: `SiteNotificationPublisherAdapter` (BL) реализует
  метод делегированием существующему `SiteNotificationPublisher.AddRegistrationNotificationsAsync(context, user)`.
  Алгоритм приветственного уведомления и уведомлений по ожидающим приглашениям, ключи событий
  `welcome:{userId}` и `invitation:{id}:{token}`, тексты, обрезка полей и внутренний
  `SaveChangesAsync` остаются на месте.
- Модуль внедряет `ISiteNotificationPublisher` через конструктор и вызывает метод на том же шаге
  прежнего алгоритма: после `SaveAsync` профиля и до применения промокода.

Границы транзакции: адаптер получает тот же scoped `OpenCRMContext` (единый DI-контейнер на
запрос), работает внутри уже открытой транзакции `IApplicationUnitOfWork` и не открывает
собственную. Отправка сообщений и писем до commit не появляется; скрытых `SaveChanges` сверх
существующего не добавляется. Прямая запись в `TelegramNotificationOutbox` через
`OpenCRM.Infrastructure.Data.Notifications.TelegramAdminNotificationPublisher` сохраняется: это
разрешённая зависимость от общей инфраструктуры, поведение не меняется. Добавление метода к интерфейсу требует одновременной правки существующей реализации-заглушки `DatabaseSiteNotificationPublisher` в `tests/OpenCRM.Modules.Organizations.Tests/Integration/OrganizationsDatabaseFixture.cs`: без этого набор проверок Organizations перестаёт собираться.

## 4. Точки подключения

- `OpenCRM.sln`: новый модуль в Backend → Application, тестовый проект в Tests, все
  12 конфигураций Debug/Release × Any CPU/x64/x86, как у `OpenCRM.Modules.Workspace`.
- `src/Dockerfile`: `COPY` csproj нового модуля до restore; публикация получает сборку через
  `ProjectReference` от WebApp.
- `src/OpenCRM.WebApp/OpenCRM.WebApp.csproj`: проектная ссылка на `OpenCRM.Modules.Accounts`
  (без неё вызов `AddOpenCRMAccounts` не компилируется) — как у остальных модулей.

- `tests/Accounts.runsettings`: включаемый пакет `[OpenCRM.Modules.Accounts]*`, параллелизм как в
  `Workspace.runsettings`.
- `scripts/assert-backend-boundaries.ps1`: проверка границ уже покрывает новые проекты правилами
  «нет циклов», «модуль не ссылается на BL/другую реализацию», «тесты в проекте Tests и в обеих
  сборках»; запускается как есть и должна проходить на новом графе.

Промежуточная сборка: пока реализации модуля перенесены, а зависящие проверки
`OpenCRM.Infrastructure.Tests` ещё ссылаются на прежние пространства имён BL, полная сборка
решения красная — это ожидаемое состояние переноса, а не дефект. Сборка затронутых проектов
проверяется по мере переноса; зелёная сборка `dotnet build OpenCRM.sln` требуется после переноса
проверок.

## 5. Тесты модуля

`OpenCRM.Modules.Accounts.Tests`: `net10.0`, `IsPackable=false`, пакеты как у
`OpenCRM.Modules.Workspace.Tests` (Testcontainers.PostgreSql, Microsoft.AspNetCore.TestHost, Moq,
Microsoft.NET.Test.Sdk, coverlet.collector, xunit, xunit.runner.visualstudio), ссылки на
`OpenCRM.Modules.Accounts`, `OpenCRM.WebApp`, `OpenCRM.Testing`,
`OpenCRM.Infrastructure.Migrations`, `tests/xunit.runner.json`. Ссылок на другие тестовые проекты
нет. Физические папки `Unit` и `Integration` с соответствующими пространствами имён.

Перенесены проверки, относящиеся к реализации модуля:

- `tests/OpenCRM.Modules.Accounts.Tests/Integration/AccountProfileRemovalTests.cs` → `Integration`
  (использует реальный `AccountProfileService`, TestServer и PostgreSQL);
- `tests/OpenCRM.Modules.Accounts.Tests/Integration/RegistrationErrorTests.cs` → `Integration`
  (использует реальный `AccountRegistrationService` и форму регистрации).

Остаются на месте проверки, не относящиеся к реализации модуля: `RegistrationTransactionTests`,
`RegistrationAttributionTests` (WebApp/`RegistrationAttribution`), `AccountProfileModeTests`
(Domain), `AuthMvcAcceptanceTests`, `AdminAuthenticationApiTests` и проверки других модулей,
которые подставляют интерфейсы `Services.Interfaces` — контракты не переезжают, поэтому их
регистрации-заглушки сохраняются.

Покрытие измеряется для поиска непроверенных участков; обязательного процента нет.
Содержательные сценарии (обязательный минимум):

- регистрация: создание пользователя с паролем и без, согласия и их отметки времени, режим
  профиля и организационный тип, промокод, атрибуция, приветственное уведомление и уведомления по
  ожидающим приглашениям, строка Telegram-outbox; отказ Identity, откат транзакции и отсутствие
  уведомлений после отката;
- профиль: чтение и сохранение режима, fallback организационного типа, рекомендованные и
  включённые возможности, сброс пустым режимом, кеш каталога и его сброс;
- административные профили и возможности: создание/обновление/удаление, уникальность кодов,
  ошибки с прежними текстами, порядок выдачи;
- вход по email-коду: неверный, истёкший и повторный код, лимит попыток, анти-спам минута, отказ
  отправки письма, согласие для регистрации, создание аккаунта, подтверждение email, удалённый
  аккаунт (по email и по `OriginalEmail`);
- удаление: маскирование email и UserName, `OriginalEmail`, `IsDeleted`/`DeletedAt`, удаление
  внешних логинов, возвращаемое значение, строка Telegram-outbox;
- пользовательские записи и статистика: вход/визит, счётчики, личная область, неудалённые записи,
  поиск удалённого оригинального email, исключение для неизвестного пользователя;
- HTTP/DI: маршруты профиля аккаунта через TestServer, регистрация `AddOpenCRMAccounts` (scoped и
  transient, единственность), отсутствие зависимости от BL на уровне проекта.

Интеграционные проверки используют настоящие Identity, PostgreSQL (Testcontainers) и TestServer,
подменяя только внешнюю отправку писем. Рабочая и боевая БД не подключаются.

## 6. Проверки и воспроизведение

```powershell
dotnet build OpenCRM.sln -m:1 -p:BuildScenarioClientsOnBuild=false
dotnet test tests/OpenCRM.Modules.Accounts.Tests/OpenCRM.Modules.Accounts.Tests.csproj -m:1 -p:BuildScenarioClientsOnBuild=false -p:BuildScenarioClientsOnPublish=false --settings tests/Accounts.runsettings --collect "XPlat Code Coverage" --logger trx
./scripts/assert-module-coverage.ps1 -ModuleName Accounts -ReportPath <coverage.cobertura.xml>
./scripts/assert-backend-boundaries.ps1
dotnet sln OpenCRM.sln list
```

Целевые наборы запускаются во время работы, общая backend-регрессия — после последних изменений.
Отчёты и TRX размещаются в изолированном каталоге (аргумент `--artifacts-path`), сборка клиентов
отключена. Проверки контроллера выполняются argv-командами без shell, поэтому используются сборка
решения и целевые наборы модуля в пределах лимита времени.

## 7. Риски и границы решения

Перенос не меняет схему БД, миграции, HTTP-контракты, правила авторизации и границы транзакций.
Единственная новая связь — вызов интерфейса `ISiteNotificationPublisher` вместо статического метода
владельца; тот же scoped контекст и та же транзакция сохраняют атомарность регистрации. Если при
реализации выяснится, что сохранение поведения требует изменения публичных контрактов, схемы БД,
правил доступа или открытия отдельных транзакций, работа останавливается и выносится на отдельное
решение: такой сценарий выходит за границы этого этапа.

## Уточнения окончательной реализации

Неиспользуемый InternalsVisibleTo не добавляется: тесты используют публичные контракты, внутренних типов в модуле нет. Email-код получает необязательный TimeProvider с System по умолчанию для воспроизводимых проверок срока и повторной отправки. Integration использует локальную AccountsDatabaseFixture только с PostgreSQL и настоящими миграциями; RabbitMQ и EF InMemory для этих проверок не нужны. Подробные результаты приведены в [руководстве](../README.md).

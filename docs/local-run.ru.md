# Локальный запуск

[English](local-run.md) | **Русский** | [简体中文](local-run.zh-CN.md)

Эта инструкция поднимает базовое веб-приложение OpenCRM с собственной PostgreSQL. SMTP, RabbitMQ, сервис уведомлений, ИИ и платежи для регистрации и основной работы CRM не требуются.

## Требования

- .NET SDK 10.
- Git.
- Node.js `^20.19.0` или `>=22.12.0` и npm с поддержкой workspaces. Такое требование задаёт Vite 7 из `src/OpenCRM.Frontend/package-lock.json`. В корневом `package.json` отдельного поля `engines` нет.
- PostgreSQL 17. Более ранние поддерживаемые версии могут работать, но чистая установка проекта проверена на PostgreSQL 17.
- Для HTTPS — локальный сертификат разработки .NET.

Docker не нужен, если PostgreSQL уже установлен или доступен отдельно.

## Получение исходников и frontend-зависимости

Из терминала выполните команды из своей рабочей папки.

**Linux (Bash):**

```bash
git clone https://github.com/eddchervov/OpenCRM.git
cd OpenCRM/src/OpenCRM.Frontend
npm ci
cd ../..
```

После `npm ci` последняя команда возвращает в корень клона `OpenCRM`.

**Windows (PowerShell):**

```powershell
git clone https://github.com/eddchervov/OpenCRM.git
Set-Location OpenCRM/src/OpenCRM.Frontend
npm ci
Set-Location ../..
```

## Создание локальной базы

Выберите один из вариантов. Пароль в примерах — заполнитель: замените его собственным значением и используйте то же значение в строке подключения.

### Вариант A: PostgreSQL в Docker

Для новой локальной установки с постоянным именованным томом выполните одну команду (подходит для Bash и PowerShell):

```text
docker run --name opencrm-local-db --detach --publish 127.0.0.1:5432:5432 --volume opencrm-local-data:/var/lib/postgresql/data --env POSTGRES_DB=opencrm_local --env POSTGRES_USER=opencrm_local --env POSTGRES_PASSWORD=replace-with-your-local-password postgres:17-alpine
```

Команда создаёт роль и БД `opencrm_local`. Перед миграциями дождитесь ответа `accepting connections` от проверки TCP внутри контейнера:

```text
docker exec opencrm-local-db pg_isready -h 127.0.0.1 -U opencrm_local -d opencrm_local
```

Если PostgreSQL ещё запускается, повторите проверку через несколько секунд. Если порт `5432` занят, выберите другой опубликованный порт и укажите его в строке подключения. Уже созданный контейнер запускайте повторно без удаления его тома и без смены пароля существующей БД. После этого переходите к разделу «Подключение и миграции».

### Вариант B: установленный PostgreSQL

Для команд ниже нужен клиент `psql` в PATH. SQL также можно выполнить в своём клиенте администрирования PostgreSQL.

Создайте отдельную роль PostgreSQL и пустую базу, которыми владеет эта роль. Выполняйте SQL учётной записью администратора PostgreSQL; замените пример пароля собственным локальным случайным значением и не сохраняйте его в Git.

**Linux (Bash):**

```bash
psql -h localhost -U postgres -d postgres -c "CREATE ROLE opencrm_local LOGIN PASSWORD 'replace-with-your-local-password';"
psql -h localhost -U postgres -d postgres -c "CREATE DATABASE opencrm_local OWNER opencrm_local;"
```

**Windows (PowerShell):**

```powershell
psql -h localhost -U postgres -d postgres -c "CREATE ROLE opencrm_local LOGIN PASSWORD 'replace-with-your-local-password';"
psql -h localhost -U postgres -d postgres -c "CREATE DATABASE opencrm_local OWNER opencrm_local;"
```

Если PostgreSQL уже настроен, можно использовать собственные имя роли, пароль, адрес, порт и имя новой базы. Не направляйте команды миграции на существующую рабочую базу.

## Подключение и миграции

Задайте `ConnectionStrings__OpenCRMConnection` в текущем терминале. Пример указывает на новую локальную базу. Используйте собственный пароль.

**Linux (Bash):**

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__OpenCRMConnection='Host=localhost;Port=5432;Database=opencrm_local;Username=opencrm_local;Password=replace-with-your-local-password'
```

**Windows (PowerShell):**

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__OpenCRMConnection = 'Host=localhost;Port=5432;Database=opencrm_local;Username=opencrm_local;Password=replace-with-your-local-password'
```

Восстановите локальный EF CLI из манифеста репозитория. Версия `10.0.7` соответствует проекту миграций; глобальные инструменты других проектов менять не нужно:

```text
dotnet tool restore
```

Затем из корня клона примените миграции к созданной базе:

```text
dotnet ef database update --project src/OpenCRM.Infrastructure.Migrations --startup-project src/OpenCRM.Infrastructure.Migrations
```

Проект миграций читает ту же переменную `ConnectionStrings__OpenCRMConnection`. Применение миграций — отдельный шаг; веб-приложение не создаёт схему при старте.

## Запуск и первый вход

На Windows и macOS создайте и доверьте локальный HTTPS-сертификат:

```text
dotnet dev-certs https --trust
```

В Linux выполните `dotnet dev-certs https --check`. Если сертификат ещё не создан, создайте его командой `dotnet dev-certs https`; затем добавьте этот сертификат в доверенные сертификаты вашей системы и браузера средствами дистрибутива.

Запустите приложение из корня клона:

```text
dotnet run --project src/OpenCRM.WebApp/OpenCRM.WebApp.csproj --launch-profile https
```

Откройте `https://localhost:7142`. Проверка готовности приложения и базы данных находится по адресу `/api/health/ready`.

Зарегистрируйте пользователя на `/auth/register`, войдите и создайте организацию. Для начальной регистрации почтовый сервис не требуется: `Notifications:Transport=Disabled`, а `EmailConfirmation:Required=false`. Приложение не отправляет письмо при регистрации и не подтверждает адрес автоматически. Если локальная среда не позволяет настроить доверенный HTTPS, профиль `http` доступен по `http://localhost:5134` для проверки запуска и готовности. Для входа используйте HTTPS: сессионная cookie приложения помечена как Secure.

Учетной записи администратора и пароля по умолчанию нет. Административный доступ требует соответствующей роли. Назначение первого администратора в своей новой БД и запуск обеих админок описаны в [руководстве по администрированию](administration.ru.md).

## Сборка frontend и решения

После установки npm-пакетов frontend можно собрать из `src/OpenCRM.Frontend`:

```text
npm run build
```

Для запуска веб-CRM на любой поддерживаемой серверной ОС из корня клона собирайте веб-проект:

```text
dotnet restore src/OpenCRM.WebApp/OpenCRM.WebApp.csproj
dotnet build src/OpenCRM.WebApp/OpenCRM.WebApp.csproj
```

Полное решение также включает Windows WPF Launcher. Для сборки всех приложений, включая него, используйте Windows:

```text
dotnet restore OpenCRM.sln
dotnet build OpenCRM.sln
```

## Дальнейшая настройка

- [Конфигурация приложения](configuration.ru.md) описывает источники значений, основные секции и дополнительные интеграции.
- [Общий файл бренда](../configuration/README.ru.md) описывает `configuration/brand.json` и серверные переопределения бренда.
- [Архитектура приложения](architecture-current.ru.md) помогает найти существующие модули и точки изменения продукта.

После изменения конфигурации перезапустите соответствующие процессы.

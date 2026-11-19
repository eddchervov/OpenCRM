# NotificationService

[English](README.md) | [Русский](README.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

用于OpenCRM和其他应用程序的通知微服务.  一个工人进程通过RabbitMQ接收命令,在PostgreSQL中存储状态,发送电子邮件/SMS,并返回应用程序的队列结果. 命令只能通过RabbitMQ接受.  一个电子邮件提供者 callback Webhook可能运行在内部端口8080;没有HTTP API来发送通知.

<a name="message-flow"></a>

## 信件流

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

指令接受和交付在同一过程中使用不同的处理器. 内部队列被保留,以便与先前接受的任务相兼容并被推迟发送.

<a name="features"></a>

## 特征

- 已处理通知创建、模板更新和状态请求的命令。
- 每个应用程序分别命令和结果队列.
- 应用软件公司和规范化的《同上》软件公司的数据不充足。
- 接收命令的交易收件箱/出件箱;交付结果的公交出件箱。
- 推迟交付,四次尝试,在工人倒车后收回租约。
- 排队,处理,重试,提供者接受,模拟,失败,未找到状态.
- 默认情况下仅登录: 没有发送真实的邮件或短消息 。
- PostgreSQL,RabbitMQ,JSON日志,多克编曲和CI.

`provider-accepted` 是指提供者的接受,而非确认的接受方交付. `simulated` 单独表示 Log Only 。  在配置了 Unisender 调用后,签名的提供者事件可以单独报告送出;单SMTP接受并不能确认送出.

来源 `services/notifications`  在 OpenCRM 存储器中。  所有服务项目也包含在根中 `OpenCRM.sln`.  此文档中的命令从服务目录中运行; `NotificationService.slnx` 允许单独的构建和测试运行。 工人部署及其数据库迁移仍然独立于OpenCRM.

<a name="structure"></a>

## 结构

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

<a name="local-setup"></a>

## 本地设置

运行此区域的命令 `services/notifications`.

1. 复制 `.env.example` 改为: `.env` 并给出您自己对空的值 `POSTGRES_PASSWORD` 和 `RABBITMQ_PASSWORD`.
2. 运行 `docker compose up --build -d`.
3. 检查 `docker compose logs --tail=100 worker` 和消费者在兔子MQUI `http://localhost:15672`.

 例如命令和结果处理器,参见 [OpenCRM 整合](docs/opencrm-integration.zh-CN.md). 使用 `RabbitMq__Applications__1=catalog`  在工人的环境下。 ID由小写拉丁字母,数字,和连字符组成并被限定为100个字符.  每个应用程序必须有一个单独的RabbitMQ用户,只能访问自己的队列. 应用程序列表创建了路由, 但不会取代代理权限 。

<a name="production"></a>

## 生产

工作人員使用标准.NET配置密钥而无需使用 `NOTIFICATIONS_`  前缀。 WebApplication.CreateBuilder 载入应用程序,并在开发中为用户保密 `src/Notifications.Worker`; 程序然后添加共享 `brand.json`,无前缀的环境变量,和命令行参数。  对于直接启动, 设置诸如 `ConnectionStrings__NotificationsDb`, `RabbitMq__Host`, `RabbitMq__Password`,以及 `Delivery__LogOnly`.

 对于生产和直接的工人启动,使用无前缀的标准密钥: `ConnectionStrings__NotificationsDb`, `RabbitMq__Host`, `RabbitMq__Username`, `RabbitMq__Password`,以及 `Delivery__LogOnly`. 将秘密保存在外部商店中并作为Worker环境变量提供;有轨程序只包含参数名称和空密值.

EF 设计时间工厂决定环境来自 `DOTNET_ENVIRONMENT`,则 `ASPNETCORE_ENVIRONMENT`.  在开发中,它以与"工人"相同的UserSecretsId加载用户机密,然后所有无前置环境变量;环境优先. 移民只需要 `ConnectionStrings__NotificationsDb`.

<a name="integration-settings"></a>

## 整合设置

地址、 端口、 短消息. ru 超时、 发送者显示名称、 应用程序列表和模式在 sc/ Notifications. Worker/appsings.json 中配置,并在环境中覆盖 。 选项类不提供工作默认地址,端口,名称,或LogOnly模式. 空字符串和集合表示一个设置尚未被填入 。

工人在开始消费前先验证所需的参数。 交付:仅Logly必须明确;缺值不能使真实发送成为可能. 电子邮件:Enabled,Sms:Enabled,Telegram:Enabled,和TelegramClient:Enabled 独立启用频道.  在LogOnly= true中,外部提供者的秘密是不必要的.

仅用于本地作曲 `POSTGRES_PASSWORD` 和 `RABBITMQ_PASSWORD` 内 `.env`: 构成供应其余所需值,包括 `Delivery:LogOnly=true`,启用了电子邮件/SMS频道、SMTP端口和SMS.ru参数。 单位 `LogOnly`, SMTP和SMS.ru 秘密不被验证,实际上没有发送任何信息.

 对于真实的只发邮件,设置为 DELIVERY_LOG_ONLY=假发,EMAIL_ENABLED=真发,SMS_ENABLED=假发,并填写为 SMTP_HOST,SMTP_Port,SMTP_FROM_ADADSESS.  如果设置了SMTP_USERNAME,则需要SMTP_PASWORD;允许未经认证的接力.  一个空的 `SMTP_FROM_NAME` 内 `.env.example` 开始 `Smtp:FromName` 使用共享名称 `brand.json`;一个明确的非空值会凌驾于品牌.

 对于真实的SMS,启用SMS_ENBLED=reference并设置SMSRU_API_KEY.

 对于所有者消息,启用 TELGRAM_ENABLED= truth并设置 TELGRAM_BOT_Token 和 TELGRAM_OWNER_CHAT_ID 。 机器人只寄给接收者 `owner`.  如果无法直接连接到Bot API, 请使用该网关 `telegram-relay`. 部署后,设置 TELEGRAM_RELAY_URL(一个 HTTPS URL 结束于 `/sendMessage`和TELEGRAM_RELAY_KEY。 (笑声) (掌声) (掌声)  在此模式下, 提供 bot 令牌和聊天 ID 作为网关工人秘密 `TELEGRAM_BOT_TOKEN`, `TELEGRAM_OWNER_CHAT_ID`; `RELAY_KEY` 必须在通知服务中与 TELEGRAM_RELAY_KEY相匹配。  如果网关 URL 为空, 直接模式仍然活跃 。 真实发送仍然需要TELEGRAM_ENABLED= truth and DELIVERY_LOG_ONLY=虚假. OpenCRM 可以通过TelegramAdminifics 进行消息排队__启用=真实 。

部署网关从 `telegram-relay` 与 `npx wrangler deploy`,则 `npx wrangler secret put RELAY_KEY`, `npx wrangler secret put TELEGRAM_BOT_TOKEN`,以及 `npx wrangler secret put TELEGRAM_OWNER_CHAT_ID`. 将 TELGRAM_RELAY_ URL 设置为指定的工人的 HTTPS URL `/sendMessage`. RELAY_KEY 必须匹配 TELEGRAM_RELAY_KEY; 永远不要在其中存储秘密 `wrangler.jsonc`.

 对于客户电报,请使用单独的 `telegram-client` 频道: 启用 TELGRAM_ CCLIENT_ ENULED= truth并设置 TELGRAM_ CLIENT- 认证 _ BASE_ URL( OpenCRM HTTPS URL), TELGRAM_ CLIENT_ 认证 _KEY, TELGRAM_ CLIENT_ RELAY_ URL( 工作人与 `/sendOrganizationMessage`),和TELEGRAM_CLIENT_RELAY_KEY. (中文(简体) ). OpenCRM 密钥必须匹配Telegram Organizations 。__ServiceKey;中继键必须匹配网关工人的RELAY_KEY秘密. 顾客的收受者是 `<organizationGuidN>:<chatId>`.  在发送前,工人会通过Get来取回信物 `/api/internal/notification-bots/telegram/{organizationId:N}` 与"X-通知-服务-Key"和"X-通知-Key"标题相接. 答复中包含: `botToken`;该符号从未输入通知队列或通知服务数据库。

网关工人也暴露了受保护的 POST `/getOrganizationBot` 和 `/setOrganizationWebhook` 用于验证令牌和注册webhooks的结束点。 后一组 `/updates/{organizationId}`  在网关上的工人本身。  它只将收到的更新转发到固定的 OpenCRM 地址 `OPENCRM_BASE_URL`时, `/api/internal/notification-bots/telegram/{organizationId}/updates`,保留 `X-Telegram-Bot-Api-Secret-Token` 用于 OpenCRM 验证。 设定 `npx wrangler secret put OPENCRM_BASE_URL`  在网关设置时。 第一次部署后, 请使用网关工人的 URL 设置 。 `npx wrangler secret put TELEGRAM_WEBHOOK_BASE_URL`;它使用这个固定的基数,而不是主机头,用于 Webhook URL.

 对于客户MAX消息,启用MA ⁇ enableD= truth并设置MA ⁇ credential_BASE_URL(OpenCRM HTTPS URL)和MA ⁇ credential_KEY.  此密钥必须匹配 Max 通知__OpenCRM 中的服务Key, 可用 Max Notifications 提供__启用=真实 。 组织bot令牌仍保留在OpenCRM中:通知Service在发送前会取回所需的令牌,并且不会将其存储在队列中. 无效的 URL、 端口和测试值取代密钥, 防止启动 。

EF设计时工厂不插入本地数据库或密码:需要连接字符串.  在开发中,它来自工人用户机密或 `ConnectionStrings__NotificationsDb` 在生产中,它来自环境变量。 -- migrate 模式只验证数据库,既不需要兔子MQ,也不需要提供者.

<a name="channels-and-providers"></a>

## 频道和提供者

业务逻辑和命令通过 INotificChannelSender 与电子邮件/sms/telegram 频道合作. 适配器选择与其连接参数分离:

```json
{
  "Sms": { "Enabled": true, "Provider": "smsru" },
  "Email": { "Enabled": true, "Provider": "smtp" },
  "Telegram": { "Enabled": false, "Provider": "telegram" },
  "TelegramClient": { "Enabled": false, "Provider": "telegram" }
}
```

SmsRu参数(BaseUrl,ApiKey等)只属于SMS.ru适配器;Smtp包含SMTP适配器设置;Telegram包含BotToken,OwideChatId,和RequestTimeout Seconds.  它们不是共享的频道设置 。  在环境中,选择使用SMS_PROVIDER,EMAIL_PROVIDER,以及TELEGRAM_PROVIDER. 被禁用的频道设置不被验证,其适配器也没有注册.

目前的实施是SMS的smsru,电子邮件的smtp,行政信息的电报,以及代表一个组织的客户信息的电报客户. 未知的提供商名称可以阻止启动,而不是隐含地掉回SMS.ru.  为了支持另一个服务,在基础设施中添加一个InotificChannelSender执行,注册,和参数验证,然后在配置中选择. OpenCRM,RabbitMQ消息,通知发布者,数据库模式仍然在提供方之间共享. 仅改变 BaseUrl 是不够的: 提供者使用不同的协议和响应格式.

<a name="email-templates"></a>

## 电子邮件模板

系统电子邮件在基础设施的模板/OpenCRM/V1中作为.cshtml,并有共享的布局和打入的模型. OpenCRM通过TemplateKey,TemplateVersion,和Data;系统模板不需要通过消息代理注册.  该服务在排队前验证参数并存储已提供的电子邮件 。 被推迟的交付和重复使用相同的内容。 SystemTemplates:应用程序决定了外观和访问:每个应用程序配置都有"品牌"(BrandName)和一个模板列表. 添加客户端不需要更改代码 。  可编辑运动和短消息系统保留一个单独的数据库-模板机制.

[模板、模型和版本](docs/templates.zh-CN.md) · [参数示例](examples/system-emails)

<a name="migrations"></a>

## 移徙

表格和EF历史使用 `notifications` 计划。 20260909054007_VersionedTemplateSnapshots 添加了模板版本和电子邮件快照. 初出所出所出所出所出所出所出所出所出所出所出所出所出所出所出所出所出所出所出所出所出所出所出所出所出所出所出.  见 [模板指南](docs/templates.zh-CN.md) 用于更新顺序和回滚限制。

自动迁移在生产中被禁用. 与工人图像分开运行 :

```bash
docker run --rm --env ConnectionStrings__NotificationsDb notification-service-worker --migrate
```

 导出 `ConnectionStrings__NotificationsDb`  在此命令之前的调用环境中,并使用您所构建的图像。 编曲 `.env` 替换名不是取代 Worker 的.NET 配置密钥。

 此模式在不启动消费者或发送通知的情况下应用迁移和退出.  对于使用 SDK 的 EF 指令, 使用 基础设施 作为其设计-时间工厂的启动项目 :

```bash
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/Notifications.Infrastructure --startup-project src/Notifications.Infrastructure
```

 在Development中,EF从Worker用户机密以及随后的环境变量读取连接字符串;在Production中,只读取环境.  它不装入 `.env` 自己来。 密码 [SQL 合并计划](scripts/maintenance/consolidate-schema.sql)  作为手动维护脚本保留;不需要再运行。

<a name="validation"></a>

## 验证

```bash
dotnet build NotificationService.slnx -c Release
dotnet test NotificationService.slnx -c Release
```

集装箱集成测试需要运行多克并创建/移动自己孤立的PostgreSQL 15和RabbitMQ 4.  他们使用没有外部电子邮件/短信的测试发送器. Razor和DI的注册检查工作没有Docker。 单元测试无需多克操作 :

```bash
dotnet test NotificationService.slnx -c Release --filter "FullyQualifiedName~.Unit."
```

 见 [结构](docs/architecture.zh-CN.md) 边界、保障和限制。

 在 Windows 上,当手动指定 Docker 桌面端点时,使用 `$env:DOCKER_HOST = "npipe://./pipe/dockerDesktopLinuxEngine"` 用于当前PowerShell进程。 Docker CLI 字符串, 后面有四下划线 `npipe:` 与正在使用的 Testcontainers 版本不符。

<a name="opencrm-notifications-through-rabbitmq"></a>

# 通知

[English](notifications.md) | [Русский](notifications.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

 在主要应用中, `OpenCRM.Modules.Notifications` 拥有电子邮件、网站通知、发件箱/处理器以及MAX/Telegram设置。  它通过下列方式登记服务: `AddOpenCRMNotifications`;账户和订阅假想使用狭义的合同. 域事件和意向被存储在OpenCRM共享数据库中.

单独的通知服务 `services/notifications` 制作 Razor 电子邮件和电话提供者。  它的工人、数据库和图像保持独立;通信用途 `Notifications.Contracts`  还有兔子MQ.  主应用程序不使用SMTP,SMS.ru或Unisender密钥. 模块测试于 `OpenCRM.Modules.Notifications.Tests`,以及以下列方式交付服务: `Notifications.Tests`.

 每个进程都读取自己的配置和环境变量. 开发用户机密仅适用于配置为 `UserSecretsId`. 写作读取 `.env` ; 直接启动的.NET进程不会自动加载该文件。  见 [配置说明](../configuration/README.zh-CN.md).

<a name="why-both-a-database-and-rabbitmq-are-needed"></a>

## 为什么需要数据库和RabitMQ

1. OpenCRM将业务变更和通知任务保存在其数据库中.  例如,支持请求及其电子邮件被记录在一个交易中。
2.  一个OpenCRM背景处理器会读取其电子邮件Messages表,并将任务发送给RabbitMQ.
3. 工作者接收命令,保存在自己的数据库中,并发送电子邮件.
4.  结果通过RabbitMQ返回并更新了OpenCRM日志.

数据库记录是一种持续的意图;RabbitMQ在应用程序之间传输.  如果消息代理无法到场,意图将保留在数据库中,供日后发送。  如果停止工作,队列会保留命令直到开始. 工人既不需要访问OpenCRM的数据库,也不需要了解其表格。  其他应用程序使用相同的合同。

 这是一个有重复命令发送的发件箱模式 。 稳定的通知Key让工人识别重复。 单一的物理SMTP发送无法保证:在提供者接受之后但结果持续之前的失败可能导致重复.

<a name="connection"></a>

## 连接

开发:RabbitMq:Host=192.0.2.1,应用Id=opencrm-开发.
部署: 操作员提供消息代理地址和网络; 应用程序Id=opencrm.

共享参数:端口5672,VirtualHost=/,用户名=

命令使用 MassTransit 发送到通知...<applicationId>- 命令;结果到通知...<applicationId>-结果  每个结果队列对应一个应用程序数据库. 本地数据库不得竞争生产成果;开放式开发为其保留。

<a name="emails-and-log"></a>

## 电子邮件和日志

| 假设 | Razor 模板 |
| --- | --- |
| 电子邮件确认 | 系统/电子邮件确认 |
|  八位数登录代码 | 系统/登录码 |
| 浏览器密码重置 | 系统/密码重置 |
| 移动应用程序密码重置 | 系统/密码-重置-移动 |
| 支助请求 | 系统/支持信息 |
| 付费订阅、演示、演示可用性 | 系统/订阅通知 |
|  邀请新用户到一个组织 | 品牌布局内的系统/海关电子邮件 |
| 编辑电子邮件和散装运动 | 系统/海关电子邮件 |

OpenCRM通过密钥和数据.  在邀请一个没有账户的地址时, 所提供的电子邮件会被排入与邀请相同的交易中 。 链接导致电子邮件编码签名,然后是邀请列表;接受需要电子邮件验证. Worker使用已编译的版本1 cshtml和共享版式.  对于系统/海关电子邮件,编辑器内容作为htmlBody提供;它被插入为HTML,而不是被执行为Razor. 临时的HTML模板注册是不必要的.

<a name="owner-notifications-in-telegram"></a>

## Telegram中的所有者通知

 在账号创建,自去除,以及确认的订阅支付上,OpenCRM保存了Telegram NotificationOutbox记录.  对于支付,这种情况发生在订阅激活交易中,所以重复的webhooks不会添加另一个通知. 注册内容包括网站、电子邮件编码登录和API。 信件中包含电子邮件,用户ID,以及可用的事件细节;密码,登录代码和支付权限不传输.

 背景处理器发送带有频道的命令 `telegram` 和逻辑收件人 `owner` 通知处。 命令重试使用相同的事件密钥 。 发送状态返回到Telegram Notification Outbox 。  对于直接发送,请在通知服务中配置bot sident并聊天ID(`Telegram__BotToken`, `Telegram__OwnerChatId`; 当使用 Cloudflare Worker 时,它们是网关机密,通知服务只接收其URL和访问密钥. 需要真实发送 `Telegram__Enabled=true` 和 `Delivery__LogOnly=false`.  虽然 `TelegramAdminNotifications__Enabled=false`,则Telegram处理器不注册,并保留OpenCRM数据库中的工作.  在准备通知服务后, 设置 `TelegramAdminNotifications__Enabled=true`  在 OpenCRM 中重新启动。 业主必须开始与机器人交谈,以便实际交付. Telegram 失败不会取消业务操作; 工人重试 。  在Telegram接受消息后失败,可能造成重复.

<a name="organization-customer-notifications-in-telegram"></a>

## Telegram中的组织客户通知

 每个组织都有自己的Telegram bot. 拥有者通过"BotFather"创建并在其组织设置中输入其代号;OpenCRM安全地存储了代号.  一个客户从他们的记录中获得一次性链接,并自己启动机器人. 同意,聊天,历史与MAX和行政管理的Telegram bot是分开的. 最初的活动是设立任命、重新安排任期、取消和车辆移交准备。  每个频道分别创建一个通知,允许MAX,Telegram,或两者兼有. 设想方案的工作是详细列出、修理商店和车队的维修记录,这些记录的车辆与一个组织的客户有联系;没有客户记录的内部车队车辆不接收客户信息。

OpenCRM 有频道的客户端信息队列 `telegram-client`. 通知服务只为有效消息取出一个令牌,验证客户端,匹配聊天,以及活动组织,然后通过Cloudflare Worker发送. 工作人員还验证了bot令牌,注册了webhooks,并转发到OpenCRM所收到事件;OpenCRM验证了一个单独的bot秘密. 消息文本/状态出现在客户和维护记录中. 启用方式 `TelegramOrganizationNotifications:Enabled`, `ServiceKey`, `RelayBaseUrl`,以及 `RelayKey`  在 OpenCRM 中和 `TelegramClient` 通知服务中的设置。 `TelegramClient:CredentialKey` 等于 `ServiceKey`, `TelegramClient:RelayKey` 等于 `RelayKey`网关 `RELAY_KEY` 等于 `RelayKey`. 网关需要 `OPENCRM_BASE_URL` 和 `TELEGRAM_WEBHOOK_BASE_URL`. 行政 `/sendMessage` 保留它的固定机器人和聊天。

<a name="organization-customer-notifications-in-max"></a>

## MAX中的组织客户通知

 每个组织在组织环境中连接自己批准的MAX bot. 拥有者输入符; OpenCRM 验证了bot,并自动注册了HTTPS webhook. ASP.NET 数据保护加密令牌和 Webhook 密钥; 在其中保存密钥 `DataProtection:KeysDirectory` 跨度升级。 网钩使用 `AppSettings:BaseUrl`.

员工从客户记录中一次性发布链接. 客户启动"机器人",而"网络呼克"将他们的MAXID链接到唱片上.  没有这个链接, 信件就不会排队 。 停止机器人发送 。 最初的活动是设立任命、重新安排任期、取消和车辆准备。  这在车辆与一个组织客户联系时,可以详细介绍、修理商店和车队。 OpenCRM检查预约有效性和客户同意,然后通过RabbitMQ将通知传递给通知服务公司.  在发送前,微服务通过受保护的HTTPS请求取回了该令牌到OpenCRM;令牌从不入队. 文本和状态出现在客户和维护记录中。 启用方式 `MaxNotifications__Enabled=true` 和 `MaxNotifications__ServiceKey`  在 OpenCRM 中和 `MAX_ENABLED=true`, `MAX_CREDENTIAL_BASE_URL`,以及 `MAX_CREDENTIAL_KEY`  在通知处。 未知的MAX响应可能导致重复重试.

通知状态已改变。 Content 返回最终对象、 body、 IsHtml 和 TextBody 到自有应用程序的队列 。 OpenCRM 保存用于管理中的电子邮件查看 。  在第一次"Worker"响应之前,该日志在TemplateDataJson中有一个初步主题和原始参数.

移动电子邮件保存了opencrm://和一个倒回的网页链接. 登录代码在一个小时后仍会过期;排队不会延长有效性.

<a name="reliability-and-statuses"></a>

## 可靠性和地位

电子邮件Messages既是日志,也是耐用的发件箱. 下个 DistapatchAt 确定下一个调度或状态检查 。  对于更新 SKIP 锁定, 允许多个 OpenCRM 实例处理队列 。

 当没有结果到达时, OpenCRM 发送 Get Notification status 。  未找到的响应会用相同的密钥重排原命令的顺序 。 重复的终端结果不会增加计数器. 较老的州不会取代较新的州。

- 已队列——已保存任务 。
- 重试——"工人"重试.
- 发送/提供方-接受——提供方接受消息;接收方的发送仍未证实。
- 模拟——"LogOnly";实际上没有发送.
- 失败——命令被拒绝或尝试已用尽。

Campaign SentCount和提醒SentAt在提供者接受后更新. 模拟Count被单独跟踪. 暂停/取消一项运动,停止为更多的受援者排队;不再召回已经派遣的工作。 终端失败仍然保留在日志中,而不会自动创建新的工作.

<a name="provider-events"></a>

## 提供者活动

Unisender 向 /webhooks/unisender-go 发送 HTTPS 网络hook 。  一个倒置代理转发给通知-工人:8080. 工人验证签名,用发件箱保存事件,并返回通过RabbitMQ报到的通知。 OpenCRM存储 EventId 并在一次交易中更新日志 : 重复不增加打开/ 点击计数 。  未订户或投诉活动撤销广告宣传同意.

Webhook是一个提供商的回调频道.  从 OpenCRM 发送通知时没有 HTTP API 。

 每个新邮件都需要应用程序_id和通知_key. 缺少这些字段的已签名的 Webhooks 被忽略 。

<a name="deployment"></a>

## 部署

1. 停止"老工人"并更新其形象. 应用全部通知服务迁移,包括版本TemplateSnapshots和20260909092759_ProviderWebhookEvents,然后启动工作人. 更新后配置既允许开放crm/opencrm-development配置,也允许所有上市模板.
2.  对于Unisender,设置EMAIL_PROVIDER=unisender-go,SMTP设置,UNISENder_WEBHOOK_ENABLED= true,在服务器的.env中设置UNISENder_WEBHOOK_KEY.
3. 配置自己的 HTTPS `/webhooks/unisender-go` 前往工人的路线。 部署决定了URL,TLS和代理规则;服务验证了webhook秘密.
4. 停止旧的OpenCRM. 应用20260909083524_Addunification Outbox和202609092118_Conferentement into its database.
5. 设置电子邮件首选项__ 未订阅TokenSecret.  要保留已有的未订阅链接, 需要传输先前有效的电子邮件设置: 未订阅 TokenSecret , 或者, 如果缺少的话, 电子邮件设置: Password 。  此单独标记链接; 未来的 SMTP 密码更改不影响它 。
6. 设置支持__邮件和兔子Mq__密码,将应用程序连接到消息代理的网络,并启动OpenCRM.
7. 校验命令、结果和日志。 禁用发送: 只有故意允许真实发送时才会禁用 。

地方发展秘密通过用户保密方式提供.

 从寄存器根移出 OpenCRM 示例 :

~~~powershell
dotnet ef database update --project src/OpenCRM.Infrastructure.Migrations --startup-project src/OpenCRM.Infrastructure.Migrations
~~~

设计时工厂读取应用程序和环境变量. 设置连接串__用于预定数据库的 OpenCRMConnection.

下方的添加通知输出框禁止有未处理的工作 。  回滚删除了服务字段并模拟了统计数据. 下为完整通知集成可以去除去去去除的ID并恢复300个字符的受限; PostgreSQL 拒绝在存在更长的受限对象而不是被截取内容时缩小. 恢复模板注册At不恢复其先前的值.

<a name="build-and-validation"></a>

## 构建和验证

通知服务处 `services/notifications`;其项目包括在 `OpenCRM.sln`. 生产代码只取决于通知. Contacts. 测试项目参考了工人实施端到端检查。  该服务保留了自己的构建设置,Docker图像,作曲,以及数据库;它可以单独建设和部署.  从寄存器根运行以下 。

~~~powershell
docker build -f src/Dockerfile -t opencrm .
dotnet test tests/OpenCRM.Modules.Notifications.Tests/OpenCRM.Modules.Notifications.Tests.csproj
dotnet test services/notifications/NotificationService.slnx
docker build -f services/notifications/src/Notifications.Worker/Dockerfile -t notification-service-worker .
~~~

测试在 Docker 中分别开始 PostgreSQL 15 和 RabbitMQ 例. 端到端测试使用真实的Worker,Razor,同时使用OpenCRM处理器,以及HTTP webhook;LogOnly防止真实的电子邮件.  不使用生产数据库和队列。

<a name="smtp-outbound-worker-network"></a>

## SMTP: 出厂工人网络

电子邮件发送使用来自工人网络的外出TLS连接.  该平台没有规定MTU,网络地址,或名称:部署环境决定了它们.  一些提供商和网络可能会遇到与 MTU 1500 和包分解的 TLS 超时; 通过您安装的工具减少 Worker 网络 MTU , 并在需要时只重新启动 Worker 容器 。  不为此更改清除 RabbitMQ 队列或数据库 。 TLS 证书验证是强制性的, 永远不会被禁用; 一个已完成的电子邮件发送显示正确的配置 。

<a name="emails-from-administration"></a>

## 行政部门的电子邮件

 在“QQ电子邮件”(电子邮件历史)中,“QQ”(制作电子邮件)打开了共享编辑器。 接收者可以是任何电子邮件地址(不需要账户),一个选定的用户,一个组织类型,或者所有用户. 用户搜索覆盖整个数据库并返回最多30个匹配.  在发送前审查地址并预览文本 。

纯文本被逃脱并保存段落; HTML 是一个单独的模式.  所有电子邮件都使用一个运动,OpenCRM的发件箱,以及微服务系统/客户电子邮件模板. "工人"增加了品牌布局;编辑器不需要提供头/脚. 其结果仍保留在电子邮件历史中。  对任意地址没有核查和同意过滤器。

通知服务中的共享模板/OpenCRM/V1/_Layout.cshtml定义了所有系统和行政电子邮件的外观. 品牌来源于配置/brand.json(ProjectBrand)并填入SystemTemplates配置图外加电子邮件:FromName和Smtp:FromName作为默认:明确的环境/应用程序设置优先. 电子邮件标记是名称的第一个字母;应用程序可以通过SystemTemplates:Applications指定自己的:<applicationId>:布兰德马克. 发件人地址以 Smtp: From Address 单独配置.

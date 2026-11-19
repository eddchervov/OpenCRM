<a name="connect-opencrm-through-rabbitmq"></a>

# 通过 RabbitMQ 连接 OpenCRM

[English](opencrm-integration.md) | [Русский](opencrm-integration.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

OpenCRM 已经使用 EmailMessages 发件箱和结果处理器 。 运行的应用程序仅依赖于通知. Contacts.  在这个解决办法中,这就是项目参考服务/通知/src/通知。 CI从一次离职中构建应用程序和工人;在发布时保持合同和消费者版本的一致性.

<a name="addresses"></a>

## 地址

生产:通知-公开命令和通知-公开命令-结果。
开发:通知-开放-开发-命令和通知-开放-开发-结果。
部署操作员提供消息代理地址和网络;默认端口为5672.
本地OpenCRM配置为192.0.2.1:5672.
RabbitMq:工人的应用程序包含允许的应用程序ID.
客户端应用程序的名称通过RabbitMq:ApplicationId设置.
工作者创建客户对话队列( W)`TelegramChat` 和 `ConversationDelivery`仅用于 `Conversations:ApplicationId`;该ID必须属于 `RabbitMq:Applications` 并匹配 OpenCRM 应用程序 `RabbitMq:ApplicationId`.
 有多个应用程序, `Conversations:ApplicationId` 强制的。 仅允许在 `RabbitMq:Applications` 只有一个项目。  不要将生产和开发对话连接到同一个 Worker 实例: 它共享一个 API URL 和服务对服务密钥 。

 一个结果队列属于一个应用程序数据库.  不将本地数据库连接到生产队列 。 命令使用带有标准信封的MassTransit Send,而不是Publish. 通知发送请求是工人的内部合同。

<a name="commands-and-results"></a>

## 命令和结果

发送通知接受创建通知请求:IdepotencyKey, Channel,收件人,TemplateKey, Data, and TemplateVersion.  不为 Razor 电子邮件提供主题/ Body 。 复习工作使用同样的"同上"(IdepotencyKey). Get Notification status 返回通知状态改变或未找到永久结果队列 。

排队/处理/重试/提供-接受/模拟/失败描述处理。 提供方接受的还并不意味着接收方交付;模拟排除了真正的发送。 内容中包含申请日志的最终主题、body、IsHtml和TextBody。

行政信息使用频道 `telegram` 和受援方 `owner`. 代表一个组织发送的客户信息使用单独的 `telegram-client` 频道和收件者 `<organizationGuidN>:<chatId>`. 工人们从信使那里拿回信使 `GET https://<opencrm-host>/api/internal/notification-bots/telegram/{organizationId:N}` 与 `X-Notification-Service-Key`, `X-Notification-Key`,以及 `X-Notification-Recipient` 信头( 收件人的聊天ID); 成功的响应是 `{"botToken":"..."}`.  该令牌立即被传送到HTTPS网关,从不存储在通知队列或数据库中.

组织所有人通过受保护的工人终点验证该标志 `POST /getOrganizationBot` 与 `{"bot_token":"..."}`. 设置网点通过 `POST /setOrganizationWebhook` 与 `{"bot_token":"...","organization_id":"<uuid>","secret":"<telegram-secret>"}`. 电报电话 `POST https://<worker-host>/updates/{organizationId}`;工人将身体向前走, `X-Telegram-Bot-Api-Secret-Token` 改为固定 `https://<opencrm-host>/api/internal/notification-bots/telegram/{organizationId}/updates` 地址 地址 地址 地址 OpenCRM必须对照该组织的秘密核查提供的秘密.

OpenCRM登记了变更通知状态和驳回通知的通知发布者,以及报告的通知提交者。 端点使用配置ConsumeTopology=false. 交易中的状态和对帐更新;重复的交付事件被 EventId 忽略.

 其他客户端可以通过Upsert Notification Template注册不可变换的数据库模板版本,然后还必须使用AnnexTemplate Upspated. OpenCRM 不使用此命令: 编辑器电子邮件使用系统/ Custom- email 。

<a name="version-1-razor-templates"></a>

## 第1版 Razor 模板

- 系统/电子邮件确认:名称、确认Url。
- 系统/密码-重置:名称,重置Url.
- 系统/登录码:代码-八位数.
- system/password-reset-mobile:用户ID,代码,重置Url.
- 系统/支持-消息:消息ID,类型Name,名称,电子邮件,电话,话题,文本.
- 系统/订阅通知:类型(即将到来、付费过期、演示过期、演示可用)、组织名称、计划Name、endDate、days Remaining、持续日、ActionUrl。
- system/custom-email:主题,htmlBody – 从应用程序编辑器中信任的 HTML.
- 系统/订阅 - 过期: 名称, 订阅Name, endDate, 更新Url.

SystemTemplates:应用程序中允许键 :<applicationId>:腾讯. 配置图开放crm和开放crm-开发包含在基础应用中. 模板和模型载于模板/OpenCRM/V1。 数据不是作为Razor执行的;自定义电子邮件在不逃跑的情况下插入了HTML,所以其来源必须被信任.  见 [规则和版本](templates.zh-CN.md).

<a name="provider-events"></a>

## 提供者活动

使用email:Provider=unisender-go,适配器在X-UNISENder-GO头中将应用程序_id和通知_key添加到global_metadata. UnisenderGo:TrackLinks和有轨电车控制跟踪. 工人验证了webhook的签名,并向应用程序的地址队列发送了已报告的通知。 OpenCRM不知道HTTP请求格式或提供者密钥.

 没有应用程序_id或通知_key的事件会被忽略. 与旧协议没有兼容性 。 普通 smtp 不添加 Unisender 头条 。

 关于移徙和融合环境,见 [./README.md (英语).](../README.zh-CN.md),集成设置和迁移部分;nginx配置相同 `docs`  目录。  先用模板和迁移来提升Worker构建,再用OpenCRM和HTTPS webhook路由来更新.  一位老工人不支持新的合同。

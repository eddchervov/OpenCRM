# 配置 OpenCRM

[English](configuration.md) | [Русский](configuration.ru.md) | **简体中文**

OpenCRM 读取标准 .NET 配置。JSON 中使用 `Parent:Child` 表示分层键，环境变量以双下划线代替冒号，例如 `ConnectionStrings__OpenCRMConnection`。

Web 应用依次加载 `appsettings.json`、`appsettings.{Environment}.json`、共享的 `brand.json`、环境变量及命令行参数。配置键重复时，以靠后的来源为准。请通过环境变量或开发密钥提供本地值和凭据。

## 主要设置

| 键 | 用途及默认行为 |
| --- | --- |
| `ConnectionStrings:OpenCRMConnection` | 主 CRM 的 PostgreSQL 连接。在迁移和启动 WebApp 前设置 `ConnectionStrings__OpenCRMConnection`。 |
| `Brand:DisplayName`、`Brand:PublicBaseUrl` | 应用名称和公开网址。服务器进程接受对应的 `Brand__DisplayName` 和 `Brand__PublicBaseUrl`，其他品牌字段在共享 JSON 中设置。参见[品牌说明](../configuration/README.zh-CN.md)。 |
| `Administration:Sections` | Web 与桌面管理端共享的可选产品链接。在 `brand.json` 中指定各链接的 `Title`、本地绝对路径 `Url` 及现有的 `Permission`。界面按权限筛选链接，目标端点也须自行检查权限。 |
| `AdminDesktop:*` | `brand.json` 中的可选绝对路径，分别用于桌面 Chrome 配置、待处理的支付操作、受保护的会话和仪表板刷新设置。省略时使用内置的用户级路径。 |
| `UserTracking:Enabled` | 默认 `false`。打开后增加访问和登录计数、客户端访问快照。基本的最近登录日期、登录方式和独立的活动审计不受此开关影响。 |
| `Export1C:ProcessingDownloadUrl` | 启用 1C 导出功能时可显示该本地网址。外部或无效网址会被忽略。 |
| `EmailConfirmation:Required` | 默认 `false`。如需邮箱验证，请单独配置并接入邮件投递。 |
| `Notifications:Transport` | 默认 `Disabled`，基础登录和 CRM 可直接使用。启用实际投递时使用 `RabbitMq` 和单独的 Notification Worker。`InMemory` 用于独立测试。 |
| `RabbitMq:*` | 与 Worker 通信使用的消息代理参数和 `ApplicationId`。Worker 必须运行并连接同一消息基础设施。 |
| `PlatformPolicy:Commercial:Enabled` | 默认 `false`，CRM 无套餐限制。设置为 `true` 后启用商业模块并初始化内置套餐。需在管理端核对价格、配额和条款。实际购买另需支付基础设施。 |
| `Assistant:*`、`AssistantTelegram:*` | AI 助手和 Telegram 配置。默认关闭，启用时需自行配置提供方与渠道。密钥请存放在 Git 之外。 |
| `PaymentsService:*`、`YooKassa:*` | 订阅支付服务的集成设置。默认关闭，启用时需提供相应服务、密钥和返回网址。 |
| `S3:*` | 对象存储参数。凭据单独配置，基础本地部署可以省略。 |
| `IdentitySettings:*` | 密码、登录与账户锁定策略。 |
| `Security:AllowedCorsOrigins` | 浏览器请求允许的来源，填写当前部署的客户端地址。 |
| `DataProtection:ApplicationName`、`DataProtection:KeysDirectory` | 数据保护的隔离标识和密钥环目录。应用名称默认为 `OpenCRM.WebApp`，升级时保留两项设置。 |
| `ReverseProxy:KnownProxy` | Production 环境要求填写受信任的反向代理 IP；本地开发可省略。 |

### 桌面管理端路径

桌面启动器和管理应用读取与应用一起提供的 `brand.json` 中的 `AdminDesktop` 对象。

| 键 | 用途 |
| --- | --- |
| `ChromeProfileDirectory` | 桌面启动器的 Chrome 配置目录。 |
| `PendingActionsDirectory` | 服务端响应未知时，保存待处理支付操作的本地目录。 |
| `SessionDirectory` | 桌面管理会话的保存目录。Windows 使用当前用户的 DPAPI 保护会话文件。 |
| `DashboardPreferencesFile` | 保存仪表板刷新间隔的文件。 |

环境变量展开后，非空路径必须为绝对路径。省略某个值或整个 `AdminDesktop` 段时使用内置的用户级路径。会话目录应只允许相应的操作系统用户访问。

`Administration:Sections` 的每条记录包含 `Title`、以单个 `/` 开头的本地绝对路径 `Url`，以及 `Permission`（例如 `Admin.Dashboard.View`）。路径不接受查询参数和片段。两个管理端根据当前账户的权限或系统管理员身份筛选链接。Web 管理端加上请求的 `PathBase`，桌面管理端在系统浏览器中打开路径并保留服务器的基础路径。浏览器执行正常登录流程，JWT 或其他凭据不会附加到网址。各产品端点仍须自行实施授权检查。参见[定制指南](extending-opencrm.md#product-links-in-both-admin-panels)。

为读取每项设置的目标进程提供环境变量。WebApp、通知 Worker 和支付服务分别配置。

## 凭据与本地值

连接字符串、RabbitMQ 密码、AI 和支付服务密钥、SMTP 凭据及对象存储密钥应放在部署环境的密钥存储中，或放进不受 Git 跟踪的本地配置。开发时可在当前会话设置环境变量。`dotnet user-secrets` 仅用于设置了 `UserSecretsId` 的项目，WebApp、迁移项目与服务没有共用 ID。

`configuration/brand.json` 用于品牌配置。服务器进程可通过 `Brand__DisplayName` 与 `Brand__PublicBaseUrl` 覆盖相应值，桌面应用读取 JSON。一次部署中的各进程应使用一致的品牌文件与网址。

## 可选集成

### 通知与邮件

`Notifications:Transport=Disabled` 和 `EmailConfirmation:Required=false` 时可正常注册。`Smtp:*` 由独立的 Notification Worker 读取。启用邮箱验证前应验证整个邮件投递链路。参见[通知服务说明](../services/notifications/README.zh-CN.md)。

### 移动端密码重置链接

Worker 的 `SystemTemplates:Applications:<applicationId>:MobileDeepLinkScheme` 设置移动端重置邮件中的 URI 方案，默认 `opencrm`。环境变量使用双下划线，例如 `SystemTemplates__Applications__sample-crm__MobileDeepLinkScheme=sample-crm`。值须以 ASCII 字母开头，最多 64 个 ASCII 字母、数字、加号、句点或连字符。Web 重置网址仍随消息数据传递。

### 商业模式与支付

将 `PlatformPolicy__Commercial__Enabled` 设为 `true` 后重启 WebApp。启动时初始化内置套餐。向用户开放购买前，应在管理端配置价格、配额和条款，并按[支付集成指南](payments-service-integration.zh-CN.md)接入支付服务、设置返回网址和密钥。

`PlatformPolicy:Commercial:Defaults` 支持 `FreeVehicleLimit`、`FreeClientLimit`、`FreeMembersLimit`、`WorkspaceMembersLimit`、`PersonalVehicleLimit` 和 `DemoVehicleLimit`。默认 `0` 表示无限制，值须为非负数。它们用于初始化新套餐、补充缺失的分配并提供商业模式默认值。已有价格和已保存的配额不会被覆盖。商业模式关闭时无套餐配额限制。

WebApp 的 `PaymentsService:ApplicationId` 与支付服务的 `Payments:ApplicationId` 必须相同，默认为 `opencrm`。ID 长度 1 至 64，允许小写 ASCII 字母、数字和连字符，且须以字母或数字开头。命令与事件队列分别为 `payments-{ApplicationId}-commands` 和 `payments-{ApplicationId}-events`。升级时保留已部署的 ID 和已存储的命令负载。更改显示名称无需迁移支付标识。

### AI 助手

`Assistant` 包含 `Enabled`、提供方网址和允许地址、模型、限制及处理参数。`Enabled` 默认为 `false`。启用时配置提供方及环境密钥 `Assistant__RouterApiKey`（旧名称为 `AiPilot__ApiKey`）。参见[服务端 AI 助手](ai-assistant.zh-CN.md)。

## 查看可用配置

- WebApp 默认值：`src/OpenCRM.WebApp/appsettings.json` 和 `src/OpenCRM.WebApp/appsettings.Development.json`。
- 配置加载顺序：`src/OpenCRM.Hosting/OpenCRMApplication.cs`。
- PostgreSQL、Identity 和 Hangfire 注册：`src/OpenCRM.WebApp/Extensions/ServiceCollectionExtensions.cs`。
- 助手配置：`src/OpenCRM.WebApp/Infrastructure/Assistant/AssistantOptions.cs`。
- 品牌设置：`configuration/brand.json` 与[品牌说明](../configuration/README.zh-CN.md)。
- 通知配置：`services/notifications/src/Notifications.Worker/appsettings.json` 与[通知服务说明](../services/notifications/README.zh-CN.md)。
- 支付配置：各支付项目的 `appsettings.json` 与[支付服务说明](../services/payments/README.zh-CN.md)。

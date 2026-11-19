# OpenCRM 微服务

[English](microservices.md) | [Русский](microservices.ru.md) | **简体中文**

CRM 核心在 ASP.NET Core 主机中运行。通知和支付由独立应用处理，可以按需接入。

## 各服务如何独立运行

- 可在 CRM Web 界面之外单独部署、启动和更新服务。
- 每个服务使用自己的连接、密钥和数据库。
- 队列处理和与服务提供方的交互在后台进行。
- 本地注册、密码登录和基本 CRM 工作可直接使用核心应用。

Web 主机组成和业务模块边界见[应用架构](architecture-current.zh-CN.md)。

| 组件 | 职责 | 说明 |
| --- | --- | --- |
| WebApp 与业务模块 | 用户、组织、权限、业务数据、单据和工单收款记录 | [本地部署](local-run.zh-CN.md) |
| 通知 | 投递队列、模板、渠道适配、后台重试和状态 | [服务说明](../services/notifications/README.zh-CN.md) · [核心集成](notifications.zh-CN.md) |
| 支付 | 支付操作、状态、服务提供方集成和后台对账 | [服务说明](../services/payments/README.zh-CN.md) · [核心集成](payments-service-integration.zh-CN.md) |

## 通知

代码位于 [`services/notifications`](../services/notifications)，解决方案为 [`NotificationService.slnx`](../services/notifications/NotificationService.slnx)。可执行程序 `Notifications.Worker` 提供队列处理、健康检查和已配置服务提供方的回调。启用渠道及外部投递服务时，请使用自己的配置。该服务的投递入口为队列。

基础 CRM 默认使用 `Notifications:Transport=Disabled`。要接入投递，请准备通知服务、RabbitMQ、服务数据库及相应渠道配置，并为 WebApp 设置 `RabbitMq` 传输和匹配的消息代理参数。SMTP 为可选项，密码注册与内部支持请求可独立运行。`InMemory` 传输用于独立测试。

参数和操作命令见[通知服务说明](../services/notifications/README.zh-CN.md)，本地容器环境见 [`compose.yaml`](../services/notifications/compose.yaml)。

## 支付

代码位于 [`services/payments`](../services/payments)。服务有自己的 API、存储和提供方配置。接入时需要提供服务器地址、密钥、允许的返回网址及两侧匹配的设置。

平台订阅默认关闭。商业模式和支付提供方分别启用。CRM 中记录客户工单收款可独立使用。启动说明见[支付服务说明](../services/payments/README.zh-CN.md)，连接核心的步骤见[支付集成](payments-service-integration.zh-CN.md)，容器环境见 [`compose.yaml`](../services/payments/compose.yaml)。

## 部署步骤

1. 按[部署指南](local-run.zh-CN.md)启动并验证基础 CRM。
2. 选择需要的集成，并阅读服务说明。
3. 创建独立数据库，为相应进程提供密钥。
4. 按指南运行服务迁移，再启动服务的 API 或 Worker。
5. 为连接双方设置匹配的地址和服务密钥。
6. 接入生产服务提供方之前，验证健康检查、测试操作以及失败重试。

环境变量名称和前缀取决于目标应用。WebApp、通知服务和支付服务分别配置。参见[配置](configuration.zh-CN.md)。升级时保留原有数据并核对核心与服务之间的接口。更换 PostgreSQL 版本或迁移数据目录前请单独做好准备。

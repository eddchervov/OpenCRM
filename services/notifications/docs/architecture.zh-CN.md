<a name="architecture"></a>

# 建筑

[English](architecture.md) | [Русский](architecture.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

通知服务有一个可执行的项目,即通知. Worker. OpenCRM和其他应用程序决定为什么需要通知;服务拥有模板,交付,重试和历史.  它不共享应用程序的数据库或域模型.

<a name="external-boundary"></a>

## 外部边界

公共合同载于通知。

| 命令 | 目的 |  结果 |
| --- | --- | --- |
| SendNotification | 创建带有 idepotency 密钥的通知 | NotificationStatusChanged |
| GetNotificationStatus | 以原始密钥获取状态 | NotificationStatusChanged |
| UpsertNotificationTemplate | 存储模板 | NotificationTemplateUpdated |

无效的数据会以原命令的MessageId生成NotalCommand Reject. 损坏的封套和基础设施故障导致排出废气的运输重试 `*_error` 队列; 无法保证他们获得企业认可。

命令使用 MassTransit 发送到 `notifications-{applicationId}-commands`. 配置 ConsumeTopology 已禁用: 在全球发布命令类型并不将它与每个应用程序绑定. 应用程序Id来自所接入的队列,不需要在命令机构内被通过.  该服务还选择了响应地址;一个用户提供的响应Address不用于业务结果.

 结果直接发送给 `notifications-{applicationId}-results`,没有全球出版。 RabbitMQ用户和权限限制申请权限.  不给应用程序访问其它应用程序的队列,内部通知发送队列,或共享管理员账户.

<a name="transactions-and-retries"></a>

## 交易和重试

外部消费者使用EF消费者出柜/收发箱. 命令处理和发出的信件会在一个交易中执行 。 收件箱消除了重复的MessageIds;一个独特的(applicationId,同上potencyKey)索引可以保护使用新MessageId重复的命令. 钥匙在检查和坚持之前会修剪。 重复时,第一份通知仍然具有权威性:新数据不会取代其内容.

内部通知发送请求保留其原始合同并排队。 发送的消费者用公交车的发车箱创建了单独的依赖范围.  它可以在一次交易中坚持尝试、状态和处理事件,而不在SMTP/HTTP呼叫中持有收件箱交易。 送货盘与登记分开。

 每10秒,调度员会公布到期的延期工作,并发布超过10分钟的处理租赁. 租借重置上一个出版物标记。 原子更新公司声称该通知;终端记录不怨恨。 四次开始尝试后,普通的提供商出错导致失败.  未处理的基础设施故障仍然在错误排队,需要诊断。

 这提供至少一电文发送。  如果提供者接受一个消息,但进程在坚持结果之前退出,则可以重复外部发送。  没有提供者的一意孤行支持,没有绝对的一地发送担保。 成功呼叫后的持续失败不归为提供者拒绝。

<a name="telegram-customer-conversations"></a>

## 电报客户对话

正在发送的对话信件被存储在 OpenCRM 中,并作为 `ConversationDeliveryWakeup(ProviderCode, MessageId, RequestId)` 转到单独的频道队列 : `notifications-{applicationId}-conversation-{provider}`. 合同只承认Telegram和MAX;每个相接的适配器都得到自己的队列和工人,保持提供者的货币和韧性独立. 信号只携带标识符;信使和附加字节在获得交付租赁后通过被保护的HTTPS API被转移. RabbitMQ是一个醒悟机制;最终的交付状态被存储在OpenCRM和NotesService中.

工人宣布一个单独的耐用队列 `x-overflow=reject-publish`;默认限制为500个信件和8个米B,并带有边框前缀和通货.  它还预先宣布了500个消息和8个米B的有界限的错误排队,以能力拒绝新的出版物. OpenCRM发布到主队列的交换中带有强制性的路由,并等待出版商的认可. 这使得出版商无法创建有不同参数的队列,如果消息代理拒绝出版,则在发件箱中保留了命令. 只有在评价内存和处理吞吐量后才能增加限制.

兔子的消费者只表演 `INSERT ... ON CONFLICT DO NOTHING` 输入持久收件箱,并在行承诺后确认消息。  它不调用OpenCRM或Telegram. 受限人数的工人要求有适当的记录 `FOR UPDATE SKIP LOCKED`,坚持租赁和下一次尝试时间;核心API出错和409个答复产生持久的后退. 重复使用同一种ID的唤醒既不创建第二行,也不同时拨打提供者.  未处理的记录超过24小时 `expired` 并出现在周期性队列快照中。

Inbox 坚持性错误 得到三个有限制的 Mass Transit 重试 。 之后,原始消息进入受约束的 `*_error` 队列。  没有自动批量回放;一个完整的回放可以重复一个结果未知的外部API调用. 原因一经解决,就逐一调查消息. 国家 `LeaseId` 防止盲目地重新进行已经开始的尝试:其结果仍然不确定。  在收件箱持续后,兔子重复操作是不必要的:数据库工作人员进行恢复,服务后无法使用 OpenCRM 用相同的ID重新刷出醒来.

该机制的基础是: [Mass Transit 发件箱文档](https://masstransit.io/documentation/patterns/transactional-outbox).  该项目保留了MassTransit版本8.5.7.

<a name="states"></a>

## 国家

| 成果状况 | 含义 |
| --- | --- |
| 已排入队列 | 保存的通知,包括延期发送 |
| 处理 | 处理已启动; 当请求快照时返回 |
| 正在重试 | 尝试失败; 将随之重试 |
| 供应商接受 | 提供商接受了该消息 |
| 模拟 | 仅应用日志; 未发生真实发送 |
| 失败 | 尝试限制已用尽 |
|  未发现 | 应用程序没有该密钥的记录 |

现有的数据库仍然保存着Sent状态下的成功尝试. 模拟是由一个坚持不懈的供应商标识前缀的 `log:`,所以该快照在重启后或仅更改日志后仍然正确。

事件可能重复或被推迟。 客户端被 EventId 调试,匹配应用程序Id/IDEMpotencyKey,考虑SpecialCount,从不以更早的状态来替换终端状态. Get Notification Status 恢复快照 。 旧通知不会收到追溯事件;其状态可以通过密钥请求.

模板在(应用程序、密钥、频道、版本)密钥中有一个不可改变的版本。  确认再次登记同一内容;否决一个版本。  对于数据库模板,客户端会等待通知Templated Updated with the request supplate. 系统/系统下的系统电子邮件* 由Razor.cshtml与"工人"编译. SystemTemplates:应用程序决定访问和应用程序名称.  他们使用属于自己版本的打字模型和布局.  它们不能登记,也不能被消息代理命令所取代。 命令接收使电子邮件快照与发件箱同步并持续。 交付和重试不会重读模板; 旧任务在第一次发送前获得版本 1 的快照 。  见 [模板](templates.zh-CN.md) 详细信息及移民指令。

 没有HTTP通知发送API. 工人揭露 `/health/live` 和 `/health/ready`;在退化或不健康时,准备返回HTTP 503。 检查原木和消费者以及卫生对策。 与 UnisenderGo:WebhookEnabled 一起,工作人接受在 /webhooks/unisender-go 上签名的事件,并将共享的通知交付合同转发给应用程序队列. 提供商WebhookEvents 和公交发货箱 执行一次交易。 稳定事件确定重复 。  一个成功的SMTP呼叫意味着提供者接受;交付的到来作为单独的事件. 通知状态已改变。 Content 仅包含为自己的应用程序提供的电子邮件快照 。

<a name="switching-providers"></a>

## 切换提供者

通知发送器依赖于INotific化ChannelSender,并按频道选择一个执行. SMS.ru不是域名模型或RabbitMQ合同的一部分. 电子邮件:提供,Sms:提供,而Telegram:提供选择一个特定的基础设施适配器;Smtp,SmsRu,和Telegram部分仅包含各自的集成参数. 新的协议需要适配器、注册和配置验证;业务逻辑和服务客户端保持不变。

混凝土适配器和设置分别位于基础设施目录中:提供商/SmsRu包含SmsRu NotificationSender和SmsRu Options;提供商/Smtp包含Email NotificationSender和Smtp Options. 命名空间匹配目录 。 提供商/电报包含Telegram NotificationSender和Telegram可选功能.  它只接受逻辑 `owner` 地址;其配置存储了bot 符和所有者聊天_id. 共有的交付环境以及依赖性登记仍留在基础设施中。

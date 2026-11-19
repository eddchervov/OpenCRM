<a name="opencrm-subscription-payment-service"></a>

# OpenCRM 订阅支付服务

[English](README.md) | [Русский](README.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

独立支付.NET 10及其自己的PostgreSQL和RabbitMQ.  所有三个项目都包括在 `OpenCRM.sln`: `Payments.Contracts` 和 `Payments.Service` 包含在 `Services/Payments`,以及 `Payments.Tests` 已经进入 `Tests`.  他们不参考 OpenCRM 。 检查和账单通过单独的金融运输连接;参见 [融合和恢复指南](../../docs/payments-service-integration.zh-CN.md). 生产要求列于 [服务器设置](../../docs/payments-service-integration.md#server-setup).

 这是另一个阶段的 [结构设计](../../docs/payments-service-design.zh-CN.md):命令收录,操作坚持性,测试/活取YooKassa适配器,webhook收件箱,调和,退款,以及可靠的状态交付. 订阅补助金、促销代码和目前的收款方式仍然在主要应用中。

<a name="modes-and-boundaries"></a>

## 模式和边界

- `Disabled`  是默认模式。 收据和 `payments_disabled` 拒绝被存储; 没有创建可执行的货币操作 。 启用一个模式不会执行先前拒绝的命令 。
- `YooKassaTest` 明确使用测试商店。  它需要店面的身份证 和秘密的前缀 `test_`; 答复必须 `test=true`.
- `YooKassaLive` 使用有明确内容的活店 `IsTest=false`,数字商店身份证,和 `live_` 键。 意图、存储的付款、储存和提供者的反应模式必须相匹配。
- 单阶段一次性订阅付款 `capture=true` 已支持。 自动支付、保存卡片和为工作室获取不启用。 `Purpose=subscription`, `InitiationMode=customer`; 拒绝其他值。
- 未知的结果不是失败。 Retries保存了原来的正体/钥匙;在从第一个可能的发送开始23小时后,一个新的POST被屏蔽,操作需要调查. 获取已知对象在此时期后仍然可用 。
- 全额和部分退款有单独的命令,操作,密钥和金额预留. 货币状态通过提供者API确认. 开出账单记录退款,需要人工审查应享权利;不自动缩短订阅时间。

<a name="start-a-separate-environment"></a>

## 启动单独的环境

 从PowerShell 7 中的寄存器根中运行 。  在重用已存在的卷时, 保留相同的密码 。  此示例为第一个启动生成随机值 。 保存在您自己的受保护的商店中, 并重用相同的值, 供以后从这些卷开始 :

```powershell
$env:PAYMENTS_POSTGRES_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$env:PAYMENTS_RABBITMQ_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$env:PAYMENTS_SERVICE_KEY = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
docker compose -f services/payments/compose.yaml up -d --build
```

密码 `payments-service` 堆栈使用自己的卷, `payments` 数据库和用户,以及 `payments` 兔MQ虚拟主机. 迁移容器在服务开始前完成。 正常的应用程序启动不会自动应用迁移. 应用容器运行时没有根.

本地地址: HTTP `http://localhost:5087`,邮编SQL `localhost:5434`, AMQP 数据 `localhost:5674`兔子MQ管理 `http://localhost:15674`. 端口绑定只能回回转 。 外部的webhooks需要单独的HTTPS倒置代理和测试商店;这个Compose设置不是生产部署.

 要让柳卡萨测试请求, 设置 `PAYMENTS_MODE=YooKassaTest`, `PAYMENTS_SHOP_ID`,以及 `PAYMENTS_TEST_SECRET_KEY`  在受保护的环境中,只重新创建支付服务。  不要从主应用程序的配置中取出这些值 。  在商帐户之间切换已经接受的操作是不支持的:该帐户在意图中是固定的,以及不匹配的块执行.

 对于没有 Docker 的启动, 应用程序接受 `ConnectionStrings__PaymentsDb`, `Payments__ServiceKey`, `RabbitMq__Host`, `RabbitMq__Port`, `RabbitMq__VirtualHost`, `RabbitMq__Username`,以及 `RabbitMq__Password`. 数值在 `appsettings.json`  没有密码。 单独引用移入:

```powershell
dotnet run --project services/payments/src/Payments.Service -- --migrate
dotnet run --project services/payments/src/Payments.Service
```

`/health/live` 检查程序; `/health/ready` 检查付款办法和大众运输。 健康服务 `Disabled` 模式并不意味着可以充电。

<a name="contracts"></a>

## 合同

直接通过 MassTransit 发送命令 `Send` 改为: `queue:payments-opencrm-commands`.  没有HTTP支付创建命令. 授予 RabbitMQ vhost 和队列访问权限, 只用于信任的应用程序 ; `ApplicationId`  不取代运输许可。

| 合同 |  主要领域 |
| --- | --- |
| `CreatePayment` | `CommandId`, `ApplicationId=opencrm`, `PurchaseId`, `AmountMinor` 以考佩克语写作, `Currency=RUB`,描述, `ReturnUrl`,发起人/买方 `BuyerId`,权利接受者 `BeneficiaryId`. |
| `RequestRefund` | `CommandId`, `ApplicationId`, `PurchaseId`,以kopeck为单位的金额,理由,并请求作者. |
| `PaymentStateChanged` | `EventId`, `PurchaseId`,意向识别,增加 `AggregateVersion`,创建/支付状态,金额,提供者ID,URL,测试旗. |
| `PaymentCommandRejected` |  在创建新的货币操作前,命令/购买ID和拒绝代码. |
| `PaymentOperationStateChanged` | 操作/命令 ID, Kind/ status, 安全错误代码, 包括取消/ 拒绝退款 。 |

 只提供新的客户参考 `Payments.Contracts`. 单独测试发送器的示例命令 :

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

成果归根结底 `payments-opencrm-events` 队列。  它在没有消费者连接的情况下保留消息. 消息代理承认并不意味着已经获得订阅。 账单存储一个耐用收件箱和最新版本;应享权利赠款和应用程序是原子式的。 尚未执行将应享权利申请确认为支付;应用程序状态在OpenCRM中存储并检查。

重复 `CommandId` 内容完全一样, 则不产生任何内容; 不同的内容被拒绝 。 重复 `PurchaseId` 具有不变意图和新的 `CommandId`  不创建第二次付款。 接受意图的价格、货币或受益人不能改变。 开出账单保留受益人未完成的购买;不同购入清单下不相容的采购被冻结,直到知道结果为止。

`ReturnUrl`  必须完全匹配来自 `Payments:AllowedReturnUrls`  加号 `?purchaseId={PurchaseId:D}`;默认情况下,列表仅包含当前HTTPS支付完成路由.  在启用独立测试界面时明确配置允许的 URL 。 数额限于不超过1,000,000卢布的瑞联付款正数。

<a name="http-and-notification-processing"></a>

## HTTP和通知处理

- `GET /internal/payments/opencrm/{purchaseId}` 需求 `X-Payments-Key`.  它返回已核实的状态和操作,没有请求机构,提供者密钥,或卡片数据.  这是内部服务器 API: 永远不要将其密钥给浏览器.
- `POST /webhooks/yookassa` 接受被绑定的JSON,存储一个未经验证的信号,并持续后返回 HTTP 200 。 重复生成没有新记录 。  如果没有数据库,则不返回成功确认 。 无效的 JSON/ event 类型收到400;超限触发限制器拒绝.
- 工人通过YooKassa API重新检查对象,核实金额、RUB、商号、 `test`和内部意图ID。 单靠网路浏览无法确认付款 。 未知对象仍然排队进行再检查:在创建响应前可能收到通知.
- 存储的webhoks和操作在重启后进行处理. 到期的租约允许另一名工人接管;租约令牌保护记录不被一个老的执行者所继承。 收到的钱不会因为另一起行动失败而退缩。

 其他应用程序无法通过HTTP API获得结果. 初始阶段的单个可信应用有一个服务密钥;多应用程序授权需要单独的配置.

<a name="database-and-migration"></a>

## 数据库和移徙

EF 核心 10, Npgsql, e. `payments` 计划 和一个单独的移民历史表 初始迁移会创建意向,使用外键操作,可持久地命令分解,一个webhook收件箱,以及MassTransit EF收件箱/出件箱. `CommandId` 独立于运输收件箱清理而保存。 金额为整数kopeck;数据库限制禁止负退,退款超过支付,并禁止复制提供者ID.

`InitialPayments.Down` 放弃服务表和所有付款数据。  这是为空地环境进行技术退缩,而不是安全退缩的活期付款。  一旦使用,恢复需要备份和提供者的核对;不进行自动数据库回滚。 生产使用前,建立建筑设计中描述的经核实的备份/恢复过程,供应商登记册,以及财务事故处理.

<a name="validation"></a>

## 验证

 从寄存器根 :

```powershell
dotnet sln OpenCRM.sln list
dotnet build services/payments/tests/Payments.Tests/Payments.Tests.csproj
dotnet test services/payments/tests/Payments.Tests/Payments.Tests.csproj --filter FullyQualifiedName~.Unit.
dotnet test services/payments/tests/Payments.Tests/Payments.Tests.csproj --filter FullyQualifiedName~.Integration.
dotnet build OpenCRM.sln
```

集成测试需要多克克并启动自己在随机端口上的临时PostgreSQL/RabbitMQ例. HTTP适配器使用受控模拟器来检查;自动测试不执行真实收费或调用真实的YooKassa API.  这些检查既不需要主数据库,也不需要运行中的 OpenCRM 实例 。

 目前的限制:外部登记册的核对、当地未知的付款和操作员通知需要单独的验收测试。 历史的支付/收款仍然保存在OpenCRM;历史不会被自动转移到新的数据库. 工作室的购置和自动收费仍然是未来的扩展。 自动测试没有使用真正的YooKassa API或实收费用。 现场直播模式和外部网络hook发送尚未核实 。

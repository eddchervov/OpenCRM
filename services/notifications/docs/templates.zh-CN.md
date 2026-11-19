<a name="razor-system-emails"></a>

# Razor 系统电子邮件

[English](templates.md) | [Русский](templates.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

通知Service系统电子邮件作为.cshtml文件存储在寄存器中. OpenCRM通过Send Notification发送模板密钥,精确版本和数据.  该服务将 Data 转换为打入的模型, 验证它, 并制作电子邮件 。  在确认队列之前,将标的、正文和版本与通知和发件箱一并坚持。 交付,推迟发送,再试使用此快照.

<a name="structure"></a>

## 结构

系统模板由应用程序和电子邮件共享。 应用程序配置配置文件决定了访问权限和在电子邮件中显示的名称.

~~~text
src/Notifications.Infrastructure/
  Templating/
    RazorSystemTemplateRenderer.cs
  Templates/OpenCRM/V1/
    _Layout.cshtml
    _ViewImports.cshtml
    Models/
      EmailConfirmationModel.cs
      PasswordResetModel.cs
      SubscriptionExpiringModel.cs
    EmailConfirmation/
      Subject.cshtml
      Body.cshtml
    PasswordReset/
      Subject.cshtml
      Body.cshtml
    SubscriptionExpiring/
      Subject.cshtml
      Body.cshtml
~~~

object.cshtml 使主题没有布局. body.cshtml使用共享_Layout.cshtml. 模型声明需要的字段和长度限制. Razor支持标准@model,@if,@foreach,以及部分视图;当前电子邮件使用共享的布局并输入数据而不附加过多的条件.

Razor SDK在构建过程中将模板编译入基础设施组装. [剃刀 电镀 核心](https://github.com/soundaranbu/Razor.Templating.Core) 制造字符串。  未启用从收到信件或数据库中生成的运行时间汇编 。 "Worker Docker"的图像对Razor和提供者的回叫网呼使用aspnet:10. 申请仅通过RabbitMQ提交通知.

<a name="application-profiles"></a>

## 应用程序配置

 在appsings.json中配置 RabbitMQ 命令收件人和系统电子邮件配置 :

~~~json
{
  "RabbitMq": {
    "Applications": ["opencrm", "catalog"]
  },
  "SystemTemplates": {
    "Applications": {
      "opencrm": {
        "BrandName": "opencrm.example.invalid",
        "Templates": [
          "system/email-confirmation",
          "system/password-reset",
          "system/subscription-expiring"
        ]
      },
      "catalog": {
        "BrandName": "Каталог",
        "Templates": ["system/email-confirmation"]
      }
    }
  }
}
~~~

 这里的应用程序ID和名称是部署设置 。 引擎和Razor模板不检查特定客户端的名称.  通过配置和工人重启添加另一个应用程序,而不修改或重建代码 。

BrandName 用于主题和共享布局.  它通过来自配置文件的VideoData提供,而不是来自用户的命令,而Razor逃脱了它.  它需要,限制在200个字符之内,并且可能不包含控制字符.

模板是明确的密钥允许列表 。 未知的密钥, 重复, 一个空名, 或者一个空列表在启动时被拒绝 。  一个缺失的配置文件不允许该应用程序的系统电子邮件,而无需更改普通信件或数据库模板的访问权限.

RabbitMq:应用程序仍然定义了来袭的队列. 演示文稿配置既不创建队列,也不允许访问另一个应用程序的数据.

 在 Docker 中, 使用标准. NET 环境变量, 如 SystemTemplates , 覆盖设置__应用程序__目录__品牌名称和系统模板__应用程序__目录__模板__0.  还加上了相应的RabitMq__应用程序__N入门.  新配置文件既需要名称,也需要模板列表.

<a name="keys-and-models"></a>

## 钥匙和模型

| TemplateKey | TemplateVersion | 数据字段 |
| --- | --- | --- |
| 系统/电子邮件确认 | 1 | 名称、 确认Url |
| 系统/密码重置 | 1 | 名称, 重置Url |
| 系统/订阅-终止 | 1 | 名称、 订阅Name、 endDate、 更新Url |

 见 [实例/系统电子邮件](../examples/system-emails) 。endDate使用一个ISO 8601日期,例如2026-09-16。  它只在模型中是 Date Only; Razor 模板明确使用 ru-RU 来进行用户化格式化.

系统电子邮件在工人启动后可以到具有相应配置的应用程序中使用. 没有必要事先发送UpsertTemplate或等待通知。 系统/前缀被保留:其下方的模板不能通过RabbitMQ注册或覆盖. 未知的密钥,版本,应用程序或频道被拒绝;不会发生倒向另一个版本或数据库模板的情况.

<a name="sending-from-opencrm"></a>

## 从 OpenCRM 发送

~~~csharp
await endpoint.Send(
    new SendNotification(new CreateNotificationRequest(
        IdempotencyKey: $"email-confirmation:{confirmationRequestId}",
        Channel: "email",
        Recipient: userEmail,
        TemplateKey: "system/email-confirmation",
        Data: new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement(userName),
            ["confirmationUrl"] = JsonSerializer.SerializeToElement(confirmationUrl)
        },
        Subject: null,
        Body: null,
        ScheduledAt: null,
        TemplateVersion: 1)),
    cancellationToken);
~~~

请求是特定 OpenCRM 任务的持续标识符 。 重试该工作使用同样的"同上"powerencyKey. 用户对电子邮件的新请求收到新密钥 。 OpenCRM 构建了象征性的 URL;服务只将其插入到电子邮件中.

数据被转换成一个模型,具有需要的字段,类型,和长度验证. 未知的字段也被拒绝,所以不会引起人们的注意。 JSON 字段名称对大小写不敏感;示例使用骆驼Case.

URL必须是绝对的HTTP(S),没有证书或控制字符. Razor逃脱了模型值:包含 HTML 或 C# 的字符串仍为文本而未执行. Html.Raw不用于用户数据. 所提供主体不得超过1000个字符或包含控制字符.

无效的数据在通知创建或提供者调用之前会以原始的MessageId和 ErrorCode=invalid-command 命令生成通知命令。

<a name="versions-and-changing-emails"></a>

## 版本和更改的电子邮件

RazorSystem TemplateRender目录选择了取景路径. 输入的命令路径不用于查找文件 。 配置文件由应用程序Id从所接入的队列中选择,并验证允许的密钥.

V1包括电子邮件文本,模型,以及共享的布局. 更改内容或模型时 :

1. 增加自有版式和型号的新版本,保留上一个版本.
2. 将精确的应用/键/通道/版本映射添加到目录中.
3.  通过消息代理验证Razor的渲染和交付.
4. 以新版本部署工人。
5. 切换 OpenCRM 到所需的模板Version 。

 不编辑已出版的V1版式以发布V2:已接受的被推迟的邮件被快照所保护,但明确选择V1的新命令必须保留其行为.  当客户端或旧任务可能使用时, 请不要从图像中删除旧模板 。  新版本需要工人的构建和部署。

<a name="editable-campaigns-and-sms"></a>

## 可编辑运动和短消息

 这些保留了数据库模板,其中有" ⁇ "和" ⁇ "( ⁇ url:link ⁇ )等替代.  他们的密钥不能从系统/ 开始.  这是没有Razor处决的单独机制.

Upsert Notification Template 注册了一个不可变的版本,由(applicationId, Key, Channel, Version)来进行. 重复同样的内容是得到承认的;改变现有版本则被拒绝。 发送前, 接收通知 Template 更新后, 并附带相应的密钥、 通道和版本 。

行政运动可以将其最终标记作为模板运动-123版本1存储. 收件人收到密钥,版本,和个人参数;每个收件人不需要单独的模板.

数据库模板只允许平面字符串,数字和布尔数据值. 对象、阵列和无效都被拒绝 。 HTML 参数被逃脱; 将电子邮件标记存储在 BodyTemplate 中 。 循环,表达式,和原始的HTML参数在此机制中是不支持的. 短消息模板使用纯文本.

旧指令没有版本/TemplateVersion使用版本 1. 旧的电子邮件确认和订阅过期的密钥没有系统/ 仍然引用数据库; 内置的 Razor 目录不覆盖它们 。

<a name="snapshot-and-database-update"></a>

## 抓图和数据库更新

主体,body,IsHtml,TemplateKey,和TemplateVersion被存储在命令接受. 重覆的"IdeoptencyKey"返回了之前的状态;新数据不会取代第一个电子邮件. IsHtml=null的老工人记录在联系提供者之前会收到快照.

 快照仍需要之前准备的20260909054007_VersionedTemplateSnapshots迁移. 剃刀不需要额外的迁移。 现有的数据库模板仍为版本1;初始迁移未变.

1. 备份通知数据库 。
2. 拦住老工人.
3.  通过 -- migrate from the new image 应用迁移。
4. 启动新的"工人",再将客户端切换到系统密钥.

 新老工人在过渡期间不得同时处理工作。  所有数据库模板都是第1版,没有通知有TemplateVersion > 1,允许下放。  否则,在删除数据前会停止回滚;需要单独的数据保存计划或备份恢复.

提供者接受是指提供者接受。 工人接收Unisender报告,并通过RabbitMQ向OpenCRM转发正常的交付事件.

<a name="emails-from-the-editor"></a>

## 编辑器的电子邮件

系统/海关电子邮件接受主题和htmlBody。 所编译的模板将可信任的编辑器HTML插入到共享布局;该字符串既不被编译,也不被执行为Razor.  其他系统模板的普通参数仍然未找到。 OpenCRM 不为每个电子邮件注册单独的数据库模板 。

通知状态已改变。 Content 将主题、 HTML 和纯文本快照返回到自有应用程序的队列中, 这样 OpenCRM 可以在其日志中显示已读取的电子邮件 。 独立提供商WebhookEvents的迁移涉及提供者事件,而不是Razor.

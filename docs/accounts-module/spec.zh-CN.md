<a name="accounts-module-extraction-specification"></a>

# 账户模块提取规范

[English](spec.md) | [Русский](spec.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

> **型号变更.**  下文所述的提取是一个历史规格。 账户设想、模式、建议和能力及其API和存储已被删除。 账户拥有注册,签入,用户删除,个人记录. 组织类型目录属于组织. 以往保存账户概况和登记其服务的要求已不再是时事。 目前的模式、数据库编制和恢复进程正在进行之中。 [配置文件删除](profile-removal.zh-CN.md).

<a name="1-goal"></a>

## 1. 目标

摘录 `OpenCRM.Modules.Accounts`  作为单独的应用程序模块,从 `OpenCRM.Infrastructure.BL`.  它拥有账户概况和账户应用情景,并登记自己的服务. 模块不依赖于 `OpenCRM.Infrastructure.BL` 或实施其他模块。 公共合同和HTTP行为保持不变。

<a name="2-extraction-scope"></a>

## 2. 采掘范围

将这6个执行移出 `OpenCRM.Infrastructure.BL`:

|  文件 | 执行 | 终身 |
| --- | --- | --- |
| `AccountProfiles/AccountProfileService.cs` | `IAccountProfileService` | 范围d |
| `AccountProfiles/AccountProfileAdminService.cs` | `IAccountProfileAdminService` | 范围d |
| `Users/UsersService.cs` | `IUsersService` | 瞬变 |
| `Users/AccountRegistrationService.cs` | `IAccountRegistrationService` | 范围d |
| `Users/AccountDeletionService.cs` | `IAccountDeletionService` | 范围d |
| `Users/EmailCodeLoginService.cs` | `IEmailCodeLoginService` | 范围d |

 这些部件没有移动,而是留在其所有者那里:

- `Admin/AdminUsersService.cs` 和行政用户查询;
- 收费、订阅和促销码(`IPromoCodeService`);
- 电子邮件发送( E)`IEmailNotificationService`通知发送和排队操作人员;
- 工作单位 `IApplicationUnitOfWork`/`ApplicationUnitOfWork`;
- 身份适配器、 cookie、 CSRF、 视图、 回调 URL 和重定向 `WebApp`.

 该模块使用其已存在的接口作为外部端口.

<a name="3-contracts"></a>

## 3. 合同

- 公共合同和DTO仍在 `OpenCRM.Services.Interfaces` (`Users`, `AccountProfiles`).
- 不创建单独的 `OpenCRM.Modules.Accounts.Contracts`:这些合同没有消费者。 `Services.Interfaces`,而现有的消费者已经依赖于该项目。
- 保持合同名称空间、DTO和JSON字段不变,因此消费者(WebApp,第1行) `Auth`,模块和测试)继续使用相同的接口.

<a name="4-module-dependency-boundaries"></a>

## 4. 模块依赖边界

允许的项目参考文献: `OpenCRM.Domain`, `OpenCRM.Infrastructure.Data`,以及 `OpenCRM.Services.Interfaces`; 使用时 `Helpers` 只有在确认有实际需要时。 仅包括实际使用的软件包; `UserManager<ApplicationUser>`/`IdentityResult` 可能使用身份包(版本家庭 10.0.7,如相邻项目)。  不要添加未使用的引用或软件包。

禁止:提及 `OpenCRM.Infrastructure.BL`,在另一个模块中的执行,或 `OpenCRM.WebApp`.  没有 `*.Contracts` 这项任务需要参考。

服务注册通过公开方式属于模块 `AddOpenCRMAccounts(this IServiceCollection)`.  删除以前的6个注册 `BusinessServiceCollectionExtensions`,保留其寿命(见上表)。 WebApp 电话 `AddOpenCRMAccounts` 与其它模块注册。

<a name="5-removing-the-notification-dependency"></a>

## 5. 消除对通知的依赖

`AccountRegistrationService` 直接调用静态 `OpenCRM.Infrastructure.BL.Notifications.SiteNotificationPublisher.AddRegistrationNotificationsAsync(context, user)`.  这种依赖不能移动为-:模块不能引用 BL,并且禁止将欢迎和邀请通知算法复制到账户中.

使用通知中包含的狭义合同:

- 扩展现有的 `ISiteNotificationPublisher` (`OpenCRM.Services.Interfaces.Notifications`),并采用方法为注册用户添加通知(`AddRegistrationNotificationsAsync(ApplicationUser user)`). 使用与静态方法相同的参数,因此其算法和数据不需要重写.
- 与通知所有者保持执行:现有的BL适配器 `SiteNotificationPublisherAdapter` 代表团致大会主席的信 `SiteNotificationPublisher` 算法。  不将算法复制到账户中 。
- 模块只调用接口,接收到相同的范围 `OpenCRMContext`  通过 DI.

保存写入命令(欢迎通知后再发出待请通知)、事件密钥、文本、标题/体裁、算法的内部 `SaveChangesAsync`,并参与当前的登记交易。  不引入新的交易、 单独的上下文、 推迟发送或承诺前发送 。

直接写给 `TelegramNotificationOutbox` 结束 `OpenCRM.Infrastructure.Data.Notifications.TelegramAdminNotificationPublisher` (共享的静态方法) `OpenCRMContext`) 仍为: `OpenCRM.Infrastructure.Data` 并且改变这个调用超出了范围,不会改变行为。

<a name="6-behavior-to-preserve"></a>

## 6. 保护行为

要求的行为, 对照源代码和测试进行检查 :

- **注册。** 将原子处决 `IApplicationUnitOfWork.ExecuteInTransactionAsync` 交易 : `UserManager.CreateAsync` (有或无密码)、个人数据和广告同意,并附有时间戳、归属、 `IAccountProfileService.SaveAsync` (模式和组织类型),欢迎和邀请通知,Telegram出栏行,促进码应用通过 `RedeemForRegistrationAsync`(a) 身份拒绝、回滚和无通知; `AccountCreationResult(User, Errors)` 带有已存在的错误文本。 **源的规格缩写为 `EmailConfirmed = request.Password =`;咨询俄罗斯消息来源,并落实这一条件。**
- **配置模式和组织类型。** `personal`/`organization`, `ServiceStation` 用于组织登记, `personal` 用于个人模式,选择可用的类型(要求)_当前_服务站_首次可用),推荐能力,以空模式清理配置,一天目录缓存和 `ClearCatalogCache`.
- **行政概况和能力。** 独一无二的代码, `InvalidOperationException` 带有已存在的信件, DTO 顺序和组成, 缓存在更改后重置 。
- **电子邮件编码登录.** 代码长度8, 限制5次尝试, 1小时过期, 缓存密钥 `auth:email-code:{EMAIL}`,一分钟反垃圾邮件窗口,SHA-256 of `{EMAIL}|{code}|OpenCRM.EmailLoginCode.v1`,恒定时间比较,删除电子邮件发送失败或已用尽尝试后的条目,现有错误文本和 `ErrorField`, `RequiresRegistrationConsent`,用于创建有效注册代码的账户,以及用于现有用户的电子邮件确认。
- **账户删除。** 遮盖邮件和用户Name `deleted_{yyyyMMddHHmmss}_{Id}@deleted.local`,保留 `OriginalEmail`设置 `IsDeleted`/`DeletedAt`中,删除外部登录,写出一个 Telegram 输出行,返回 `originalEmail`,参与一项交易。
- **用户记录.** `DateTime.Now.ToUniversalTime()`,访问和登录柜台,个人统计(个人领域和非被删除的记录),例外文本“{}}\\ \ \ id= ” (“没有找到使用 id=... 的用户”) ,并由先前被删除的原始电子邮件进行检索。

<a name="7-compatibility"></a>

## 7. 兼容性

- 不改变 HTTP 路线,参数, JSON, 状态代码, 消息, 或公共合同 。
- 保留 `UserManager`/`SignInManager`, cookie, CSRF, 视图, 回调 URL, 并在 WebApp 中重定向; 不重写 `AuthController` 以禁止控制器中的身份。
- 不修改数据库的设置、迁移或产品规则。

<a name="8-checks-and-acceptance-criteria"></a>

## 8. 检查和接受标准

1. 6项执行工作在 `OpenCRM.Modules.Accounts`;他们的前档案和登记从 `Infrastructure.BL`.
2. `AddOpenCRMAccounts` (b) 不存在账户-BL项目依赖; `OpenCRM.Services.Interfaces`.
3. 静态注册依赖 `Infrastructure.BL.Notifications.SiteNotificationPublisher`  通过狭义的合同和所有者适配器去除;范围相同 `OpenCRMContext` 并使用交易,没有单独的交易或预先承诺发送。
4. 路线、JSON、结果、错误信息以及公共合同得到保留;同意、归属、促进码、剖面模型、组织类型、能力、电子邮件代码过期/限制、删除账户掩码、 `OriginalEmail`,外部记录规则保持不变。
5. `OpenCRM.Modules.Accounts.Tests`  在溶液中创建 `Tests` 文件夹并添加到 `OpenCRM.sln` 调试/ 释放, 物理 `Unit`/`Integration` 文件夹和测试项目之间没有引用。 转移了适当的现有测试;共用固定装置仍在 `OpenCRM.Testing`.
6. 模块被添加到 `OpenCRM.sln`, `src/Dockerfile` (恢复/出版)模块覆盖CI矩阵,以及 `scripts/assert-backend-boundaries.ps1`; 调试/放行解决方案建立通关.
7. 整合检查使用真实身份/PostgreSQL/TestServer和替代的外部电子邮件发送:注册拒绝和回滚,回滚后没有通知,被删除的账户,无效/过期/再用代码,配置,权限,以及数据隔离.

<a name="9-known-limitations"></a>

## 9. 已知的限制

- 本地 Docker/ PostgreSQL 可能无法使用 。  在这种情况下,集装箱检查必须记录为没有运行,而不是报告为成功。
- Telegram 输出框行仍然是依赖共享的直接模块 `OpenCRM.Infrastructure.Data` (允许的模块-单体边界与共享) `OpenCRMContext` 和数据库。
- 真正的电子邮件和信件发送、工作和制作数据库、浏览器飞行员和服务器发布都超出了范围。

<a name="last-visit-information"></a>

## 最后访问信息

`identity.AspNetUsers` 存储上次访问的最新快照 : `LastAccessAt` (UTC),IP,用户代理,Accept-Langage,Sec-CH-UA,平台,移动客户端旗帜,以及认证计划. 缺失的 HTTP 信头被存储为 NULL; 先前的值没有被结转 。 客户端头有长度限制,不作为已验证的设备属性处理. 地理定位, cookie 内容,令牌,和 URL 参数不包含在本快照中.

 该快照在成功认证后被写出,在用保存的会话成功打开了MVC一页后被写出,在API-会话以刷新符号成功更新后被写出.  背景请求, 静态文件, 登录失败, 管理员冒充不更新 。 打开一页并刷新一个符号不会递增 `CountLogins` 或更改 `LastLoginDate`; `LastLoginMethod` 单独描述最后的显式认证。 返回焦点到一个没有导航的已打开的分页, 不算是另一次访问 。

IP 读取自 `Connection.RemoteIpAddress`  在已有的可信任代理处理后。 无法写入访问记录, 并且不会中断制作页面或返回已刷新的 API 令牌 。 现有账户不追溯回填.

移徙 `20261021161413_AddUserLastAccessInfo`  只添加已失效的列。 DbContext/迁移元数据在项目EF Core 10.0.7格式中为Designer;快照一致. 向下删除新访问和登录方法信息,同时保留用户和已存在的认证字段.  如果需要的话, 请先保存新信息 。  在单独的 PostgreSQL 实例上检查上下, 而不是在工作数据库上 。

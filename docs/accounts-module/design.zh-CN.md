<a name="accounts-module-technical-design"></a>

# 账户模块设计

[English](design.md) | [Русский](design.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

> 完成提取的历史文件 。 旧阶段、命令和中间状态不是当前的任务。 打开的工作列表于 [指数](../README.zh-CN.md);当前边界在当前的架构中被描述.

 当前模式:账户没有模式;每个人都有个人车辆,组织类型和权限属于选定的组织。 遗留账户配置及其字段被删除,而不是作为兼容性模型保留;见 [配置文件删除](profile-removal.zh-CN.md). 组织类型目录仍然活跃.  以下设计记录了历史提取,不得作为目前的schema/API合同.

提取决定 `OpenCRM.Modules.Accounts`. 其规格如下:`spec.md`:该模块拥有其实施权并登记自己的服务;合同仍然共享。  这里只有一个单独的建筑选择:取代第3节所述的对通知的静态登记依赖。

<a name="1-projects"></a>

## 1.  项目

|  项目 | 路径 | 解决方案文件夹 | 目的 |
| --- | --- | --- | --- |
| `OpenCRM.Modules.Accounts` | `src/OpenCRM.Modules.Accounts/OpenCRM.Modules.Accounts.csproj` | 后端 – 应用程序 | B. 账户概况和设想方案的执行情况 |
| `OpenCRM.Modules.Accounts.Tests` | `tests/OpenCRM.Modules.Accounts.Tests/OpenCRM.Modules.Accounts.Tests.csproj` | 测试 | 单位/整合模块检查 |

 无新内容 `*.Contracts` 创建项目 。 模块项目目标 `net10.0`,与 `ImplicitUsings` 和 `Nullable`时,如 `OpenCRM.Modules.Workspace`.

执行结构遵循了原始 BL 的域名文件夹 :

```
src/OpenCRM.Modules.Accounts/
  AccountProfiles/AccountProfileService.cs
  AccountProfiles/AccountProfileAdminService.cs
  Users/UsersService.cs
  Users/AccountRegistrationService.cs
  Users/AccountDeletionService.cs
  Users/EmailCodeLoginService.cs
  AccountsServiceCollectionExtensions.cs
```

命名空间是 `OpenCRM.Modules.Accounts.AccountProfiles` 和 `OpenCRM.Modules.Accounts.Users`,将他们的文件夹与其他模块相匹配。 公共执行类型仍然公开,如: `OpenCRM.Modules.Workspace`; 私人筑巢类型,例如: `EmailLoginCodeCacheEntry` 保持隐私。 方法机构和消息文本不需要更改。

<a name="2-dependencies-and-registration"></a>

## 2. 依赖和登记

模块的项目参考文献: `OpenCRM.Domain`, `OpenCRM.Infrastructure.Data`,以及 `OpenCRM.Services.Interfaces` (`Helpers` 只有在确认有需要时才能这样做)。 使用与已解析的图表相兼容的 10.0.x 家族版本, 明确根据实际移动的文件声明软件包 : `Microsoft.Extensions.Identity.Core` 10.0.7 (`UserManager<T>`, `IdentityResult`), `Microsoft.EntityFrameworkCore` (`Include`/`AsNoTracking`/`ToListAsync`), `Microsoft.Extensions.Caching.Memory` (`IMemoryCache`),以及 `Microsoft.Extensions.Logging.Abstractions` (`ILogger<T>`). 中转包引用不取代明示声明;不添加未用包.

参考文献 `OpenCRM.Infrastructure.BL`,另一个模块的执行,以及 `WebApp` 禁止进入。

`AccountsServiceCollectionExtensions.AddOpenCRMAccounts` 总共登记了6个服务机构及其以往的寿命:

```
AddTransient<IUsersService, UsersService>();
AddScoped<IAccountRegistrationService, AccountRegistrationService>();
AddScoped<IAccountDeletionService, AccountDeletionService>();
AddScoped<IEmailCodeLoginService, EmailCodeLoginService>();
AddScoped<IAccountProfileService, AccountProfileService>();
AddScoped<IAccountProfileAdminService, AccountProfileAdminService>();
```

将这6个注册从 `BusinessServiceCollectionExtensions.AddOpenCRMBusinessServices` (`Infrastructure.BL`;不影响其他BL注册。 `IApplicationUnitOfWork`, `IPromoCodeService`, `IEmailNotificationService`, `ISiteNotificationPublisher`, `IMemoryCache`,以及 `ILogger<T>` 输入模块,作为现有组件拥有的外部依赖。

WebApp 电话 `AddOpenCRMAccounts()` 与其他模块一起登记在 `Extensions/ServiceCollectionExtensions.AddOpenCRMInfrastructure`.

<a name="3-registration-notifications-narrow-contract-instead-of-static-call"></a>

## 3. 登记通知:狭义合同,而不是静态电话

在此之前: `AccountRegistrationService` 电话 `OpenCRM.Infrastructure.BL.Notifications.SiteNotificationPublisher.AddRegistrationNotificationsAsync(context, user)`  在登记交易中。

之后:

- `OpenCRM.Services.Interfaces.Notifications.ISiteNotificationPublisher` 收益 `Task AddRegistrationNotificationsAsync(ApplicationUser user)`. `ApplicationUser` 合同项目已提及 `OpenCRM.Domain`. 实体参数是刻意的:它与当前静态调用参数相匹配,因此所有者的算法没有被重写和值(.`Id`, `NormalizedEmail`)不再被获取。
- 通知所有人保留执行: `SiteNotificationPublisherAdapter` (BL)通过授权给现有的 `SiteNotificationPublisher.AddRegistrationNotificationsAsync(context, user)`.  欢迎和待邀请通知算法,事件密钥 `welcome:{userId}` 和 `invitation:{id}:{token}`,文本、字段校正和内部 `SaveChangesAsync` 保持原样。
- 模块收到 `ISiteNotificationPublisher`  通过它的构造器,并在与上一个算法相同的步骤引用它: profile 后 `SaveAsync`  在应用促销代码之前

交易边界:适配器收到相同的范围d `OpenCRMContext` (每个请求一个DI容器),在已打开的容器内工作 `IApplicationUnitOfWork` ,而不打开自己的交易。 执行前没有发送信件或电子邮件;没有隐藏 `SaveChanges` 在现有电话之外还增加了电话。 直接写到 `TelegramNotificationOutbox` 结束 `OpenCRM.Infrastructure.Data.Notifications.TelegramAdminNotificationPublisher` 保留;这是允许对共享基础设施的依赖,不会改变行为。

 在界面上添加一个方法也需要更新现有的 `DatabaseSiteNotificationPublisher` 插接点 `tests/OpenCRM.Modules.Organizations.Tests/Integration/OrganizationsDatabaseFixture.cs`;否则各组织的测试项目将不再编译。

<a name="4-integration-points"></a>

## 4. 融合点

- `OpenCRM.sln`: 在后端 → 应用程序下添加新模块,在测试下添加测试项目,所有12个调试/放出 × Any CPU/x64/x86 配置如: `OpenCRM.Modules.Workspace`.
- `src/Dockerfile`: 添加一个 `COPY` 用于恢复前的新模块 csproj; 发布后通过 WebApp 获取其组装 `ProjectReference`.
- `src/OpenCRM.WebApp/OpenCRM.WebApp.csproj`: 添加项目引用 `OpenCRM.Modules.Accounts` (没有它, `AddOpenCRMAccounts` 和其他模块一样。
- `tests/Accounts.runsettings`: 包括 `[OpenCRM.Modules.Accounts]*` 软件包和并行 `Workspace.runsettings`.
- `scripts/assert-backend-boundaries.ps1`:现有的检查已经涵盖“无周期”、“模块未提及BL/其他执行”和“测试设在测试楼和两个楼内”的新项目;进行修改,要求其传递新的图表。

中间构建状态:模块执行被移动,但依赖性检查 `OpenCRM.Infrastructure.Tests` 仍然参考了旧的 BL 命名空间, 完整的溶液构建为红色 。 预计将在撤离时进行,而不是缺陷。  在文件移动时构建受影响的项目; `dotnet build OpenCRM.sln` 测试转移后必须经过。

<a name="5-module-tests"></a>

## 5. 模块测试

`OpenCRM.Modules.Accounts.Tests`目标 `net10.0`, `IsPackable=false`, 软件包匹配 `OpenCRM.Modules.Workspace.Tests` (Testcontainers.PostgreSql,微软.AspNetCore.Testhost,Moq,微软.NET.Test.Sdk,被封面.collector,xunit,xunit.runner.visitastudio;参考文献有: 互联网档案馆的存檔,存档日期2013-12-21.). `OpenCRM.Modules.Accounts`, `OpenCRM.WebApp`, `OpenCRM.Testing`, `OpenCRM.Infrastructure.Migrations`,以及 `tests/xunit.runner.json`.  未提及其他测试项目。 物质 `Unit` 和 `Integration` 文件夹使用匹配的名称空间。

移动模块执行的检查 :

- `tests/OpenCRM.Modules.Accounts.Tests/Integration/AccountProfileRemovalTests.cs` → `Integration` (使用真实的) `AccountProfileService`测试服务员和PostgreSQL);
- `tests/OpenCRM.Modules.Accounts.Tests/Integration/RegistrationErrorTests.cs` → `Integration` (使用真实的) `AccountRegistrationService` 登记表。

保持与模块执行无关的检查: `RegistrationTransactionTests`, `RegistrationAttributionTests` (网易新闻/网易).`RegistrationAttribution`), `AccountProfileModeTests` (域名), `AuthMvcAcceptanceTests`, `AdminAuthenticationApiTests`,并在其他模块中进行测试 `Services.Interfaces` 接口。 合同不动,因此其分包登记保留。

使用覆盖物寻找未测试区域;不需要百分比。 有意义的设想(最低):

- 注册:创建有或没有密码的用户、同意书及其时间戳、配置模式和组织类型、宣传码、归属、欢迎通知和待请通知、电报出行; 身份拒绝、交易回滚和回滚后无通知;
- profile:读取并保存模式,组织类型倒置,推荐并启用能力,以空模式重置,目录缓存和缓存重置;
- 管理配置和能力:创建/更新/删除、独特的代码、现有文本出错、结果顺序;
- 电子邮件编码登录:无效、过期和重复使用代码、尝试限制、1分钟反垃圾邮件延迟、电子邮件发送失败、同意注册、账户创建、电子邮件确认、删除账户(通过电子邮件和 `OriginalEmail`);
- 删除: 遮盖邮件和用户Name, `OriginalEmail`, `IsDeleted`/`DeletedAt` 删除外部登录、 返回值、 Telegram 输出框行;
- 用户记录和统计:登录/访问、柜台、个人区域、未删除的记录、查找被删除的原始电子邮件、排除身份不明的用户;
- HTTP/DI:通过 TestServer 的账户配置路由, `AddOpenCRMAccounts` 登记(范围广而短暂、独特),没有项目一级对BL的依赖。

集成检查使用真实身份,PostgreSQL(Testcontainers)和TestServer,取而代之的只是外部电子邮件发送.  它们不连接工作或生产数据库。

<a name="6-checks-and-reproduction"></a>

## 6. 检查和复制

```powershell
dotnet build OpenCRM.sln -m:1 -p:BuildScenarioClientsOnBuild=false
dotnet test tests/OpenCRM.Modules.Accounts.Tests/OpenCRM.Modules.Accounts.Tests.csproj -m:1 -p:BuildScenarioClientsOnBuild=false -p:BuildScenarioClientsOnPublish=false --settings tests/Accounts.runsettings --collect "XPlat Code Coverage" --logger trx
./scripts/assert-module-coverage.ps1 -ModuleName Accounts -ReportPath <coverage.cobertura.xml>
./scripts/assert-backend-boundaries.ps1
dotnet sln OpenCRM.sln list
```

执行期间运行目标套件,并在最后修改后运行完整的后端回归. 将报告和TRX文件放入一个孤立的目录(即 `--artifacts-path` 参数)和禁用客户端构建。 控制器检查在没有 shell 的情况下使用 argv 命令,因此在时限内使用溶液构建和定向模块套件.

<a name="7-risks-and-decision-boundaries"></a>

## 7. 风险和决定界限

提取不会改变数据库计划、迁移、HTTP合同、授权规则或交易边界。 唯一的新连接是呼叫 `ISiteNotificationPublisher` 而不是所有者的静态方法; 相同的范围上下文和交易保留了注册的原子性.  如果执行显示保留行为需要改变公共合同,则数据库计划、访问规则或单独开立交易,停止并提出单独决定:该假设情况不在本阶段。

<a name="final-implementation-clarifications"></a>

## 最后执行情况说明

 不添加未使用的内容 `InternalsVisibleTo`:测试使用公共合同,模块没有内部类型. 电子邮件编码登录接受可选选项 `TimeProvider` 与 `System`  因此,可以确定性地测试过期和重发时间。 融合使用当地 `AccountsDatabaseFixture` 仅使用 PostgreSQL 并进行真实的迁移; 这些检查不需要 RabbitMQ 和 EF InMemory 。 详细结果载于 [指南](../README.zh-CN.md).

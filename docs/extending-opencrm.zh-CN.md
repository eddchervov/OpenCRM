<a name="build-your-product-on-opencrm"></a>

# 定制、扩展及同步上游更新

[English](extending-opencrm.md) | [Русский](extending-opencrm.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

OpenCRM是一个共享的CRM基础.  从可用的设置开始,然后添加出您产品的差异,同时将共享修正保留在核心中.

<a name="choose-the-level-of-change"></a>

## 选择变化的级别

| 所需经费 | 何处去换? |
| --- | --- |
| 名称、安装 URL、 连接、 提供者 | [配置](configuration.zh-CN.md), [品牌开发](../configuration/README.zh-CN.md) |
| 通知、付款和背景业务 | [单独的微服务](microservices.zh-CN.md) |
|  你个人的帮助文章 | 执行 `IOrganizationWikiContentProvider` 并在主机中注册 |
|  自定义屏幕或情景 | 客户端应用程序和共享 `packages/ui`, `packages/workspace` |
| 共享业务能力或固定 | 相关 `OpenCRM.Modules.*` 模块及其测试 |
|  一个全新的模块 |  项目、合同、明确的主机登记、客户路线和测试 |

<a name="what-is-already-supported"></a>

## 已经得到支持的内容

安装设置与应用程序代码是分开的. 业务单元、合同、基础设施的实施以及共享的UI组件分别属于不同的项目和一揽子项目。 标准依赖性注入允许您在构建主机时替换支持的执行 。

 可重复使用 [OpenCRM. hosting (中文(简体) ).](../src/OpenCRM.Hosting/OpenCRM.Hosting.csproj) Razor 库从现有的 WebApp 源树编辑共享控制器,服务和视图. 中立者 [方案.cs](../src/OpenCRM.WebApp/Program.cs) 代表出席会议的代表 `OpenCRMApplication.ConfigureBuilder(builder, args)` 和 `ConfigureApplication(app)`.  一个产品主机可以调用相同的成分,并在之前注册自己的服务. `builder.Build()`.  未提供自动插件发现和单独发布的 NuGet 扩展 SDK 。

<a name="a-product-host-and-one-source-tree"></a>

## 一个产品主机和一个源树

产品应用程序参考 [共享主机库](../src/OpenCRM.Hosting/OpenCRM.Hosting.csproj),而不是中性可执行文件。 密码 [产品主机 MSBuild 导入](../build/OpenCRM.ProductHost.targets) 接受一个 `OpenCRMRoot`,添加共享项目和静态资源,并从源树上构建所有客户端管理。 消费者自己供养 `brand.json`, `appsettings.json`,和切入点。  可选 `configureProductMiddleware` 调用 `OpenCRMApplication.ConfigureApplication` 运行在转发头和错误处理之后,然后是 HTTPS 重定向、安全头、路由和静态文件。 用于产品特异性的克恩管重定向;省去它保持了中性管. 客户端文件在.NET SDK发现静态资产前被重建. `dotnet publish --no-build` 使用先前已建资产,不重建前端;在前端修改后先构建产品.

导入复制本核心默认为 `opencrm.defaults.json`.  在产品设置前装入该文件,然后重新应用环境变量和命令行参数.  在共同构建器配置后注册产品服务;对已建成的应用程序应用共同管线. 中性应用仍然可以独立运行.

 为了地方发展, `OpenCRMRoot` 可能选择邻居的离职。  对于可再生的CI和释放,在产品储存库内使用被绑定的源修订. 后端和前端必须使用同根;不维护共享的CRM代码的可编辑第二个副本.

`OPENCRM_FRONTEND_OUTPUT_ROOT` 绝对是通往消费者之路 `wwwroot`. 进口品传递给维特,保护公众 `/clients/*`, `/admin-panel`,以及 `/app` URLs(英语:URL).  没有它, `npm run build` 保留中性应用程序的现有输出路径。  每个主机都需要自己的实体客户端文件,因为Razor从 `WebRootPath`.

替换中性自控器的产品必须明确排除该控制器出自MVC的发现并加入自己的应用部分. 仅一个同名的新控制器就会产生模糊的路由. 测试汇编了Razor视图,CSS/JS,以及两个来自已公布产出的管理切入点.

 在本地主机上运行两个主机时,使用不同的 cookie 名称和密钥目录;单靠不同的端口并不能孤立 cookie. `DataProtection:ApplicationName`  可配置和默认值 `OpenCRM.WebApp`.  在升级时保留现有安装的区分器和密钥环. 将构建输出分开, 并避免同时写入共享的中间文件 。

<a name="1-start-with-configuration"></a>

## 1.  从配置开始

 在不改变共享行为的情况下配置自己的值 :

- [`configuration/brand.json`](../configuration/brand.json) 定义品牌和公共URL。
- 通过环境变量或秘密商店向相关过程提供连接字符串和秘密.
- 根据需要启用电子邮件、 AI 和支付基础设施 。
- `PlatformPolicy__Commercial__Enabled` 控制可选的商业模式。
- `UserTracking:Enabled` 实值 `false` 默认; 仅在产品需要时允许额外访问计数器和客户端访问快照。  见 [配置](configuration.zh-CN.md).
- `AdminDesktop`  可以移动发射装置的Chrome profile和桌面管理员应用程序的本地状态文件;所有四个可选路径必须是绝对的.  见 [配置](configuration.zh-CN.md#desktop-administration-paths).
- `Export1C:ProcessingDownloadUrl`  在导出能力响应中可选地发布本地 1C 处理文件 URL ; 忽略非本地 URL 。

服务器支持 `Brand__DisplayName` 和 `Brand__PublicBaseUrl`;桌面应用程序使用品牌文件. 保持分布中所有进程设置的一致性。 改变品牌并不能去除所需的OpenCRM核心归属.

 见 [配置](configuration.zh-CN.md) 用于细节和配置源优先。

<a name="2-separate-your-version-from-upstream"></a>

## 2. 将您的版本从上游分离

 为单独的产品创建自己的叉/存储器.  在它的克隆中, `origin`  应该指向您的产品和 `upstream` 转到 OpenCRM 。

```bash
git remote add upstream https://github.com/eddchervov/OpenCRM.git
git remote -v
```

 若为: `upstream` 已配置,不再添加。  这些命令适用于您的产品克隆.

保持小的和分开的更改 :

- 部署的配置和秘密属于共享储存库之外。
- 将产品特定类别纳入自己的文件/名称空间,并酌情纳入项目。
- 将您的屏幕构建为产品组件重用共享的 UI 包 。
- 在相关的OpenCRM模块中进行共享修正,避免产品中同一逻辑的第二个手动修正.

 从上游更新是目前接收共享逻辑的实用机制.  不提供单独包装的现成NuGet核心SDK。

<a name="3-use-existing-extension-points"></a>

## 3. 使用现有的扩展点

<a name="help-content"></a>

### 帮助内容

密码 [`IOrganizationWikiContentProvider`](../src/OpenCRM.WebApp/Models/Organizations/IOrganizationWikiContentProvider.cs) 合同描述了内容的提供、搜索和文章检索。 基础分发会登记一个空的提供者; 当没有内容时, 帮助项会被隐藏 。

合同和条款模型保留 `OpenCRM.WebApp.Models.Organizations` 命名空间并编译为 `OpenCRM.Hosting`.  在产品主机或产品库中执行供应商,参考主机. 共享库不得将产品回溯;这样可以将依赖方向保持为一种方式.

已执行 `ProductWikiContentProvider`,将登记改为: `OpenCRMApplication.ConfigureBuilder(builder, args)` 和之前 `builder.Build()`  在产品输入点中:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenCRM.WebApp.Models.Organizations;

builder.Services.Replace(ServiceDescriptor.Singleton<
    IOrganizationWikiContentProvider,
    ProductWikiContentProvider>());
```

 这说明连接了现有主机中的一个类.  您必须执行类本身; 不注册就将 DLL 设置在应用程序目录中不会连接到它 。 考虑单通寿命和线条安全性.

<a name="interface"></a>

### 接口

- [`packages/ui`](../src/OpenCRM.Frontend/packages/ui) 包含共享的 UI 元素。
- [`packages/workspace`](../src/OpenCRM.Frontend/packages/workspace) 包含工作空间和客户关系管理组件。
- [`Scenarios`](../src/OpenCRM.Frontend/Scenarios) 包含客户端应用程序。
- [`Admin`](../src/OpenCRM.Frontend/Admin) 包含管理应用程序。

重新使用已有的组件,主题样式,和权限处理. 使用共享 `UiDatePicker` 和 `UiTimePicker` 日期和时间。  在改变客户端组件后,构建工作空间并检查相关情景,收缩屏幕,以及两个主题.

<a name="product-links-in-both-admin-panels"></a>

### 两个管理面板中的产品链接

将产品自主导航链接添加到根中 `Administration.Sections` 阵列在 `brand.json`. 网络管理员面板和桌面管理员应用读取了相同的条目;不需要复制共享屏幕.  每个条目都有这个形状:

```json
{
  "Administration": {
    "Sections": [
      {
        "Title": "Product analytics",
        "Url": "/admin/product-analytics",
        "Permission": "Admin.Dashboard.View"
      }
    ]
  }
}
```

`Url` 必须是本地的绝对路径,从一个开始 `/`. 协议相对的URL,回击,控制字符,查询字符串,和碎片被否决. 网络面板前缀当前 `PathBase`;桌面应用程序在系统浏览器中打开路由,同时保留服务器的基础路径. 桌面应用程序不会将其会话令牌传递给浏览器 。 浏览器通过正常的网络流量来签名.

`Permission` 是一个已有的管理员权限代码。  这两个界面都隐藏了当前配置文件无法看到的条目,但这仅仅是导航过滤:在目的页面及其API上执行授权.  不要将已配置的链接或隐藏的菜单项作为访问控制边界。

<a name="product-registration-consent-view"></a>

### 产品注册同意的意见

共享注册和电子邮件编码登录视图 `Views/Shared/_RegistrationConsentFields.cshtml` 带一个 `bool` 模式,表示复选框是否先前被选中。 使用特定产品同意措辞或标注,提供产品观点。 `Views/Shared/_RegistrationConsentFields.cshtml` 路径。 留着 `PersonalDataProcessingConsent` 字段名称和相容布尔绑定,所以共享的 POST 验证继续工作.  这是 Razor 视图覆盖; 它不需要更改共享控制器或核心视图 。

<a name="commercial-mode"></a>

### 商业模式

使用支持的配置启用标准模式,然后配置自己的计划、价格和集成。 商业逻辑使用的一部分 [`IPlatformCommercialPolicy`](../src/OpenCRM.Modules.Billing.Contracts/IPlatformCommercialPolicy.cs).  其他组件,包括HTTP过滤器、付款登记和背景工作,也读作 `PlatformPolicy:Commercial:Enabled` 直接来.  因此,仅取代接口并不能将整个安装切换:使用标准设置并验证两种模式的一致性.

不论商业环境如何,都要保留会员资格,角色,许可检查和技术限制.

<a name="providers-and-your-own-business-logic"></a>

### 供应商和你自己的业务逻辑

 首先检查已有的集成是否支持您需要的协议和设置 。 一项新的协议要求执行适配器和明确的DI登记。 基础设施登记居住在 `src/OpenCRM.Infrastructure.*` 和 `*ServiceCollectionExtensions.cs` 文档。

 在增加一个单元时,界定其责任、公共合同、登记和测试。  通过相邻模块的合同进行互动.  如果需要新的表格,则设计数据所有权和迁移;不自动发现任意实体。

<a name="4-receive-core-updates"></a>

## 4. 接收核心更新

 在您的产品克隆中, 保存您的工作更改并创建一个单独的更新分支 :

```bash
git fetch upstream
git switch -c update/opencrm-core
git merge upstream/main
```

合并后 :

1. 解决冲突,同时保留产品设置和核心检查。
2. 恢复依附关系、构建和运行 [测试](../CONTRIBUTING.zh-CN.md#validation).
3. 在一个单独的数据库中验证迁移,并检查您的安装的主要情景 。
4.  只有这样,更新内容才会包含在您产品的发布中.

冲突是可能的,特别是在相同的共享文件已经改变的情况下. 单独组件中的小产品变化简化了更新.  在 OpenCRM 中修复一个共享的bug, 然后通过上游将固定带入您的产品 。

<a name="attribution"></a>

## 归属

 您可以更改产品名称、外观和功能。 保留 [许可证条款](../LICENSE) 和可见的“基于 [OpenCRM](https://github.com/eddchervov/OpenCRM)网站/网络界面的归属,包括商业和闭源定制。

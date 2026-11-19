<a name="opencrm-admin--avalonia"></a>

# OpenCRM 行政- Avalonia

[English](README.md) | [Русский](README.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

人工智能助理行政管理科控制未知费用的可用性、渠道、模式、预算、组织访问、审计、支出和调节。 配置和支出权限分别检查;设置版本冲突需要管理员再次审查.  见 [辅助文档](../../../../docs/ai-assistant.zh-CN.md).

组织记录支持对未删除的组织进行强制存档/恢复 `Admin.Users.Manage`,一个原因, 和明确的确认。 现有的30天档案清理期适用;在答复失败后,新的行动被阻止,直到状态和解。  见 [服务器合同和限制](../../../../docs/admin-organizations.zh-CN.md). 这些行动需要独立于桌面部署的服务器 API 更新.

Windows和macOS的独立.NET 10/Avalonia桌面客户端:电子邮件/密码登录,只读管理员配置,以及仪表板.  它使用一个带有白文本的黑色主题而不管OS主题.

<a name="start"></a>

## 开始

 从寄存器根 :

```powershell
dotnet run --project src/OpenCRM.Frontend/Admin/OpenCRM.Admin.App
```

API 基础 URL 从共享读取 `configuration/brand.json` 复制到应用程序旁边。 `OPENCRM_ADMIN_API_URL` 超过它。  除了本地开发期间的回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回回

```powershell
$env:OPENCRM_ADMIN_API_URL = 'https://localhost:7142/'
dotnet run --project src/OpenCRM.Frontend/Admin/OpenCRM.Admin.App
```

HTTP只被允许用于回路地址. HTTPS 证书验证从未被禁用 。 客户端使用HTTP/1.1. 服务器 API 设置,JWT 签名密钥,数据库访问权限不传递给桌面应用程序.

客户端使用服务器认证和管理API. 服务器仍然负责认证、角色、权限和数据访问。

<a name="sign-in-and-permissions"></a>

## 签名和权限

- `POST /api/v1/auth/login`  在验证电子邮件/密码字后发出一对符号 。
- `GET /api/v1/admin/current-user` 检查 `Admin.Access` 或当前 `System`  在服务器上的角色 。  在成功时,如果允许,它打开了仪表板,否则就打开了配置.
- 服务器在每次请求时对照数据库检查行政角色/许可;删除一个角色或删除一个用户用旧的JWT来阻断访问.
- 客户端每分钟重新装入配置文件,并通过配置文件的“QQ”(新鲜版)重新装入。 Profile 401/403关闭会话;仪表板 403关闭页面并刷新权限.
- 在 Windows 上,刷新令牌与当前用户 DPAPI 一起保护 `%LOCALAPPDATA%/OpenCRM/Admin/Sessions`,针对每个API URL分别设置. 密码和访问令牌没有保存 。 启动时,客户端会刷新符并检查服务器配置/许可. 将可移植应用程序移动到另一台计算机或作为另一台Windows用户运行需要再次签名. 尚未支持关于macOS的会话持久性 。
- 过期访问令牌刷新通过 `POST /api/v1/auth/refresh`.  在401之后,读取的剖面图/报告被重审一次。
- 关闭窗口通常会保留登录。 “{} (签名出) 删除已保存的会话并调用 `POST /api/v1/auth/revoke`. 过期的访问令牌在撤销前被刷新 。  没有连通性,服务器被撤销并不保证;本地会话被删除. 刷新- token 过期或授权拒绝删除已保存的登录; 临时的网络故障保留它 。
- Windows允许每个用户有一个桌面实例,所以并行窗口不会共享一个旋转的刷新令牌.

菜单只包含经常账户允许的章节. 标有“ ⁇ ”(后)的项目是占位符,不打开工作屏幕。 配置文件没有用于更改电子邮件、角色或权限的表格 。

<a name="menu-counters"></a>

## 菜单计数器

用户、请求和电子邮件历史徽章显示自上次成功浏览相应区域以来的新记录。  首先使用从当前本地日开始计数记录;之后的未读记录持续到午夜并重新启动应用程序. 读取标记存储于 `admin.NavigationReadStates`  按账户/款次分列。

付款徽章显示支付款项的全时排行,未完成处理或没有上传收据. 处理完成于 `completed`; 当文件或 URL 保存时, 收据存在 。  尽管任一条件都未得到满足,但付款仍然排队。  未支付,取消的,全额退款不包括在内. 查看该部分既不改变队列,也不为它保存已读标记 。

客户端在装入用户、请求或电子邮件历史时,通过 `GET /api/v1/admin/navigation/counts?unread=true`. 只有在成功装入了当前屏幕后,它才会通过 `POST /api/v1/admin/navigation/sections/{section}/viewed`. 负载出错或取消不会清除徽章;边界后的事件仍然是新的.  同时确认只能推进标记. 柜台每分每分每秒刷新,在确认后立即刷新. 编目建议队列显示所有待决的温和请求,无论是否取景.

`/navigation/counts` 不含 `unread=true` 保留老年客户的每日统计数据。  未读事件需要更新的服务器和 `AddAdminNavigationReadStates` 迁移; 没有服务器 `AsOfUtc` 无法从新客户端收到任何已查看的确认。

<a name="users"></a>

## 用户

要求数 `Admin.Users.View`  或者说 `System`.  该表填充了剩余的工作空间,有固定的页眉/插页;默认的页码为50个,可用25/50/100. 电子邮件/手机搜索适用于“QQ” (查找) 或 Enter; 页面大小和删除的用户开关立即适用。 “QQ” (重置) 清除搜索并恢复初始排序 。  没有单独的UTM过滤器.

服务器的排序由用户,车辆,维护,组织,上一个登录,注册,和UTM源头所触发;又一点击反向方向.  一个页眉箭头显示当前顺序 。 车辆、保养和组织清点在用户之后立即分别出现,然后是最后一次登录和登记。 UTM源是先入为主的归属,而不是一种认证方法. 计划属于各组织;用户行没有个人计划栏. 点击组织计数会打开记录中的分页,其中每个分页都有自己的角色,地位和计划. 列表不载入每行概览 。

 通过行按钮打开一个记录,双击,或者在选定的用户上输入。  回放保存了页面,选择和搜索. 被选中/被覆盖的行使用与对比文本相通的暗色背景.

记录包括概览、组织、订阅、付款、车辆/维修、支助请求、活动历史和个人特征。 其标题显示用户、状态、登录/登记日期以及登录/组织计数。 Tabs使用高相接按钮;概览在宽屏幕上有两列,其他记录使用列表,字段使用紧凑的列或名称/值行.  各组织的分页只显示目前的会员/所有权,没有单独的邀请历史卡。 本地状态选择在默认情况下是活动的,存档的,或删除的,并保存在刷新中. 组织条目显示联合日期;邀请日期/作者仅在被接受的邀请与组织、用户电子邮件和联合日期相符时才会出现。  如果 API 没有提供匹配邀请, 其他记录不会被替换 。  当商业模块被启用时,订阅标签只显示组织订阅有活动(默认),完成,或全部被本地选中;选择存续了刷新. 旧有的个人计划/历史被隐藏. 个人车辆和保养不包括有下列设备的车辆: `OrganizationId` 并仅显示已知个人车辆的维护。 API将总体选择上限为30辆车和20个维护记录;界面明确标记达到极限,未知所有权不表现为个人. 时间轴倒塌了 字段可以选择/复制;时间戳使用当地时区. 财务标签、付款、订阅和计划只有在商业模式启用且用户已使用时才提供 `Admin.Billing.View`. 记录数据只在打开或刷新时才加载,而不是计时器. 出站/接入撤销清除数据;导航取消请求. 刷新保存所选标签 。

现有用途 `GET /api/v1/admin/users` 和 `/users/{userId}/overview`. 服务器将一些收藏限制在最近的记录;在达到限制时,标签会明确披露.  本节只读;对编辑用户或冒充用户没有控制.

API 限制 : `overview` 需求 `Admin.Users.View` 并且可能包括财务领域,不论 `Admin.Billing.View`. 桌面在无法访问时会隐藏商业数据,但UI不会取代服务器侧授权.

<a name="dashboard"></a>

## 盘子

需要自动调试板 `Admin.Dashboard.View` 或 `System` 角色。 。 。 仪表板显示选定期间(今天、7天或月)的客户关系管理数据:付款、新的登记和组织以及维护活动。 财务卡、付款提示和付款总额只显示给有下列功能的管理员: `Admin.Billing.View` 或 `System` 角色。 。 。 OpenCRM仪表板上不显示公众访问、搜索提供者报告和广告源分析。

 该页面在所选间隔(5,10,30,或60秒;默认为60秒)时自动刷新,或手动刷新. 间隔保存于 `%LOCALAPPDATA%/OpenCRM/Admin/dashboard-refresh.txt`. 单独的“损失统计”行动为客户关系管理总体统计提供了载荷。 用户请求日志由 `UserActivity:Enabled` ,并在标准配置中被禁用。  当仪表板不活动或应用程序窗口被最小化时请求停止;导航和签出取消待处理的请求.

桌面客户端读取 `GET /api/v1/admin/dashboard` 并且,在允许商业计费时,计费汇总终点。  它会显示一个在无法提供所需API合同的情况下需要更新的信息;它不会以全时数据来代替特定时期的结果.

<a name="structure"></a>

## 结构

- `OpenCRM.Admin.App`: XAML, ViewModel, shell, settings, 和窗口生命周期.
- `OpenCRM.Admin.Api`: HTTP,会话,以及没有域,EF,或服务器服务依赖关系的小型客户端 DTO.
- `OpenCRM.Admin.App.Tests`:通过Avalonia Headless进行查看模型和实际XAML屏幕测试.
- `OpenCRM.Admin.Api.Tests`:会话,令牌刷新,拒绝,以及HTTP合同测试.

 新项目设在 `Frontend/Admin` 解析文件夹。 旧克洛姆发射器 `OpenCRM.Admin.Launcher` 继续分开。

<a name="validation"></a>

## 验证

```powershell
dotnet test tests/OpenCRM.Admin.Api.Tests
dotnet test tests/OpenCRM.Admin.App.Tests
dotnet test tests/OpenCRM.Infrastructure.Tests -p:BuildScenarioClientsOnBuild=false --filter 'FullyQualifiedName~AdminAuthenticationApiTests|FullyQualifiedName~AdminAuthorizationHttpTests'
```

上一个命令通过现有的固定使用多克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克克  它测试真正的登录/标注控制器,身份,JWT,普通用户拒绝,行政政策,信使刷新和撤销.

 要用测试数据捕捉实际的 XAML 屏幕 :

```powershell
$env:OPENCRM_ADMIN_SCREENSHOTS = "$PWD/output/admin-desktop"
dotnet test tests/OpenCRM.Admin.App.Tests
```

 这些图像显示一个测试剖面,而不是一个生产-服务器会话.

<a name="payments-and-subscriptions"></a>

## 付款和订阅

 本节仅在下列时间提供: `PlatformPolicy:Commercial:Enabled=true`  而管理员有 `Admin.Billing.View`. 商业模块默认被禁用;CRM的使用不需要订阅. 付款和组织订阅标签使用服务器页码,其中包含25/50/100条目、搜索、状态和标题排序。  今天,即7/30天的付款支持,或全时过滤。 汇总表的300个付款上限不适用于这些名单。 未来订阅有单独的非开始状态.

打开,双击,或 Enter 显示细节/更改历史 。 返回保存页面; 退出/访问取消清除数据 。 列表需要新的服务器端点 `GET /api/v1/admin/billing/payments` 和 `GET /api/v1/admin/billing/subscriptions/organizations`. 使用更古老的服务器,客户端会显示一个更新所需的消息.

<a name="os-builds"></a>

## 操作系统构建

```powershell
dotnet publish src/OpenCRM.Frontend/Admin/OpenCRM.Admin.App -c Release -r win-x64 --self-contained true -o output/AdminApp/win-x64
dotnet publish src/OpenCRM.Frontend/Admin/OpenCRM.Admin.App -c Release -r osx-arm64 --self-contained true -o output/AdminApp/osx-arm64
```

使用 `osx-x64`  为英特尔麦克斯。 macOS的构建不会取代MacBook上的测试. 命令发布自成一体的二进制. 签名、安装器和自动更新不包括在内。

<a name="build-for-a-product-installation"></a>

## 建造产品安装装置

同样的桌面来源可以在不改变核心配置的情况下使用消费者的品牌文件. 绝对通过 `ProjectBrandFile` 用于构建/打印的路径; 将其复制到输出为 `brand.json`.  没有财产,共享的 `configuration/brand.json` 已使用。 相应的公共基础URL决定了API地址; `OPENCRM_ADMIN_API_URL` 可以在运行时覆盖地址 。

```powershell
dotnet publish --configuration Release -p:ProjectBrandFile="C:/work/Product/brand.json"
```

 从此工程的目录中运行并替换示例路径 。 使由此产生的配置与消费者的网络主机保持一致.  此更改配置包装; 它不会重复行政用户界面或禁用服务器侧访问检查 。

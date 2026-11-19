<a name="crm-administration-dashboard"></a>

# 管理仪表板

[English](admin-dashboard-analytics.md) | [Русский](admin-dashboard-analytics.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

OpenCRM管理仪表板报告客户关系管理的活动:登记、组织、维护记录和(在允许商业模式时)付款。  它不包括公共网站、广告归属、搜索引擎服务或第三方跟踪供应商的分析。 安装可以在OpenCRM核心外再添加自己的站点分析.

需要自动调试板 `Admin.Dashboard.View` 或 `System` 角色。 。 。 桌面仪表板支持"今日",7天,和"月"时期,自动或人工刷新,以及单独加载的整体统计视图. 付款数据只提供给有下列功能的管理员: `Admin.Billing.View` 或 `System` 角色。 。 。 网络仪表板采用相同的服务器侧权限检查.

服务器 API 是 `GET /api/v1/admin/dashboard`; 执行中 `src/OpenCRM.Infrastructure.Admin/AdminDashboardService.cs` 和 `src/OpenCRM.WebApp/Controllers/Admin/Api/DashboardApiController.cs`. 与两个行政客户协调DTO的响应变化.

<a name="opencrm-overview"></a>

# 技术概览

[English](project-overview.md) | [Русский](project-overview.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

OpenCRM管理客户和各组织的相关业务工作流程.

<a name="solution-contents"></a>

## 解决方案内容

- `src/OpenCRM.WebApp` ——服务器网页主机,页面,以及API.
- `src/OpenCRM.Frontend` ——客户界面和共享组件.
- `src/OpenCRM.Modules.*` ——业务逻辑.
- `src/OpenCRM.Infrastructure.*` ——存储和外相组件集成.
- `services/notifications` 和 `services/payments` 单独的背景服务。

使用 `OpenCRM.sln`. 配置和本地设置请参见根 [README](../README.zh-CN.md); 建筑界限在 [技术文件](architecture-current.zh-CN.md).

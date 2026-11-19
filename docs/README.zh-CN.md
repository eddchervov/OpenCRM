# OpenCRM 技术文档

[English](README.md) | [Русский](README.ru.md) | **简体中文**

在这里查找 OpenCRM 的部署、日常使用、扩展和内部接口说明。代码、配置示例和截图保留原样。翻译稿如有歧义，可通过每页顶部的链接核对英文或俄文版本。

## 快速开始

- [OpenCRM 项目概览](../README.zh-CN.md)
- [界面截图与说明](screenshots.zh-CN.md)
- [本地部署、数据库迁移和首次登录](local-run.zh-CN.md)
- [应用配置](configuration.zh-CN.md) · [品牌配置](../configuration/README.zh-CN.md)
- [通知与支付微服务](microservices.zh-CN.md)
- [首位管理员、Web 管理端和桌面管理端](administration.zh-CN.md)
- [定制、扩展及同步上游更新](extending-opencrm.zh-CN.md)
- [开发、测试与贡献](../CONTRIBUTING.zh-CN.md) · [项目规则（俄文）](../AGENTS.md)

<a name="diagrams"></a>

## 架构与基础设施

- [应用架构](architecture-current.zh-CN.md) · [服务健康检查](service-health-monitoring.zh-CN.md)
- [通知](notifications.zh-CN.md) · [支付服务的边界](payments-service-design.zh-CN.md) · [支付服务集成](payments-service-integration.zh-CN.md)
- [账户模块设计](accounts-module/design.zh-CN.md) · [移除账户场景和功能](accounts-module/profile-removal.zh-CN.md) · [账户模块提取规范](accounts-module/spec.zh-CN.md)
- [数据模型、组件与服务交互图](uml/README.zh-CN.md)
- [界面示意图说明](plan.zh-CN.md) · [国际化计划](internationalization-plan.zh-CN.md)

<a name="workspaces"></a>

## 工作区与日常业务

- [预约和排班可用时间](booking-availability.zh-CN.md) · [调整预约](calendar-rescheduling.zh-CN.md)
- [接车检查与前后对比照片](detailing-intake.zh-CN.md) · [工作看板](detailing-workday.zh-CN.md)
- [保养周期](maintenance-periods.zh-CN.md) · [工单服务和收款](maintenance-services-payments.zh-CN.md)
- [服务目录完整性](service-catalog-integrity.zh-CN.md) · [服务工时](service-duration.zh-CN.md) · [作业日志](service-journal.zh-CN.md)
- [组织设置](organization-setup.zh-CN.md) · [员工](organization-employees.zh-CN.md) · [数据导出](organization-data-export.zh-CN.md)
- [CRM 数据导入](universal-crm-import.zh-CN.md) · [车辆标识](vehicle-identity.zh-CN.md) · [客户档案](client-card.zh-CN.md)
- [客户沟通](communications.zh-CN.md) · [咨询设计](inquiries-design.zh-CN.md) · [咨询实现](inquiries-implementation.zh-CN.md)
- [员工班次和工时](employee-shifts-design.zh-CN.md) · [员工工作区](employee-workplace.zh-CN.md) · [员工工作区实现](employee-workplace-implementation.zh-CN.md)
- [团队与权限](team-and-access-design.zh-CN.md) · [美容门店与维修厂共用流程](simple-maintenance-workflow.zh-CN.md) · [维修厂与车队功能开发](sto-fleet-development.zh-CN.md)

## 系统管理与其他模块

- [管理仪表板](admin-dashboard-analytics.zh-CN.md) · [订阅收款及自雇收据](admin-finance.zh-CN.md) · [桌面管理端的组织管理](admin-organizations.zh-CN.md) · [订阅管理](admin-subscriptions.zh-CN.md)
- [服务端 AI 助手](ai-assistant.zh-CN.md) · [语音助手操作](voice-ai-actions.zh-CN.md) · [可选邮件验证](email-confirmation.zh-CN.md)
- [共享 CRM 界面框架](crm-workspace-shell.zh-CN.md) · [导航](navigation.zh-CN.md) · [表单和区域标题](form-headers.zh-CN.md)
- [技术概览](project-overview.zh-CN.md)

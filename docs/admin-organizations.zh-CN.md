<a name="organizations-in-the-opencrm-admin-interfaces"></a>

# 桌面管理端的组织管理

[English](admin-organizations.md) | [Русский](admin-organizations.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

网络和桌面管理客户列出了各种受支助和遗留情况的组织。 搜索和组织细节包括所有者、状况、成员、使用次数和审计信息。 阅读要求 `Admin.Users.View`;改变组织地位也要求 `Admin.Users.Manage`.

行政官可以强制管理或恢复一个组织.  这一行动要求有多达2000个字符的理由,并明确确认。 已删除的组织不能更改;只能从档案中恢复。 即使在用户配置时也允许进行行政归档 `Organizations:ArchivingEnabled` 已禁用,不要求管理员为成员,也不替换所有者。 服务器执行交易中的变更,并在组织审计活动中记录管理员和理由.

存档关闭成员工作空间访问. 例行清理标志着一个存档组织在30天后被删除.  在清理之前,可以用其数据来还原.  一个存档的组织继续占用其类型的组织创建时间档.

桌面 API 是 `POST /api/v1/admin/organizations/{id}/archive` 和 `/restore`,与身体结合 `{ "reason": "..." }`. 空或过长的理由被拒绝; 无效状态返回冲突 。  如果桌面客户端丢失了响应,则不会重试写入,并屏蔽进一步的状态变化,直到卡片刷新成功. 服务器 API 必须与桌面客户端分开更新 。

<a name="optional-commercial-information"></a>

## 任择商业信息

`PlatformPolicy:Commercial:Enabled` 默认为 `false`.  在停用时,从行政界面和组织记录中省略了计费和订阅信息。 启用时,没有付费订阅的活跃的"细节"和服务站组织使用无限免费访问;归档和被删除的组织保持其实际状态. 商业管制还需要有关计费许可。  见 [订阅管理](admin-subscriptions.zh-CN.md).

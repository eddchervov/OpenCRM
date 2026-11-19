<a name="detailing-service-journal"></a>

# 作业日志

[English](service-journal.md) | [Русский](service-journal.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

密码 `#work-journal`  该节显示各组织从始至终的任命情况。 默认状态为“已完成”。  你可以选择所有,有计划,有进展的,或取消的预约. 每日时间安排和日历仍保留在“任命”中。

 一行为一行维护记录,包括所有服务、车辆、客户、状况、计划和实际日期以及折扣后的总额。 选择要打开的行 `#maintenance/{id}`. 材料已包括在总数中,但没有列为服务。

 通过服务,客户,制作,模型,和车牌的搜索,在发号施令前在PostgreSQL进行. 服务行位于 `MaintenanceDetails` 表格; 参数化的 SQL 由 `OpenCRM.Modules.Workspace/Queries/OrganizationMaintenanceJournalQueryService.cs`. `%` 和 `_` 被从字面上搜索。 回复中仅包含当前页面和匹配记录的总数.

日期范围是可选的. 已完成的记录采用完成日期;其他记录采用计划日期。  如果旧记录没有这样的日期,过滤会使用存储的维护 `Date`. 上行包含整个所选择的天数 。  结果从最新到最老,然后按计划时间、创建时间和身份证进行排序。

API : (英语). `GET /api/v1/organizations/{organizationId}/maintenance-journal`.

- `status`: `0` - 所有人,我们... `1` ——计划, `2` ——进行中, `3` -- 已完成(违约), `4` ——取消.
- `dateFrom`, `dateTo`: 可选 `YYYY-MM-DD` 日期。 日期。
- `search`:可选文本,最多200个字符.
- `page`, `pageSize`: 页面和页面大小;服务器将大小限制为100.

组织成员资格和 `WorkJournalView` 和 `MaintenanceView` 需要权限。  无 `ClientsView`,客户姓名被隐藏并排除在搜索之外. 已删除的维修记录和被删除的车辆不包括在内;其他组织无法进入。

 没有迁移或数据变化。 现有的"服务站"和"舰队"联合期刊保留了它的API和行为.

`MaintenanceJournalTests` 检查状况、旧记录、标注、排除其他组织和删除数据、按手工和目录名称、服务和材料搜索、客户访问限制、日期范围和订购。 浏览器检查使用测试API在光和暗主题中的反应,包括窄屏幕,加载错误,以及导航到记录卡.

<a name="removing-account-scenarios-and-capabilities"></a>

# 移除账户场景和功能

[English](profile-removal.md) | [Русский](profile-removal.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

<a name="current-model"></a>

## 当前模式

账户没有模式、组织类型、建议或启用能力。 账户拥有注册,签入,用户删除,个人记录.  每个用户都可以使用个人车辆。 进入一个组织是由成员、许可和选定组织的订阅决定的。

拥有的组织 `OrganizationTypeDefinitions` 和 `IOrganizationTypeCatalog`. 支持的代码是固定的: `detailing`, `service-station`,以及 `fleet`. 数据库约束链接 `IsSupported` 这三个代码。 创建和邮件过滤选项只使用 `IsSupported && IsActive`;读取现有组织的名称包括历史记录. 存档类型记录不创建新工作流程. `OrganizationScenarioProcessCatalog` 保留对历史过程和共享用途的描述 `OrganizationTypeCodes`.

移徙 `20261015071909_RemoveAccountProfiles` 移动所有名称、描述、活动旗帜、顺序、ID,以及旧目录中的日期。 对任何缺失的被支持代码创建标准记录 。 现有组织代码和已保存的竞选价值没有被改写.  没有目录记录的代码被显示为原始代码.

旧迁移的活性值不能被当前值所取代。 迁移保留了旧名和设置,但历史类型无法为新创作所取用.

 以下内容被移除: API路线 `/api/v1/account-profile` 和 `/api/v1/admin/account-profiles`,配置服务和DTO,注册和登录请求中的模式字段,管理界面中配置字段,以及5个表格: `UserAccountFeatures`, `UserAccountProfiles`, `AccountScenarioFeatures`, `AccountFeatureDefinitions`,以及 `AccountScenarioDefinitions`. “工作空间”页仍保留在以前的MVC路线上 `Personal/AccountProfile` 但不保存配置文件; 用于模式选择和配置文件保存的空 POST 处理器被删除 。

<a name="before-release"></a>

## 释放前

使用共享部署指令准备选定服务器修改以发布 :

1. 检查服务器和外部集成日志以获取正在删除的 APIs 的呼叫 。 4个Vue客户的来源中没有发现任何消费者. 将日志周期与实际使用相比较;没有外部客户源并不能证明没有呼叫.
2. 保存一个一致的全数 `pg_dump`  在应用迁移之前使用生产数据库。 检查可读性,并进行一个测试恢复到一个单独的数据库。 备份包含个人数据;将其保存在存储库之外,并在既定进程下公开目录.
3. 运行并保存下面查询的结果,并导出所有五个表格与备份一起被移除 。 检查未知的代码, 非标准名称/ 活动/ 顺序, 以及三个支持行的存在 。  不要自动更正代码或将历史简介类型移入一个组织。
4. 将迁移到已恢复的副本,并检查名称/代码和工作方案是否得到保存。  只有这样,移民才包括在释放中。

```sql
SELECT "Code", "Name", "Description", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt"
FROM "AccountScenarioDefinitions" ORDER BY "SortOrder", "Code";

SELECT "PrimaryScenarioCode", COUNT(*) FROM "Organizations"
GROUP BY "PrimaryScenarioCode" ORDER BY "PrimaryScenarioCode";

SELECT "PrimaryScenarioCode", COUNT(*) FROM "Organizations"
WHERE "PrimaryScenarioCode" NOT IN ('detailing', 'service-station', 'fleet')
GROUP BY "PrimaryScenarioCode";

SELECT "AccountMode", "PrimaryScenarioCode", COUNT(*) FROM "UserAccountProfiles"
GROUP BY "AccountMode", "PrimaryScenarioCode";

SELECT "FeatureCode", COUNT(*) FROM "UserAccountFeatures" GROUP BY "FeatureCode";

SELECT scenario."Code", feature."Code", link."SortOrder"
FROM "AccountScenarioFeatures" link
JOIN "AccountScenarioDefinitions" scenario ON scenario."Id" = link."AccountScenarioDefinitionId"
JOIN "AccountFeatureDefinitions" feature ON feature."Id" = link."AccountFeatureDefinitionId"
ORDER BY scenario."Code", link."SortOrder", feature."Code";

SELECT "TargetOrganizationTypeCode", COUNT(*) FROM "EmailCampaigns"
GROUP BY "TargetOrganizationTypeCode";
```

<a name="rollback"></a>

## 倒转

`Down` 重新创建五个表格的图案,并将所有组织类型名称都移回,并带有ID和设置。  没错 ** 没有** 恢复配置文件、 特性或关系; 备份 `Up`  这些需要。

 在回滚时,停止应用程序写作,并首先在单独的拷贝上测试序列. 之后 `Down`,则情景表格中已经包含迁移的条目:在从一致的备份中恢复5个表格之前,将这些条目一起清除,然后恢复数据和序列值。  按此顺序装入数据:特征/外观定义,剖面图,情景链接,以及用户能力.  不要有选择地重新装入 `AspNetUsers`、组织或其他生产表格。 比较数,ID,以及与备份的关系. 仅在检查相容的 schema后返回到旧的应用程序版本. 完整的数据库还原是一个单独的决定,因为它失去了自备份以来所做的更改;不要自动执行.

本地化测试使用单独的 PostgreSQL 实例和检查 `Up`, `Down`,从一致的 SQL 副本中恢复所有五个表格,并重复 `Up`.  它不会取代生产数据库备份和服务器消费者的验证.

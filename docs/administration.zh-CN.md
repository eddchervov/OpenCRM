# 管理 OpenCRM 部署

[English](administration.md) | [Русский](administration.ru.md) | **简体中文**

OpenCRM 提供 Web 管理界面和独立的桌面管理应用。它们连接同一个服务端管理 API，共用服务器上的角色与权限。

## 首位管理员

新部署的所有者先注册普通账户，再在自己的数据库中单独授予管理权限。以下步骤适用于**已完成迁移的自有新库**。`System` 角色拥有全部管理权限，只应授予经核验的所有者账户。组织成员资格和组织角色单独管理。

### 1. 注册账户

按照[部署指南](local-run.zh-CN.md)启动 CRM，在 `/auth/register` 用自己的密码注册。基本密码登录无需 SMTP 或邮箱验证。

### 2. 查找准确的用户 ID

使用有数据库管理权限的账户通过 `psql` 连接自有数据库。指南中的本地数据库示例：

```text
psql -h localhost -U opencrm_local -d opencrm_local
```

把下列邮箱地址换成注册时使用的地址：

```sql
SELECT "Id", "Email"
FROM identity."AspNetUsers"
WHERE "NormalizedEmail" = upper('owner@example.invalid');
```

确认查询只返回一行且属于自己的账户，然后复制其 `Id`。

### 3. 在事务中授予角色

在 `psql` 中执行以下示例。`\set` 是客户端命令。将 `REPLACE_WITH_OWNER_ID` 换成已核验的 `Id`。

```sql
\set owner_id 'REPLACE_WITH_OWNER_ID'

BEGIN;

INSERT INTO identity."AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
VALUES (gen_random_uuid()::text, 'System', 'SYSTEM', gen_random_uuid()::text)
ON CONFLICT DO NOTHING;

INSERT INTO identity."AspNetUserRoles" ("UserId", "RoleId")
SELECT u."Id", r."Id"
FROM identity."AspNetUsers" AS u
JOIN identity."AspNetRoles" AS r ON r."NormalizedName" = 'SYSTEM'
WHERE u."Id" = :'owner_id'
ON CONFLICT DO NOTHING;

SELECT u."Id", u."Email", r."Name" AS "Role"
FROM identity."AspNetUsers" AS u
JOIN identity."AspNetUserRoles" AS ur ON ur."UserId" = u."Id"
JOIN identity."AspNetRoles" AS r ON r."Id" = ur."RoleId"
WHERE u."Id" = :'owner_id' AND r."NormalizedName" = 'SYSTEM';
```

核对最后一个查询只返回选中的账户及 `System` 角色。结果正确时提交事务：

```sql
COMMIT;
```

结果有误或找不到该账户时回滚：

```sql
ROLLBACK;
```

该示例只为选中的账户授予角色，不创建用户或修改密码。未配置管理权限的普通 `Admin` 角色无法完成首位管理员的初始化。

### 4. 重新登录

退出当前会话并重新登录，让 Cookie 或令牌使用新的角色信息。然后访问 **https://localhost:7142/admin-panel/index.html**。

以后通过管理界面设置管理角色与权限。请勿批量向普通成员分配 `System` 角色。

## Web 管理界面

管理端随前端一起构建，可从本部署的 `/admin-panel/index.html` 或 `/admin-panel/` 打开。未登录用户会被重定向到登录页。服务端会检查访问权限。

## 桌面应用

启动 WebApp，然后在 Windows PowerShell 中从仓库根目录运行：

```powershell
$env:OPENCRM_ADMIN_API_URL = 'https://localhost:7142/'
dotnet run --project src/OpenCRM.Frontend/Admin/OpenCRM.Admin.App
```

使用自己的管理员账号登录。显示名称和默认网址由品牌文件设置。`OPENCRM_ADMIN_API_URL` 覆盖当前进程使用的服务端网址。HTTP 仅限环回地址，其他部署应配置可信的 HTTPS。

详见[桌面管理端说明](../src/OpenCRM.Frontend/Admin/OpenCRM.Admin.App/README.zh-CN.md)。Windows 项目 [`OpenCRM.Admin.Launcher`](../src/OpenCRM.Frontend/Admin/OpenCRM.Admin.Launcher) 还可以用 Chrome 应用模式打开同一个 Web 管理端，账户与权限仍由服务器处理。

## 商业模式

默认情况下，Web 与桌面管理端均隐藏商业功能。可在服务端设置 `PlatformPolicy__Commercial__Enabled=true` 后重启相关进程。套餐、价格、支付服务提供方和密钥由各部署自行配置，访问仍须通过管理权限检查。参见[配置](configuration.zh-CN.md)与[微服务](microservices.zh-CN.md)。

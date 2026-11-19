# 本地部署 OpenCRM

[English](local-run.md) | [Русский](local-run.ru.md) | **简体中文**

本指南使用独立的 PostgreSQL 实例运行基础 OpenCRM。注册、密码登录和基础 CRM 功能无需 SMTP、RabbitMQ、通知服务、AI 或支付服务。

## 环境要求

- .NET SDK 10、Git。
- Node.js `^20.19.0` 或 `>=22.12.0`，以及支持工作区的 npm。版本要求来自 `src/OpenCRM.Frontend/package-lock.json` 中的 Vite 7。
- PostgreSQL 17。项目已在该版本上验证过全新部署。
- 用于 HTTPS 的 .NET 本地开发证书。

如果 PostgreSQL 实例已经可用，可以直接使用它，无需 Docker。

## 获取代码并安装前端依赖

Linux（Bash）

```bash
git clone https://github.com/eddchervov/OpenCRM.git
cd OpenCRM/src/OpenCRM.Frontend
npm ci
cd ../..
```

Windows（PowerShell）

```powershell
git clone https://github.com/eddchervov/OpenCRM.git
Set-Location OpenCRM/src/OpenCRM.Frontend
npm ci
Set-Location ../..
```

## 创建独立的本地数据库

请选择以下任一方法。把示例密码换成自选密码，并在连接字符串中使用同一密码。

### 方法 A：在 Docker 中启动 PostgreSQL

以下命令在 Bash 和 PowerShell 中均可运行。它创建本地数据库和持久化数据卷，并将端口绑定到环回地址。

```text
docker run --name opencrm-local-db --detach --publish 127.0.0.1:5432:5432 --volume opencrm-local-data:/var/lib/postgresql/data --env POSTGRES_DB=opencrm_local --env POSTGRES_USER=opencrm_local --env POSTGRES_PASSWORD=replace-with-your-local-password postgres:17-alpine
```

等待以下检查返回 `accepting connections`：

```text
docker exec opencrm-local-db pg_isready -h 127.0.0.1 -U opencrm_local -d opencrm_local
```

如果 `5432` 已被占用，请换一个宿主机端口，并在连接字符串中同步修改。再次使用已有容器时，直接启动它以保留数据卷和既有密码。

### 方法 B：使用已有 PostgreSQL 实例

用 PostgreSQL 管理员账号创建一个独立角色和一个由该角色拥有的空数据库。可以用 `psql` 或常用的数据库管理工具执行 SQL。请将实际密码保存在 Git 之外。

```text
psql -h localhost -U postgres -d postgres -c "CREATE ROLE opencrm_local LOGIN PASSWORD 'replace-with-your-local-password';"
psql -h localhost -U postgres -d postgres -c "CREATE DATABASE opencrm_local OWNER opencrm_local;"
```

可以使用自己的主机、端口、角色和新数据库名称。运行迁移前请核对连接目标，确保它是本次部署使用的数据库。

## 配置连接并运行迁移

Linux（Bash）

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__OpenCRMConnection='Host=localhost;Port=5432;Database=opencrm_local;Username=opencrm_local;Password=replace-with-your-local-password'
```

Windows（PowerShell）

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__OpenCRMConnection = 'Host=localhost;Port=5432;Database=opencrm_local;Username=opencrm_local;Password=replace-with-your-local-password'
```

在仓库根目录还原本地 EF CLI。工具清单中的 `10.0.7` 与迁移项目匹配。

```text
dotnet tool restore
dotnet ef database update --project src/OpenCRM.Infrastructure.Migrations --startup-project src/OpenCRM.Infrastructure.Migrations
```

迁移项目从 `ConnectionStrings__OpenCRMConnection` 读取连接。Web 应用启动前需单独执行迁移。

## 启动并登录

Windows 和 macOS 可创建并信任本地 HTTPS 开发证书：

```text
dotnet dev-certs https --trust
```

Linux 可用 `dotnet dev-certs https --check` 检查证书是否存在。如有需要，用 `dotnet dev-certs https` 创建证书，再用发行版提供的工具将其加入系统和浏览器的信任存储。

从仓库根目录启动应用：

```text
dotnet run --project src/OpenCRM.WebApp/OpenCRM.WebApp.csproj --launch-profile https
```

访问 **https://localhost:7142**。应用和数据库的就绪状态见 `/api/health/ready`。在 `/auth/register` 注册后登录并创建组织。默认 `Notifications:Transport=Disabled` 且 `EmailConfirmation:Required=false`，应用不会发送注册邮件或自动将邮箱标为已验证。

`http` 启动配置在 `http://localhost:5134` 提供启动和就绪检查。登录请使用 HTTPS，应用的会话 Cookie 带有 `Secure` 标志。

系统无默认管理员账号和密码。请按[管理指南](administration.zh-CN.md)给首位管理员分配角色并打开管理界面。

## 构建

安装 npm 依赖后，在 `src/OpenCRM.Frontend` 中构建前端：

```text
npm run build
```

在仓库根目录构建 Web 应用：

```text
dotnet restore src/OpenCRM.WebApp/OpenCRM.WebApp.csproj
dotnet build src/OpenCRM.WebApp/OpenCRM.WebApp.csproj
```

完整解决方案包含 Windows WPF 启动器，请在 Windows 构建所有项目：

```text
dotnet restore OpenCRM.sln
dotnet build OpenCRM.sln
```

## 后续配置

- [应用配置](configuration.zh-CN.md)说明配置来源、主要章节和可选集成。
- [品牌配置](../configuration/README.zh-CN.md)说明 `configuration/brand.json` 和服务端覆盖项。
- [应用架构](architecture-current.zh-CN.md)介绍模块位置和产品扩展点。

修改配置后，请重启使用相关设置的进程。

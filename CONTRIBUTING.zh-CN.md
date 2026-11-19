<a name="developing-opencrm"></a>

# 开发、测试与贡献

[English](CONTRIBUTING.md) | [Русский](CONTRIBUTING.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

开发核心中共享客户关系管理基础的改进;将网站或产品的特有功能保留在自己的定制层. 密码 [扩展指南](docs/extending-opencrm.zh-CN.md) 描述分离和更新工作流程。

<a name="preparation"></a>

## 准备

1. 填写 [本地设置](docs/local-run.zh-CN.md).
2. 读取 [结构](docs/architecture-current.zh-CN.md)时,最接近 `AGENTS.md`,则 `.editorconfig` 用于您将更改的组件。
3. 找到一个具有适当责任的现有模块、合同、UI组件或适配器。

整合测试需要多克:它们使用孤立的PostgreSQL和RabbitMQ例.  它们不需要秘密或真实的证明。

<a name="where-to-make-changes"></a>

## 何处可作改变

| 变动 | 区域 |
| --- | --- |
| 客户关系管理业务规则和业务 | `src/OpenCRM.Modules.*` |
| 跨模块 API 和 DTO | 相应 `*.Contracts`  项目 |
| 储存、数据库、提供者 | `src/OpenCRM.Infrastructure.*` |
| HTTP 层,页,模块注册 | `src/OpenCRM.WebApp` |
| 客户端屏幕 | `src/OpenCRM.Frontend/Scenarios` |
| 共享接口 | `src/OpenCRM.Frontend/packages/ui`, `packages/workspace` |
| 行政应用程序 | `src/OpenCRM.Frontend/Admin` |
| 微服务 | `services/notifications`, `services/payments` |

重新使用合同和组件。 保存角色,权限检查,组织隔离,以及现有数据.  在没有相应配置的情况下,不启用基础模式的外部集成或商业限制.

<a name="validation"></a>

## 验证

完整的解决方案包括Windows WPF启动器.  从寄存器根上运行 Windows 上的这些全解决方案命令 :

```bash
dotnet tool restore
dotnet restore OpenCRM.sln
dotnet build OpenCRM.sln
dotnet test OpenCRM.sln --no-restore -p:BuildScenarioClientsOnBuild=false
```

 主要解决方案包括后端测试项目,包括服务.  一个正常的构建也会构建客户端应用程序; 第一次运行 `npm ci` 内 `src/OpenCRM.Frontend`,如设置指南所描述。

 对于Linux/macOS上的后端开发,构建网络项目并运行单个模块测试项目;验证Windows上的Windows Launger.  例如:

```bash
dotnet restore src/OpenCRM.WebApp/OpenCRM.WebApp.csproj
dotnet build src/OpenCRM.WebApp/OpenCRM.WebApp.csproj
```

修改单个模块时,您可以运行其测试项目:

```bash
dotnet test tests/OpenCRM.Modules.Clients.Tests/OpenCRM.Modules.Clients.Tests.csproj -p:BuildScenarioClientsOnBuild=false
```

模块项目中的后端检查分离为 `Unit` 和 `Integration`. 全面运作必须包括两个团体。 单凭单位测试不能取代对HTTP、数据库、消息代理和文件渲染的检查。

 前端, 巴什 :

```bash
cd src/OpenCRM.Frontend
node --test tests/*.test.mjs
npm run build
cd ../..
```

 前端, PowerShell : (原始内容存档于2019-09-21) (英语).

```powershell
Push-Location src/OpenCRM.Frontend
node --test (Get-ChildItem tests/*.test.mjs | ForEach-Object FullName)
npm run build
Pop-Location
```

 要检查后端边界, 请在 PowerShell 7 中运行以下 :

```powershell
./scripts/assert-backend-boundaries.ps1
```

 根据您更改的风险选择额外检查 。  对于UI更改,检查键盘访问,收窄屏幕,以及轻/暗主题;对于访问控制,测试所有者,一个成员没有要求的许可,和一个无关的用户.

<a name="modules-projects-and-data"></a>

## 模块、项目和数据

- 包含每个新的后端测试项目 `OpenCRM.sln`,则 `Tests` 解析文件夹,并同时构建配置。
- 在自己的项目中保留一个模块的测试,在共享的测试库中保留共享的固定装置,在测试项目之间不引用.
- 相伴数据库模型以相容的迁移来改变. 对照相邻的迁移检查元数据,主文件,设计者,以及快照;在单独的数据库上建立和验证.
- 计划备份,制作数据库更新,与应用程序汇编分开回滚.
- 测试数据必须是虚构的、有效的和可区分的。 意外地清除密码,令牌,或独有的电话号码,可以使测试的目的无效.
- 确定检验时保留检验合同. 不得无端地取代有意义的断言,也不得以增加拖延来掩盖问题。

<a name="documentation-languages"></a>

## 文件语文

 英语是项目文件的默认语言.  每个指南旁边有一个俄文版本: `guide.md` 和 `guide.ru.md`.  在改变行为,配置,或指令时,保持两个版本的同步.

- 保留 `English | Русский` 链接在文档标题下方。
- 链接到一个指南的匹配语言版本,并在翻译标题时更新标题主锚.
- 两个版本共享截图和其他二进制资产;翻译字幕而不是复制图像.
- 保存命令, API 标识符, 配置密钥, 迁移ID, 以及可执行实例 。  俄罗斯UI标签或语音实例仍可引用并附英文解释.
- 在保存标识符和关系的同时翻译图表标签; 独立的 PlantUML 源使用配对 `.puml` 和 `.ru.puml` 文档。
- 不翻译或更改第三方许可文本或可操作 `AGENTS.md`  作为指南翻译的一部分的指令。

<a name="before-submitting-a-change"></a>

## 在提交更改之前

描述问题和由此产生的行为. 列出你所开的支票 以及尚未核实的 修改合同、配置或启动程序时更新相关模块文档。

保存编码和行尾. 以新添加的代码用俄语写出注释. 不包括您运行中的安装、构建输出或临时报告中的秘密、设置。

保留 [许可证](LICENSE),版权通知,以及可见的“基于 [OpenCRM](https://github.com/eddchervov/OpenCRM)“归属。 界面不需要作者的姓名或配置链接;作者信息仍保留在核心README和许可证中.

Python 3 安装后, 验证本地文档链接, 标题锁定, 语言对等, 以及来自寄存器根的截图路径 :

```powershell
python scripts/check-documentation.py
```

<a name="opencrm-server-side-ai-assistant"></a>

# 服务端 AI 助手

[English](ai-assistant.md) | [Русский](ai-assistant.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

 此文档是新任务的切入点:先读它,然后读它 [命令目录](voice-ai-actions.zh-CN.md).  它不会取代读取您要更改的代码 。

<a name="status-and-boundaries"></a>

## 地位和边界

助理通过安装特定设置启用. 外部供应商需要自己的钥匙和预算。  主要情况是详细介绍和修理商店。 个人车辆的现行规则和隐藏车队的情况仍然有效。

客户关系管理 `#assistant` 详细/修理商店和单独的工作人员Telegram bot使用一个服务器侧对讲器和执行器。 旧的 `/assistant/{organizationId}` 检查访问权限,并重新导向适当的客户关系管理。 密码 `/ai-pilot` 演示页面不是基础分布的一部分。 发动机的一部分还在 `Infrastructure/AiPilot`  出于历史原因;不得以这个名称作为将执行移入浏览器的理由.

路由器AI将音频转录,将一个短语分解为允许的命令,并在启用语音输出时生成音频响应.  该模型接收用户动作和需要字段的目录,而不是代码,数据库计划,cookie,应用程序机密,或者任意的HTTP请求工具. 向提供者发送有问题的信息和必要的上下文,以便随后进行解析。

<a name="access-and-budgets"></a>

## 获取和预算

 在中性模式下,CRM工作不需要商业订阅.  在商业政策允许的情况下,访问考虑 `workspace.assistant` 和 `workspace.client_messaging`. 服务器检查成员、用户权限、频道开关、授权到期和预算,不论商业模式如何。

`AssistantAccessService.EnsureAvailableAsync` 检查命令处理和AI呼叫前的可用性; 草稿确认重复服务器检查. 频道配置不替换权限 。 访问更改不删除历史或支出;会读取服从权限/保留规则并不会触发AI呼叫.

预算是独立的限制。  在接到电话之前,服务器按当前限额预留资金。 特定装置的应用定义了计划、晋升和试用条件;核心既未定义促销代码,也未界定卖方报价。

客户信息在发送到提供者之前重新检查访问权限 。 问题不得绕过残疾通道;以前交付的历史根据组织规则保留。

<a name="administrative-management"></a>

## 行政管理

桌面管理有一个单独的AI助理科. `Admin.Configuration.Manage` 允许进入全球交换机、网站/工作人员-电话频道交换机、语音输出、模型、语音、每日/月度预算、预订和象征性限制。  全局禁用块操作,并在上下文刷新后隐藏 CRM 助手按钮;通道可以独立被禁用. 最大值来自服务器 `AssistantOptions`;客户端块保存出行值,服务器进行最终验证. 保存设置发送 `Revision`;在相冲突时,界面会重新装入服务器版本,显示消息,不自动重试写入. 设置通过保存按钮应用 。

在同一节中,一个管理员: `Admin.Configuration.Manage` 搜索组织以及全球管理的赠款、渠道、终止期和预算。 访问请求和设置更改输入服务器审计.  该组织的表格有意只包含现有行政API支持的字段;合同中还没有单独的赠款评论.

查看开支需要 `Admin.Billing.View`; 将已完成的请求与 `unknown` 状态要求 `Admin.Configuration.Manage`.  为了核对,管理员提供服务器审计的实际成本和理由。 报告日期使用共享桌面 `AdminDateTimeField`;边界作为日历日期发送。  全球禁用不会抹去历史、预算或组织设置。

<a name="command-path-and-mandatory-checks"></a>

## 命令路径和强制检查

1. 服务器建立雇员、组织和频道,然后检查成员、权限、订阅、交换机和批准失效。 客户端无法代表另一个用户选择任意执行器 。
2.  在对外通话之前,服务器会预留预算. 承认的言论遵循与文字相同的对话方式;用户可以在网站上更正笔录.
3. 引擎验证了针对目录的命令,并询问缺少的信息. 新的任命、接收和完成的访问都需要一个工作项目;完成需要价格。 明确为零价格是有效的。 服务与目录相匹配;未知的工作成为了自由形式行,模糊性需要选择.
4. 处理器读取目标,准备修改,并保存一个带有数据快照的服务器侧面草稿. 需要挑选多辆汽车/任命或类似的服务。 共同政策使登记牌照正常化;雇员仍在检查确认牌照。
5. 更改仅通过确认按钮进行;删除/取消需要额外的确认. 键入“_________________________(是)”不执行动作。  确认重新检查访问权限和目标快照,并在解剖上将草稿移入执行中.
6. 商业服务适用普通的客户关系管理规则. 重复完成的草稿返回,结果始终如一。 不得自动重审未知/部分结果:雇员检查客户关系管理。 订单创建与单独支付之间没有共享交易.

计划,角色,网站管理,代码/SQL执行,任意URL,以及外部活动都在助理目录之外. 阅读也受许可限制. 助手不保证准确识别注册牌照或金额,绝不绕过域名验证.

助理分页不横向滚动:宽屏幕上有3个列;最高可达560 px,有2个列并有最后一个项横行.  主页上仍然保留着对话。  新信件打开原生信件 `dialog` 用于发言/文字;准备的行动打开单独的审查窗口。 关闭不会执行动作或删除草稿;对话横幅会重新打开它. 恢复对话显示,横幅没有自动打开草稿. 关闭消息窗口在录制,定稿,识别,发送时被屏蔽;模式更改,又一个音频输入,发送时也等待当前输入. 即使在助理访问被禁用后,录音也可以被停止或麦克风等待被取消. 成功发送关闭对话框; 文本保存后, 错误会留在内部 。 语音/文本共享一个可编辑字段;更改输入模式不会清除. 示例短语被崩溃;澄清选择属于对话. 所准备的行动单独列出以确认;发送信息不执行。

<a name="data-history-and-spending"></a>

## 数据、历史和开支

| 数据 | 储存和用途 |
| --- | --- |
|  二. 对话和信息 | `AssistantConversations`, `AssistantMessages`:用户,组织,频道,上下文,以及信息. 网站显示网络/电报历史;只有组织所有者/管理员可以读取其他团队成员的历史. |
| 草稿 | `AssistantDrafts`:目标,快照,过期,执行状态,以及结果;重复保护存在于服务器上. |
| 开支 | `AssistantUsage`: 雇员、组织、频道、模型、信使、费用/保留和申请地位。 对话清理不删除支出记录. |
| 设置和审计 | `AssistantSettings`, `AssistantOrganizationAccess`, `AssistantAudits`:全球/组织开关和预算、赠款到期和行政变化。 |
| 电报 | `AssistantTelegramBindings`, `AssistantTelegramLinks`, `AssistantTelegramUpdates`:员工装订,一次性签入,排队,并重复更新保护. |

`Assistant:ConversationHistoryRetentionDays` 定义历史保留 : 当前初始值是对话后90天 。 `ExpiresAt`. 电报技术记录有单独的 `HistoryRetentionDays`. `AssistantCleanupService`  删除已过期的记录; 没有永久存档或恢复已删除的对话。  只有用户自己的活跃的网络对话才能在网站上恢复;档案会返回没有内部状态或命令的信息.

预算是全球性和针对具体组织的,每日和每月;网站和Telegram分担本组织的预算。 预算重组和剩余余额卡的使用 `Assistant:BudgetTimeZone`,与预请求服务器验证一样。 选定报告日期使用该组织的 `ShiftTimeZoneId`,倒转到 `Assistant:DefaultTimeZone`; API 返回 `timeZone` 和 `budgetTimeZone` 分别。 本地日边界表示在时钟变化时跳出/重复的午夜 。 限制和使用情况显示支出、预订、剩余数额以及按雇员/渠道分列的细目。  全团队的细分均需 `members.view` 和 `finance.view`;否则用户只看到自己的细节. 个人雇员的限额尚未确定。

零预算并不意味着无限。  在提出要求之前,保留与实际支出一起计算;超过任何预算都会阻碍新的呼吁。 未知的成本继续占据着它的保留地。 实际反应费用可能超过初步储备金:预算停止以后的调用,不能保证对一项请求有准确的上限。 与提供者账户核对数据;对未知费用进行行政调整需要理由。

助理使用客户关系管理软件 `opencrm.workspace.theme` 选择:共享 `wwwroot/js/organizations-entry.js` CSS 之前初始化, 并选择调色板 `data-crm-theme`. 光是默认值; OS 暗模式不会覆盖它. 密码 `storage` 事件在标签之间同步主题选择。

<a name="configuration-and-operation"></a>

## 配置和操作

- `src/OpenCRM.WebApp/appsettings.json`: `Assistant`, `AssistantTelegram`,并共享 `VehicleRegistrationPlates` 政策。 模型,语音,URL,技术限制,以及保留期都来自配置.
- `/admin-panel#/assistant`: 赋能/可见度、渠道、模式、预算、组织准入和支出。 数据库设置在重新启动后幸存;配置提供初始值,而不是在每次启动时覆盖它们.
- 路由器 : `Assistant:RouterApiKeyFile`/`RouterApiKey`,有遗产 `AiPilot:ApiKeyFile`/`ApiKey` 兼容性。 Telegram sign and signant secret us secretive files. 电报令牌和签名秘密使用单独的文件. 永远不要在文档、来源、日志或截图中放置秘密值。
- 本地验证使用您自己的孤立数据库, 并测试在寄存器外提供的值 。 README 描述一般启动; 通过设置提供者密钥 `Assistant:RouterApiKeyFile`. 核心指令硬码既不是数据库URL,也不是组织ID或预算.
- 本地电报只使用长选票 `AiPilot` 没有隧道。 普通服务器使用带有密页头的HTTPS webhook.  不要运行两例相同的机器人。 网站的一次性链接约束雇员;普通 `/start` 不允许进入。  这个员工机器人与客户CRM对话bots是分开的.
- 助手的Telegram按钮打开Telegram连接:当前的TelegramID,日期,连接/替换链接,以及断开.  这是账户绑定,而不是一个设备列表; Telegram names/ phone number 不存储 。 拥有者/管理人员在机器人中看到选择该组织的现任成员的联系;其他组织的约束不暴露。 用户只能同时在所有组织中断开自己的账户;等待一次性链接到期,历史依然存在。
- 组织专用的 API 密钥和 URL 尚未执行 。 提供任意的模型 URL 无法使它们得以使用;需要单独的安全存储、地址验证和保存权限/会计。

<a name="code-map"></a>

## 代码映射

| 区域 |  主要文件相对于寄存器根 |
| --- | --- |
| 网络和行政条目 | 单位 `src/OpenCRM.WebApp/Controllers/`: `AssistantController.cs`, `Api/V1/AiPilotApiController.cs`, `Api/V1/AssistantDashboardApiController.cs`, `Admin/Api/AssistantAdminApiController.cs`. |
| 谈话和确认 | `src/OpenCRM.WebApp/Infrastructure/Assistant/AssistantService.cs`, `AssistantAccessService.cs`, `AssistantActionExecutor.cs`, `Actions/`. |
| 解析、目录、路由器AI | `src/OpenCRM.WebApp/Infrastructure/AiPilot/`: `AiPilotConversationService.cs`, `AiPilotActionCatalog.cs`, `RouterAiPilotClient.cs`. |
| 支出、历史、设置 | `Infrastructure/Assistant/AssistantUsageService.cs`, `AssistantDashboardService.cs`, `AssistantSettingsStore.cs`, `AssistantCleanupService.cs`, `AssistantOptions.cs`. |
| 电报 | `Infrastructure/Assistant/Telegram/`: 投票,队列处理器,服务,API客户端,按钮签名,以及设置. |
| 模式和移徙 | `src/OpenCRM.Domain/Assistant/`, `src/OpenCRM.Infrastructure.Data/EntityTypeConfigurations/AssistantEntityTypeConfiguration.cs`,迁移 `20261026135550_AddServerAssistant`  还有设计师和快照 |
| 接口 | 客户关系管理条目 : `src/OpenCRM.Frontend/packages/workspace/src/components/WorkspaceAssistantButton.vue`; 行政管理: `Admin/admin-client/src/views/AssistantView.vue`. |
| 验证 | `tests/OpenCRM.Assistant.Tests/Unit`, `Integration`; 各测试项目中的对讲和企业回归; 前端 `src/OpenCRM.Frontend/tests`. |

<a name="change-validation-and-rollback"></a>

## 更改验证和回滚

 对于受影响的情景,测试澄清,模棱两可的目标,准备后的权限/残疾,呆板的快照,重复确认,未知的结果,预算,以及用户/组织/渠道之间没有泄漏.  对于Telegram,检查重复更新,签名,绑定过期/单次使用;对于接口,历史,详细查看权限,以及移动布局. 普通商业服务的改变需要他们自己的回归检查.

股票余额调整通过已读项目 `UpdatedAt` 给商业服务。  如果在草稿准备和确认之间出现股票变化,服务器拒绝 stale 调整,而不覆盖更新的数据.

 在保存数据的同时使助理屏蔽条目/新请求/确认功能失效. 倒车 `AddServerAssistant`  删除带有历史/支出的助理表格;它已经创建的订单、车辆、客户和付款不会自动被取消。 部署有迁移核查和备份;当地启动/准备不会取代生产接受。

`src/OpenCRM.Frontend/tests/assistantDialogs.test.mjs` 附加检查对话框转换:在访问禁用后停止记录,同步停止 ~ ASR 间隙, 屏蔽 Escape/sending/ input 更改, 文本保存, 重新打开草稿, 以及强制危险动作确认 。 UI测试不会取代服务器的授权/能力检查.

对话历史对对话列表和消息有单独的 pagination;当只有一个页面存在时,导航会隐藏. 电讯管理测试于 `assistantTelegram.test.mjs` 和 `TelegramManagementIntegrationTests.cs`.

使用实际共享的剃刀周期字段 `UiDatePicker` 组件从 `@opencrm/ui`. 密码 `packages/ui/src/razorDateTimeFields.ts` 适配器还支持 `UiTimePicker` 时间田地。 隐藏字段为 API 存储 ISO 日期/ HH:mm; `opencrm:field-sync` 将加载值和已禁用状态传递到组件,而组件选择则更新隐藏字段并释放变化。 `npm run build --workspace @opencrm/ui` 生产 `wwwroot/app/date-time-fields.bundle.js` 和CSS;普通前端建设和出版包括两者,出版检查需要两种文件.  在此不要重新引入本地浏览器拾取器; root/ 前端 `AGENTS.md` 执行这个规则。

验证此整合 `razorDateTimeFields.test.mjs`, `vue-tsc` 键入检查、WebApp构建和浏览器选择/手动输入/清除/禁用这两个主题,以及以320px宽度的右手字段。 限制页不要求时间,所以没有在那里添加单独的时间字段.

“__________________________________________________________________________________________________________________ `#assistant` 共享详细信息/修理车间部分 `WorkspaceCrmShell`. 标题按钮打开时不离开CRM;左侧菜单,配置和通知仍然可用. `WorkspaceAssistantSection.vue` 检查可用性,并将现有接口嵌入同一来源 `/assistant/{organizationId}/embedded` iframe 不重复标题、组织标题或页脚。  这隔离了现有的JS处理者/内在对话,而不复制业务逻辑. 嵌入式端点会重新检查会话,访问,和组织,返回无存储/noindex,而X-Frame-options/CSP只允许同一来源. 母公司的CRM端点和帧都允许麦克风=自己;浏览器仍然请求麦克风许可.  结果链接默认为目标QQtop, 因此记录以 CRM 打开, 而不是嵌入式 。 主题使用共享存储密钥 。 离开会破坏iframe/ handlers;服务器对话/草稿在返回时恢复.  未同意的本地信件文本不是服务器的对话 。  经确认的行动后,孩子会通知CRM;父母只接受来自自身框架、来源和组织的事件,然后刷新上下文。 助推土司使用单独的 `assistant-toast` 类来避免继承靴子 `.toast` 规则。

 在 Free 上, 助理按钮打开一个静态 `#assistant` 区域,其中视图特性在共享的 PRO 对话框中显示描述和演示草稿。 可用性、iframe、麦克风和外部AI的电话不在此模式下运行。 计划导航需要相关组织的许可;服务器访问、预算、确认和单独的 `/assistant/{organizationId}` 保持不变。

静态实例/描述明确涵盖网站的文字和语音命令以及工作人员Telegram bot,外加口头回答.  该助理被标记为BETA:能力在不断发展,必须检查准备的行动。 描述保留了bot连接条件,管理员对语音输出的许可,以及单独的AI预算. 演示回放控制既不会触发音频,麦克风,也不会引发请求.

 全局网站切换器作为模块被传递. Assistant Visiable 并在 CRM 上下文刷新后隐藏按钮/静态自由预览. 直接导航到# 助手与助手隐藏显示一个没有可用性或iframe的已禁用消息 。 PRO检查,服务器权限和预算仍在执行中.

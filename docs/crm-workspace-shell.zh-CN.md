<a name="shared-crm-shell"></a>

# 共享 CRM 界面框架

[English](crm-workspace-shell.md) | [Русский](crm-workspace-shell.ru.md) | **简体中文**

> 本页为机器译稿。涉及部署、接口或数据变更时，请同时核对英文或俄文原文。

外壳被详细,汽车服务,以及车队客户使用. 公共网站和行政界面不使用.

 当前风格是进一步发展客户关系管理的基础. 新的屏幕和对现有屏幕的修改应该遵循这个设计:

- 全宽工作空间,左通航,并有紧凑的上行.
- 平和中性表面,细边框,并有橙色口音;光和暗的主题同样得到支持.
- 紧凑的表格、卡片和图表。  一个更宽的屏幕可以让内容有更多的空间,而不是让控制更高.
- 窗体标题中的Bradecrumbs;最后一个项目是当前屏幕标题,而不是链接.
- 除了相关数据之外,没有不必要的按钮或解释性标签的行动。
- 用户可以看到他们可用的动作. 未经许可管理某项计划的雇员会立即看到该计划的到期状态。
- 在电话上,内容符合取景口宽度,导航在单独的面板中打开.

车辆登记场使用共享的Maska图书馆和服务器侧 `VehicleRegistrationPlates` 配置。 同样的配置定义了客车和商用车辆,拖车,摩托车和拖拉机式车牌的字母和数字位置. 车辆卡和快速预订表都使用 `VehicleRegistrationPlateInput`; 个人 Vue 2 表格使用共享 `v-registration-plate` “其他车牌格式”保留了外国或非标准的拼写;没有口罩的车辆类型使用免费形式的输入。  一个不变的遗存板块并不妨碍编辑其他字段. 搜索接受一个部分板块,并找到古老的拉丁语,西里尔语和混合拼音而无需重写历史.

粘贴一个值或改变其类型不得默默地通过投放不支持的字符而将其变成不同的盘子. 服务器在保存前也进行验证. `vehicleRegistrationPlateCoverage.test.mjs` 扫描Vue和Razor源,检查共享口罩是否与每个可编辑的板块字段相接,并检查捆绑是否出现在个人页面上. 标准俄语格式检查位置和字母QQ,不查询已发行牌照的登记;即使正确格式的语音识别牌照也必须由一名雇员在草稿中审查。

 目前共享部分的实施是风格参考.  通过共享调色板和组件扩展它 如此详细,汽车服务和车队保持一致性。 视觉方向的改变需要与用户单独决定.

- `packages/workspace/src/components/WorkspaceCrmShell.vue` ——全宽工作空间,被贴上顶条,组织,剖面,移动菜单.
- `packages/workspace/src/components/WorkspaceCrmNavigation.vue` 区域、组、图标和活动项目。 同样的组件被用在桌面和电话上.
- 每一个 `Scenarios/*-client/src/App.vue` 提供上下文、其部分和面包屑。 路由和工作屏幕内容仍保留在情景客户端中.

详细和汽车服务导航按日常工作、客户、资源、报告和帮助/接入分类。 服务器上下文定义了组和顺序;权限和路由代码被保存.  在恢复预约时,目前的用户和组织保留日期、时间、视图、工位、槽期和工位-视图搜索。  见 [执行、检查和限制](navigation.zh-CN.md).

 在维护卡中,相位会适应其内容的宽度,而分页会不作水平滚动来包装. 格式错误出现在相关字段旁边,与键盘使用有关,并将焦点移到该字段;底部的摘要补充了内行解释. 执行情况和核查情况分别记录在案。

 通过已有的情景区块列表和服务器-context菜单来添加新的区块. 顶端栏没有“创造”按钮。  在“组织”上可以设立组织,而任命行动则保留在相应的工作部门。 没有任何方案有单独的菜单项来创建维护或工作顺序:创建起始于预约部分内的按钮. `maintenance` 表格和卡片路线仍然可供这些行动和直接链接使用。  此 shell 不给予权限; 服务器访问检查仍然是强制的 。

模块可用性不取决于计划:基本访问通过只读模式限制数据变化.  在设置中,导入仍然可见并带有解释并禁用上传;工作导入表不在此模式下开始. 组织存档管理其生命周期:可用 `workspace.archive`,无论只读模式或订阅,并使用正常确认。  这是保留期内可收回的归档,而不是实际删除。

 在多个组织中,顶端栏上的名称打开了切换器。“组织”和光/暗主题切换器都在剖面菜单中;在电话中,“组织”也留在侧面菜单中。 “AI助理”按钮打开 `#assistant` 在同一详细/自动服务外壳内;左菜单和上栏保留。 旧的 `/assistant/{organizationId}` 链接在访问检查后在此重定向 。  从左到右,标题中包含“AI助手”(如果有的话),即将到来的行动,通知,以及最右边的描述. 助手和简介使用橙色口音,即将到来的动作琥珀和通知蓝色. 颜色来自共享 `--crm-action-*` 调色板,为光和暗的主题分别定义;标签,提示和计数器被保留.  在560 px下方,组织名称会移动在用QQ打开的菜单内,并被隐藏在上行栏. 控制按钮为40×40px,有8个px漏洞(宽屏幕为10px). 移动菜单使用模式 `dialog`:焦点留在面板内,"逃出"关闭后,焦点返回到扳机上.

贝壳样式在其Vue组件中被隔离. 共享卡使用所选的CRM主题调色板.  不要将重复的侧向导航或外壳宽度限制重新引入情景样式.

核查:构建 `build:detailing`, `build:autoservice`,以及 `build:fleet`;浏览器-以320,390,768,1024,1440和2560 px的宽度检查所有三个客户端,并带有测试API上下文. 检查导航,允许的动作,只读模式,分区更改,路由参数保存,配置,以及没有水平溢出.

面包屑出现在主屏幕卡(`UiCard page`) 向上行驶. 弹壳通过键入提供母部分 `pageBreadcrumbsKey`;组织根被省略,最终项目来自卡的实际 `title` 而不是一个链接。 二级卡不接收导航.  在CRM壳外, `UiCard` 保留它以前的行为。

详细情况、汽车服务和车队共用 [工作日,有紧凑的栏和工作卡](detailing-workday.zh-CN.md). 分析图表仍保留在“统计”中,并不取代业务工作空间。

 在“Bays”和“Slots”任用模式中,一个没有配置开放时间的组织看到“设定工作时间”与设置的链接;没有编辑许可的员工被告知与管理员联系。 “今天关闭的”只在其他工作日配置工作时间时才会出现。 选定日期的现有任命仍然明显可见。 空调度响应与加载错误区分开来. 检查 : `src/OpenCRM.Frontend/tests/schedulerSchedule.test.mjs`; 预览 : `/tests/scheduler-preview.html?scenario=autoservice&no-schedule&empty#scheduler`.

profile-菜单项目切换出光和暗的主题. `packages/ui/src/workspaceTheme.ts` 将选项存储于 `localStorage` 下级 `opencrm.workspace.theme`;光是默认值.  每个客户端在挂起前都会恢复选择. 密码 `storage` 事件同步此站点上的打开标签。  如果浏览器阻断了存储,切换器仍然在当前的分页中工作,没有出错.

深色调色板定义于 `packages/ui/src/workspaceTheme.css` 结束 `--crm-*` 变量。 光值仍为组件样式倒置;暗的CRM颜色在CRM之外没有被激活. 将这些变量用于新的中性颜色 。 图表观察主题变化并更新标签,网格,和大纲. 下拉使用其库的变量;日历使用其 `isDark` 选项。

主题检查覆盖切换,再装入,分页同步,被封存,以及所有三个客户端的表格,扩展列表,图表和移动导航的可读性.

工作区共享布局 `packages/workspace/src/styles.css`:有副类选择器的目录,带邀请表的团队名单,分组税细表,紧凑报告,和画廊. `workspace-operations`, `workspace-finance`,以及 `workspace-media` 范围样式与相关的屏幕; 容器查询适应内容宽度。

 在工作史上,日期上,车辆上的工作,以及结果上分别出现一栏.  在狭长的屏幕上,日期会移动到上面。 库存在所有三种情况中都分享结余、变动、收据和发行的样式。  通过共享打开相片 `UiImagePreview`;媒体编辑动作被隐藏在只读模式中.  在改变这些区域时,检查被居住列表,开放编辑器,字段保存,两个主题,以及宽度从320px到2560px.

 在库存中,“所有物品”、“低于最低限量”和“库存外”汇总控制过滤清单;其清点和库存价值适用于整个组织。 零余额与最低或低于最低的正余额分开计算。  在摘要下单行列出行动,右边为“接收”。 搜索会自动刷新列表 。 手头数量、可用数量、保留数量、单位成本和库存价值是分开的;保留库存目前显示为总数,与工作订单没有联系。 平和分页大小为下. 批量发行有自己的搜索和项目页面,独立于平衡过滤器. 接收和发放用户返回到先前的列表条件.  在窄屏幕行上显示字段标签;两个主题都得到支持.

下页 `/personal`、工作空间、密码和账户删除使用 `/Views/Personal/_PersonalLayout.cshtml`.  此服务器发送的部分从组织页面重用调色板和主题切换器(`organizations-entry.css`, `organizations-entry.js`),而它自己的网格、菜单、卡片和表格则被隔离 `.personal-page` 内 `personal.css`.  在电话上,菜单成为了两栏的网格. 表格保留服务器验证和请求-伪造保护;样式不更改密码或账户管理. 简介中没有个人订阅;计划由组织工作空间管理。 旧的 `/personal/subscription` URL 重定向到 `/organizations`.

个人账户外壳被限制在 1600 px,其内容被限制在 1280 px,所以设置不会伸展到宽显示器之间.

个人车辆(`/vehicles`、卡片、编辑器、相片、相册和日记) `_PersonalLayout.cshtml`. 页面提供自己的面包屑通过 `Breadcrumbs` 区域及依附关系 `Styles` 和 `Scripts`. `personal-vehicles.css` 仅在 `PersonalVehiclePage` 设定范围为: `.personal-vehicles`;组织网页没有收到。 列表为初始个人车辆视图,所选视图被保存在浏览器中. 共享网站样式仍在装入 `_Layout.cshtml`:视觉隔离并不意味着CSS捆绑完全分离.

个人车辆卡、创建、编辑和服务历史也使用 `_PersonalLayout.cshtml` 和 `personal-vehicles.css`. 历史是共享服务器视图: 个人 shell 仅当 `organizationId` 各组织则保留其现有的空壳。  收到共同意见 `BreadcrumbLinks`; 单独的页面继续使用 `Breadcrumbs` 节。  这隔离了WebApp内部的视图和样式;它不是一个单独的客户端应用程序.

 在个人相片管理器中,打开一幅相片并对其进行管理是单独的动作:相片是一个按钮,封面选择和删除在下方. 日历字段、列表箭头和相片卡被限制到 `.personal-vehicles`.

共用组织车辆卡 `VehicleCardSection.vue` 在所有三种情景中。 顶部显示照片, 制作标志来自 `brandLogoKey`,注册号,以及关键细节;文档分别扩充. 服务历史支持状态过滤,一次装入4个记录. 活工进度计已完工工作,不计材料;如无进度,栏杆会被隐藏. 侧栏显示一个链接的客户或部门概况,最多有3个当前维护记录,并链接到其余部分. 未付和支付的数额来自工作订单数据。 相册和更改历史保留了现有的路线. 编辑和归档不可用只读模式;节链接尊重组织菜单.

带有测试 API 的本地卡预览 : `npm run preview:visualization`,则 `/tests/vehicle-card-preview.html#vehicles/1`. `scenario=detailing|autoservice|fleet` 选择情景, `readonly` 允许只读模式,以及 `empty`  删除历史和媒体。 测试数据不写入服务器 。 进行逻辑检查 `src/OpenCRM.Frontend/tests/vehicleCard.test.mjs`; 运行所有客户端测试 `node --test tests/*.test.mjs`.

 在详细安排、汽车服务和车队的任用中,“Bays”显示垂直时间和横向湾。 指定颜色代表状态;标志使用目录中的制作密钥. 搜索和工位过滤是可用的,没有时间的记录仍保留在单独的列表中. 现有的槽、列表和日历模式依然存在。

 在详细化和自动服务中,从日网格创建时会以所选的日期,时间,和湾来打开日历上现有的快表. 保存刷新当前日期而不打开卡片 。 出错出现在模式中;可用性和权限检查使用当前的API. 舰队打开其现有的完整维护表格.

“Calendar”在相同大小的月份单元格中出现最多两个紧凑的预约。 “+N ” 以可搜索、可过滤的模式开放了当天的所有预约;它为当天的工位视图和任命创建提供了导航。 单击日期或选择日期时使用 Enter 或 Space 打开同一天的模式;日历下方的列表被删除。 周和日历视图显示带有时间和车辆的紧凑色块;颜色表示状态。 日湾电网也省略了注册号码,服务和员工. 常规点击打开一个共享预约预览,其中包含全程服务,注册号,起止(包括没有时间的多日预约),工位,员工,以及"开放维护"按钮. 重排时程仍然是单独的动作; Ctrl/Cmd- 点击保持链接导航. 周视图同时显示两个相重叠的块;其他块则通过“+N”提供。日历模式是月份、周和日。“周列表”被删除,因为已经存在单独的“列表”模式;以前保存的周列表首选打开了周视图。 检查 : `src/OpenCRM.Frontend/tests/schedulerPreview.test.mjs`.  所有 FullCalendar 视图都使用共享模式切换器,两个主题,以及移动布局.

“名单”组按日期在明确选定的“从......到......”范围进行任命,并支持搜索加工位和州过滤器。  在500个记录的服务器限制下,一个警告说这一时间段是不完整的。 “槽”提供了特定时间段内每个工位可用的间隔,同时考虑开放时间、休息时间和主动预约时间。 所选的日期,湾和间隔被传递到现有的创建格式;拥有只读访问的用户无法创建.

客户卡使用共享 `ClientCardSection.vue`:联系人和笔记,下一个计划任命,最多4辆车,以及最近与搜索和状态过滤有关的工作. `ClientVehicles.vue`  在模式中打开完整的清单,并进行名称/登记搜索和内部滚动;对选定车辆的新指定使用现有路线。 动作尊重快速行动权限,分区访问,归档状态,以及只读模式. 徽标出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自自自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自出自自出自出自出自出自出自出自自出自出自自出自出自出自出自出自 支付总额包括客户在组织中购买的所有工作,不包括被取消的支付;新的财务总额只有在财务查看/管理许可的情况下才能返还.  此表的历史仍限于最新的8个API记录;过滤器适用于该套,总体计数单独显示. 下一次任命是独立于这8名人员的,采用组织时区和当前时间。

本地预览 : `/tests/client-card-preview.html#clients/client-1`; 参数包括 `vehicles=50`, `scenario=detailing|autoservice|fleet`, `readonly`, `empty`, `archived`, `restricted`,以及 `error`.  这些是测试 API 响应; 预览更改不保存到服务器 。 客户端逻辑检查 : `node --test tests/clientCard.test.mjs`.

使用现有的客户卡车辆照片 `photoUrl`  在牌和完整列表中。  如果没有照片,则显示制作标志;在负载失败时,同样的倒计时符会用无法取取取的相片标记出现.  最后的工作日期被从瓷砖中去掉,因为历史出现在下面. 左栏分为与电话复制动作的联系细节,笔记,以及下个预约;作者和日期不是从共享文本字段中发明的.

共用组织车辆创建/编辑表 `VehicleFormSection.vue`:主要细节,身份,可扩展文件,所有者,和描述. 输入字段时更新摘要 。  在编辑时,可以切换并扩展现有的相片 `UiImagePreview`;照片管理在保存未保存输入的同时,会在单独的标签上打开.  新表单没有空相块 。 相片加载错误不会阻断编辑 。  布局适应内容宽度,并带有容器查询。 预览 : `/tests/vehicle-card-preview.html?form#vehicles/1/edit`; 为客户创建 : `#vehicles/new/client-1`; `save-error` 模拟保存错误而不写入服务器 。 逻辑检查 : `node --test tests/vehicleForm.test.mjs`.

编辑在 `ClientCardSection.vue` 将联系人和税务细节,客户和团队的单独评论,状态,以及管理员/受委托人结合起来. 个人的新配置文件默认值 。 类型切换器只显示各组织的税收细节;储蓄合同不变. 标记用逗号或 Enter 添加并单独去除;未确认的文本包含在保存上 。 形式适应内容宽度. 预览 `/tests/client-card-preview.html?company#clients/client-1` 打开一个组织; 编辑器使用“ Edit” ; `#clients/new` 用于创造。 `save-error` 模拟失败而不丢失输入的数据。 检查 : `node --test tests/clientCard.test.mjs`.

服务选择 `MaintenanceServicesEditor.vue` 使用宽模式,按名称和捆绑内容进行搜索,可扩展捆绑细节,以及单独的选定服务列表.  总价为服务总和;未知价格或持续时间不作"零"处理. 现有服务不重复;明确选定的全套服务将持续时间转到现有计算。 重叠的捆绑仍然被选中:共享服务一旦添加,并在列表中标出重复.  对于这种捆绑,持续时间从独特的服务中计算,而不应用相冲突的捆绑期限。  删除一个捆绑可以保存其他捆绑的服务并单独选定工作。 `MaintenancePayments.vue` 显示付款表、摘要和最近的三次付款; 完整历史和有必要理由的付款倒置仍然存在。 历史错误不会阻止记录付款 。 提交时不能更改模式;失败的重试保留了操作ID.  这一记录收到了付款;它不自动收取或退还款项。 本地预览 : `/tests/maintenance-card-preview.html?commerce#maintenance/1`; `save-error` 和 `history-error` 模拟失败。  所有更改均保留在页面内存中. 检查 : `node --test tests/serviceSelection.test.mjs tests/maintenancePayments.test.mjs`.

共享目录 `WorkspaceCatalogSection.vue`:服务,捆绑,工位/盒子,以及材料都有特定的创建/编辑标题,按名称和描述搜索,状态,以及共享取景器. Bundle 成分支持服务搜索,在过滤时保留选择,每行价格和持续时间,以及一个总数. 空包期限是指服务期限的总和;明确的期限优先. 未知值不作为零处理;选定的存档服务仍然可见并带有警告. 保留旧物品的节约合同和限制。  目录编辑不重写历史工作顺序行. 预览 : `/tests/catalog-preview.html#catalog?catalog=service_bundle`; 类型 `service`, `work_post`, `material`参数 `many`, `empty`, `readonly`, `save-error`, `archived-service`, `scenario=detailing|autoservice|fleet`. 测试更改在页面内存中仍然保留. 逻辑检查 : `node --test tests/catalogEditor.test.mjs`.

团队使用一个列表 `WorkspaceEmployeesSection.vue`:雇员、工作分配、客户关系管理注册地位和作用。  没有账户的简介,链接的员工,以及待发邀请书,都使用现有的链接进行合并,不重复.  所有雇员,包括不活跃的雇员,都可以在名单和过滤器中找到。 添加表单有名称,电话,独立的"可以分配工作"旗号,并通过邀请或账户链接可选择访问.  可以邀请但不能管理员工的用户只看到允许的邀请字段.

本地预览 : `/tests/team-preview.html#employees`; `scenario=detailing|autoservice|fleet`, `readonly`,以及 `save-error` 选择方案,只读状态,并保存失败。  请柬和编辑只出现在测试 API 内存中. 处理器检查 : `node --test tests/teamEditor.test.mjs`.

角色权限使用共享 `WorkspaceRolePermissions.vue`:从 API 中选择一个角色,加载组,按名称/描述/分区搜索,并显示更改的许可数.  在编辑时,选择另一个角色被禁用;无论搜索与否,都保存提交选定角色的完整草稿并保持该角色的开放. 取消不发送请求; 重新编辑恢复已保存的权限 。 所需系统作用 `workspace.view`,只读模式,以及飞行中的请求都受到保护,不进行编辑。 移动权限依赖性仍保留在现有处理器中 。 检查 : `src/OpenCRM.Frontend/tests/rolePermissions.test.mjs`; 本地预览 `/tests/team-preview.html#members`.

不再有单独的成员名单或表格。 `#members`;现在仅是“职责和权限”。 角色变化,取消邀请,去除客户关系管理访问,以及所有权转让都在员工配置中;有风险的行动需要确认并保留现有的服务器检查. 雇员数据和选定的现有账户链接用一个“保存更改”按钮保存。 选择一个账户可以启用此按钮; 没有单独的链接确认 。 在一个服务器交易中运行的并行配置和链接更改与已有的权限和版本检查. 角色分配和发出邀请仍然是明确的单独行动。  在出错时,输入的数据会保留在表格中,提交会防止重复行动.

 在团队模式表格中, `UiSelect` 用途 `appendTo` 和它自己的 `dialog` 选择器:列表被移动到滚动正文之外,在字段上打开,不改变形式维度. 库中的 Popper 计算位置和宽度; 移动的列表保留光和暗调色板 `ui-select-options`.  在这些表格之外选择保留其上一个位置。

现有工作可用性 `OrganizationEmployee.IsActive`; 解除其资格并不取消客户关系管理的成员资格。 历史不活跃的剖面仍然不活动,新的任务和班次继续检查旗帜. 以邀请函或现有账户的方式创建雇员的原子作业得到接受; `IsActive`; 未输入字段的旧请求收到 `true`.  对于无法被指派工作的雇员,也提供邀请和接受;组织、许可和独特联系的限制依然存在。 不需要数据迁移。 服务器检查在 `OrganizationEmployeeForMemberTests` 和 `OrganizationInvitationEmployeeLinkTests`.

使用共享的组织设置 `WorkspaceSettingsSection.vue`:一般详情、税务/联系信息、工作和时间表、文件、通知、进口、出口和档案。  在电话中,边菜单变成共享 `UiSelect`. 所选区域存储于 `#settings?tab=…`; 共享部分之间的直接链接和导航保持同步. 概况和税务表保留了现有的API合同;在保留选定的部分之后,仍然开放,如果失败,则保留草案。  在编辑时,部分更改被禁用,直到保存或取消.

“通知”标签打开一个设置列表 `WorkspaceNotificationSettings.vue`:预约-提醒自动化和Telegram/MAX频道。 选择项目仅显示其形式;“所有通知”返回清单。  在第一次选择时填入表单,在切换时保留草稿,在组织更改时重置. 保存时导航被屏蔽 。 电报和MAX份额 `WorkspaceNotificationChannel.vue`:每个频道分别配置连接、启用通知和任命/准备活动。  从 API 响应中从未填入一个符号, 并且成功保存后清除 。  所有者权限,只读模式,无法使用的频道,以及重试分别处理. 进出口部门保留自己的准入检查;档案使用现有的确认。

“工作和时间表”的使用 `WorkspaceSettingsChoices.vue` “简单”和“分层”模式有解释。 排程通过内线打开 `OrganizationWorkingHoursSettings.vue`,没有第二个对话框;其旧模式形式在别处仍然可用。 保存发送上一个修订并验证间隔;一个错误保留了草稿 。 切换分页被屏蔽直到保存或取消;返回在保存时也被禁用. 权限和只读模式仍然有效。 检查 : `organizationSettings.test.mjs`, `organizationWorkingHoursSettings.test.mjs`,以及 `working-hours.test.mjs`;浏览器检查覆盖了内在形式,既包括主题,也包括宽度为320/390 px.

详细介绍和自动服务重复访问是雇员从移交工作命令中补充的可选建议。 交接不自动创建一个,不需要填入. `RepeatVisitRecommendations.vue`  在客户卡上除历史之外显示推荐内容。  在工作订单卡中,用一个计数的“建议”按钮替换一个单独的块:列表以模式打开,同时创建/编辑它打开一个单独的对话框并返回列表. 推荐书储存服务、理由、拟议日期、说明和设定的任用。 下一个访问日期和客户接触到期日的含义不同。 “提醒我与客户联系”默认取消;如果启用,则需要单独的“在...之前联系客户”最后期限。 被指派者是可选的:没有指定者,该行动由雇员分担,他们可以接触客户、车辆和保养;如果有指定者,则成为个人行动。 工作日时间 `WorkdayRepeatVisitActions.vue` 显示所选日期或逾期日到期的共享和当前用户动作,使用棋盘和模式列表上方的计数器。 任何被允许记录动作的员工可以完成或重新安排一个共享动作;只读模式和组织界限依然被强制. 推荐编辑器是标准日期/时间拾取器的一种模式;字段出错会被突出显示并清晰显示在保存旁. 完成联系保留了建议;重新安排只更改到期日。 自动化的客户邀请已超出范围。

建议将相关内容储存在 `OrganizationRepeatVisits`,带有组织,客户,车辆,源工作秩序等外国钥匙,并创建了工作秩序. 更改使用一个版本和行锁;用同一ID重试创建并不重复推荐。“创造任命”将现有的假想表与已知的客户和车辆打开;确认创建并链接在一个交易中。 复次确认还原了现有的任命.  这是一次商定的下一次访问,而不是按里程安排的维修。 移徙 `20261024165338_AddOrganizationRepeatVisits` 增加一个单独的表格; `Down` 取消建议以及与新任命的联系,但保留工作订单本身。 只有在从备份中滚回后,才能恢复建议数据。

设置预览 : `/tests/settings-preview.html#settings`参数 `scenario=detailing|autoservice|fleet`, `readonly`, `employee`, `no-import`, `unavailable`, `load-error`, `save-error`. 测试保存保留在页面内存中 。 检查 : `src/OpenCRM.Frontend/tests/organizationSettings.test.mjs` 和 `src/OpenCRM.Frontend/tests/notificationChannel.test.mjs`.

客户、车辆和保养清单使用紧凑的表格,这些表格成为窄屏幕上贴有标签的块。 搜索,过滤器,页面按用户和组织分别恢复;切换组织清除未保存状态. 客户过滤器包括类型、受让人、状态、标记和分类。 支付总额与客户卡相匹配并包括有效支付;债务使用当期工作订单余额,如"财务"中. 这两笔款额均需 `FinanceView`  或者说 `FinanceManage`.

 在车辆列表中,上传相片优先于所制作的相片;相片负载出错回了相片. VIN以小文本出现在注册号下,仍可被搜索.  当前的工作标题、准备状态和进度按每页一批装入并隐藏,不包含 `MaintenanceView`. 进度使用当前服务行并排除了材料和被删除的操作.  在多次任命后,积极工作优先,然后做好移交准备,然后是下一个计划任用。

共享 `MaintenanceJournalSection.vue` 使用方式包括详细信息、汽车服务和车队。  该日记有服务器侧过滤器,按状态,日期范围,工作命令或服务受让人,以及文本. 工作构成不断扩大,显示了工人和价格;缺价并不意味着零成本。 只有在实际工作顺序号码存在时才显示数字。 付款通过服务器财务检查加以保护;维护构成和金额保留了现有的访问合同。 自动服务和机队在单独的标签上保留先前的事件和日历;命令列表不会取代事件历史.

三个列表的预览 : `/tests/directories-preview.html#clients`, `#vehicles`,以及 `#work-journal`参数 `scenario=detailing|autoservice|fleet`, `readonly`, `no-finance`, `no-maintenance`, `empty`,以及 `error`.  这些是测试数据,不会改变工作组织。 处理器检查 : `clientsSection.test.mjs`, `vehiclesSection.test.mjs`,以及 `maintenanceJournal.test.mjs`; 服务器假想 : `ClientQueryTests`, `VehicleQueryServiceTests`,以及 `MaintenanceJournalTests`. 整合检查需要PostgreSQL通过多克.

初等网形和模式头使用左侧有窄橙口音的淡桃花表面;内侧标题使用中性灰色表面. 黑暗主题保留了两个关卡. 共享 `ui-form-header` 和 `ui-section-header` 类使用 `--crm-form-header-*`, `--crm-section-header-*`,以及 `--crm-header-accent`; `UiCard page` 和 `UiModal` 选择适当的级别。 内部面板没有第二个主页头 。 表格行标题、 单项列表项和空状态不需要边框 。

网络管理员和服务器上的个人账户在自己的贝壳中使用相同的分级. 内置面板内容使用 `--crm-surface`:以光为主题为白色,以暗模式为主题表面. 灰色 `--crm-section-header-bg` 突出标题,而不是整个形式正文。 客户卡中的车辆为紧凑行;其他动作和数据仍保留.  见 [执行和检查](form-headers.zh-CN.md).

时区通过“Organization sets → General”选择,并单独保存. Shifts 显示当前值并链接到共享设置 。 工作日和日历过滤器使用组织的当天,协调世界时的活动显示在其时区;计划任命的日期和时间仍然是日历值.  如果未配置区域,则使用世界协调时。 共享终点 `/api/v1/organizations/{id}/time-zone` 检查组织访问, `WorkspaceProfileEdit`,写入模式,以及上一个设置。 遗留的转向端点仍然保留,并使用相同的编辑-许可检查.

“我的工作”使用共享的下一班摘要和两个计数器、合并过滤器以及选定列表的空状态。  在电话上,分班需要一整行,计数器会停留在两列内,过滤器会均匀地包裹.  在背景刷新期间,会保留转会细节,直到出现新的响应,因此汇总不会每15秒改变一次高度. 业务过渡、权限和业务规则仍保留在现有服务中。

<a name="local-help-for-pro-features"></a>

## 本地对 PRO 特性的帮助

增加了“视频特性”按钮。 `WorkspaceProLock` 低于访问状态。  每个区段和付费设置都有自己在静态中的钥匙 `proFeatureDetails.ts` 目录; `WorkspaceProFeatureDialog` 显示相关的动作和以演示标记的插图。 导航到计划是由组织许可和通过共享提供/注入在服务器上下文中计费的能见度决定的,不改变权限或调用付费API. 未经许可,该描述仍然可用,但支付链接被隐藏.

本地对话框支持 Escape, Close, Tab/Shift+Tab 陷阱,焦点返回,滚动状态保存,光/暗主题,以及手机的内部滚动. 预览 : `/tests/free-pro-preview.html#employees`; 雇员视图 : `?member#employees`; 个人特性 : `?feature=albums#feature`.  这些URL是用于当地Vite的吊带. Free上的嵌入式助手使用相同的格式;没有更改单独的Razor视图. 库存、维基和普通车辆相片仍按现行自由规则提供。

<a name="accessible-date-and-time-fields"></a>

## 可访问的日期和时间字段

共享 `UiDatePicker` 和 `UiTimePicker` 被精炼:日期字段有一个独立于开放日历内容的可访问名称,而月变化按钮通过标准v-日历槽来命名. 时间字段与其标签相关,通过 `UiFieldLabel.forId`; 如果没有自定义 `id` 提供稳定的 `useId` 已使用。 价值模型、本地化、限制和只读检查都保留下来。

更改时 `UiDatePicker` 包装,说明v-日历 3.1.2 对焦点的生命周期依赖:移入外层 `div` 已曝光 `dayIndex` 选择日期后关闭时。 最终的改动保留了原来的包装并使用ARIA. 复制、选项比较和核查限度记录在接口审查记录中。

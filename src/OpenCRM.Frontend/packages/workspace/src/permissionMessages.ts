import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  "ru": {
    "rolePermissions": {
      "general": "Общие права",
      "title": "Права ролей",
      "hint": "Настройки применяются ко всем сотрудникам с выбранной ролью.",
      "chooseRole": "Выбор роли",
      "rolePermissions": "Права роли",
      "configure": "Настроить права",
      "search": "Поиск прав",
      "searchPlaceholder": "Найти право или раздел…",
      "bookings": "Записи",
      "clients": "Клиенты",
      "vehicles": "Автомобили",
      "noRoles": "Роли не найдены.",
      "editingHint": "Сохраните или отмените изменения перед выбором другой роли.",
      "systemRole": "Системная роль",
      "readOnly": "Только просмотр",
      "noResults": "По вашему запросу ничего не найдено.",
      "required": "Обязательное право для входа в рабочее пространство",
      "allowed": "Разрешено",
      "denied": "Нет доступа",
      "noChanges": "Нет изменений",
      "cancel": "Отмена",
      "saving": "Сохраняем…",
      "save": "Сохранить права",
      "count": "{count} из {total} прав",
      "enabledCount": "{count} из {total} прав включено",
      "changed": "Изменено прав: {count}",
      "backToTeam": "← Команда",
      "entries": {
        "workspace_view": {
          "name": "Просмотр рабочего пространства",
          "description": "Открывать разделы организации.",
          "group": "Организация"
        },
        "workspace_catalog_view": {
          "name": "Просмотр справочников",
          "description": "Видеть услуги, посты, материалы и другие справочники организации.",
          "group": "Организация"
        },
        "workspace_profile_edit": {
          "name": "Изменение профиля организации",
          "description": "Менять название, описание и тип организации.",
          "group": "Организация"
        },
        "workspace_requisites_edit": {
          "name": "Изменение реквизитов организации",
          "description": "Менять юридические реквизиты и контакты.",
          "group": "Организация"
        },
        "workspace_archive": {
          "name": "Архивация организации",
          "description": "Отправлять организацию в архив и восстанавливать ее.",
          "group": "Организация"
        },
        "billing_manage": {
          "name": "Управление тарифом",
          "description": "Покупать, продлевать и активировать тариф организации.",
          "group": "Организация"
        },
        "employees_manage": {
          "name": "Управление сотрудниками",
          "description": "Добавлять, редактировать и архивировать сотрудников без изменения доступа пользователей.",
          "group": "Команда"
        },
        "shifts_view": {
          "name": "Просмотр смен",
          "description": "Видеть график всей команды.",
          "group": "Смены"
        },
        "shifts_manage": {
          "name": "Управление сменами",
          "description": "Планировать, менять и публиковать смены.",
          "group": "Смены"
        },
        "shifts_self_view": {
          "name": "Мои смены",
          "description": "Видеть собственные опубликованные смены.",
          "group": "Смены"
        },
        "members_view": {
          "name": "Просмотр участников",
          "description": "Видеть участников и приглашения.",
          "group": "Команда"
        },
        "members_invite": {
          "name": "Приглашение участников",
          "description": "Приглашать пользователей в CRM.",
          "group": "Команда"
        },
        "members_roles_manage": {
          "name": "Управление ролями участников",
          "description": "Менять роли пользователей.",
          "group": "Команда"
        },
        "members_remove": {
          "name": "Удаление участников",
          "description": "Удалять доступ пользователей к организации.",
          "group": "Команда"
        },
        "clients_view": {
          "name": "Просмотр клиентов",
          "description": "Видеть список и карточки клиентов.",
          "group": "Клиенты"
        },
        "clients_create": {
          "name": "Создание клиентов",
          "description": "Добавлять клиентов.",
          "group": "Клиенты"
        },
        "clients_edit": {
          "name": "Редактирование клиентов",
          "description": "Менять карточки клиентов, статусы, ответственных и теги.",
          "group": "Клиенты"
        },
        "clients_archive": {
          "name": "Архивация клиентов",
          "description": "Отправлять клиентов в архив и восстанавливать их.",
          "group": "Клиенты"
        },
        "clients_vehicles_manage": {
          "name": "Управление техникой клиента",
          "description": "Привязывать, отвязывать и переносить технику между клиентами.",
          "group": "Клиенты"
        },
        "messages_templates_manage": {
          "name": "Управление шаблонами сообщений",
          "description": "Создавать, изменять и архивировать группы и шаблоны ответов организации.",
          "group": "Сообщения"
        },
        "messages_view": {
          "name": "Просмотр переписки",
          "description": "Открывать диалоги организации и отмечать прочитанные сообщения.",
          "group": "Сообщения"
        },
        "messages_send": {
          "name": "Отправка сообщений",
          "description": "Отвечать клиентам от имени организации.",
          "group": "Сообщения"
        },
        "messages_manage": {
          "name": "Связь диалогов с клиентами",
          "description": "Связывать диалоги с карточками клиентов.",
          "group": "Сообщения"
        },
        "inquiries_view": {
          "name": "Просмотр обращений",
          "description": "Видеть обращения клиентов до записи.",
          "group": "Клиенты"
        },
        "inquiries_manage": {
          "name": "Управление обращениями",
          "description": "Создавать и изменять обращения, действия и этапы.",
          "group": "Клиенты"
        },
        "vehicles_view": {
          "name": "Просмотр техники",
          "description": "Видеть технику организации.",
          "group": "Техника"
        },
        "vehicles_create": {
          "name": "Создание техники",
          "description": "Добавлять технику в организацию.",
          "group": "Техника"
        },
        "vehicles_edit": {
          "name": "Редактирование техники",
          "description": "Менять карточки техники.",
          "group": "Техника"
        },
        "vehicles_archive": {
          "name": "Архивация техники",
          "description": "Отправлять технику в архив и восстанавливать ее.",
          "group": "Техника"
        },
        "vehicles_counters_manage": {
          "name": "Управление пробегом и наработкой",
          "description": "Добавлять и удалять записи пробега или моточасов.",
          "group": "Техника"
        },
        "maintenance_view": {
          "name": "Просмотр обслуживаний",
          "description": "Видеть обслуживание и историю работ.",
          "group": "Обслуживание"
        },
        "maintenance_create": {
          "name": "Создание обслуживаний",
          "description": "Добавлять обслуживание и работы.",
          "group": "Обслуживание"
        },
        "maintenance_edit": {
          "name": "Редактирование обслуживаний",
          "description": "Менять обслуживание, workflow, даты и состав работ.",
          "group": "Обслуживание"
        },
        "maintenance_delete": {
          "name": "Удаление обслуживаний",
          "description": "Удалять обслуживание.",
          "group": "Обслуживание"
        },
        "maintenance_documents_manage": {
          "name": "Управление документами обслуживания",
          "description": "Загружать и удалять документы обслуживания.",
          "group": "Обслуживание"
        },
        "maintenance_work_view": {
          "name": "Мои работы",
          "description": "Видеть назначенные операции.",
          "group": "Обслуживание"
        },
        "maintenance_work_execute": {
          "name": "Выполнение работ",
          "description": "Начинать, приостанавливать, продолжать и завершать назначенные операции.",
          "group": "Обслуживание"
        },
        "defects_view": {
          "name": "Просмотр неисправностей",
          "description": "Видеть неисправности.",
          "group": "Неисправности"
        },
        "defects_create": {
          "name": "Создание неисправностей",
          "description": "Добавлять неисправности.",
          "group": "Неисправности"
        },
        "defects_edit": {
          "name": "Редактирование неисправностей",
          "description": "Менять описание, важность и технику неисправности.",
          "group": "Неисправности"
        },
        "defects_delete": {
          "name": "Удаление неисправностей",
          "description": "Удалять неисправности.",
          "group": "Неисправности"
        },
        "defects_status_manage": {
          "name": "Изменение статуса неисправностей",
          "description": "Переводить неисправности в работу, откладывать и закрывать.",
          "group": "Неисправности"
        },
        "defects_maintenance_link": {
          "name": "Связь неисправностей с обслуживанием",
          "description": "Привязывать и отвязывать обслуживания от неисправностей.",
          "group": "Неисправности"
        },
        "scheduler_view": {
          "name": "Просмотр планировщика",
          "description": "Видеть календарь и список задач.",
          "group": "Планировщик"
        },
        "scheduler_manage": {
          "name": "Управление планировщиком",
          "description": "Менять статусы задач, переносить и закрывать задачи.",
          "group": "Планировщик"
        },
        "counters_view": {
          "name": "Просмотр счетчиков",
          "description": "Видеть пробег, моточасы, историю и графики.",
          "group": "Счетчики"
        },
        "counters_manage": {
          "name": "Управление счетчиками",
          "description": "Добавлять, менять и удалять записи счетчиков.",
          "group": "Счетчики"
        },
        "materials_view": {
          "name": "Просмотр склада",
          "description": "Видеть склад, остатки и движения материалов.",
          "group": "Материалы"
        },
        "materials_manage": {
          "name": "Управление складом",
          "description": "Добавлять поступления, списания и корректировки материалов.",
          "group": "Материалы"
        },
        "work_journal_view": {
          "name": "Просмотр журнала работ",
          "description": "Видеть закрытые работы, события обслуживания и историю изменений.",
          "group": "Журнал работ"
        },
        "finance_view": {
          "name": "Просмотр финансов",
          "description": "Видеть выручку, долги, себестоимость, прибыль, графики и заказы финансового раздела.",
          "group": "Финансы"
        },
        "finance_manage": {
          "name": "Полный доступ к финансам",
          "description": "Управлять финансовыми действиями, оплатами и будущими настройками финансового раздела.",
          "group": "Финансы"
        },
        "analytics_view": {
          "name": "Просмотр статистики и аналитики",
          "description": "Открывает раздел «Статистика» и аналитические блоки рабочего стола: KPI, графики, отчеты, выручку и сводные показатели.",
          "group": "Статистика"
        },
        "documents_export": {
          "name": "Экспорт документов",
          "description": "Формировать PDF и Word документы.",
          "group": "Документы"
        },
        "wiki_view": {
          "name": "Просмотр wiki",
          "description": "Открывать внутреннюю wiki организации.",
          "group": "Wiki"
        }
      }
    }
  },
  "en": {
    "rolePermissions": {
      "general": "General permissions",
      "title": "Role permissions",
      "hint": "Settings apply to every employee with the selected role.",
      "chooseRole": "Select a role",
      "rolePermissions": "Role permissions",
      "configure": "Configure permissions",
      "search": "Search permissions",
      "searchPlaceholder": "Find a permission or section…",
      "bookings": "Bookings",
      "clients": "Clients",
      "vehicles": "Vehicles",
      "noRoles": "No roles found.",
      "editingHint": "Save or cancel changes before selecting another role.",
      "systemRole": "System role",
      "readOnly": "View only",
      "noResults": "No matches found.",
      "required": "Required to enter the workspace",
      "allowed": "Allowed",
      "denied": "No access",
      "noChanges": "No changes",
      "cancel": "Cancel",
      "saving": "Saving…",
      "save": "Save permissions",
      "count": "Permissions: {count} of {total}",
      "enabledCount": "Enabled permissions: {count} of {total}",
      "changed": "Permissions changed: {count}",
      "backToTeam": "← Team",
      "entries": {
        "workspace_view": {
          "name": "View workspace",
          "description": "Open organization sections.",
          "group": "Organization"
        },
        "workspace_catalog_view": {
          "name": "View catalogs",
          "description": "View the organization’s services, bays, materials and other catalogs.",
          "group": "Organization"
        },
        "workspace_profile_edit": {
          "name": "Edit organization profile",
          "description": "Change the organization’s name, description and type.",
          "group": "Organization"
        },
        "workspace_requisites_edit": {
          "name": "Edit organization details",
          "description": "Change legal details and contacts.",
          "group": "Organization"
        },
        "workspace_archive": {
          "name": "Archive organization",
          "description": "Archive and restore the organization.",
          "group": "Organization"
        },
        "billing_manage": {
          "name": "Manage plan",
          "description": "Buy, renew and activate the organization’s plan.",
          "group": "Organization"
        },
        "employees_manage": {
          "name": "Manage employees",
          "description": "Add, edit and archive employees without changing user access.",
          "group": "Team"
        },
        "shifts_view": {
          "name": "View shifts",
          "description": "View the whole team’s schedule.",
          "group": "Shifts"
        },
        "shifts_manage": {
          "name": "Manage shifts",
          "description": "Plan, edit and publish shifts.",
          "group": "Shifts"
        },
        "shifts_self_view": {
          "name": "My shifts",
          "description": "View your own published shifts.",
          "group": "Shifts"
        },
        "members_view": {
          "name": "View members",
          "description": "View members and invitations.",
          "group": "Team"
        },
        "members_invite": {
          "name": "Invite members",
          "description": "Invite users to CRM.",
          "group": "Team"
        },
        "members_roles_manage": {
          "name": "Manage member roles",
          "description": "Change user roles.",
          "group": "Team"
        },
        "members_remove": {
          "name": "Remove members",
          "description": "Remove users’ access to the organization.",
          "group": "Team"
        },
        "clients_view": {
          "name": "View clients",
          "description": "View the client list and profiles.",
          "group": "Clients"
        },
        "clients_create": {
          "name": "Create clients",
          "description": "Add clients.",
          "group": "Clients"
        },
        "clients_edit": {
          "name": "Edit clients",
          "description": "Change client profiles, statuses, assignees and tags.",
          "group": "Clients"
        },
        "clients_archive": {
          "name": "Archive clients",
          "description": "Archive and restore clients.",
          "group": "Clients"
        },
        "clients_vehicles_manage": {
          "name": "Manage client vehicles",
          "description": "Link, unlink and move vehicles between clients.",
          "group": "Clients"
        },
        "messages_templates_manage": {
          "name": "Manage message templates",
          "description": "Create, edit and archive the organization’s reply groups and templates.",
          "group": "Messages"
        },
        "messages_view": {
          "name": "View conversations",
          "description": "Open organization conversations and mark messages as read.",
          "group": "Messages"
        },
        "messages_send": {
          "name": "Send messages",
          "description": "Reply to clients on behalf of the organization.",
          "group": "Messages"
        },
        "messages_manage": {
          "name": "Link conversations to clients",
          "description": "Link conversations to client profiles.",
          "group": "Messages"
        },
        "inquiries_view": {
          "name": "View inquiries",
          "description": "View client inquiries before booking.",
          "group": "Clients"
        },
        "inquiries_manage": {
          "name": "Manage inquiries",
          "description": "Create and edit inquiries, actions and stages.",
          "group": "Clients"
        },
        "vehicles_view": {
          "name": "View vehicles",
          "description": "View the organization’s vehicles.",
          "group": "Vehicles"
        },
        "vehicles_create": {
          "name": "Create vehicles",
          "description": "Add vehicles to the organization.",
          "group": "Vehicles"
        },
        "vehicles_edit": {
          "name": "Edit vehicles",
          "description": "Change vehicle profiles.",
          "group": "Vehicles"
        },
        "vehicles_archive": {
          "name": "Archive vehicles",
          "description": "Archive and restore vehicles.",
          "group": "Vehicles"
        },
        "vehicles_counters_manage": {
          "name": "Manage mileage and operating hours",
          "description": "Add and delete mileage or operating-hour entries.",
          "group": "Vehicles"
        },
        "maintenance_view": {
          "name": "View maintenance",
          "description": "View maintenance and work history.",
          "group": "Maintenance"
        },
        "maintenance_create": {
          "name": "Create maintenance",
          "description": "Add maintenance and work.",
          "group": "Maintenance"
        },
        "maintenance_edit": {
          "name": "Edit maintenance",
          "description": "Change maintenance, workflow, dates and work items.",
          "group": "Maintenance"
        },
        "maintenance_delete": {
          "name": "Delete maintenance",
          "description": "Delete maintenance records.",
          "group": "Maintenance"
        },
        "maintenance_documents_manage": {
          "name": "Manage maintenance documents",
          "description": "Upload and delete maintenance documents.",
          "group": "Maintenance"
        },
        "maintenance_work_view": {
          "name": "My work",
          "description": "View assigned operations.",
          "group": "Maintenance"
        },
        "maintenance_work_execute": {
          "name": "Perform work",
          "description": "Start, pause, resume and complete assigned operations.",
          "group": "Maintenance"
        },
        "defects_view": {
          "name": "View defects",
          "description": "View defects.",
          "group": "Defects"
        },
        "defects_create": {
          "name": "Create defects",
          "description": "Add defects.",
          "group": "Defects"
        },
        "defects_edit": {
          "name": "Edit defects",
          "description": "Change defect descriptions, severity and vehicles.",
          "group": "Defects"
        },
        "defects_delete": {
          "name": "Delete defects",
          "description": "Delete defects.",
          "group": "Defects"
        },
        "defects_status_manage": {
          "name": "Change defect status",
          "description": "Start work on defects, defer and close them.",
          "group": "Defects"
        },
        "defects_maintenance_link": {
          "name": "Link defects to maintenance",
          "description": "Link and unlink maintenance records and defects.",
          "group": "Defects"
        },
        "scheduler_view": {
          "name": "View scheduler",
          "description": "View the calendar and task list.",
          "group": "Scheduler"
        },
        "scheduler_manage": {
          "name": "Manage scheduler",
          "description": "Change task statuses, reschedule and close tasks.",
          "group": "Scheduler"
        },
        "counters_view": {
          "name": "View counters",
          "description": "View mileage, operating hours, history and charts.",
          "group": "Counters"
        },
        "counters_manage": {
          "name": "Manage counters",
          "description": "Add, edit and delete counter entries.",
          "group": "Counters"
        },
        "materials_view": {
          "name": "View inventory",
          "description": "View inventory, stock balances and material movements.",
          "group": "Materials"
        },
        "materials_manage": {
          "name": "Manage inventory",
          "description": "Add material receipts, issues and adjustments.",
          "group": "Materials"
        },
        "work_journal_view": {
          "name": "View work journal",
          "description": "View completed work, maintenance events and change history.",
          "group": "Work journal"
        },
        "finance_view": {
          "name": "View finances",
          "description": "View revenue, debts, costs, profit, charts and finance orders.",
          "group": "Finance"
        },
        "finance_manage": {
          "name": "Full finance access",
          "description": "Manage financial actions, payments and future finance settings.",
          "group": "Finance"
        },
        "analytics_view": {
          "name": "View statistics and analytics",
          "description": "Open statistics and dashboard analytics: KPIs, charts, reports, revenue and summaries.",
          "group": "Statistics"
        },
        "documents_export": {
          "name": "Export documents",
          "description": "Generate PDF and Word documents.",
          "group": "Documents"
        },
        "wiki_view": {
          "name": "View wiki",
          "description": "Open the organization’s internal wiki.",
          "group": "Wiki"
        }
      }
    }
  },
  "zh-Hans": {
    "rolePermissions": {
      "general": "通用权限",
      "title": "角色权限",
      "hint": "设置适用于具有所选角色的所有员工。",
      "chooseRole": "选择角色",
      "rolePermissions": "角色权限",
      "configure": "配置权限",
      "search": "搜索权限",
      "searchPlaceholder": "查找权限或功能区…",
      "bookings": "预约",
      "clients": "客户",
      "vehicles": "车辆",
      "noRoles": "未找到角色。",
      "editingHint": "选择其他角色前请保存或取消更改。",
      "systemRole": "系统角色",
      "readOnly": "仅查看",
      "noResults": "未找到匹配项。",
      "required": "进入工作区所必需的权限",
      "allowed": "已允许",
      "denied": "无访问权限",
      "noChanges": "无更改",
      "cancel": "取消",
      "saving": "保存中…",
      "save": "保存权限",
      "count": "权限：{count}/{total}",
      "enabledCount": "已启用权限：{count}/{total}",
      "changed": "已更改权限：{count}",
      "backToTeam": "← 返回团队",
      "entries": {
        "workspace_view": {
          "name": "查看工作区",
          "description": "打开组织各功能区。",
          "group": "组织"
        },
        "workspace_catalog_view": {
          "name": "查看目录",
          "description": "查看组织的服务、工位、材料及其他目录。",
          "group": "组织"
        },
        "workspace_profile_edit": {
          "name": "编辑组织资料",
          "description": "更改组织名称、描述和类型。",
          "group": "组织"
        },
        "workspace_requisites_edit": {
          "name": "编辑组织信息",
          "description": "更改法律信息和联系方式。",
          "group": "组织"
        },
        "workspace_archive": {
          "name": "归档组织",
          "description": "归档和恢复组织。",
          "group": "组织"
        },
        "billing_manage": {
          "name": "管理套餐",
          "description": "购买、续订和启用组织套餐。",
          "group": "组织"
        },
        "employees_manage": {
          "name": "管理员工",
          "description": "添加、编辑和归档员工，但不更改用户访问权限。",
          "group": "团队"
        },
        "shifts_view": {
          "name": "查看排班",
          "description": "查看整个团队的排班。",
          "group": "排班"
        },
        "shifts_manage": {
          "name": "管理排班",
          "description": "计划、修改和发布班次。",
          "group": "排班"
        },
        "shifts_self_view": {
          "name": "我的班次",
          "description": "查看自己已发布的班次。",
          "group": "排班"
        },
        "members_view": {
          "name": "查看成员",
          "description": "查看成员和邀请。",
          "group": "团队"
        },
        "members_invite": {
          "name": "邀请成员",
          "description": "邀请用户加入 CRM。",
          "group": "团队"
        },
        "members_roles_manage": {
          "name": "管理成员角色",
          "description": "更改用户角色。",
          "group": "团队"
        },
        "members_remove": {
          "name": "移除成员",
          "description": "取消用户对组织的访问权限。",
          "group": "团队"
        },
        "clients_view": {
          "name": "查看客户",
          "description": "查看客户列表和档案。",
          "group": "客户"
        },
        "clients_create": {
          "name": "创建客户",
          "description": "添加客户。",
          "group": "客户"
        },
        "clients_edit": {
          "name": "编辑客户",
          "description": "更改客户档案、状态、负责人和标签。",
          "group": "客户"
        },
        "clients_archive": {
          "name": "归档客户",
          "description": "归档和恢复客户。",
          "group": "客户"
        },
        "clients_vehicles_manage": {
          "name": "管理客户车辆",
          "description": "关联、取消关联并在客户间转移车辆。",
          "group": "客户"
        },
        "messages_templates_manage": {
          "name": "管理消息模板",
          "description": "创建、编辑和归档组织的回复分组与模板。",
          "group": "消息"
        },
        "messages_view": {
          "name": "查看会话",
          "description": "打开组织会话并将消息标记为已读。",
          "group": "消息"
        },
        "messages_send": {
          "name": "发送消息",
          "description": "代表组织回复客户。",
          "group": "消息"
        },
        "messages_manage": {
          "name": "关联会话与客户",
          "description": "将会话关联到客户档案。",
          "group": "消息"
        },
        "inquiries_view": {
          "name": "查看咨询",
          "description": "查看预约前的客户咨询。",
          "group": "客户"
        },
        "inquiries_manage": {
          "name": "管理咨询",
          "description": "创建和编辑咨询、操作与阶段。",
          "group": "客户"
        },
        "vehicles_view": {
          "name": "查看车辆",
          "description": "查看组织车辆。",
          "group": "车辆"
        },
        "vehicles_create": {
          "name": "创建车辆",
          "description": "向组织添加车辆。",
          "group": "车辆"
        },
        "vehicles_edit": {
          "name": "编辑车辆",
          "description": "更改车辆档案。",
          "group": "车辆"
        },
        "vehicles_archive": {
          "name": "归档车辆",
          "description": "归档和恢复车辆。",
          "group": "车辆"
        },
        "vehicles_counters_manage": {
          "name": "管理里程和运行小时",
          "description": "添加和删除里程或运行小时记录。",
          "group": "车辆"
        },
        "maintenance_view": {
          "name": "查看保养",
          "description": "查看保养和工作历史。",
          "group": "保养"
        },
        "maintenance_create": {
          "name": "创建保养",
          "description": "添加保养和工作。",
          "group": "保养"
        },
        "maintenance_edit": {
          "name": "编辑保养",
          "description": "更改保养、工作流程、日期和工作项目。",
          "group": "保养"
        },
        "maintenance_delete": {
          "name": "删除保养",
          "description": "删除保养记录。",
          "group": "保养"
        },
        "maintenance_documents_manage": {
          "name": "管理保养单据",
          "description": "上传和删除保养单据。",
          "group": "保养"
        },
        "maintenance_work_view": {
          "name": "我的工作",
          "description": "查看分配的操作。",
          "group": "保养"
        },
        "maintenance_work_execute": {
          "name": "执行工作",
          "description": "开始、暂停、继续和完成分配的操作。",
          "group": "保养"
        },
        "defects_view": {
          "name": "查看故障",
          "description": "查看故障。",
          "group": "故障"
        },
        "defects_create": {
          "name": "创建故障",
          "description": "添加故障。",
          "group": "故障"
        },
        "defects_edit": {
          "name": "编辑故障",
          "description": "更改故障描述、严重程度和车辆。",
          "group": "故障"
        },
        "defects_delete": {
          "name": "删除故障",
          "description": "删除故障。",
          "group": "故障"
        },
        "defects_status_manage": {
          "name": "更改故障状态",
          "description": "开始处理、推迟和关闭故障。",
          "group": "故障"
        },
        "defects_maintenance_link": {
          "name": "关联故障与保养",
          "description": "关联或取消关联保养记录与故障。",
          "group": "故障"
        },
        "scheduler_view": {
          "name": "查看计划",
          "description": "查看日历和任务列表。",
          "group": "计划"
        },
        "scheduler_manage": {
          "name": "管理计划",
          "description": "更改任务状态、改期和关闭任务。",
          "group": "计划"
        },
        "counters_view": {
          "name": "查看计数器",
          "description": "查看里程、运行小时、历史和图表。",
          "group": "计数器"
        },
        "counters_manage": {
          "name": "管理计数器",
          "description": "添加、编辑和删除计数器记录。",
          "group": "计数器"
        },
        "materials_view": {
          "name": "查看库存",
          "description": "查看库存、余额和材料变动。",
          "group": "材料"
        },
        "materials_manage": {
          "name": "管理库存",
          "description": "添加材料入库、出库和调整。",
          "group": "材料"
        },
        "work_journal_view": {
          "name": "查看工作日志",
          "description": "查看已完成的工作、保养事件和变更历史。",
          "group": "工作日志"
        },
        "finance_view": {
          "name": "查看财务",
          "description": "查看收入、欠款、成本、利润、图表和财务订单。",
          "group": "财务"
        },
        "finance_manage": {
          "name": "完整财务权限",
          "description": "管理财务操作、付款及未来的财务设置。",
          "group": "财务"
        },
        "analytics_view": {
          "name": "查看统计与分析",
          "description": "打开统计及仪表盘分析：指标、图表、报告、收入和汇总。",
          "group": "统计"
        },
        "documents_export": {
          "name": "导出单据",
          "description": "生成 PDF 和 Word 单据。",
          "group": "单据"
        },
        "wiki_view": {
          "name": "查看 Wiki",
          "description": "打开组织内部 Wiki。",
          "group": "Wiki"
        }
      }
    }
  }
} satisfies Record<UiLocale, unknown>

for (const [locale, localeMessages] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, localeMessages)
}

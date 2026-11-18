import { registerMessages } from '@opencrm/ui/i18n'

const workspaceChromeMessages = {
  ru: {
    workspaceChrome: {
      labels: {
        workspace: 'Рабочий день', visualization: 'Визуализация', scheduler: 'Записи', inquiries: 'Обращения',
        messages: 'Сообщения', clients: 'Клиенты', vehicles: 'Автомобили', maintenance: 'Заказ-наряды',
        defects: 'Неисправности', counters: 'Пробег и наработка', materials: 'Склад', 'work-journal': 'История работ',
        finance: 'Финансы', statistics: 'Статистика', shifts: 'Смены', employees: 'Сотрудники', members: 'Команда', 'my-work': 'Мои работы',
        settings: 'Настройки организации', billing: 'Тариф и оплата', wiki: 'База знаний', organization: 'Организация',
        catalog: 'Справочники', assistant: 'Помощник'
      },
      groups: { workspace: 'Рабочая область', management: 'Управление', data: 'Данные', operations: 'Работа', administration: 'Администрирование', reports: 'Отчёты', access: 'Справка и доступ' },
      skip: 'К содержимому', workspace: 'Рабочее пространство', help: 'Развернуть меню', collapse: 'Свернуть меню',
      support: 'Помощь и поддержка', openMenu: 'Открыть меню', notifications: 'Уведомления', noNotifications: 'Новых уведомлений нет',
      notificationHistory: 'Вся история уведомлений', close: 'Закрыть', profile: 'Профиль', organizations: 'Организации',
      lightTheme: 'Включить светлую тему', darkTheme: 'Включить тёмную тему', closeHistory: 'Закрыть историю',
      savedMessages: 'Сохранённая переписка', mobileNavigation: 'Навигация организации', closeMenu: 'Закрыть меню',
      availableInPro: 'Доступно в PRO', unavailable: 'Функция недоступна', pro: 'Доступно в PRO', unread: 'Непрочитанных: {count}',
      featureDisabled: 'Функция отключена в настройках установки', proUnavailable: 'Недоступно в этом тарифе',
      statisticsPro: 'Статистика доступна в PRO', employeesPro: 'Команда доступна в PRO', rolesPro: 'Роли и права доступны в PRO',
      shiftsPro: 'Смены доступны в PRO', messagesPro: 'Сообщения доступны в PRO', myWorkPro: 'Мои работы доступны в PRO',
      available: 'Доступно в PRO'
    }
  },
  en: {
    workspaceChrome: {
      labels: {
        workspace: 'Workday', visualization: 'Visualization', scheduler: 'Appointments', inquiries: 'Inquiries',
        messages: 'Messages', clients: 'Clients', vehicles: 'Vehicles', maintenance: 'Work orders',
        defects: 'Defects', counters: 'Mileage and usage', materials: 'Inventory', 'work-journal': 'Work history',
        finance: 'Finance', statistics: 'Analytics', shifts: 'Shifts', employees: 'Employees', members: 'Team', 'my-work': 'My work',
        settings: 'Organization settings', billing: 'Plan and billing', wiki: 'Knowledge base', organization: 'Organization',
        catalog: 'Directories', assistant: 'Assistant'
      },
      groups: { workspace: 'Workspace', management: 'Management', data: 'Data', operations: 'Operations', administration: 'Administration', reports: 'Reports', access: 'Help and access' },
      skip: 'Skip to content', workspace: 'Workspace', help: 'Expand menu', collapse: 'Collapse menu',
      support: 'Help and support', openMenu: 'Open menu', notifications: 'Notifications', noNotifications: 'No new notifications',
      notificationHistory: 'Notification history', close: 'Close', profile: 'Profile', organizations: 'Organizations',
      lightTheme: 'Switch to light theme', darkTheme: 'Switch to dark theme', closeHistory: 'Close history',
      savedMessages: 'Saved conversation', mobileNavigation: 'Organization navigation', closeMenu: 'Close menu',
      availableInPro: 'Available in PRO', unavailable: 'Feature unavailable', pro: 'Available in PRO', unread: 'Unread: {count}',
      featureDisabled: 'This feature is disabled in the installation settings', proUnavailable: 'Unavailable on this plan',
      statisticsPro: 'Analytics are available in PRO', employeesPro: 'Team is available in PRO', rolesPro: 'Roles and permissions are available in PRO',
      shiftsPro: 'Shifts are available in PRO', messagesPro: 'Messages are available in PRO', myWorkPro: 'My work is available in PRO',
      available: 'Available in PRO'
    }
  },
  'zh-Hans': {
    workspaceChrome: {
      labels: {
        workspace: '工作台', visualization: '可视化', scheduler: '预约', inquiries: '客户咨询', messages: '消息',
        clients: '客户', vehicles: '车辆', maintenance: '维修工单', defects: '故障', counters: '里程与使用时长',
        materials: '库存', 'work-journal': '工作记录', finance: '财务', statistics: '统计', shifts: '班次',
        employees: '员工', members: '团队', 'my-work': '我的工作', settings: '组织设置', billing: '套餐与付款', wiki: '知识库',
        organization: '组织', catalog: '目录', assistant: '助手'
      },
      groups: { workspace: '工作区', management: '管理', data: '数据', operations: '业务', administration: '管理', reports: '报告', access: '帮助与访问' },
      skip: '跳转到内容', workspace: '工作区', help: '展开菜单', collapse: '收起菜单',
      support: '帮助与支持', openMenu: '打开菜单', notifications: '通知', noNotifications: '没有新通知',
      notificationHistory: '所有通知记录', close: '关闭', profile: '个人资料', organizations: '组织',
      lightTheme: '切换到浅色主题', darkTheme: '切换到深色主题', closeHistory: '关闭记录',
      savedMessages: '已保存的对话', mobileNavigation: '组织导航', closeMenu: '关闭菜单',
      availableInPro: 'PRO 可用', unavailable: '功能不可用', pro: 'PRO 可用', unread: '未读：{count}',
      featureDisabled: '此功能已在安装设置中关闭', proUnavailable: '当前套餐不支持',
      statisticsPro: '统计功能在 PRO 中提供', employeesPro: '团队功能在 PRO 中提供', rolesPro: '角色和权限在 PRO 中提供',
      shiftsPro: '班次功能在 PRO 中提供', messagesPro: '消息功能在 PRO 中提供', myWorkPro: '我的工作在 PRO 中提供',
      available: 'PRO 可用'
    }
  }
} satisfies Record<'ru' | 'en' | 'zh-Hans', Record<string, unknown>>

for (const locale of Object.keys(workspaceChromeMessages) as Array<keyof typeof workspaceChromeMessages>) {
  registerMessages(locale, workspaceChromeMessages[locale])
}

export function workspaceLabelKey(code: string): string {
  return `workspaceChrome.labels.${code}`
}

export function workspaceGroupKey(title: string): string | undefined {
  const keys: Record<string, string> = {
    'Рабочая область': 'workspace',
    'Основное': 'workspace',
    'Управление': 'management',
    'Данные': 'data',
    'Работа': 'operations',
    'Администрирование': 'administration',
    'Отчеты': 'reports',
    'Отчёты': 'reports',
    'Справка и доступ': 'access'
  }
  const key = keys[title]
  return key ? `workspaceChrome.groups.${key}` : undefined
}

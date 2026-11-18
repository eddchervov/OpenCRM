import type { UiLocale } from '@opencrm/ui/i18n'

export type AdminUiLocale = UiLocale

type OrganizationStatus = {
  isDeleted: boolean
  isArchived: boolean
  isActive: boolean
}

type PermissionLabel = {
  code: string
  name: string
  group: string
}

const organizationStatuses: Record<AdminUiLocale, Record<string, string>> = {
  ru: { active: 'Активна', inactive: 'Неактивна', archived: 'Архив', deleted: 'Удалена' },
  en: { active: 'Active', inactive: 'Inactive', archived: 'Archived', deleted: 'Deleted' },
  'zh-Hans': { active: '已启用', inactive: '未启用', archived: '已归档', deleted: '已删除' }
}

const scenarios: Record<AdminUiLocale, Record<string, string>> = {
  ru: {
    personal: 'Личный кабинет',
    'service-station': 'Автосервис',
    detailing: 'Детейлинг',
    wrapping: 'Оклейка автомобилей',
    'car-wash': 'Автомойка',
    fleet: 'Автопарк',
    'driving-school': 'Автошкола'
  },
  en: {
    personal: 'Personal workspace',
    'service-station': 'Auto repair shop',
    detailing: 'Car detailing',
    wrapping: 'Vehicle wrapping',
    'car-wash': 'Car wash',
    fleet: 'Fleet',
    'driving-school': 'Driving school'
  },
  'zh-Hans': {
    personal: '个人工作区',
    'service-station': '汽车维修',
    detailing: '汽车美容',
    wrapping: '汽车贴膜',
    'car-wash': '汽车洗车',
    fleet: '车队',
    'driving-school': '驾校'
  }
}

const vehicleKinds: Record<AdminUiLocale, Record<number, string>> = {
  ru: {
    1: 'Легковой автомобиль', 2: 'Грузовая техника', 3: 'Мотоцикл', 4: 'Квадроцикл',
    5: 'Багги / UTV', 6: 'Снегоход', 7: 'Гидроцикл', 8: 'Лодка / катер',
    9: 'Спецтехника', 10: 'Прицеп', 99: 'Другая техника'
  },
  en: {
    1: 'Passenger car', 2: 'Truck', 3: 'Motorcycle', 4: 'ATV', 5: 'Buggy / UTV',
    6: 'Snowmobile', 7: 'Personal watercraft', 8: 'Boat', 9: 'Special-purpose vehicle',
    10: 'Trailer', 99: 'Other vehicle'
  },
  'zh-Hans': {
    1: '乘用车', 2: '货车', 3: '摩托车', 4: '全地形车', 5: '越野车 / UTV',
    6: '雪地摩托', 7: '摩托艇', 8: '船艇', 9: '特种车辆', 10: '拖车', 99: '其他车辆'
  }
}

const permissionLabels: Record<AdminUiLocale, Record<string, { name: string; group: string }>> = {
  ru: {
    'Admin.Access': { name: 'Доступ к административному модулю', group: 'Общее' },
    'Admin.Dashboard.View': { name: 'Просмотр дашборда', group: 'Дашборд' },
    'Admin.Users.View': { name: 'Просмотр пользователей', group: 'Пользователи' },
    'Admin.Users.Manage': { name: 'Управление пользователями', group: 'Пользователи' },
    'Admin.Users.Impersonate': { name: 'Вход от имени пользователя', group: 'Пользователи' },
    'Admin.Billing.View': { name: 'Просмотр платежей и подписок', group: 'Платежи' },
    'Admin.Billing.Manage': { name: 'Управление платежами и подписками', group: 'Платежи' },
    'Admin.Content.View': { name: 'Просмотр контента и обращений', group: 'Контент' },
    'Admin.Content.Manage': { name: 'Управление контентом и обращениями', group: 'Контент' },
    'Admin.Communications.View': { name: 'Просмотр сообщений и рассылок', group: 'Коммуникации' },
    'Admin.Communications.Manage': { name: 'Управление сообщениями и рассылками', group: 'Коммуникации' },
    'Admin.Configuration.Manage': { name: 'Управление настройками', group: 'Настройки' },
    'Admin.Permissions.Manage': { name: 'Управление ролями и правами', group: 'Настройки' }
  },
  en: {
    'Admin.Access': { name: 'Access to admin module', group: 'General' },
    'Admin.Dashboard.View': { name: 'View dashboard', group: 'Dashboard' },
    'Admin.Users.View': { name: 'View users', group: 'Users' },
    'Admin.Users.Manage': { name: 'Manage users', group: 'Users' },
    'Admin.Users.Impersonate': { name: 'Sign in as a user', group: 'Users' },
    'Admin.Billing.View': { name: 'View payments and subscriptions', group: 'Billing' },
    'Admin.Billing.Manage': { name: 'Manage payments and subscriptions', group: 'Billing' },
    'Admin.Content.View': { name: 'View content and requests', group: 'Content' },
    'Admin.Content.Manage': { name: 'Manage content and requests', group: 'Content' },
    'Admin.Communications.View': { name: 'View messages and campaigns', group: 'Communications' },
    'Admin.Communications.Manage': { name: 'Manage messages and campaigns', group: 'Communications' },
    'Admin.Configuration.Manage': { name: 'Manage settings', group: 'Settings' },
    'Admin.Permissions.Manage': { name: 'Manage roles and permissions', group: 'Settings' }
  },
  'zh-Hans': {
    'Admin.Access': { name: '访问管理模块', group: '通用' },
    'Admin.Dashboard.View': { name: '查看仪表盘', group: '仪表盘' },
    'Admin.Users.View': { name: '查看用户', group: '用户' },
    'Admin.Users.Manage': { name: '管理用户', group: '用户' },
    'Admin.Users.Impersonate': { name: '以用户身份登录', group: '用户' },
    'Admin.Billing.View': { name: '查看付款和订阅', group: '账单' },
    'Admin.Billing.Manage': { name: '管理付款和订阅', group: '账单' },
    'Admin.Content.View': { name: '查看内容和请求', group: '内容' },
    'Admin.Content.Manage': { name: '管理内容和请求', group: '内容' },
    'Admin.Communications.View': { name: '查看消息和邮件活动', group: '通信' },
    'Admin.Communications.Manage': { name: '管理消息和邮件活动', group: '通信' },
    'Admin.Configuration.Manage': { name: '管理设置', group: '设置' },
    'Admin.Permissions.Manage': { name: '管理角色和权限', group: '设置' }
  }
}

export function getOrganizationStatusLabel(status: OrganizationStatus, locale: AdminUiLocale): string {
  const statusCode = status.isDeleted
    ? 'deleted'
    : status.isArchived
      ? 'archived'
      : status.isActive
        ? 'active'
        : 'inactive'

  return organizationStatuses[locale][statusCode]
}

export function getOrganizationScenarioLabel(code: string, fallback: string, locale: AdminUiLocale): string {
  return scenarios[locale][code] ?? fallback
}

export function getVehicleKindLabel(kind: number, fallback: string, locale: AdminUiLocale): string {
  return vehicleKinds[locale][kind] ?? fallback
}

export function getPermissionGroupLabel(permission: PermissionLabel, locale: AdminUiLocale): string {
  return permissionLabels[locale][permission.code]?.group ?? permission.group
}

export function getPermissionNameLabel(permission: PermissionLabel, locale: AdminUiLocale): string {
  return permissionLabels[locale][permission.code]?.name ?? permission.name
}

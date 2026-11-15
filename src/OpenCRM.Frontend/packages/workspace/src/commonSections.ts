import type { WorkspaceContext } from './types'

export type WorkspaceCommonSectionCode =
  | 'organization'
  | 'statistics'
  | 'billing'
  | 'wiki'
  | 'shifts'
  | 'employees'
  | 'my-work'
  | 'members'
  | 'catalog'
  | 'settings'

export type WorkspaceCommonSectionItem = {
  code: WorkspaceCommonSectionCode
  title: string
  description: string
  groupTitle: string
  href: string
}

const commonSectionTemplates: Array<Omit<WorkspaceCommonSectionItem, 'href'>> = [
  {
    code: 'organization',
    title: 'Организация',
    description: 'Профиль, доступ и сводка рабочего пространства.',
    groupTitle: 'Справка и доступ'
  },
  {
    code: 'statistics',
    title: 'Статистика',
    description: 'Графики, отчеты и ключевые показатели организации.',
    groupTitle: 'Отчеты'
  },
  {
    code: 'billing',
    title: 'Тарифы',
    description: 'Тариф, лимиты и доступные возможности.',
    groupTitle: 'Справка и доступ'
  },
  {
    code: 'wiki',
    title: 'Вики',
    description: 'Внутренняя справка и рабочие правила.',
    groupTitle: 'Справка и доступ'
  },
  {
    code: 'my-work',
    title: 'Мои работы',
    description: 'Назначенные операции и фактическое выполнение.',
    groupTitle: 'Работа'
  },
  {
    code: 'employees',
    title: 'Команда',
    description: 'Сотрудники, назначение на работы и доступ к CRM.',
    groupTitle: 'Работа'
  },
  {
    code: 'shifts',
    title: 'Смены',
    description: 'График команды.',
    groupTitle: 'Работа'
  },
  {
    code: 'members',
    title: 'Роли и права',
    description: 'Роли и расширенные настройки доступа.',
    groupTitle: 'Работа'
  },
  {
    code: 'catalog',
    title: 'Справочники',
    description: 'Услуги, посты и материалы детейлинг-центра.',
    groupTitle: 'Работа'
  },
  {
    code: 'settings',
    title: 'Настройки организации',
    description: 'Организация, рабочий процесс, документы и уведомления.',
    groupTitle: 'Справка и доступ'
  }
]

export const workspaceCommonSectionCodes = commonSectionTemplates.map(section => section.code)

export function isWorkspaceCommonSectionCode(code: string): code is WorkspaceCommonSectionCode {
  return workspaceCommonSectionCodes.includes(code as WorkspaceCommonSectionCode)
}

export function buildWorkspaceCommonSections(context: WorkspaceContext | null | undefined): WorkspaceCommonSectionItem[] {
  const organizationId = context?.organization.id ?? ''

  return commonSectionTemplates.map(section => ({
    ...section,
    href: resolveCommonSectionHref(section.code, organizationId)
  }))
}

export function resolveCommonSectionHref(code: WorkspaceCommonSectionCode, organizationId: string): string {
  if (!organizationId) {
    return '#'
  }

  switch (code) {
    case 'organization':
      return '#workspace'
    case 'statistics':
      return '#statistics'
    case 'billing':
      return '#billing'
    case 'wiki':
      return '#wiki'
    case 'employees':
      return '#employees'
    case 'shifts':
      return '#shifts'
    case 'my-work':
      return '#my-work'
    case 'members':
      return '#members'
    case 'catalog':
      return '#catalog'
    case 'settings':
      return '#settings'
    default:
      return '#workspace'
  }
}

export function resolveWorkspaceWikiSlug(sectionCode: string, entityId = '', action = ''): string {
  if (sectionCode === 'vehicles' && (entityId === 'new' || action === 'edit')) {
    return 'vehicle-form'
  }

  if (sectionCode === 'maintenance' && (entityId === 'new' || action === 'edit' || !entityId)) {
    return 'maintenance-form'
  }

  switch (sectionCode) {
    case 'workspace':
      return 'workday'
    case 'organization':
      return 'start'
    case 'visualization':
      return 'visualization'
    case 'finance':
      return 'finance'
    case 'statistics':
      return 'reports'
    case 'scheduler':
      return 'scheduler'
    case 'clients':
      return 'clients'
    case 'inquiries':
      return 'inquiries'
    case 'messages':
      return 'client-messages'
    case 'assistant':
      return 'assistant'
    case 'vehicles':
      return 'client-vehicles'
    case 'defects':
      return 'defects'
    case 'counters':
      return 'counters'
    case 'materials':
      return 'materials-and-documents'
    case 'maintenance-journal':
    case 'work-journal':
      return 'service-journal'
    case 'maintenance':
      return 'maintenance'
    case 'employees':
      return 'employees'
    case 'my-work':
      return 'my-work'
    case 'shifts':
      return 'shifts'
    case 'members':
      return 'roles-and-modules'
    case 'catalog':
      return 'services-and-packages'
    case 'settings':
      return 'workspace-settings'
    case 'billing':
      return 'billing'
    case 'wiki':
      return 'filters-and-tooltips'
    default:
      return 'start'
  }
}

export function resolveWorkspaceWikiHref(sectionCode: string, entityId = '', action = ''): string {
  return `#wiki?slug=${encodeURIComponent(resolveWorkspaceWikiSlug(sectionCode, entityId, action))}`
}

import {
  buildWorkspaceCommonSections,
  workspaceCommonSectionCodes,
  type WorkspaceCommonSectionCode
} from '@opencrm/workspace'
import type { AutoServiceMenuItem } from '@/api/autoserviceBff'

export type AutoServiceSectionCode =
  | 'assistant'
  | 'workspace'
  | 'visualization'
  | 'scheduler'
  | 'inquiries'
  | 'messages'
  | 'clients'
  | 'vehicles'
  | 'maintenance'
  | 'defects'
  | 'counters'
  | 'materials'
  | 'work-journal'
  | 'finance'
  | WorkspaceCommonSectionCode

export type AutoServiceSection = {
  code: AutoServiceSectionCode
  title: string
  description: string
  groupTitle?: string
  source?: AutoServiceMenuItem
}

const fallbackSections: AutoServiceSection[] = [
  {
    code: 'workspace',
    title: 'Рабочий день',
    description: 'Обзор заказ-нарядов, быстрых действий и состояния смены.'
  },
  {
    code: 'visualization',
    title: 'Визуализация',
    description: 'Объёмная схема постов и состояние работ.'
  },
  {
    code: 'scheduler',
    title: 'Записи',
    description: 'Календарь приемки, загрузка постов и свободное время.'
  },
  {
    code: 'messages',
    title: 'Сообщения',
    description: 'Переписка с клиентами и новые сообщения.'
  },
  {
    code: 'inquiries',
    title: 'Обращения',
    description: 'Интерес клиента, договорённости и следующее действие.'
  },
  {
    code: 'clients',
    title: 'Клиенты',
    description: 'База клиентов, телефоны и история обращений.'
  },
  {
    code: 'vehicles',
    title: 'Автомобили',
    description: 'Автомобили, номера, привязка к клиентам и заказам.'
  },
  {
    code: 'materials',
    title: 'Склад',
    description: 'Запчасти, жидкости, расходники и движения по заказам.'
  },
  {
    code: 'work-journal',
    title: 'История работ',
    description: 'Закрытые заказ-наряды, оплаты и история выдачи.'
  },
  {
    code: 'finance',
    title: 'Финансы',
    description: 'Выручка, долги, себестоимость, прибыль и заказы за период.'
  }
]

const commonSectionDescriptions: Partial<Record<WorkspaceCommonSectionCode, string>> = {
  organization: 'Профиль СТО, доступ и сводка рабочего пространства.',
  billing: 'Тариф, лимиты и доступные возможности автосервиса.',
  wiki: 'Внутренняя справка, регламенты и рабочие правила.',
  members: 'Мастера, приемщики, роли и приглашения.',
  catalog: 'Услуги, посты, работы, материалы и справочники СТО.',
  settings: 'Профиль, реквизиты и оформление документов.'
}

const supportedCodes = new Set<AutoServiceSectionCode>([
  ...fallbackSections.map(section => section.code),
  'assistant',
  'maintenance',
  'defects',
  'counters',
  ...workspaceCommonSectionCodes
])

export function isAutoServiceSectionCode(code: string): code is AutoServiceSectionCode {
  return supportedCodes.has(code as AutoServiceSectionCode)
}

export function buildAutoServiceSections(menu: AutoServiceMenuItem[] | undefined): AutoServiceSection[] {
  const visibleMenu = (menu ?? []).filter(item => item.isVisible && item.code !== 'maintenance' && isAutoServiceSectionCode(item.code))
  if (!menu) {
    const commonSections: AutoServiceSection[] = buildWorkspaceCommonSections(undefined).map(section => ({
      code: section.code,
      title: section.title,
      description: commonSectionDescriptions[section.code] ?? section.description,
      groupTitle: section.groupTitle
    }))

    return [...fallbackSections, ...commonSections]
  }

  const fromMenu = visibleMenu.map(item => ({
    code: item.code as AutoServiceSectionCode,
    title: item.title,
    description:
      fallbackSections.find(section => section.code === item.code)?.description
      ?? commonSectionDescriptions[item.code as WorkspaceCommonSectionCode]
      ?? buildWorkspaceCommonSections(undefined).find(section => section.code === item.code)?.description
      ?? '',
    groupTitle: item.groupTitle,
    source: item
  }))

  return fromMenu
}

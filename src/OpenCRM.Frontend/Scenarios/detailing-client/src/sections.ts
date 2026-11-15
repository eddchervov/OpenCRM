import type { DetailingMenuItem } from '@/api/detailingBff'
import {
  buildWorkspaceCommonSections,
  workspaceCommonSectionCodes,
  type WorkspaceCommonSectionCode
} from '@opencrm/workspace'

export type DetailingSectionCode =
  | 'assistant'
  | 'workspace'
  | 'visualization'
  | 'scheduler'
  | 'inquiries'
  | 'messages'
  | 'clients'
  | 'vehicles'
  | 'maintenance'
  | 'materials'
  | 'work-journal'
  | 'finance'
  | WorkspaceCommonSectionCode

export type DetailingSection = {
  code: DetailingSectionCode
  title: string
  description: string
  groupTitle?: string
  source?: DetailingMenuItem
}

const fallbackSections: DetailingSection[] = [
  {
    code: 'workspace',
    title: 'Рабочий день',
    description: 'Обзор записей, быстрых действий и состояния смены.'
  },
  {
    code: 'visualization',
    title: 'Визуализация',
    description: 'Объёмная схема постов и состояние работ.'
  },
  {
    code: 'scheduler',
    title: 'Записи',
    description: 'Календарь услуг и свободное время боксов.'
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
    description: 'Автомобили, номера, привязка к клиентам и визитам.'
  },
  {
    code: 'materials',
    title: 'Склад',
    description: 'Химия, расходники и движения материалов.'
  },
  {
    code: 'work-journal',
    title: 'Журнал услуг',
    description: 'Все записи и услуги: поиск, статусы и даты.'
  },
  {
    code: 'finance',
    title: 'Финансы',
    description: 'Выручка, долги, материалы, средний чек и услуги за период.'
  }
]

const supportedCodes = new Set<DetailingSectionCode>([
  ...fallbackSections.map(section => section.code),
  'assistant',
  'maintenance',
  ...workspaceCommonSectionCodes
])

export function isDetailingSectionCode(code: string): code is DetailingSectionCode {
  return supportedCodes.has(code as DetailingSectionCode)
}

export function buildDetailingSections(menu: DetailingMenuItem[] | undefined): DetailingSection[] {
  const visibleMenu = (menu ?? []).filter(item => item.isVisible && item.code !== 'maintenance' && isDetailingSectionCode(item.code))
  if (!menu) {
    const commonSections: DetailingSection[] = buildWorkspaceCommonSections(undefined).map(section => ({
      code: section.code,
      title: section.title,
      description: section.description,
      groupTitle: section.groupTitle
    }))

    return [...fallbackSections, ...commonSections]
  }

  const fromMenu = visibleMenu.map(item => ({
    code: item.code as DetailingSectionCode,
    title: item.title,
    description:
      fallbackSections.find(section => section.code === item.code)?.description
      ?? buildWorkspaceCommonSections(undefined).find(section => section.code === item.code)?.description
      ?? '',
    groupTitle: item.groupTitle,
    source: item
  }))

  return fromMenu
}

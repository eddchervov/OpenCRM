import type { FleetMenuItem } from '@/api/fleetBff'
import {
  buildWorkspaceCommonSections,
  workspaceCommonSectionCodes,
  type WorkspaceCommonSectionCode
} from '@opencrm/workspace'

export type FleetSectionCode =
  | 'workspace'
  | 'visualization'
  | 'scheduler'
  | 'clients'
  | 'vehicles'
  | 'defects'
  | 'counters'
  | 'maintenance'
  | 'materials'
  | 'work-journal'
  | 'finance'
  | WorkspaceCommonSectionCode

export type FleetSection = {
  code: FleetSectionCode
  title: string
  description: string
  groupTitle?: string
  source?: FleetMenuItem
}

const fallbackSections: FleetSection[] = [
  {
    code: 'workspace',
    title: 'Состояние парка',
    description: 'Готовность техники, быстрые действия и контроль важных работ.'
  },
  {
    code: 'visualization',
    title: 'Визуализация',
    description: 'Объёмная схема постов и состояние работ.'
  },
  {
    code: 'scheduler',
    title: 'План ТО',
    description: 'Календарь заявок, плановых работ и занятость ремонтных зон.'
  },
  {
    code: 'clients',
    title: 'Подразделения',
    description: 'Водители, подразделения или ответственные за технику.'
  },
  {
    code: 'vehicles',
    title: 'Техника',
    description: 'Единицы парка, номера, VIN, пробег и история эксплуатации.'
  },
  {
    code: 'defects',
    title: 'Неисправности',
    description: 'Замечания по технике, критичность, статусы и связь с обслуживанием.'
  },
  {
    code: 'counters',
    title: 'Пробег и наработка',
    description: 'Показания одометра, моточасы, история и актуальность счетчиков.'
  },
  {
    code: 'materials',
    title: 'Склад',
    description: 'Запчасти, масла, шины, расходники и движения материалов.'
  },
  {
    code: 'work-journal',
    title: 'Журнал работ',
    description: 'Выполненные ТО, ремонты, осмотры и закрытые заявки.'
  },
  {
    code: 'finance',
    title: 'Расходы',
    description: 'Стоимость работ, материалов, долгов и затрат по технике.'
  }
]

const supportedCodes = new Set<FleetSectionCode>([
  ...fallbackSections.map(section => section.code),
  'maintenance',
  ...workspaceCommonSectionCodes
])

export function isFleetSectionCode(code: string): code is FleetSectionCode {
  return supportedCodes.has(code as FleetSectionCode)
}

export function buildFleetSections(menu: FleetMenuItem[] | undefined): FleetSection[] {
  const visibleMenu = (menu ?? []).filter(item => item.isVisible && item.code !== 'maintenance' && isFleetSectionCode(item.code))
  if (!menu) {
    const commonSections: FleetSection[] = buildWorkspaceCommonSections(undefined).map(section => ({
      code: section.code,
      title: section.title,
      description: section.description,
      groupTitle: section.groupTitle
    }))

    return [...fallbackSections, ...commonSections]
  }

  const fromMenu = visibleMenu.map(item => ({
    code: item.code as FleetSectionCode,
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

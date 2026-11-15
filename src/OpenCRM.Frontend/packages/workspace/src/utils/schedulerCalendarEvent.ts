import { getVehicleBrandLogo } from './vehicleBrandLogo'
import type { SchedulerCalendarItem } from './schedulerDay'

export function schedulerCalendarStatus(item: SchedulerCalendarItem) {
  if (item.status === 4) return 'crm-calendar-status--cancelled'
  if (item.handedOverAt || item.readyForHandoverAt || item.status === 3) return 'crm-calendar-status--done'
  if (item.status === 2) return 'crm-calendar-status--progress'

  return 'crm-calendar-status--planned'
}

export function renderSchedulerCalendarEvent(item: SchedulerCalendarItem, view: string, isStart = true) {
  const root = document.createElement('div')
  root.className = 'crm-calendar-entry'
  const isList = view === 'listWeek'
  root.title = [item.vehicleDisplayName, item.registrationPlate || item.vehicleSubtitle, item.serviceNames?.join(', '), item.location, item.employeeNames?.join(', '), item.statusName].filter(Boolean).join(' · ')

  function append(parent: HTMLElement, className: string, text?: string | null) {
    if (!text) return
    const element = document.createElement('span')
    element.className = `crm-calendar-entry__${className}`
    element.textContent = text
    parent.appendChild(element)
  }

  if (!isList) {
    root.classList.add('crm-calendar-entry--compact')
    const start = item.plannedStartTime?.slice(0, 5)
    const end = item.plannedEndTime?.slice(0, 5)
    const endDate = item.plannedEndDate?.slice(0, 10)
    const endLabel = end && endDate && endDate !== item.date.slice(0, 10)
      ? `${endDate.split('-').reverse().slice(0, 2).join('.')} ${end}`
      : end
    append(root, 'time', !isStart ? 'Продолжается' : start || 'Без времени')
    if (endLabel) append(root, 'time-end', `до ${endLabel}`)
    append(root, 'title', item.vehicleDisplayName || 'Запись')

    return { domNodes: [root] }
  }

  const vehicle = document.createElement('div')
  vehicle.className = 'crm-calendar-entry__vehicle'
  const logo = getVehicleBrandLogo(item.brandLogoKey)
  if (logo) {
    const image = document.createElement('img')
    image.src = logo.src
    image.alt = `Логотип ${logo.name}`
    vehicle.appendChild(image)
  }

  const name = document.createElement('div')
  name.className = 'crm-calendar-entry__name'
  append(name, 'title', item.vehicleDisplayName || 'Запись')
  append(name, 'plate', item.registrationPlate || item.vehicleSubtitle)
  vehicle.appendChild(name)
  root.appendChild(vehicle)
  append(root, 'services', item.serviceNames?.join(', '))
  append(root, 'assignment', [item.location, item.employeeNames?.join(', ')].filter(Boolean).join(' · '))

  const footer = document.createElement('div')
  footer.className = 'crm-calendar-entry__footer'
  const status = item.status === 4 ? item.statusName : item.handedOverAt ? 'Выдано' : item.readyForHandoverAt ? 'Готово к выдаче' : item.statusName
  append(footer, 'status', status)
  root.appendChild(footer)

  return { domNodes: [root] }
}

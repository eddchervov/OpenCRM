export type OccupiedBookingSlot = {
  id: string
  startMinute: number
  endMinute: number
  vehicle: string
  service?: string | null
  startAt?: string | null
  endAt?: string | null
}

export async function loadOccupiedBookingSlots(
  organizationId: string,
  date: string,
  location: string,
  excludeMaintenanceId: string | undefined,
  signal: AbortSignal,
  dateTo?: string
): Promise<OccupiedBookingSlot[]> {
  const query = new URLSearchParams({ organizationId, date, location })
  if (dateTo) query.set('dateTo', dateTo)
  if (excludeMaintenanceId) query.set('excludeMaintenanceId', excludeMaintenanceId)

  const response = await fetch(`/api/v1/maintenance/occupied-slots?${query}`, { signal, credentials: 'same-origin', cache: 'no-store' })
  if (!response.ok) throw new Error('Не удалось проверить занятость поста. Повторите проверку.')

  return await response.json()
}

export function timeMinutes(value: string): number | null {
  const match = /^(\d{2}):(\d{2})$/.exec(value)
  if (!match) return null
  const hours = Number(match[1])
  const minutes = Number(match[2])

  return hours < 24 && minutes < 60 ? hours * 60 + minutes : null
}

export function bookingDayOffset(date: string, endDate: string) {
  if (!date || !endDate) return 0

  return (Date.parse(`${endDate.slice(0, 10)}T00:00:00Z`) - Date.parse(`${date.slice(0, 10)}T00:00:00Z`)) / 60000
}

export function bookingEndMinute(start: number, end: string, dayOffset = 0) {
  const value = timeMinutes(end)

  return value !== null ? dayOffset + value : start + 60
}

export function findBookingConflicts(slots: OccupiedBookingSlot[], start: number, end: number) {
  return slots.filter(slot => start < slot.endMinute && slot.startMinute < end)
}

export function describeOccupiedSlot(slot: OccupiedBookingSlot) {
  if (slot.startAt && slot.endAt) {
    const formatDateTime = (value: string) => `${value.slice(8, 10)}.${value.slice(5, 7)}.${value.slice(0, 4)} ${value.slice(11, 16)}`
    return `${formatDateTime(slot.startAt)}–${formatDateTime(slot.endAt)} · ${slot.vehicle}${slot.service ? ` · ${slot.service}` : ''}`
  }
  const format = (minute: number) => {
    const time = `${String(Math.floor(minute / 60) % 24).padStart(2, '0')}:${String(Math.floor(minute % 60)).padStart(2, '0')}`
    return minute >= 1440 ? `${time} (+1 день)` : time
  }

  return `${format(slot.startMinute)}–${format(slot.endMinute)} · ${slot.vehicle}${slot.service ? ` · ${slot.service}` : ''}`
}

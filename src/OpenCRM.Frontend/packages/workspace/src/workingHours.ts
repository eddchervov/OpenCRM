import { translate as t } from '@opencrm/ui/i18n'
import '@opencrm/workspace/workingHoursMessages'
import type { WorkspaceSetup, WorkspaceWorkingDay } from './api/setup'

export const workingWeek = [
  { id: 1, get name() { return t('workingHours.monday') } }, { id: 2, get name() { return t('workingHours.tuesday') } }, { id: 3, get name() { return t('workingHours.wednesday') } },
  { id: 4, get name() { return t('workingHours.thursday') } }, { id: 5, get name() { return t('workingHours.friday') } }, { id: 6, get name() { return t('workingHours.saturday') } }, { id: 0, get name() { return t('workingHours.sunday') } }
]

export function workingScheduleFromSetup(setup: WorkspaceSetup): WorkspaceWorkingDay[] {
  if (setup.weeklySchedule?.some(day => day.isWorking)) return setup.weeklySchedule.map(day => ({ ...day, breaks: day.breaks.map(pause => ({ ...pause })) }))
  const days = setup.workingDays.length ? setup.workingDays : [1, 2, 3, 4, 5, 6]
  return workingWeek.map(day => ({
    dayOfWeek: day.id, isWorking: days.includes(day.id),
    startTime: setup.workdayStart ?? '09:00:00', endTime: setup.workdayEnd ?? '19:00:00', breaks: []
  }))
}

export function workingIntervals(day: WorkspaceWorkingDay) {
  if (!day.isWorking || !day.startTime || !day.endTime) return []
  const intervals: { startTime: string, endTime: string }[] = []
  let start = day.startTime.slice(0, 5)
  for (const pause of [...day.breaks].sort((a, b) => a.startTime.localeCompare(b.startTime))) {
    if (start < pause.startTime.slice(0, 5)) intervals.push({ startTime: start, endTime: pause.startTime.slice(0, 5) })
    start = pause.endTime.slice(0, 5)
  }
  if (start < day.endTime.slice(0, 5)) intervals.push({ startTime: start, endTime: day.endTime.slice(0, 5) })
  return intervals
}

export function hasWorkingSchedule(schedule: WorkspaceWorkingDay[]) {
  return schedule.some(day => workingIntervals(day).length > 0)
}

export function nextWorkingDay(date: string, schedule: WorkspaceWorkingDay[]) {
  const candidate = new Date(date + 'T12:00:00Z')
  for (let offset = 1; offset <= 7; offset++) {
    candidate.setUTCDate(candidate.getUTCDate() + 1)
    const day = schedule.find(item => item.dayOfWeek === candidate.getUTCDay())
    const intervals = day ? workingIntervals(day) : []
    if (intervals.length) return { date: candidate.toISOString().slice(0, 10), intervals }
  }

  return null
}

export function scheduleBusinessHours(schedule?: WorkspaceWorkingDay[]) {
  if (!schedule?.some(day => day.isWorking)) return false
  return schedule.flatMap(day => workingIntervals(day).map(interval => ({ daysOfWeek: [day.dayOfWeek], ...interval })))
}

export function workingScheduleError(schedule: WorkspaceWorkingDay[]) {
  if (!schedule.some(day => day.isWorking)) return t('workingHours.oneDayRequired')
  for (const day of schedule.filter(day => day.isWorking)) {
    const name = workingWeek.find(value => value.id === day.dayOfWeek)?.name ?? t('workingHours.workday')
    if (!day.startTime || !day.endTime || day.startTime.slice(0, 5) >= day.endTime.slice(0, 5)) return t('workingHours.endAfter', { day: name })
    let previousEnd = day.startTime.slice(0, 5)
    for (const pause of [...day.breaks].sort((a, b) => a.startTime.localeCompare(b.startTime))) {
      const start = pause.startTime.slice(0, 5), end = pause.endTime.slice(0, 5)
      if (!start || !end || start >= end || start < previousEnd || end > day.endTime.slice(0, 5)) return t('workingHours.breaksInvalid', { day: name })
      previousEnd = end
    }
  }
  return ''
}

export function schedulePayload(schedule: WorkspaceWorkingDay[]) {
  return schedule.map(day => day.isWorking ? {
    ...day, startTime: day.startTime?.slice(0, 5) + ':00', endTime: day.endTime?.slice(0, 5) + ':00',
    breaks: day.breaks.map(pause => ({ startTime: pause.startTime.slice(0, 5) + ':00', endTime: pause.endTime.slice(0, 5) + ':00' }))
  } : { ...day, startTime: null, endTime: null, breaks: [] })
}

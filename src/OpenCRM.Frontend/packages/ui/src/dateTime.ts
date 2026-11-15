import { formatDateOnly } from './dateOnly'

export function formatLocalDateTime(date: Date = new Date(), timeZoneId = 'UTC') {
  const time = new Intl.DateTimeFormat('en-GB', {
    timeZone: timeZoneId, hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23'
  }).format(date)

  return `${formatDateOnly(date, timeZoneId)}T${time}`
}

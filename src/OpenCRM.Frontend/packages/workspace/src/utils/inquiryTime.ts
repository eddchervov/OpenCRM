import { formattingCulture, translate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/workdayActionMessages'

export function resolveInquiryTimeZone(timeZoneId?: string | null): string {
  return timeZoneId || 'UTC'
}

export function inquiryLocalTime(value: string | null | undefined, timeZoneId: string): string {
  if (!value) return ''
  const parts = new Intl.DateTimeFormat('sv-SE', {
    timeZone: timeZoneId, year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
  }).formatToParts(new Date(value))
  const part = (type: string) => parts.find(item => item.type === type)?.value ?? ''
  return `${part('year')}-${part('month')}-${part('day')}T${part('hour')}:${part('minute')}`
}

export function inquiryUtcTime(value: string, timeZoneId: string, originalValue?: string | null): string | null {
  if (!value) return null
  if (originalValue && inquiryLocalTime(originalValue, timeZoneId) === value) return originalValue
  const target = Date.parse(`${value}:00Z`)
  if (!Number.isFinite(target)) throw new Error(translate('workdayActions.invalidTime'))
  let instant = target
  for (let attempt = 0; attempt < 4; attempt++) {
    const local = inquiryLocalTime(new Date(instant).toISOString(), timeZoneId)
    const difference = target - Date.parse(`${local}:00Z`)
    if (difference === 0) return new Date(instant).toISOString()
    instant += difference
  }
  throw new Error(translate('workdayActions.missingTime'))
}

export function formatInquiryTime(value: string | null | undefined, timeZoneId?: string | null): string {
  return value ? new Intl.DateTimeFormat(formattingCulture.value, { timeZone: resolveInquiryTimeZone(timeZoneId), day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }).format(new Date(value)) : ''
}

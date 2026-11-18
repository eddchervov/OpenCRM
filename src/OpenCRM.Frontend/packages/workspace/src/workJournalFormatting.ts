import { formattingCulture } from '@opencrm/ui/i18n'

export function formatWorkJournalDate(value: string, timeZoneId: string, culture = formattingCulture.value): string {
  const localParts = value.match(/^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2})(?::[\d.]+)?)?$/)
  if (localParts) {
    const [, year, month, day, hour = '00', minute = '00'] = localParts
    const date = new Date(Date.UTC(Number(year), Number(month) - 1, Number(day), Number(hour), Number(minute)))

    return new Intl.DateTimeFormat(culture, {
      timeZone: 'UTC', day: '2-digit', month: '2-digit',
      ...(hour === '00' && minute === '00' ? {} : { hour: '2-digit', minute: '2-digit' })
    }).format(date)
  }

  return new Intl.DateTimeFormat(culture, {
    timeZone: timeZoneId, day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit'
  }).format(new Date(value))
}

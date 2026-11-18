import { i18n, localeCultures, type UiLocale } from '@opencrm/ui/i18n'

function russianUnit(value: number, forms: [string, string, string]) {
  const remainder = value % 100
  if (remainder >= 11 && remainder <= 14) return forms[2]

  switch (value % 10) {
    case 1: return forms[0]
    case 2:
    case 3:
    case 4: return forms[1]
    default: return forms[2]
  }
}

export function getWorkDurationSeconds(value?: string | null): number | null {
  const match = value?.trim().match(/^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?$/)
  if (!match || Number(match[2]) > 23 || Number(match[3]) > 59 || Number(match[4]) > 59) return null

  const seconds = Number(match[1] ?? 0) * 86400 + Number(match[2]) * 3600
    + Number(match[3]) * 60 + Number(`${match[4]}.${match[5] ?? 0}`)

  return Number.isFinite(seconds) ? seconds : null
}

export function hasRecordedWorkTime(value?: string | null) {
  const match = value?.trim().match(/^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?$/)
  if (!match || Number(match[2]) > 23 || Number(match[3]) > 59 || Number(match[4]) > 59) return false

  return match.slice(1, 5).some(part => Number(part ?? 0) > 0)
}

export function formatWorkDuration(value: string, locale: UiLocale = i18n.global.locale.value) {
  const match = value.trim().match(/^(-)?(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.\d+)?$/)
  if (!match) return value

  const sign = match[1] ? '−' : ''
  const days = Number(match[2] ?? 0)
  const hours = Number(match[3])
  const minutes = Number(match[4])
  const seconds = Number(match[5])
  if (hours > 23 || minutes > 59 || seconds > 59) return value

  const totalMinutes = days * 24 * 60 + hours * 60 + minutes
  const parts: string[] = []
  const unit = (amount: number, name: Intl.NumberFormatOptions['unit'], forms: [string, string, string]) => locale === 'ru'
    ? `${amount} ${russianUnit(amount, forms)}`
    : new Intl.NumberFormat(localeCultures[locale], { style: 'unit', unit: name, unitDisplay: 'long' }).format(amount)

  if (days) parts.push(unit(days, 'day', ['день', 'дня', 'дней']))
  const remainingHours = Math.floor(totalMinutes / 60) - days * 24
  if (remainingHours) parts.push(unit(remainingHours, 'hour', ['час', 'часа', 'часов']))

  const remainingMinutes = totalMinutes % 60
  if (remainingMinutes) parts.push(unit(remainingMinutes, 'minute', ['минута', 'минуты', 'минут']))
  if (parts.length) return `${sign}${parts.join(' ')}`

  return `${sign}${unit(seconds, 'second', ['секунда', 'секунды', 'секунд'])}`
}

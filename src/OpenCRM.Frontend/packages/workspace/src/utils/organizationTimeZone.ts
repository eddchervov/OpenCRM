const existingTimeZones = [
  { value: 'Etc/UTC', label: 'Всемирное время (UTC)' },
  { value: 'Europe/Kaliningrad', label: 'Калининград (UTC+2)' },
  { value: 'Europe/Moscow', label: 'Москва (UTC+3)' },
  { value: 'Europe/Samara', label: 'Самара (UTC+4)' },
  { value: 'Asia/Yekaterinburg', label: 'Екатеринбург (UTC+5)' },
  { value: 'Asia/Omsk', label: 'Омск (UTC+6)' },
  { value: 'Asia/Novosibirsk', label: 'Новосибирск (UTC+7)' },
  { value: 'Asia/Krasnoyarsk', label: 'Красноярск (UTC+7)' },
  { value: 'Asia/Irkutsk', label: 'Иркутск (UTC+8)' },
  { value: 'Asia/Yakutsk', label: 'Якутск (UTC+9)' },
  { value: 'Asia/Vladivostok', label: 'Владивосток (UTC+10)' },
  { value: 'Asia/Magadan', label: 'Магадан (UTC+11)' },
  { value: 'Asia/Kamchatka', label: 'Камчатка (UTC+12)' }
]

function displayLocale(): string {
  return typeof document === 'undefined' ? 'ru-RU' : document.documentElement.lang || 'ru-RU'
}

function timeZoneLabel(id: string, locale = displayLocale()): string {
  if (locale.startsWith('ru')) {
    const known = existingTimeZones.find(zone => zone.value === id)
    if (known) return known.label
  }

  try {
    const name = new Intl.DateTimeFormat(locale, { timeZone: id, timeZoneName: 'long' })
      .formatToParts(new Date()).find(part => part.type === 'timeZoneName')?.value ?? id

    return `${name} · ${id}`
  } catch {
    return id
  }
}

const globalTimeZones = typeof Intl.supportedValuesOf === 'function'
  ? Intl.supportedValuesOf('timeZone')
  : ['Asia/Shanghai', 'Asia/Tokyo', 'Asia/Singapore', 'Asia/Dubai', 'Asia/Kolkata', 'Europe/London',
    'Europe/Paris', 'Europe/Berlin', 'America/New_York', 'America/Los_Angeles', 'Australia/Sydney', 'Pacific/Auckland']

const timeZoneIds = Array.from(new Set([
  ...existingTimeZones.map(zone => zone.value), ...globalTimeZones
]))

export function getOrganizationTimeZones(locale = displayLocale()) {
  return timeZoneIds.map(value => ({ value, label: timeZoneLabel(value, locale) }))
}

export const organizationTimeZones = getOrganizationTimeZones()

export function organizationTimeZoneName(id?: string | null, locale = displayLocale(), emptyLabel = 'Не настроен'): string {
  if (!id) return emptyLabel
  return timeZoneLabel(id, locale)
}

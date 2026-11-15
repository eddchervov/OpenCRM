export const organizationTimeZones = [
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

export function organizationTimeZoneName(id?: string | null): string {
  if (!id) return 'Не настроен'
  const known = organizationTimeZones.find(zone => zone.value === id)
  if (known) return known.label

  try {
    return new Intl.DateTimeFormat('ru-RU', { timeZone: id, timeZoneName: 'long' })
      .formatToParts(new Date()).find(part => part.type === 'timeZoneName')?.value ?? id
  } catch {
    return id
  }
}

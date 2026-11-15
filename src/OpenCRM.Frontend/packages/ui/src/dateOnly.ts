export function formatDateOnly(date: Date = new Date(), timeZoneId?: string | null) {
  if (timeZoneId) {
    const parts = new Intl.DateTimeFormat('sv-SE', {
      timeZone: timeZoneId, year: 'numeric', month: '2-digit', day: '2-digit'
    }).formatToParts(date)

    return `${parts.find(part => part.type === 'year')!.value}-${parts.find(part => part.type === 'month')!.value}-${parts.find(part => part.type === 'day')!.value}`
  }
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')

  return `${year}-${month}-${day}`
}

export function parseDateOnly(value: string) {
  const match = value.match(/^(\d{4})-(\d{2})-(\d{2})$/)
  if (!match) {
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? null : date
  }

  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  const date = new Date(year < 100 ? 0 : year, month - 1, day)

  if (year < 100) {
    date.setFullYear(year)
  }

  if (date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day) {
    return null
  }

  return Number.isNaN(date.getTime()) ? null : date
}


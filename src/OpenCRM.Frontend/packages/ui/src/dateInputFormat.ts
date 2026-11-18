type DateInputFormat = { mask: string; pattern: RegExp; year: number; month: number; day: number }

const formats: Record<string, DateInputFormat> = {
  'ru-RU': { mask: 'DD.MM.YYYY', pattern: /^(\d{1,2})\.(\d{1,2})\.(\d{4})(?:[\s,]|$)/, day: 1, month: 2, year: 3 },
  'en-US': { mask: 'MM/DD/YYYY', pattern: /^(\d{1,2})\/(\d{1,2})\/(\d{4})(?:[\s,]|$)/, day: 2, month: 1, year: 3 },
  'en-GB': { mask: 'DD/MM/YYYY', pattern: /^(\d{1,2})\/(\d{1,2})\/(\d{4})(?:[\s,]|$)/, day: 1, month: 2, year: 3 },
  'zh-CN': { mask: 'YYYY/MM/DD', pattern: /^(\d{4})\/(\d{1,2})\/(\d{1,2})(?:[\s,]|$)/, day: 3, month: 2, year: 1 }
}

export function getDateInputMasks(culture: string) {
  const { mask } = formats[culture] ?? formats['en-US']

  return { input: mask, inputDateTime24hr: `${mask} HH:mm` }
}

export function isImpossibleDateInput(value: string, culture: string): boolean {
  const format = formats[culture] ?? formats['en-US']
  const match = value.match(format.pattern)
  if (!match) return false

  const year = Number(match[format.year])
  const month = Number(match[format.month])
  const day = Number(match[format.day])
  const date = new Date(0)
  date.setFullYear(year, month - 1, day)

  return date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day
}

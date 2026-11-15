export type ShiftLocalDateTimeParts = {
  date: string
  time: string
}

export function splitShiftLocalDateTime(value: string): ShiftLocalDateTimeParts {
  const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})(?::\d{2}(?:\.\d+)?)?$/.exec(value)
  if (!match) {
    return { date: '', time: '' }
  }

  return { date: match[1], time: match[2] }
}

export function combineShiftLocalDateTime(date: string, time: string) {
  if (!date || !time) {
    return ''
  }

  return `${date}T${time}`
}

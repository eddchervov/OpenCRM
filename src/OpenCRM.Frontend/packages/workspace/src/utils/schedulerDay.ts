export type SchedulerCalendarItem = {
  id: string
  vehicleId: string
  vehicleDisplayName: string
  vehicleSubtitle?: string | null
  registrationPlate?: string | null
  brandLogoKey?: string | null
  serviceNames?: string[]
  employeeNames?: string[]
  status: number
  statusName?: string | null
  canReschedule?: boolean
  plannedStartTime?: string | null
  plannedEndDate?: string | null
  plannedEndTime?: string | null
  date: string
  location?: string | null
  detailUrl?: string | null
  readyForHandoverAt?: string | null
  handedOverAt?: string | null
}

export type SchedulerDayInterval = {
  start: number
  end: number
}

export type SchedulerDayLayoutItem = SchedulerDayInterval & {
  item: SchedulerCalendarItem
  top: number
  height: number
  lane: number
  laneCount: number
}

const MIN_CARD_HEIGHT = 56

export function parseSchedulerTime(value?: string | null) {
  if (!value) return null

  const match = /^(\d{1,2}):(\d{2})/.exec(value)
  if (!match) return null

  const hours = Number(match[1])
  const minutes = Number(match[2])
  if (hours > 23 || minutes > 59) return null

  return hours * 60 + minutes
}

export function getSchedulerDayInterval(item: SchedulerCalendarItem, date: string): SchedulerDayInterval | null {
  const start = parseSchedulerTime(item.plannedStartTime)
  if (start === null) return null

  const startDate = item.date.slice(0, 10)
  const endDate = (item.plannedEndDate || startDate).slice(0, 10)
  const end = parseSchedulerTime(item.plannedEndTime)
  const endMinute = end === null ? start + 60 : end
  const startOffset = getDateOffset(date, startDate)
  const endOffset = getDateOffset(date, endDate)
  if (startOffset === null || endOffset === null) return null

  const absoluteStart = startOffset * 1440 + start
  let absoluteEnd = endOffset * 1440 + endMinute
  if (absoluteEnd <= absoluteStart) absoluteEnd += 1440

  const clippedStart = Math.max(0, absoluteStart)
  const clippedEnd = Math.min(1440, absoluteEnd)
  if (clippedEnd <= clippedStart) return null

  return { start: clippedStart, end: clippedEnd }
}

export function getSchedulerDayWindow(
  items: SchedulerCalendarItem[],
  date: string,
  startTime: string,
  endTime: string
) {
  const start = parseSchedulerTime(startTime) ?? 8 * 60
  const configuredEnd = parseSchedulerTime(endTime) ?? 22 * 60
  let windowStart = start
  let windowEnd = configuredEnd <= start ? 1440 : configuredEnd

  for (const item of items) {
    const interval = getSchedulerDayInterval(item, date)
    if (!interval) continue

    windowStart = Math.min(windowStart, interval.start)
    windowEnd = Math.max(windowEnd, interval.end)
  }

  return { start: windowStart, end: windowEnd }
}

export function getSchedulerDayLayout(
  items: SchedulerCalendarItem[],
  date: string,
  windowStart: number,
  windowEnd: number,
  pixelsPerMinute = 1.2
): SchedulerDayLayoutItem[] {
  const entries = items.flatMap(item => {
    const interval = getSchedulerDayInterval(item, date)
    if (!interval) return []

    const minimumHeight = MIN_CARD_HEIGHT
    const visibleEnd = Math.max(interval.end, interval.start + minimumHeight / pixelsPerMinute)
    return [{ item, ...interval, minimumHeight, layoutEnd: Math.min(windowEnd, visibleEnd) }]
  }).sort((left, right) => left.start - right.start || left.end - right.end || left.item.id.localeCompare(right.item.id))

  const result: SchedulerDayLayoutItem[] = []
  let cluster: typeof entries = []
  let clusterEnd = -1

  const placeCluster = () => {
    const laneEnds: number[] = []
    const placed = cluster.map(entry => {
      let lane = laneEnds.findIndex(laneEnd => laneEnd <= entry.start)
      if (lane < 0) lane = laneEnds.length
      laneEnds[lane] = entry.layoutEnd
      return { entry, lane }
    })

    for (const { entry, lane } of placed) {
      const top = (entry.start - windowStart) * pixelsPerMinute
      const end = Math.min(windowEnd, Math.max(entry.end, entry.start + MIN_CARD_HEIGHT / pixelsPerMinute))
      result.push({
        item: entry.item,
        start: entry.start,
        end: entry.end,
        top,
        height: Math.min(Math.max(entry.minimumHeight, (end - entry.start) * pixelsPerMinute), (windowEnd - entry.start) * pixelsPerMinute),
        lane,
        laneCount: laneEnds.length
      })
    }
  }

  for (const entry of entries) {
    if (cluster.length && entry.start >= clusterEnd) {
      placeCluster()
      cluster = []
      clusterEnd = -1
    }

    cluster.push(entry)
    clusterEnd = Math.max(clusterEnd, entry.layoutEnd)
  }

  if (cluster.length) placeCluster()

  return result.sort((left, right) => left.start - right.start || left.lane - right.lane)
}

export function isSchedulerSlotAvailable(
  items: SchedulerCalendarItem[],
  date: string,
  location: string,
  start: number,
  duration = 60
) {
  const end = start + duration
  return !items.some(item => {
    if (item.status !== 1 && item.status !== 2) return false
    if (normalizeSchedulerLocation(item.location) !== normalizeSchedulerLocation(location)) return false

    const interval = getSchedulerDayInterval(item, date)
    if (interval && interval.start < Math.min(end, 1440) && interval.end > start) return true
    if (end <= 1440) return false

    const nextDay = addSchedulerMinutes(date, 1440).date
    const nextDayInterval = getSchedulerDayInterval(item, nextDay)
    return nextDayInterval !== null && nextDayInterval.start + 1440 < end && nextDayInterval.end + 1440 > 1440
  })
}

export function addSchedulerMinutes(date: string, minutes: number) {
  const day = new Date(`${date.slice(0, 10)}T00:00:00Z`)
  day.setUTCMinutes(day.getUTCMinutes() + minutes)
  return {
    date: day.toISOString().slice(0, 10),
    time: `${String(day.getUTCHours()).padStart(2, '0')}:${String(day.getUTCMinutes()).padStart(2, '0')}`
  }
}

export function normalizeSchedulerLocation(value?: string | null) {
  const normalized = value?.trim().toLocaleLowerCase('ru-RU')
  if (!normalized || normalized === 'без поста' || normalized === 'без зоны') return 'unassigned'

  return normalized
}

function getDateOffset(date: string, otherDate: string) {
  const current = Date.parse(`${date.slice(0, 10)}T00:00:00Z`)
  const other = Date.parse(`${otherDate.slice(0, 10)}T00:00:00Z`)
  if (!Number.isFinite(current) || !Number.isFinite(other)) return null

  return Math.trunc((other - current) / 86_400_000)
}

export function getSchedulerFreeStarts(
  items: SchedulerCalendarItem[],
  date: string,
  location: string,
  intervals: { startTime: string; endTime: string }[],
  duration: number
) {
  if (!Number.isFinite(duration) || duration <= 0) return []
  const starts = new Set<number>()
  for (const interval of intervals) {
    const start = parseSchedulerTime(interval.startTime)
    const end = interval.endTime === '24:00' ? 1440 : parseSchedulerTime(interval.endTime)
    if (start === null || end === null || end <= start) continue
    for (let minute = start; minute + duration <= end; minute += 30) {
      if (isSchedulerSlotAvailable(items, date, location, minute, duration)) starts.add(minute)
    }
  }

  return [...starts].sort((left, right) => left - right)
}

export function getClosedSchedulerPosts(posts: readonly { name: string; isActive: boolean }[]) {
  const active = new Set(posts.filter(post => post.isActive).map(post => normalizeSchedulerLocation(post.name)))

  return posts.filter(post => !post.isActive && !active.has(normalizeSchedulerLocation(post.name))).map(post => post.name)
}

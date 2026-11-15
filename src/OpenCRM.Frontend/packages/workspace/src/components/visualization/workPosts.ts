import type { WorkspaceScenarioCatalogItem } from '../../api/scenarioCatalog'
import type { WorkspaceWorkday, WorkspaceWorkdayItem } from '../../api/workday'
import type { WorkPostSceneItem } from './types'

export type VisualWorkEntry = { item: WorkspaceWorkdayItem; stage: 'planned' | 'inProgress' | 'ready'; isOverdue: boolean }
export type VisualWorkPost = WorkPostSceneItem & { entries: VisualWorkEntry[]; isLegacy: boolean }

export function isBookingOverdue(item: WorkspaceWorkdayItem, today: string, time: string): boolean {
  const date = item.date.slice(0, 10)
  if (date < today) return true
  if (date !== today || !item.startTime) return false

  return item.startTime.slice(0, 5) <= time
}

function locationKey(value: string | null | undefined): string {
  return value?.trim().toLocaleLowerCase('ru-RU') ?? ''
}

export function buildVisualWorkPosts(catalog: WorkspaceScenarioCatalogItem[], day: WorkspaceWorkday, today: string, time: string): {
  posts: VisualWorkPost[]
  unassigned: VisualWorkEntry[]
  ready: VisualWorkEntry[]
} {
  const entries: VisualWorkEntry[] = [
    ...day.inProgress.items.map(item => ({ item, stage: 'inProgress' as const, isOverdue: false })),
    ...day.planned.items.map(item => ({ item, stage: 'planned' as const, isOverdue: isBookingOverdue(item, today, time) }))
  ]

  const grouped = new Map<string, VisualWorkEntry[]>()
  for (const entry of entries) {
    const key = locationKey(entry.item.location)
    const group = grouped.get(key) ?? []
    group.push(entry)
    grouped.set(key, group)
  }

  const posts: VisualWorkPost[] = []
  const sortedCatalog = [...catalog].sort((left, right) => left.sortOrder - right.sortOrder || left.name.localeCompare(right.name, 'ru'))

  for (const post of sortedCatalog) {
    const key = locationKey(post.name)
    if (!post.isActive && !grouped.has(key)) continue
    posts.push(createPost(post.id, post.name, grouped.get(key) ?? [], !post.isActive))
    grouped.delete(key)
  }

  const unassigned = grouped.get('') ?? []
  grouped.delete('')
  for (const [key, group] of grouped) {
    posts.push(createPost('legacy:' + key, group[0].item.location!.trim(), group, true))
  }

  return {
    posts,
    unassigned,
    ready: day.ready.items.map(item => ({ item, stage: 'ready', isOverdue: false }))
  }
}

function createPost(id: string, name: string, entries: VisualWorkEntry[], isLegacy: boolean): VisualWorkPost {
  const sorted = [...entries].sort((left, right) => {
    const rank = (entry: VisualWorkEntry) => entry.stage === 'inProgress' ? 0 : entry.isOverdue ? 1 : 2

    return rank(left) - rank(right)
      || (left.item.date + (left.item.startTime ?? '99:99')).localeCompare(right.item.date + (right.item.startTime ?? '99:99'))
      || left.item.id.localeCompare(right.item.id)
  })

  const occupying = sorted.filter(entry => entry.stage === 'inProgress' || entry.isOverdue)
  const first = occupying[0]
  const state = occupying.length > 1 ? 'conflict'
    : !first ? 'free'
    : first.isOverdue ? 'overdue'
    : first.item.workPausedCount > 0 ? 'paused' : 'inProgress'

  return {
    id, name, state, isLegacy, entries: sorted,
    vehicleLabel: first?.item.vehicleName,
    registrationPlate: first?.item.registrationPlate,
    count: occupying.length
  }
}

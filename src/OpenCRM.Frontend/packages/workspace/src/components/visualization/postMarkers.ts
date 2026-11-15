type ProjectedPost = {
  id: string
  left: number
  top: number
  depth: number
  occupied: boolean
}

export type PostMarker = {
  left: number
  top: number
  width: number
  visible: boolean
  compact: boolean
}

export function layoutPostMarkers(posts: ProjectedPost[], width: number, height: number, selectedId?: string, labelWidth = 136) {
  const compact = posts.length > 12 || (width < 420 && posts.length > 4)
  const placed: Array<{ left: number; top: number; right: number; bottom: number }> = []
  const result: Record<string, PostMarker> = {}
  const candidates = [...posts].sort((first, second) =>
    Number(second.id === selectedId) - Number(first.id === selectedId)
    || Number(second.occupied) - Number(first.occupied)
    || second.top - first.top)

  for (const post of candidates) {
    const markerWidth = compact ? 28 : Math.min(labelWidth, width - 16)
    const markerHeight = compact ? 28 : post.occupied ? 62 : 44
    const left = Math.max(markerWidth / 2 + 4, Math.min(width - markerWidth / 2 - 4, post.left))
    const top = Math.max(4, Math.min(height - markerHeight - 4, post.top))
    const box = { left: left - markerWidth / 2, right: left + markerWidth / 2, top, bottom: top + markerHeight }
    const inView = width > markerWidth && height > markerHeight && post.depth > -1 && post.depth < 1
      && post.left >= 0 && post.left <= width && post.top >= 0 && post.top <= height
    const overlaps = placed.some(other =>
      box.left < other.right + 4 && box.right + 4 > other.left
      && box.top < other.bottom + 4 && box.bottom + 4 > other.top)
    const visible = inView && !overlaps
    result[post.id] = { left, top, width: markerWidth, visible, compact }
    if (visible) placed.push(box)
  }

  return result
}

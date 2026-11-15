export function getWorkdayProgress(workTotalCount: number, workCompletedCount: number) {
  const total = Math.max(0, workTotalCount)
  const completed = Math.min(total, Math.max(0, workCompletedCount))

  return {
    total,
    completed,
    percent: total ? Math.round(completed / total * 100) : 0,
    isComplete: total > 0 && completed === total
  }
}

export function shouldOpenRecord(canOpen: boolean, event: Pick<MouseEvent, 'button' | 'ctrlKey' | 'metaKey' | 'shiftKey' | 'altKey'>) {
  return canOpen && event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey
}

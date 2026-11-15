import type { WorkspaceWorkday } from './workday'

export async function loadWorkspaceVisualization(organizationId: string, date: string, signal: AbortSignal): Promise<WorkspaceWorkday> {
  const url = `/api/v1/organizations/${encodeURIComponent(organizationId)}/workspace/visualization?date=${encodeURIComponent(date)}`
  const response = await fetch(url, {
    credentials: 'same-origin',
    signal,
    headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
  })

  if (!response.ok) {
    const failure = await response.json().catch(() => null)
    throw new Error(failure?.message ?? 'Не удалось загрузить визуализацию.')
  }

  return response.json()
}

import { translate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/workspaceApiMessages'
export type WorkJournalItem = {
  id: string
  vehicleDisplayName: string
  registrationPlate?: string | null
  clientName?: string | null
  services: string[]
  status: number
  statusName: string
  plannedDate?: string | null
  plannedStartTime?: string | null
  completedDate?: string | null
  amount: number
}

export type WorkJournalData = {
  items: WorkJournalItem[]
  totalCount: number
  page: number
  pageSize: number
}

export async function loadWorkJournal(
  organizationId: string,
  options: { status: number, handover?: string, dateFrom?: string, dateTo?: string, search: string, page: number, pageSize: number },
  signal?: AbortSignal
): Promise<WorkJournalData> {
  const url = new URL(`/api/v1/organizations/${organizationId}/maintenance-journal`, window.location.origin)
  url.searchParams.set('status', String(options.status))
  if (options.handover) url.searchParams.set('handover', options.handover)
  url.searchParams.set('page', String(options.page))
  url.searchParams.set('pageSize', String(options.pageSize))
  if (options.dateFrom) url.searchParams.set('dateFrom', options.dateFrom)
  if (options.dateTo) url.searchParams.set('dateTo', options.dateTo)
  if (options.search.trim()) url.searchParams.set('search', options.search.trim())

  const response = await fetch(url, {
    credentials: 'same-origin',
    headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
    signal
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? translate('workspaceApi.loadDetailingJournal'))
  }

  return response.json()
}

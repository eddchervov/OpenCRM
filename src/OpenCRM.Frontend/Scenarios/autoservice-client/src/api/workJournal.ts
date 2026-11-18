import { translate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/workJournalMessages'

export type WorkJournalWorkItem = {
  occurredAt: string
  kind: string
  eventCode: string
  kindName: string
  title: string
  vehicleDisplayName: string
  vehicleSubtitle?: string | null
  clientId?: string | null
  clientName?: string | null
  resultText?: string | null
  metaText?: string | null
  currencyCode?: string
  price?: number | null
  detailUrl?: string | null
  vehicleUrl?: string | null
}

export type WorkJournalHistoryItem = {
  occurredAt: string
  kind: string
  eventCode: string
  kindName: string
  title: string
  description?: string | null
  vehicleDisplayName: string
  vehicleSubtitle?: string | null
  actorDisplayName?: string | null
  metaText?: string | null
  detailUrl?: string | null
  vehicleUrl?: string | null
}

export type WorkJournalData = {
  workItems: WorkJournalWorkItem[]
  historyItems: WorkJournalHistoryItem[]
  selectedKind: string
  selectedTab: string
  workItemsCount: number
  historyItemsCount: number
  maintenanceEventsCount: number
  defectsCreatedCount: number
  defectsClosedCount: number
  completedMaintenancesCount: number
  servedClientsCount: number
}

export async function loadWorkJournal(
  organizationId: string,
  options: { dateFrom: string, dateTo: string, kind?: string, tab?: string }
): Promise<WorkJournalData> {
  const url = new URL(`/api/v1/organizations/${organizationId}/work-journal`, window.location.origin)
  url.searchParams.set('dateFrom', options.dateFrom)
  url.searchParams.set('dateTo', options.dateTo)

  if (options.kind) {
    url.searchParams.set('kind', options.kind)
  }

  if (options.tab) {
    url.searchParams.set('tab', options.tab)
  }

  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? translate('workJournal.loadError'))
  }

  return response.json()
}

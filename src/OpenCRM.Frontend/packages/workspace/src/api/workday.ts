import { translate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/workdayBoardMessages'

export type WorkspaceWorkdayItem = {
  id: string
  vehicleName: string
  vehicleBrandLogoKey?: string | null
  registrationPlate?: string | null
  clientName?: string | null
  clientPhone?: string | null
  services: string[]
  workTotalCount: number
  hasTrackedWork?: boolean
  workCompletedCount: number
  workPausedCount: number
  readyForHandoverAt?: string | null
  handedOverAt?: string | null
  date: string
  startTime?: string | null
  endDate?: string | null
  endTime?: string | null
  location?: string | null
  performer?: string | null
}

export type WorkspaceWorkdayGroup = {
  totalCount: number
  items: WorkspaceWorkdayItem[]
}

export type WorkspaceWorkday = {
  hasAnyWork: boolean
  planned: WorkspaceWorkdayGroup
  inProgress: WorkspaceWorkdayGroup
  ready: WorkspaceWorkdayGroup
  handedOver: WorkspaceWorkdayGroup
}

export async function loadWorkspaceWorkday(organizationId: string, date: string, signal: AbortSignal): Promise<WorkspaceWorkday> {
  const url = `/api/v1/organizations/${encodeURIComponent(organizationId)}/workspace/workday?date=${encodeURIComponent(date)}`
  const response = await fetch(url, {
    credentials: 'same-origin',
    signal,
    headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? translate('workdayBoard.apiError'))
  }

  return response.json()
}

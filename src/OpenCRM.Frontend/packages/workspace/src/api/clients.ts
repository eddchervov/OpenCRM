import { translate as t } from '@opencrm/ui/i18n'
import '@opencrm/workspace/clientMessages'

export type WorkspaceClientTag = {
  id: string
  name: string
}

export type WorkspaceClientVehicle = {
  id: string
  displayName: string
  brandLogoKey?: string | null
  photoUrl?: string | null
  registrationPlate?: string | null
  maintenanceCount: number
  lastMaintenanceDate?: string | null
}

export type WorkspaceClientCurrencyTotals = {
  currencyCode: string
  totalPaidAmount: number
  totalDebtAmount: number
}

export type WorkspaceClient = {
  id: string
  organizationId: string
  name: string
  lastName?: string | null
  firstName?: string | null
  middleName?: string | null
  companyTypeCode?: string | null
  phone?: string | null
  partyTypeCode?: string | null
  inn?: string | null
  kpp?: string | null
  legalAddress?: string | null
  comment?: string | null
  statusCode?: string | null
  statusName?: string | null
  responsibleUserId?: string | null
  responsibleDisplayName?: string | null
  tags?: WorkspaceClientTag[]
  vehiclesCount: number
  maintenanceCount: number
  lastMaintenanceDate?: string | null
  currencyCode?: string
  otherCurrencyTotals?: WorkspaceClientCurrencyTotals[]
  totalPaidAmount?: number | null
  totalDebtAmount?: number | null
  vehicles?: WorkspaceClientVehicle[]
}

export type WorkspaceClientMaintenanceItem = {
  id: string
  vehicleId: string
  vehicleDisplayName: string
  type: number
  status: number
  date: string
  plannedDate?: string | null
  completedDate?: string | null
  plannedStartTime?: string | null
  plannedEndDate?: string | null
  plannedEndTime?: string | null
  mileage?: number | null
  comment?: string | null
  serviceNames?: string[]
  currencyCode?: string
  grossAmount?: number | null
  netAmount?: number | null
  paidAmount?: number | null
  readyForHandoverAt?: string | null
  handedOverAt?: string | null
  detailUrl: string
  vehicleUrl: string
}

export type WorkspaceClientDefectItem = {
  id: string
  vehicleId: string
  vehicleDisplayName: string
  title: string
  severityCode: string
  severityName: string
  statusCode: string
  statusName: string
  createdAt: string
  closedAt?: string | null
  detailUrl: string
  vehicleUrl: string
}

export type WorkspaceClientHistoryEvent = {
  id: string
  organizationClientId: string
  eventType: string
  title: string
  description?: string | null
  actorDisplayName?: string | null
  relatedEntityType?: string | null
  relatedEntityId?: string | null
  createdAt: string
}

export type WorkspaceClientDetails = {
  client: WorkspaceClient & {    createdAt: string
    isArchived: boolean
    archivedAt?: string | null
  }
  canViewMaintenance?: boolean
  canEdit?: boolean
  currencyCode?: string
  otherCurrencyTotals?: WorkspaceClientCurrencyTotals[]
  totalDebtAmount?: number | null
  totalPaidAmount?: number | null
  upcomingMaintenance?: WorkspaceClientMaintenanceItem | null
  recentMaintenances: WorkspaceClientMaintenanceItem[]
  defects: WorkspaceClientDefectItem[]
  historyItems: WorkspaceClientHistoryEvent[]
  openDefectsCount: number
  closedDefectsCount: number
}

export type WorkspaceClientSaveRequest = {
  name: string
  lastName?: string | null
  firstName?: string | null
  middleName?: string | null
  companyTypeCode?: string | null
  phone?: string | null
  partyTypeCode?: string | null
  inn?: string | null
  kpp?: string | null
  legalAddress?: string | null
  comment?: string | null
  statusCode?: string | null
  responsibleUserId?: string | null  tagNamesText?: string | null
}

export type WorkspaceClientSegment = {
  code: string
  name: string
  count: number
}

export type WorkspaceClientOption = {
  id: string
  name: string
  phone?: string | null
  details?: string | null
  search?: string | null
}

export type WorkspaceClientLookupItem = {
  id: string
  name: string
  phone?: string | null
  partyTypeCode?: string | null
}

export type WorkspaceClientLookupPage = {
  items: WorkspaceClientLookupItem[]
  totalCount: number
}

export type WorkspaceClientsPage = {
  items: WorkspaceClient[]
  tags: WorkspaceClientTag[]
  segments: WorkspaceClientSegment[]
  page: number
  pageSize: number
  totalCount: number
  allCount: number
  totalVehiclesCount: number
  totalMaintenanceCount: number
}

export type WorkspaceClientMaintenancesPage = {
  items: WorkspaceClientMaintenanceItem[]
  totalCount: number
  page: number
  pageSize: number
}

export type WorkspaceClientMaintenancesQuery = {
  page: number
  pageSize: number
  searchQuery?: string
  status?: 'upcoming' | 'completed'
}

function getRequestVerificationToken() {
  return document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
}

function getMutationHeaders() {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    'Content-Type': 'application/json',
    'X-Requested-With': 'XMLHttpRequest'
  }
  const token = getRequestVerificationToken()

  if (token) {
    headers.RequestVerificationToken = token
  }

  return headers
}

export async function loadClientsPage(
  organizationId: string,
  page: number,
  pageSize: number,
  options: {
    searchQuery?: string
    sort?: string
    segmentCode?: string
    isArchived?: boolean
    statusCode?: string
    responsibleUserId?: string
    tagId?: string
    partyTypeCode?: string
  } = {}
): Promise<WorkspaceClientsPage> {
  const url = new URL(`/api/v1/organizations/${organizationId}/clients/${page}/${pageSize}`, window.location.origin)

  if (options.searchQuery) {
    url.searchParams.set('searchQuery', options.searchQuery)
  }

  if (options.sort) {
    url.searchParams.set('sort', options.sort)
  }

  if (options.segmentCode) {
    url.searchParams.set('segmentCode', options.segmentCode)
  }

  if (options.isArchived) {
    url.searchParams.set('isArchived', 'true')
  }

  if (options.statusCode) {
    url.searchParams.set('statusCode', options.statusCode)
  }

  if (options.responsibleUserId) {
    url.searchParams.set('responsibleUserId', options.responsibleUserId)
  }

  if (options.tagId) {
    url.searchParams.set('tagId', options.tagId)
  }

  if (options.partyTypeCode) {
    url.searchParams.set('partyTypeCode', options.partyTypeCode)
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
    throw new Error(error?.message ?? t('clients.loadError'))
  }

  return response.json()
}

export async function loadClientDetails(
  organizationId: string,
  clientId: string
): Promise<WorkspaceClientDetails> {
  const url = new URL(`/api/v1/organizations/${organizationId}/clients/${clientId}`, window.location.origin)

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
    throw new Error(error?.message ?? t('clients.cardLoadError'))
  }

  return response.json()
}

export async function loadClientMaintenances(
  organizationId: string,
  clientId: string,
  query: WorkspaceClientMaintenancesQuery
): Promise<WorkspaceClientMaintenancesPage> {
  const url = new URL(`/api/v1/organizations/${organizationId}/clients/${clientId}/maintenances`, window.location.origin)
  url.searchParams.set('page', String(query.page))
  url.searchParams.set('pageSize', String(query.pageSize))

  const searchQuery = query.searchQuery?.trim()
  if (searchQuery) {
    url.searchParams.set('searchQuery', searchQuery)
  }

  if (query.status) {
    url.searchParams.set('status', query.status)
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
    throw new Error(error?.message ?? t('clients.historyLoadError'))
  }

  return response.json()
}

export async function createClient(
  organizationId: string,
  payload: WorkspaceClientSaveRequest
): Promise<WorkspaceClientDetails> {
  const url = new URL(`/api/v1/organizations/${organizationId}/clients`, window.location.origin)
  return saveClient(url, 'POST', payload)
}

export async function updateClient(
  organizationId: string,
  clientId: string,
  payload: WorkspaceClientSaveRequest
): Promise<WorkspaceClientDetails> {
  const url = new URL(`/api/v1/organizations/${organizationId}/clients/${clientId}`, window.location.origin)
  return saveClient(url, 'PUT', payload)
}

export async function updateClientComment(organizationId: string, clientId: string, comment: string | null): Promise<WorkspaceClientDetails> {
  const url = new URL(`/api/v1/organizations/${organizationId}/clients/${clientId}/comment`, window.location.origin)
  return saveClient(url, 'PUT', { comment })
}

export async function loadClientLookup(organizationId: string, searchQuery: string, page = 1): Promise<WorkspaceClientLookupPage> {
  const url = new URL(`/api/v1/organizations/${encodeURIComponent(organizationId)}/clients/lookup`, window.location.origin)
  url.searchParams.set('searchQuery', searchQuery)
  url.searchParams.set('page', String(page))
  const response = await fetch(url.toString(), {
    credentials: 'same-origin', headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message || t('clients.lookupError'))
  }

  return response.json()
}

export async function loadClientOptions(organizationId: string): Promise<WorkspaceClientOption[]> {
  const url = new URL(`/api/v1/organizations/${organizationId}/clients/options`, window.location.origin)

  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    return []
  }

  return response.json()
}

async function saveClient(
  url: URL,
  method: 'POST' | 'PUT',
  payload: WorkspaceClientSaveRequest | { comment: string | null }
): Promise<WorkspaceClientDetails> {
  const response = await fetch(url.toString(), {
    method,
    credentials: 'same-origin',
    headers: getMutationHeaders(),
    body: JSON.stringify(payload)
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? t('clients.clientSaveError'))
  }

  return response.json()
}

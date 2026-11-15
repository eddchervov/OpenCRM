export type FleetCounterVehicle = {
  vehicleId: string
  organizationClientId?: string | null
  organizationClientName?: string | null
  vehicleDisplayName: string
  vehicleSubtitle?: string | null
  mileageUnit: number
  currentValue?: number | null
  updatedAt?: string | null
}

export type FleetCounterEntry = {
  id: string
  vehicleId: string
  organizationClientId?: string | null
  organizationClientName?: string | null
  vehicleDisplayName: string
  vehicleSubtitle?: string | null
  mileageUnit: number
  value: number
  recordedAt: string
  comment?: string | null
  sourceType: string
  sourceName: string
  timelineType: string
  maintenanceType?: number | null
  canManage: boolean
}

export type FleetCounterVehiclesPage = {
  items: FleetCounterVehicle[]
  page: number
  pageSize: number
  totalCount: number
  allCount: number
  vehiclesWithCountersCount: number
  hasMore: boolean
}

export type FleetCounterHistoryPage = {
  items: FleetCounterEntry[]
  page: number
  pageSize: number
  totalCount: number
  allCount: number
  hasMore: boolean
}

export type FleetCounterSaveRequest = {
  entryId?: string | null
  vehicleId: string
  value: number
  recordedAt: string
  comment?: string | null
}

export async function loadCounterVehicles(
  organizationId: string,
  options: { page: number, pageSize: number, organizationClientId?: string, sort?: string }
): Promise<FleetCounterVehiclesPage> {
  const url = new URL(`/api/v1/organizations/${organizationId}/counters/vehicles/${options.page}/${options.pageSize}`, window.location.origin)
  appendParam(url.searchParams, 'organizationClientId', options.organizationClientId)
  appendParam(url.searchParams, 'sort', options.sort)
  return sendJson(url.toString(), 'GET', undefined, 'Не удалось загрузить технику.')
}

export async function loadCounterHistory(
  organizationId: string,
  options: { page: number, pageSize: number, vehicleId?: string, organizationClientId?: string, sort?: string }
): Promise<FleetCounterHistoryPage> {
  const url = new URL(`/api/v1/organizations/${organizationId}/counters/history/${options.page}/${options.pageSize}`, window.location.origin)
  appendParam(url.searchParams, 'vehicleId', options.vehicleId)
  appendParam(url.searchParams, 'organizationClientId', options.organizationClientId)
  appendParam(url.searchParams, 'sort', options.sort)
  return sendJson(url.toString(), 'GET', undefined, 'Не удалось загрузить историю.')
}

export async function loadCounterChart(organizationId: string, vehicleId: string): Promise<FleetCounterEntry[]> {
  const url = new URL(`/api/v1/organizations/${organizationId}/counters/chart`, window.location.origin)
  url.searchParams.set('vehicleId', vehicleId)
  return sendJson(url.toString(), 'GET', undefined, 'Не удалось загрузить график.')
}

export async function saveCounter(organizationId: string, payload: FleetCounterSaveRequest): Promise<{ message?: string }> {
  return sendJson(`/api/v1/organizations/${organizationId}/counters`, 'POST', payload, 'Не удалось сохранить показание.')
}

export async function deleteCounter(organizationId: string, entryId: string): Promise<{ message?: string }> {
  return sendJson(`/api/v1/organizations/${organizationId}/counters/${entryId}`, 'DELETE', undefined, 'Не удалось удалить показание.')
}

function appendParam(params: URLSearchParams, name: string, value?: string | null) {
  const normalized = value?.trim()
  if (normalized) {
    params.set(name, normalized)
  }
}

function getRequestVerificationToken() {
  return document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
}

function getHeaders(hasBody: boolean) {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    'X-Requested-With': 'XMLHttpRequest'
  }
  const token = getRequestVerificationToken()
  if (token) {
    headers.RequestVerificationToken = token
  }
  if (hasBody) {
    headers['Content-Type'] = 'application/json'
  }
  return headers
}

async function sendJson<T>(url: string, method: 'GET' | 'POST' | 'DELETE', payload: unknown, message: string): Promise<T> {
  const hasBody = payload !== undefined
  const response = await fetch(url, {
    method,
    credentials: 'same-origin',
    headers: getHeaders(hasBody),
    body: hasBody ? JSON.stringify(payload) : undefined
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? message)
  }

  return response.json()
}

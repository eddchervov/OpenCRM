export type FleetDefect = {
  id: string
  organizationId: string
  vehicleId: string
  vehicleDisplayName: string
  vehicleSubtitle?: string | null
  title: string
  description?: string | null
  severityCode: string
  severityName: string
  statusCode: string
  statusName: string
  isClosed: boolean
  linkedMaintenanceCount: number
  linkedMaintenance?: FleetDefectMaintenance | null
  linkedMaintenances: FleetDefectMaintenance[]
  createdAt: string
  updatedAt: string
  closedAt?: string | null
}

export type FleetDefectMaintenance = {
  id: string
  date: string
  plannedDate?: string | null
  completedDate?: string | null
  status: number
  typeName: string
  comment?: string | null
  price?: number | null
  mileage?: number | null
}

export type FleetDefectHistoryItem = {
  title: string
  description?: string | null
  actorDisplayName?: string | null
  createdAt: string
}

export type FleetDefectMaintenanceOption = {
  id: string
  text: string
}

export type FleetDefectsPage = {
  items: FleetDefect[]
  page: number
  pageSize: number
  totalCount: number
  allCount: number
  openCount: number
  resolvedCount: number
  hasMore: boolean
  selectedVehicleId?: string | null
}

export type FleetDefectSaveRequest = {
  vehicleId: string
  title: string
  description?: string | null
  severityCode: string
  statusCode: string
}

export async function loadDefects(
  organizationId: string,
  options: { page: number, pageSize: number, searchQuery?: string, statusCode?: string, vehicleId?: string, sort?: string }
): Promise<FleetDefectsPage> {
  const url = new URL(`/api/v1/organizations/${organizationId}/defects/${options.page}/${options.pageSize}`, window.location.origin)
  appendParam(url.searchParams, 'searchQuery', options.searchQuery)
  appendParam(url.searchParams, 'statusCode', options.statusCode)
  appendParam(url.searchParams, 'vehicleId', options.vehicleId)
  appendParam(url.searchParams, 'sort', options.sort)
  return sendJson(url.toString(), 'GET', undefined, 'Не удалось загрузить неисправности.')
}

export async function loadDefect(organizationId: string, defectId: string): Promise<FleetDefect> {
  return sendJson(`/api/v1/organizations/${organizationId}/defects/${defectId}`, 'GET', undefined, 'Не удалось загрузить неисправность.')
}

export async function loadDefectHistory(organizationId: string, defectId: string): Promise<FleetDefectHistoryItem[]> {
  return sendJson(`/api/v1/organizations/${organizationId}/defects/${defectId}/history`, 'GET', undefined, 'Не удалось загрузить историю неисправности.')
}

export async function loadDefectMaintenanceOptions(organizationId: string, defectId: string): Promise<FleetDefectMaintenanceOption[]> {
  return sendJson(`/api/v1/organizations/${organizationId}/defects/${defectId}/maintenance-options`, 'GET', undefined, 'Не удалось загрузить обслуживания.')
}

export async function createDefect(organizationId: string, payload: FleetDefectSaveRequest): Promise<{ id: string }> {
  return sendJson(`/api/v1/organizations/${organizationId}/defects`, 'POST', payload, 'Не удалось добавить неисправность.')
}

export async function updateDefect(organizationId: string, defectId: string, payload: FleetDefectSaveRequest): Promise<{ message?: string }> {
  return sendJson(`/api/v1/organizations/${organizationId}/defects/${defectId}`, 'PUT', payload, 'Не удалось сохранить неисправность.')
}

export async function updateDefectStatus(organizationId: string, defectId: string, statusCode: string): Promise<{ message?: string }> {
  return sendJson(`/api/v1/organizations/${organizationId}/defects/${defectId}/status`, 'POST', { statusCode }, 'Не удалось изменить статус.')
}

export async function linkDefectMaintenance(organizationId: string, defectId: string, maintenanceId: string): Promise<{ message?: string }> {
  return sendJson(`/api/v1/organizations/${organizationId}/defects/${defectId}/maintenance/${maintenanceId}`, 'POST', {}, 'Не удалось привязать обслуживание.')
}

export async function unlinkDefectMaintenance(organizationId: string, defectId: string, maintenanceId: string): Promise<{ message?: string }> {
  return sendJson(`/api/v1/organizations/${organizationId}/defects/${defectId}/maintenance/${maintenanceId}`, 'DELETE', undefined, 'Не удалось удалить связь.')
}

export async function deleteDefect(organizationId: string, defectId: string): Promise<{ message?: string }> {
  return sendJson(`/api/v1/organizations/${organizationId}/defects/${defectId}`, 'DELETE', undefined, 'Не удалось удалить неисправность.')
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

async function sendJson<T>(url: string, method: 'GET' | 'POST' | 'PUT' | 'DELETE', payload: unknown, message: string): Promise<T> {
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

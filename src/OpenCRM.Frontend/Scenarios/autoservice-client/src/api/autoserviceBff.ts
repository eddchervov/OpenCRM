import type { WorkspaceContext, WorkspaceMenuItem, WorkspaceQuickAction } from '@opencrm/workspace'

export type AutoServiceClientHost = {
  organizationId: string
  contextUrl: string
  workspaceUrl: string
}

export type AutoServiceMenuItem = WorkspaceMenuItem
export type AutoServiceQuickAction = WorkspaceQuickAction
export type AutoServiceWorkspaceContext = WorkspaceContext

export type AutoServiceMaintenanceCalendarItem = {
  id: string
  vehicleId: string
  vehicleDisplayName: string
  vehicleSubtitle?: string | null
  registrationPlate?: string | null
  brandLogoKey?: string | null
  serviceNames?: string[]
  employeeNames?: string[]
  readyForHandoverAt?: string | null
  handedOverAt?: string | null
  typeName?: string | null
  status: number
  statusName?: string | null
  statusBadgeClass?: string | null
  date: string
  plannedStartTime?: string | null
  plannedEndDate?: string | null
  plannedEndTime?: string | null
  location?: string | null
  comment?: string | null
  price?: number | null
  detailUrl?: string | null
}

export type AutoServiceCatalogItem = {
  id: string
  kindCode: string
  code: string
  name: string
  description?: string | null
  sortOrder: number
  isSystem: boolean
  isUsed: boolean
  isActive: boolean
}

export type AutoServiceScenarioCatalog = {
  organizationId: string
  scenarioCode: string
  workTypes: AutoServiceCatalogItem[]
  services: AutoServiceCatalogItem[]
  workPosts: AutoServiceCatalogItem[]
  documents: AutoServiceCatalogItem[]
  planningRules: AutoServiceCatalogItem[]
  materials: AutoServiceCatalogItem[]
}

const contextRequestTimeoutMs = 20000

export type CreateAutoServiceQuickBookingRequest = {
  isPlannedBooking?: boolean
  vehicleId?: string | null
  registrationPlate: string
  allowOtherRegistrationPlateFormat?: boolean
  kind?: number
  modelId?: number | null
  manualBrandName?: string | null
  manualModelName?: string | null
  vehicleText?: string | null
  organizationClientId?: string | null
  buyerClientId?: string | null
  clientName?: string | null
  clientPhone?: string | null
  serviceId?: string
  services?: import('@opencrm/workspace').WorkspaceMaintenanceDetail[]
  plannedDate: string
  plannedStartTime?: string | null
  plannedEndDate?: string | null
  plannedEndTime?: string | null
  price?: number | null
  location?: string | null
  comment?: string | null
}

export type AutoServiceQuickBookingResult = {
  vehicleId: string
  maintenanceId: string
  organizationClientId?: string | null
  vehicleCreated: boolean
  clientCreated: boolean
  vehicleUrl: string
  maintenanceUrl: string
}

export function resolveHostConfig(): AutoServiceClientHost {
  const config = window.opencrmAutoServiceClient ?? {}
  const organizationId = config.organizationId ?? ''

  return {
    organizationId,
    contextUrl: config.contextUrl ?? '',
    workspaceUrl: config.workspaceUrl ?? ''
  }
}

export async function loadAutoServiceContext(contextUrl: string): Promise<AutoServiceWorkspaceContext> {
  const embeddedContext = window.opencrmAutoServiceClient?.context
  if (embeddedContext) {
    return embeddedContext
  }

  if (!window.opencrmAutoServiceClient?.contextUrl) {
    throw new Error('Клиент автосервиса нужно открывать через рабочее пространство организации.')
  }

  const response = await fetchWorkspaceContext(contextUrl, 'рабочее пространство автосервиса')

  if (response.status === 401) {
    window.location.href = `/auth/login?returnUrl=${encodeURIComponent(window.location.pathname + window.location.search + window.location.hash)}`
    throw new Error('Требуется авторизация.')
  }

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить рабочее пространство автосервиса.')
  }

  return response.json()
}

async function fetchWorkspaceContext(contextUrl: string, label: string) {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), contextRequestTimeoutMs)

  try {
    return await fetch(contextUrl, {
      method: 'GET',
      credentials: 'same-origin',
      signal: controller.signal,
      headers: {
        Accept: 'application/json',
        'X-Requested-With': 'XMLHttpRequest'
      }
    })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw new Error(`Не удалось загрузить ${label}: сервер не ответил за 20 секунд.`)
    }

    throw error
  } finally {
    window.clearTimeout(timeoutId)
  }
}

export async function loadMaintenanceCalendar(
  calendarUrl: string,
  dateFrom: string,
  dateTo: string,
  options: { includeAllStatuses?: boolean } = {}
): Promise<AutoServiceMaintenanceCalendarItem[]> {
  const url = new URL(calendarUrl, window.location.origin)
  url.searchParams.set('dateFrom', dateFrom)
  url.searchParams.set('dateTo', dateTo)
  if (options.includeAllStatuses) {
    url.searchParams.set('includeAllStatuses', 'true')
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
    throw new Error(error?.message ?? 'Не удалось загрузить записи.')
  }

  return response.json()
}

export async function loadScenarioCatalog(organizationId: string): Promise<AutoServiceScenarioCatalog> {
  const response = await fetch(`/api/v1/organizations/${organizationId}/scenario-catalog`, {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить справочники.')
  }

  return response.json()
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

function readErrorMessage(error: unknown, fallback: string) {
  if (!error || typeof error !== 'object') return fallback

  const response = error as { message?: unknown, errors?: unknown }
  if (response.errors && typeof response.errors === 'object') {
    const messages = Object.values(response.errors as Record<string, unknown>)
      .flatMap(value => Array.isArray(value) ? value : [value])
      .filter((value): value is string => typeof value === 'string' && value.length > 0)

    if (messages.length > 0) return messages.join(' ')
  }

  return typeof response.message === 'string' && response.message.length > 0
    ? response.message
    : fallback
}
export async function createAutoServiceQuickBooking(
  organizationId: string,
  payload: CreateAutoServiceQuickBookingRequest
): Promise<AutoServiceQuickBookingResult> {
  const response = await fetch(`/api/v1/organizations/${organizationId}/autoservice/bookings/quick`, {
    method: 'POST',
    credentials: 'same-origin',
    headers: getMutationHeaders(),
    body: JSON.stringify(payload)
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(readErrorMessage(error, 'Не удалось создать приемку.'))
  }

  return response.json()
}

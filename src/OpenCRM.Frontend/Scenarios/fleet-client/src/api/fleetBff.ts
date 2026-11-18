import type { WorkspaceContext, WorkspaceMenuItem, WorkspaceQuickAction } from '@opencrm/workspace'

import { translate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/workspaceApiMessages'

export type FleetClientHost = {
  organizationId: string
  contextUrl: string
  workspaceUrl: string
}

export type FleetMenuItem = WorkspaceMenuItem
export type FleetQuickAction = WorkspaceQuickAction

export type FleetMaintenanceCalendarItem = {
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
  currencyCode?: string | null
  detailUrl?: string | null
}

export type FleetWorkspaceContext = WorkspaceContext

export type FleetCatalogItem = {
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

export type FleetScenarioCatalog = {
  organizationId: string
  scenarioCode: string
  workTypes: FleetCatalogItem[]
  services: FleetCatalogItem[]
  workPosts: FleetCatalogItem[]
  documents: FleetCatalogItem[]
  planningRules: FleetCatalogItem[]
  materials: FleetCatalogItem[]
}

const contextRequestTimeoutMs = 20000

export type CreateFleetQuickBookingRequest = {
  registrationPlate: string
  kind?: number
  modelId?: number | null
  manualBrandName?: string | null
  manualModelName?: string | null
  vehicleText?: string | null
  organizationClientId?: string | null
  serviceId: string
  plannedDate: string
  plannedStartTime?: string | null
  plannedEndDate?: string | null
  plannedEndTime?: string | null
  price?: number | null
  location?: string | null
  comment?: string | null
}

export type FleetQuickBookingResult = {
  vehicleId: string
  maintenanceId: string
  organizationClientId?: string | null
  vehicleCreated: boolean
  clientCreated: boolean
  vehicleUrl: string
  maintenanceUrl: string
}

export function resolveHostConfig(): FleetClientHost {
  const config = window.opencrmFleetClient ?? {}
  const organizationId = config.organizationId ?? ''

  return {
    organizationId,
    contextUrl: config.contextUrl ?? `/api/v1/fleet/organizations/${organizationId}/workspace/context`,
    workspaceUrl: config.workspaceUrl ?? `/fleet/${organizationId}`
  }
}

export async function loadFleetContext(contextUrl: string): Promise<FleetWorkspaceContext> {
  const response = await fetchWorkspaceContext(contextUrl, translate('workspaceApi.fleetScenario'))

  if (response.status === 401) {
    window.location.href = `/auth/login?returnUrl=${encodeURIComponent(window.location.pathname + window.location.search)}`
    throw new Error(translate('workspaceApi.authRequired'))
  }

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? translate('workspaceApi.loadScenarioContext', { scenario: translate('workspaceApi.fleetScenario') }))
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
      throw new Error(translate('workspaceApi.scenarioRequestTimeout', { scenario: label }))
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
): Promise<FleetMaintenanceCalendarItem[]> {
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
    throw new Error(error?.message ?? translate('workspaceApi.loadRecords'))
  }

  return response.json()
}

export async function loadScenarioCatalog(organizationId: string): Promise<FleetScenarioCatalog> {
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
    throw new Error(error?.message ?? translate('workspaceApi.loadCatalog'))
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

export async function createFleetQuickBooking(
  organizationId: string,
  payload: CreateFleetQuickBookingRequest
): Promise<FleetQuickBookingResult> {
  const response = await fetch(`/api/v1/organizations/${organizationId}/fleet/bookings/quick`, {
    method: 'POST',
    credentials: 'same-origin',
    headers: getMutationHeaders(),
    body: JSON.stringify(payload)
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? translate('workspaceApi.createFleetBooking'))
  }

  return response.json()
}

import { translate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/notificationMessages'

import { getMutationHeaders } from './maintenance'

export type RepeatVisit = {
  id: string
  organizationClientId: string
  clientName: string
  vehicleId: string
  vehicleName: string
  sourceMaintenanceId: string
  serviceName: string
  reason?: string | null
  suggestedVisitDate: string
  responsibleUserId?: string | null
  responsibleName?: string | null
  contactByUtc?: string | null
  contactCompletedAtUtc?: string | null
  isActive: boolean
  bookedMaintenanceId?: string | null
  version: number
  canEdit: boolean
  canManageContact: boolean
  canBook: boolean
  timeZoneId: string
}

export type RepeatVisitPage = { items: RepeatVisit[]; totalCount: number; page: number; pageSize: number; timeZoneId: string }
export type RepeatVisitOptions = { responsibles: Array<{ id: string; name: string }>; timeZoneId: string; canCreate: boolean }
export type RepeatVisitQuery = { clientId?: string; sourceMaintenanceId?: string; actionsOnly?: boolean; date?: string; page?: number; pageSize?: number }
export type RepeatVisitSave = { id?: string; sourceMaintenanceId: string; serviceName: string; reason?: string | null; suggestedVisitDate: string; responsibleUserId?: string | null; contactByUtc?: string | null; version?: number }

async function request<T>(organizationId: string, path: string, options: { method?: string; body?: unknown; query?: RepeatVisitQuery; signal?: AbortSignal } = {}): Promise<T> {
  const url = new URL(`/api/v1/organizations/${encodeURIComponent(organizationId)}/repeat-visits${path}`, window.location.origin)
  for (const [key, value] of Object.entries(options.query ?? {})) {
    if (value !== undefined && value !== '') url.searchParams.set(key, String(value))
  }
  let response: Response
  try {
    response = await fetch(url, {
      method: options.method ?? 'GET', credentials: 'same-origin', signal: options.signal,
      headers: getMutationHeaders({ acceptJson: true, contentTypeJson: options.body !== undefined }),
      body: options.body === undefined ? undefined : JSON.stringify(options.body)
    })
  } catch (caught) {
    if (caught instanceof DOMException && caught.name === 'AbortError') throw caught
    throw new Error(translate('notifications.networkError'))
  }
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    if (response.status === 401) throw new Error(translate('notifications.repeatSessionExpired'))
    if (response.status === 403) throw new Error(translate('notifications.repeatForbidden'))
    if (response.status === 409) throw new Error(error?.message ?? translate('notifications.repeatConflict'))
    throw new Error(error?.message ?? translate('notifications.repeatRequestError'))
  }
  return response.status === 204 ? undefined as T : response.json()
}

export const loadRepeatVisits = (organizationId: string, query: RepeatVisitQuery, signal?: AbortSignal) => request<RepeatVisitPage>(organizationId, '', { query, signal })
export const loadRepeatVisitOptions = (organizationId: string, signal?: AbortSignal) => request<RepeatVisitOptions>(organizationId, '/options', { signal })
export const loadRepeatVisit = (organizationId: string, id: string, signal?: AbortSignal) => request<RepeatVisit>(organizationId, `/${encodeURIComponent(id)}`, { signal })
export const saveRepeatVisit = (organizationId: string, id: string | null, body: RepeatVisitSave) => request<RepeatVisit>(organizationId, id ? `/${encodeURIComponent(id)}` : '', { method: id ? 'PUT' : 'POST', body })
export const completeRepeatVisitAction = (organizationId: string, id: string, version: number) => request<RepeatVisit>(organizationId, `/${encodeURIComponent(id)}/action/complete`, { method: 'POST', body: { version } })
export const rescheduleRepeatVisitAction = (organizationId: string, id: string, version: number, contactByUtc: string) => request<RepeatVisit>(organizationId, `/${encodeURIComponent(id)}/action/reschedule`, { method: 'POST', body: { version, contactByUtc } })
export const changeRepeatVisitState = (organizationId: string, id: string, version: number, isActive: boolean) => request<RepeatVisit>(organizationId, `/${encodeURIComponent(id)}/state`, { method: 'POST', body: { version, isActive } })
export const convertRepeatVisitBooking = (organizationId: string, id: string, body: unknown) => request<{ maintenanceId: string; maintenanceUrl: string }>(organizationId, `/${encodeURIComponent(id)}/booking`, { method: 'POST', body })


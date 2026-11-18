import { translate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/notificationMessages'

import { getMutationHeaders } from './maintenance'

export type VisitReminderChannelCode = 'auto' | 'telegram' | 'max' | 'both'

export type VisitReminderSettings = {
  hoursBefore: number | null
  channelCode: VisitReminderChannelCode
  template: string
  timeZoneId: string | null
}

export type MaintenanceVisitReminder = {
  enabled: boolean
  version: number
  startsAtUtc: string | null
  scheduledAtUtc: string | null
  statusCode: string
  statusText: string
  statusDetailCode?: string | null
  timeZoneId?: string | null
  channelCode: string
  preview: string
  canEdit: boolean
}

async function request<T>(url: string, method = 'GET', body?: unknown, signal?: AbortSignal): Promise<T> {
  let response: Response
  try {
    response = await fetch(url, {
      method,
      credentials: 'same-origin',
      headers: getMutationHeaders({ acceptJson: true, contentTypeJson: body !== undefined }),
      body: body === undefined ? undefined : JSON.stringify(body),
      signal
    })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error
    }
    throw new Error(translate('notifications.networkError'))
  }

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    if (response.status === 401) {
      throw new Error(translate('notifications.reminderSessionExpired'))
    }
    if (response.status === 409) {
      throw new Error(error?.message ?? translate('notifications.reminderConflict'))
    }
    throw new Error(error?.message ?? translate('notifications.reminderRequestError'))
  }

  if (response.status === 204) {
    return method === 'GET' ? undefined as T : request<T>(url, 'GET', undefined, signal)
  }

  return response.json()
}

export function loadVisitReminderSettings(organizationId: string, signal?: AbortSignal): Promise<VisitReminderSettings> {
  return request(`/api/v1/organizations/${encodeURIComponent(organizationId)}/visit-reminder-settings`, 'GET', undefined, signal)
}

export function saveVisitReminderSettings(organizationId: string, settings: Omit<VisitReminderSettings, 'timeZoneId'>, signal?: AbortSignal): Promise<VisitReminderSettings> {
  return request(`/api/v1/organizations/${encodeURIComponent(organizationId)}/visit-reminder-settings`, 'PUT', settings, signal)
}

export function loadMaintenanceVisitReminder(organizationId: string, maintenanceId: string, signal?: AbortSignal): Promise<MaintenanceVisitReminder> {
  return request(`/api/v1/maintenance/${maintenanceId}/visit-reminder?organizationId=${encodeURIComponent(organizationId)}`, 'GET', undefined, signal)
}

export function saveMaintenanceVisitReminder(
  organizationId: string,
  maintenanceId: string,
  body: { enabled: boolean; version: number },
  signal?: AbortSignal
): Promise<MaintenanceVisitReminder> {
  return request(`/api/v1/maintenance/${maintenanceId}/visit-reminder?organizationId=${encodeURIComponent(organizationId)}`, 'PUT', body, signal)
}

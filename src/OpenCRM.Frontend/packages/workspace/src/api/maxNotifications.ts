import { translate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/notificationMessages'

export type MaxBotSettings = {
  botUsername?: string | null
  isConfigured: boolean
  isEnabled: boolean
  notifyBooking: boolean
  notifyReady: boolean
  webhookUrl: string
}

export type MaxClientStatus = {
  isBotConnected: boolean
  isClientConnected: boolean
  botUsername?: string | null
  recentNotifications: MaxNotificationRecord[]
}

export type MaxNotificationRecord = {
  id: number
  eventCode: string
  body: string
  status: string
  createdAt: string
  sentAt?: string | null
  errorCode?: string | null
}

async function request<T>(url: string, method = 'GET', body?: unknown): Promise<T> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    'X-Requested-With': 'XMLHttpRequest'
  }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content
  if (token) {
    headers.RequestVerificationToken = token
  }

  let response: Response
  try {
    response = await fetch(url, {
      method,
      credentials: 'same-origin',
      headers,
      body: body === undefined ? undefined : JSON.stringify(body)
    })
  } catch {
    throw new Error(translate('notifications.networkError'))
  }
  if (!response.ok) {
    if (response.status === 401) {
      throw new Error(translate('notifications.maxSessionExpired'))
    }

    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? translate('notifications.maxError'))
  }

  return response.status === 204 ? undefined as T : response.json()
}

export function loadMaxBotSettings(organizationId: string): Promise<MaxBotSettings> {
  return request(`/api/v1/organizations/${organizationId}/max-bot`)
}

export function saveMaxBotSettings(organizationId: string, data: {
  token?: string | null
  isEnabled: boolean
  notifyBooking: boolean
  notifyReady: boolean
}): Promise<MaxBotSettings> {
  return request(`/api/v1/organizations/${organizationId}/max-bot`, 'PUT', data)
}

export function loadMaxClientStatus(organizationId: string, clientId: string): Promise<MaxClientStatus> {
  return request(`/api/v1/organizations/${organizationId}/clients/${clientId}/max`)
}

export function loadMaxMaintenanceNotifications(organizationId: string, maintenanceId: string): Promise<MaxNotificationRecord[]> {
  return request(`/api/v1/organizations/${organizationId}/maintenances/${maintenanceId}/max/notifications`)
}

export function createMaxClientLink(organizationId: string, clientId: string): Promise<{ url: string }> {
  return request(`/api/v1/organizations/${organizationId}/clients/${clientId}/max/link`, 'POST')
}

export function disconnectMaxClient(organizationId: string, clientId: string): Promise<void> {
  return request(`/api/v1/organizations/${organizationId}/clients/${clientId}/max`, 'DELETE')
}

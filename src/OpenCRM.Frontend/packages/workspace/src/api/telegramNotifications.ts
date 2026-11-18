import { translate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/notificationMessages'

export type TelegramBotSettings = {
  botUsername?: string | null
  isConfigured: boolean
  isEnabled: boolean
  isAvailable: boolean
  notifyBooking: boolean
  notifyReady: boolean
  webhookUrl: string
}

export type TelegramClientStatus = {
  isBotConnected: boolean
  isClientConnected: boolean
  botUsername?: string | null
  recentNotifications: TelegramNotificationRecord[]
}

export type TelegramNotificationRecord = {
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
      throw new Error(translate('notifications.telegramSessionExpired'))
    }

    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? translate('notifications.telegramError'))
  }

  return response.status === 204 ? undefined as T : response.json()
}

export function loadTelegramBotSettings(organizationId: string): Promise<TelegramBotSettings> {
  return request(`/api/v1/organizations/${organizationId}/telegram-bot`)
}

export function saveTelegramBotSettings(organizationId: string, data: {
  token?: string | null
  isEnabled: boolean
  notifyBooking: boolean
  notifyReady: boolean
}): Promise<TelegramBotSettings> {
  return request(`/api/v1/organizations/${organizationId}/telegram-bot`, 'PUT', data)
}

export function loadTelegramClientStatus(organizationId: string, clientId: string): Promise<TelegramClientStatus> {
  return request(`/api/v1/organizations/${organizationId}/clients/${clientId}/telegram`)
}

export function loadTelegramMaintenanceNotifications(organizationId: string, maintenanceId: string): Promise<TelegramNotificationRecord[]> {
  return request(`/api/v1/organizations/${organizationId}/maintenances/${maintenanceId}/telegram/notifications`)
}

export function createTelegramClientLink(organizationId: string, clientId: string): Promise<{ url: string }> {
  return request(`/api/v1/organizations/${organizationId}/clients/${clientId}/telegram/link`, 'POST')
}

export function disconnectTelegramClient(organizationId: string, clientId: string): Promise<void> {
  return request(`/api/v1/organizations/${organizationId}/clients/${clientId}/telegram`, 'DELETE')
}

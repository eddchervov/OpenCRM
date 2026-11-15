import { request } from './httpClient'

export interface AdminAssistantSettings {
  id: number
  isEnabled: boolean
  webEnabled: boolean
  telegramEnabled: boolean
  speechRepliesEnabled: boolean
  textModel: string
  speechModel: string
  voiceModel: string
  voiceName: string
  dailyBudgetRub: number
  monthlyBudgetRub: number
  textReservationRub: number
  audioReservationRub: number
  maximumOutputTokens: number
  revision: number
  updatedAt: string
}

export interface AdminAssistantOrganization {
  organizationId: string
  name: string
  isEnabled: boolean
  enabledUntil: string | null
  webEnabled: boolean
  telegramEnabled: boolean
  dailyBudgetRub: number
  monthlyBudgetRub: number
}

export interface AdminAssistantAudit {
  id: string
  action: string
  description: string
  createdAt: string
  adminUserId: string
}

export interface AdminAssistantResponse {
  limits: { maximumOutputTokensAllowed: number; maximumBudgetRub: number; maximumReservationRub: number; budgetTimeZone: string }
  settings: AdminAssistantSettings
  provider: {
    routerConfigured: boolean
    telegramConfigured: boolean
    telegramBotUsername: string | null
  }
  organizations: AdminAssistantOrganization[]
  totalCount: number
  page: number
  pageSize: number
  audits: AdminAssistantAudit[]
}

export interface AdminAssistantUsageTotals {
  costRub: number | null
  reservedRub: number
  inputTokens: number
  outputTokens: number
  unknownCount: number
  requestCount: number
}

export interface AdminAssistantUsageItem {
  id: string
  organizationId: string
  organizationName: string
  userId: string
  userEmail: string
  channel: string
  kind: string
  model: string
  status: string
  providerRequestId: string | null
  inputTokens: number | null
  outputTokens: number | null
  cachedTokens: number | null
  reservedCostRub: number
  costRub: number | null
  createdAt: string
  errorCode: string | null
}

export interface AdminAssistantUsageResponse {
  totals: AdminAssistantUsageTotals
  daily: Array<{ date: string; costRub: number | null; reservedRub: number }>
  byOrganization: Array<{ organizationId: string; name: string; costRub: number | null; reservedRub: number; requestCount: number }>
  items: AdminAssistantUsageItem[]
  totalCount: number
  page: number
  pageSize: number
}

export interface AdminAssistantSettingsRequest extends Omit<AdminAssistantSettings, 'id' | 'updatedAt'> {}

export interface AdminAssistantOrganizationAccessRequest {
  isEnabled: boolean
  enabledUntil?: string | null
  webEnabled: boolean
  telegramEnabled: boolean
  dailyBudgetRub: number
  monthlyBudgetRub: number
}

export interface AdminAssistantUsageFilters {
  from: string
  to: string
  organizationId?: string
  channel?: string
  model?: string
  status?: string
  page?: number
  pageSize?: number
}

const jsonHeaders = { 'Content-Type': 'application/json' }

export function getAdminAssistant(filters: { search?: string; page?: number; pageSize?: number } = {}): Promise<AdminAssistantResponse> {
  const query = new URLSearchParams({
    search: filters.search ?? '',
    page: String(filters.page ?? 1),
    pageSize: String(filters.pageSize ?? 20)
  })

  return request<AdminAssistantResponse>(`/api/v1/admin/assistant?${query.toString()}`)
}

export function saveAdminAssistantSettings(settings: AdminAssistantSettingsRequest): Promise<AdminAssistantSettings> {
  return request<AdminAssistantSettings>('/api/v1/admin/assistant/settings', {
    method: 'PUT',
    headers: jsonHeaders,
    body: JSON.stringify(settings)
  })
}

export function saveAdminAssistantOrganizationAccess(
  organizationId: string,
  access: AdminAssistantOrganizationAccessRequest
): Promise<void> {
  return request<void>(`/api/v1/admin/assistant/organizations/${encodeURIComponent(organizationId)}/access`, {
    method: 'PUT',
    headers: jsonHeaders,
    body: JSON.stringify(access)
  })
}

export function getAdminAssistantUsage(filters: AdminAssistantUsageFilters): Promise<AdminAssistantUsageResponse> {
  const query = new URLSearchParams({
    from: filters.from,
    to: filters.to,
    organizationId: filters.organizationId ?? '',
    channel: filters.channel ?? '',
    model: filters.model ?? '',
    status: filters.status ?? '',
    page: String(filters.page ?? 1),
    pageSize: String(filters.pageSize ?? 50)
  })

  return request<AdminAssistantUsageResponse>(`/api/v1/admin/assistant/usage?${query.toString()}`)
}

export function settleAdminAssistantUsage(id: string, costRub: number, reason: string): Promise<void> {
  return request<void>(`/api/v1/admin/assistant/usage/${encodeURIComponent(id)}/settle`, {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({ costRub, reason })
  })
}

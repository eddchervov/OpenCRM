import { translate } from '@opencrm/ui/i18n'
import '../inquiryMessages'

export type InquiryStage = 'new' | 'clarifying' | 'proposal' | 'ready' | 'booked' | 'closed'

export const inquiryStages: Array<{ value: InquiryStage; label: string }> = [
  { value: 'new', get label() { return translate('inquiries.stageNew') } },
  { value: 'clarifying', get label() { return translate('inquiries.stageClarifying') } },
  { value: 'proposal', get label() { return translate('inquiries.stageProposal') } },
  { value: 'ready', get label() { return translate('inquiries.stageReady') } },
  { value: 'booked', get label() { return translate('inquiries.stageBooked') } },
  { value: 'closed', get label() { return translate('inquiries.stageClosed') } }
]

export const inquirySources = [
  { value: 'phone', get label() { return translate('inquiries.phone') } },
  { value: 'visit', get label() { return translate('inquiries.sourceVisit') } },
  { value: 'website', get label() { return translate('inquiries.sourceWebsite') } },
  { value: 'messenger', get label() { return translate('inquiries.sourceMessenger') } },
  { value: 'recommendation', get label() { return translate('inquiries.sourceRecommendation') } },
  { value: 'other', get label() { return translate('inquiries.sourceOther') } }
]

export type InquiryInput = {
  contactName: string
  contactPhone?: string | null
  interestText: string
  sourceCode: string
  organizationClientId?: string | null
  vehicleId?: string | null
  vehicleDescription?: string | null
  responsibleUserId?: string | null
  nextActionText?: string | null
  nextActionAt?: string | null
}

export type Inquiry = InquiryInput & {
  id: string
  stageCode: InquiryStage
  responsibleUserName?: string | null
  organizationClientName?: string | null
  vehicleName?: string | null
  maintenanceId?: string | null
  closeReason?: string | null
  version: number
  createdAt: string
  updatedAt: string
  canManage: boolean
  canCreateBooking: boolean
  canCreateClient: boolean
  canViewLinkedMaintenance: boolean
  canViewLinkedClient: boolean
  canViewLinkedVehicle: boolean
  canViewClients: boolean
  canViewVehicles: boolean
  timeZoneId: string | null
}

export type InquiryPage = {
  items: Inquiry[]
  page: number
  pageSize: number
  totalCount: number
  canManage: boolean
  canViewClients: boolean
  canViewVehicles: boolean
  timeZoneId: string | null
}

export type InquiryColumn = { stageCode: InquiryStage; items: Inquiry[]; count: number; hasMore: boolean }
export type InquiryBoard = Omit<InquiryPage, 'items' | 'page' | 'pageSize' | 'totalCount'> & { columns: InquiryColumn[] }
export type InquiryHistoryEvent = { id: string; eventType: string; title: string; description?: string | null; previousValue?: string | null; newValue?: string | null; actorName?: string | null; createdAt: string }
export type InquiryHistory = { items: InquiryHistoryEvent[]; totalCount: number; page: number; pageSize: number }
export type InquiryQuery = { searchQuery?: string; sourceCode?: string; responsibleUserId?: string; stageCode?: string; actionDue?: string; date?: string; timeZoneId?: string; page?: number; pageSize?: number }

export class InquiryRequestError extends Error {
  constructor(message: string, public readonly status: number) { super(message) }
}

async function request<T>(organizationId: string, path: string, options: { method?: string; body?: unknown; query?: InquiryQuery; signal?: AbortSignal } = {}): Promise<T> {
  const url = new URL(`/api/v1/organizations/${encodeURIComponent(organizationId)}/inquiries${path}`, window.location.origin)
  for (const [key, value] of Object.entries(options.query ?? {})) {
    if (value !== undefined && value !== '') url.searchParams.set(key, String(value))
  }
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content
  const response = await fetch(url, {
    method: options.method ?? 'GET', credentials: 'same-origin', signal: options.signal,
    headers: { Accept: 'application/json', 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest', ...(token ? { RequestVerificationToken: token } : {}) },
    body: options.body === undefined ? undefined : JSON.stringify(options.body)
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new InquiryRequestError(error?.message ?? translate('inquiries.requestError'), response.status)
  }
  return response.status === 204 ? undefined as T : response.json()
}

export const loadInquiries = (organizationId: string, query: InquiryQuery, signal?: AbortSignal) => request<InquiryPage>(organizationId, '', { query, signal })
export const loadInquiryActionReminders = (organizationId: string, signal?: AbortSignal) => request<Inquiry[]>(organizationId, '/action-reminders', { signal })
export const loadInquiryBoard = (organizationId: string, query: InquiryQuery, signal?: AbortSignal) => request<InquiryBoard>(organizationId, '/board', { query, signal })
export const loadInquiry = (organizationId: string, id: string) => request<Inquiry>(organizationId, `/${encodeURIComponent(id)}`)
export const loadInquiryHistory = (organizationId: string, id: string, page: number, signal?: AbortSignal, pageSize = 10) => request<InquiryHistory>(organizationId, `/${encodeURIComponent(id)}/history`, { query: { page, pageSize }, signal })
export const saveInquiry = (organizationId: string, id: string | null, body: InquiryInput & { version?: number }) => request<Inquiry>(organizationId, id ? `/${encodeURIComponent(id)}` : '', { method: id ? 'PUT' : 'POST', body })
export const changeInquiryStage = (organizationId: string, id: string, version: number, stageCode: string, comment?: string) => request<Inquiry>(organizationId, `/${id}/stage`, { method: 'POST', body: { version, stageCode, comment } })
export const closeInquiry = (organizationId: string, id: string, version: number, reason: string) => request<Inquiry>(organizationId, `/${id}/close`, { method: 'POST', body: { version, reason } })
export const completeInquiryAction = (organizationId: string, id: string, version: number) => request<Inquiry>(organizationId, `/${id}/action/complete`, { method: 'POST', body: { version } })
export const rescheduleInquiryAction = (organizationId: string, id: string, version: number, nextActionAt: string) => request<Inquiry>(organizationId, `/${id}/action/reschedule`, { method: 'POST', body: { version, nextActionAt } })
export const addInquiryNote = (organizationId: string, id: string, version: number, text: string) => request<Inquiry>(organizationId, `/${id}/notes`, { method: 'POST', body: { version, note: text } })
export const convertInquiryBooking = (organizationId: string, id: string, body: unknown) => request<{ maintenanceId: string }>(organizationId, `/${id}/booking`, { method: 'POST', body })

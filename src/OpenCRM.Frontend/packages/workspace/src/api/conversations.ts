export type Conversation = {
  id: string; clientId: string | null; clientName: string | null; botUsername: string
  telegramDisplayName: string | null; lastMessageText: string | null; lastMessageAt: string | null
  unreadCount: number; isReadOnly: boolean
  channelCode: string; connectionId?: string | null; externalDisplayName?: string | null
}
export type ConversationMessage = {
  id: string; sequence: number; direction: string | number; status: string | number; text: string | null
  actorDisplayName: string | null; createdAt: string; maintenanceId?: string | null; maintenanceTitle?: string | null
  deliveryErrorCode?: string | null; retryAt?: string | null; messageTemplateId?: string | null; messageTemplateVersion?: number | null
  attachments: Array<{ id: string; fileName: string; contentType: string; sizeBytes: number }>
}
export type ConversationPage = {
  conversationId: string; conversation: Conversation; items: ConversationMessage[]; nextBeforeSequence: number | null; lastSequence: number
  isReadOnly: boolean; canSend: boolean; canManage: boolean; canViewClient: boolean; canViewMaintenance: boolean; timeZoneId: string | null
}
export type InboxPage = { items: Conversation[]; nextCursor: string | null; unreadCount: number }
export type ClientInboxGroup = { groupId: string; clientId: string | null; clientName: string | null; externalDisplayName: string | null; lastMessageText: string | null; lastMessageAt: string | null; unreadCount: number; routes: Conversation[] }
export type ClientInboxPage = { items: ClientInboxGroup[]; nextCursor: string | null; unreadCount: number }
export type InboxSummary = { unreadCount: number; revision: string }
export type ConversationDeliveryHealth = { queuedUnpublishedCount: number; queuedPublishedCount: number; oldestQueuedAt: string | null; staleQueuedCount: number; expiredSendingCount: number; failedCount: number; uncertainCount: number }
export type ConversationDraftAttachment = { id: string; fileName: string; contentType: string; sizeBytes: number }
export type ConversationChannelRoute = { conversationId: string; connectionId: string; channelCode: string; displayName: string; isReadOnly: boolean; unreadCount: number; lastMessageAt: string | null }
export type ConversationClientRoutes = { clientId: string | null; displayName: string; isUnknownContact: boolean; activeRoutes: ConversationChannelRoute[]; historyRoutes: ConversationChannelRoute[] }
export const conversationChannelName = (code: string | null | undefined) => code?.toLowerCase() === 'max' ? 'MAX' : code?.toLowerCase() === 'telegram' ? 'Telegram' : 'Мессенджер'

export class ConversationRequestError extends Error {
  constructor(message: string, public readonly status: number) { super(message) }
}

export function conversationUrl(organizationId: string, path = '') {
  return `/api/v1/organizations/${encodeURIComponent(organizationId)}/conversations${path}`
}

async function request<T>(organizationId: string, path: string, options: { method?: string; body?: unknown; signal?: AbortSignal } = {}): Promise<T> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content
  const response = await fetch(conversationUrl(organizationId, path), {
    method: options.method ?? 'GET', credentials: 'same-origin', signal: options.signal,
    headers: { Accept: 'application/json', 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest', ...(token ? { RequestVerificationToken: token } : {}) },
    body: options.body === undefined ? undefined : JSON.stringify(options.body)
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new ConversationRequestError(error?.message ?? (response.status === 403 ? 'Нет доступа к переписке.' : response.status === 429 ? 'Слишком много запросов. Подождите немного и повторите.' : 'Не удалось загрузить переписку.'), response.status)
  }
  return response.status === 204 ? undefined as T : response.json()
}

export function loadInbox(organizationId: string, query: { search?: string; onlyUnread?: boolean; cursor?: string; clientId?: string }, signal?: AbortSignal) {
  const parameters = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== '') parameters.set(key, String(value))
  return request<InboxPage>(organizationId, `?${parameters}`, { signal })
}
export function loadClientInbox(organizationId: string, query: { search?: string; onlyUnread?: boolean; cursor?: string }, signal?: AbortSignal) {
  const parameters = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== '') parameters.set(key, String(value))
  return request<ClientInboxPage>(organizationId, `/client-inbox?${parameters}`, { signal })
}
export const loadConversation = (organizationId: string, id: string, beforeSequence?: number, signal?: AbortSignal) =>
  request<ConversationPage>(organizationId, `/${encodeURIComponent(id)}/messages${beforeSequence ? `?beforeSequence=${beforeSequence}` : ''}`, { signal })
export const loadConversationDeliveryHealth = (organizationId: string, signal?: AbortSignal) => request<ConversationDeliveryHealth>(organizationId, '/delivery-health', { signal })
export const getClientConversation = (organizationId: string, clientId: string, signal?: AbortSignal) =>
  request<ConversationPage | null>(organizationId, `/for-client/${encodeURIComponent(clientId)}`, { method: 'POST', body: {}, signal })
export const getClientConversationRoutes = (organizationId: string, clientId: string, signal?: AbortSignal) =>
  request<ConversationClientRoutes | null>(organizationId, `/clients/${encodeURIComponent(clientId)}/routes`, { method: 'POST', body: {}, signal })
export const sendConversationMessage = (organizationId: string, id: string, body: { requestId: string; text: string; maintenanceId?: string; attachmentIds?: string[]; messageTemplateId?: string; messageTemplateVersion?: number }) =>
  request<ConversationMessage>(organizationId, `/${encodeURIComponent(id)}/messages`, { method: 'POST', body })
export const markConversationRead = (organizationId: string, id: string, throughSequence: number) =>
  request<void>(organizationId, `/${encodeURIComponent(id)}/read`, { method: 'POST', body: { throughSequence } })
export const linkConversationClient = (organizationId: string, id: string, clientId: string | null) =>
  request<void>(organizationId, `/${encodeURIComponent(id)}/client`, { method: 'PUT', body: { clientId } })

export async function uploadConversationFile(organizationId: string, id: string, file: File) {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content
  const body = new FormData()
  body.append('file', file)
  const response = await fetch(conversationUrl(organizationId, `/${encodeURIComponent(id)}/attachments`), {
    method: 'POST', credentials: 'same-origin', body,
    headers: { Accept: 'application/json', ...(token ? { RequestVerificationToken: token } : {}) }
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new ConversationRequestError(error?.message ?? 'Не удалось загрузить файл.', response.status)
  }
  return response.json() as Promise<ConversationDraftAttachment>
}

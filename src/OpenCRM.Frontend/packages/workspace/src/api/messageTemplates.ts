import { translate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/conversationMessages'

export type MessageTemplate = { id: string; groupId: string; name: string; body: string; version: number; sortOrder: number; isArchived: boolean }
export type MessageTemplateGroup = { id: string; name: string; version: number; sortOrder: number; isArchived: boolean; templates: MessageTemplate[] }
export type MessageTemplateCatalog = { groups: MessageTemplateGroup[]; canManage: boolean; canViewWorkspaceCatalog: boolean }
export type MessageTemplateAccess = { canUseTemplates: boolean; canManage: boolean; canViewWorkspaceCatalog: boolean }
export type MessageTemplatePreview = { templateId: string; templateVersion: number; text: string }
export type SaveMessageTemplate = { groupId: string; name: string; body: string; sortOrder: number; expectedVersion?: number; isArchived?: boolean }

async function request<T>(organizationId: string, path: string, options: { method?: string; body?: unknown; signal?: AbortSignal } = {}): Promise<T> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content
  const response = await fetch(`/api/v1/organizations/${encodeURIComponent(organizationId)}/message-templates${path}`, {
    method: options.method ?? 'GET', credentials: 'same-origin', signal: options.signal,
    headers: { Accept: 'application/json', 'Content-Type': 'application/json', ...(token ? { RequestVerificationToken: token } : {}) },
    body: options.body === undefined ? undefined : JSON.stringify(options.body)
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? (response.status === 409 ? translate('conversations.templateConflict') : translate('conversations.templateRequestError')))
  }
  return response.status === 204 ? undefined as T : response.json()
}

export const loadMessageTemplateCatalog = (organizationId: string, includeArchived = false, signal?: AbortSignal) =>
  request<MessageTemplateCatalog>(organizationId, `?includeArchived=${includeArchived}`, { signal })
export const loadMessageTemplateAccess = (organizationId: string) => request<MessageTemplateAccess>(organizationId, '/access')
export const saveMessageTemplateGroup = (organizationId: string, id: string | null, body: { name: string; sortOrder: number; expectedVersion?: number; isArchived?: boolean }) =>
  request<MessageTemplateGroup>(organizationId, `/groups${id ? `/${encodeURIComponent(id)}` : ''}`, { method: id ? 'PUT' : 'POST', body })
export const archiveMessageTemplateGroup = (organizationId: string, id: string, expectedVersion: number) =>
  request<void>(organizationId, `/groups/${encodeURIComponent(id)}/archive`, { method: 'POST', body: { expectedVersion } })
export const saveMessageTemplate = (organizationId: string, id: string | null, body: SaveMessageTemplate) =>
  request<MessageTemplate>(organizationId, `${id ? `/${encodeURIComponent(id)}` : ''}`, { method: id ? 'PUT' : 'POST', body })
export const archiveMessageTemplate = (organizationId: string, id: string, expectedVersion: number) =>
  request<void>(organizationId, `/${encodeURIComponent(id)}/archive`, { method: 'POST', body: { expectedVersion } })
export const previewMessageTemplate = (organizationId: string, conversationId: string, template: MessageTemplate, maintenanceId?: string, signal?: AbortSignal) =>
  request<MessageTemplatePreview>(organizationId, '/preview', { method: 'POST', body: { conversationId, templateId: template.id, templateVersion: template.version, maintenanceId }, signal })

export const messageTemplateVariables = [
  { code: '{{organization.name}}', get label() { return translate('conversations.organizationName') } },
  { code: '{{client.name}}', get label() { return translate('conversations.clientName') } },
  { code: '{{maintenance.date}}', get label() { return translate('conversations.maintenanceDate') } },
  { code: '{{maintenance.time}}', get label() { return translate('conversations.maintenanceTime') } },
  { code: '{{maintenance.vehicle}}', get label() { return translate('conversations.vehicle') } }
]

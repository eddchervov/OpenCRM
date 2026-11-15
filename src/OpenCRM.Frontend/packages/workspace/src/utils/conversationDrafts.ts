import type { ConversationDraftAttachment } from '../api/conversations'

export type ConversationSendRequest = { requestId: string; text: string; maintenanceId?: string; attachmentIds?: string[]; messageTemplateId?: string; messageTemplateVersion?: number }
export type ConversationDraft = { text: string; attachment?: ConversationDraftAttachment; pending?: ConversationSendRequest; template?: { messageTemplateId: string; messageTemplateVersion: number } }
const drafts = new Map<string, ConversationDraft>()

export function getConversationDraft(key: string): ConversationDraft {
  return drafts.get(key) ?? { text: '' }
}

export function saveConversationDraft(key: string, draft: ConversationDraft) {
  drafts.delete(key)
  if (!draft.text && !draft.attachment && !draft.pending) return
  drafts.set(key, draft)
  if (drafts.size > 30) drafts.delete(drafts.keys().next().value!)
}

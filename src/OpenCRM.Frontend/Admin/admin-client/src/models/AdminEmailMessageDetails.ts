import type { AdminEmailMessage } from './AdminEmailMessage'

export interface AdminEmailMessageDetails extends AdminEmailMessage {
  htmlBody: string
  textBody: string
}

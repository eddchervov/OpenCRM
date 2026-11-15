export interface AdminEmailMessage {
  id: number
  toEmail: string
  subject: string
  statusCode: string
  createdAt: string
  sentAt: string | null
  errorMessage: string | null
  lastEventCode: string | null
  openCount: number
  clickCount: number
}

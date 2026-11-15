export interface AdminEmailCampaign {
  id: string
  subject: string
  statusCode: string
  targetMode: string
  intervalSeconds: number
  startAt: string
  createdAt: string
  totalCount: number
  sentCount: number
  simulatedCount: number
  failedCount: number
  skippedCount: number
  lastError: string | null
}

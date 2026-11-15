export interface AdminSubscriptionAudit {
  id: number
  action: string
  previousEndDate: string | null
  newEndDate: string | null
  changedBy: string
  changedAt: string
  reason: string
}

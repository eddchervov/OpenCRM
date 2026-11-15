export interface AdminPaymentStatusAudit {
  id: number
  previousStatus: string
  newStatus: string
  source: string
  reason: string | null
  changedByUserId: string
  changedBy: string
  changedAt: string
}

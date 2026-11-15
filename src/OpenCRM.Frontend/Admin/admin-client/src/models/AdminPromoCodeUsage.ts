export interface AdminPromoCodeUsage {
  id: number
  userId: string
  email: string
  organizationId: string | null
  organizationName: string | null
  redeemedAt: string
  assignedAt: string | null
  demoAppliedAt: string | null
  applicationStatus: string | null
  subscriptionPaymentId: number | null
  discountValidUntil: string | null
  sourceCode: string
}

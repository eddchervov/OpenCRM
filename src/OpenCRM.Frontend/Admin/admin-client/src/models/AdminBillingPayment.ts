export interface AdminBillingPayment {
  id: number
  userId: string
  user: string
  userEmail: string | null
  userPhone: string | null
  organizationId?: string | null
  organizationName?: string | null
  planName: string
  planTypeCode: string
  paymentProviderCode?: string
  purchaseId?: string | null
  applicationStatus?: string | null
  lastErrorCode?: string | null
  providerEvents?: AdminBillingPaymentEvent[]
  paidAt?: string | null
  refundedAmount?: number
  providerStatus?: string | null
  appliedAt?: string | null
  grantedFrom?: string | null
  grantedUntil?: string | null
  isTest?: boolean | null
  amount: number
  originalAmount: number | null
  promoDiscountAmount: number | null
  promoCodeSnapshot: string | null
  createdAt: string
  statusUpdatedAt: string | null
  paymentMethodName: string
  paymentStatus: string | null
  statusGroup: string
  statusName: string
  transactionId: string | null
  externalPaymentId: string | null
  paymentLink: string | null
  hasExternalId: boolean
}

export interface AdminBillingPaymentEvent {
  id: string
  messageId: string | null
  purchaseId: string | null
  commandId: string | null
  providerOrderId: string | null
  providerVersion: number | null
  stage: string
  code: string
  createdAt: string
  processedAt: string | null
  amountMinor: number | null
  refundedAmountMinor: number | null
  currency: string
}

export type PaymentServiceActionKind = 'check' | 'refund' | 'retry-access' | 'resolve-review'

export interface AdminPaymentServiceAction {
  id: string
  action: PaymentServiceActionKind
  status: string
  code: string | null
  message: string
  amount: number | null
  createdAt: string
  completedAt: string | null
  actor: string
  reason: string
}

export interface AdminPaymentServiceActions {
  stateToken: string
  canCheck: boolean
  canRefund: boolean
  canRetryAccess: boolean
  canResolveReview: boolean
  refundableAmount: number
  checkHint: string
  refundHint: string
  retryAccessHint: string
  resolveReviewHint: string
  reviewResolved: boolean
  operations: AdminPaymentServiceAction[]
}

export interface AdminPaymentServiceActionRequest {
  operationId: string
  action: PaymentServiceActionKind
  reason: string
  amount?: number
  stateToken: string
}

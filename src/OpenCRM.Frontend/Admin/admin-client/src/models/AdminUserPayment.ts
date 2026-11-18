export interface AdminUserPayment {
  id: number
  amount: number
  paymentDate: string
  paymentMethod?: string
  paymentStatus?: string
  paymentMethodName: string
  paymentStatusName: string
  paymentStatusGroup: string
  transactionId: string | null
  externalPaymentId: string | null
  paymentLink: string | null
  planName: string
}

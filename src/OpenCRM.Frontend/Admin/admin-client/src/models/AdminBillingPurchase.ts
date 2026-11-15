export interface AdminBillingPurchase {
  paymentId: number
  userId: string
  user: string
  userEmail: string | null
  userPhone: string | null
  buyerType: 'individual' | 'business' | 'unknown'
  buyerName: string | null
  buyerInn: string | null
  planName: string
  paymentStatus: string
  amount: number
  refundedAmount: number
  paidAt: string
  npdReceiptIssued: boolean
  npdReceiptIssuedAt: string | null
  npdReceiptMarkedBy: string | null
  npdReceiptReason: string | null
  npdReceiptUrl: string | null
  npdReceiptFileUrl: string | null
  npdReceiptFileName: string | null
  npdReceiptComment: string | null
  npdReceiptDeliveryStatus: 'not_sent' | 'queued' | 'sent' | 'delivered' | 'simulated' | 'failed' | string | null
  npdReceiptSentAt: string | null
  npdReceiptSentTo: string | null
  npdReceiptSendError: string | null
}

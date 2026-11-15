export interface AdminBillingUserSubscription {
  id: number
  userId: string
  user: string
  userEmail: string | null
  userPhone: string | null
  planName: string
  startDate: string
  endDate: string
  autoRenewal: boolean
  isActive: boolean
  isExpiringSoon: boolean
  daysLeft: number
  isHistorical?: boolean
  isDemoPlan: boolean
  source: string
}

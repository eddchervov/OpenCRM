export interface AdminUserSubscriptionHistory {
  id: number
  planName: string
  startDate: string
  endDate: string
  actualEndDate: string | null
  status: string
  autoRenewal: boolean
  createdAt: string
  comment: string | null
}

export interface AdminUserSubscription {
  id: number
  organizationId?: string
  organizationName?: string | null
  planName: string
  planTypeCode: string
  startDate: string
  endDate: string
  autoRenewal: boolean
  isActive: boolean
  isDemoPlan: boolean
  daysLeft: number
}

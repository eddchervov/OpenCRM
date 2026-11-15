export interface AdminBillingOrganizationSubscription {
  id: number
  organizationId: string
  primaryScenarioCode: string
  organizationName: string
  owner: string
  ownerUserId: string
  planName: string
  startDate: string
  endDate: string
  autoRenewal: boolean
  isActive: boolean
  isExpiringSoon: boolean
  daysLeft: number
  isDemoPlan: boolean
  source: string
}

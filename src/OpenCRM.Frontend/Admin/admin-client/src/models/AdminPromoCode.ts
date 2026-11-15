export interface AdminPromoCode {
  id: number
  code: string
  name: string
  description: string | null
  isActive: boolean
  validFrom: string | null
  validUntil: string | null
  maximumRedemptions: number | null
  redemptionCount: number
  remainingRedemptions: number | null
  demoDurationDays: number | null
  discountTypeCode: string
  discountValue: number | null
  discountValidityDays: number | null
  discountPaymentLimitPerUser: number | null
  appliesToAllPaidPlans: boolean
  subscriptionPlanIds: number[]
  subscriptionPlanNames: string[]
  bonusDays: number | null
  maximumOrganizationApplications: number | null
  organizationApplicationCount: number
  remainingOrganizationApplications: number | null
  appliesToAllScenarios: boolean
  scenarioCodes: string[]
  giftTemplatePlanId: number | null
  isOrganizationCode: boolean
  createdAt: string
  updatedAt: string
}

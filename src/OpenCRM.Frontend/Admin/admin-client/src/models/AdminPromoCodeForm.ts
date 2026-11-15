export interface AdminPromoCodeForm {
  code: string
  name: string
  description: string | null
  isActive: boolean
  validFrom: string | null
  validUntil: string | null
  maximumRedemptions: number | null
  demoDurationDays: number | null
  discountTypeCode: string
  discountValue: number | null
  discountValidityDays: number | null
  discountPaymentLimitPerUser: number | null
  appliesToAllPaidPlans: boolean
  subscriptionPlanIds: number[]
  bonusDays: number | null
  maximumOrganizationApplications: number | null
  appliesToAllScenarios: boolean
  scenarioCodes: string[]
  giftTemplatePlanId: number | null
}

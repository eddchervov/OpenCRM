export interface AdminSubscriptionPlanForm {
  name: string
  description: string
  price: number
  discountPrice: number | null
  durationDays: number
  vehicleLimit: number
  order: number
  group: string | null
  planTypeCode: string
  isActive: boolean
  isDefaultPlan: boolean
  isDemoPlan: boolean
}

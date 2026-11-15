import type { AdminFeatureValue } from './AdminFeatureValue'

export interface AdminSubscriptionPlan {
  id: number
  name: string
  description: string
  price: number
  discountPrice: number | null
  effectivePrice: number
  hasDiscount: boolean
  durationDays: number
  isActive: boolean
  vehicleLimit: number
  order: number
  group: string | null
  planTypeCode: string
  planTypeName: string
  isDemoPlan: boolean
  isDefaultPlan: boolean
  hasUsageHistory: boolean
  canDelete: boolean
  features: AdminFeatureValue[]
}

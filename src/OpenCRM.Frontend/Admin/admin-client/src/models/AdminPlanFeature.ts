export interface AdminPlanFeature {
  featureId: number
  code: string
  name: string
  description: string
  category: string | null
  valueType: string
  isAssigned: boolean
  boolValue: boolean | null
  intValue: number | null
}

export interface AdminFeatureValue {
  featureId: number
  code: string
  name: string
  category: string | null
  valueType: string
  boolValue: boolean | null
  intValue: number | null
}

export interface AdminUserFeatureGrant {
  id: number
  featureCode: string
  featureName: string
  valueType: string
  boolValue: boolean | null
  intValue: number | null
  isEnabled: boolean
  createdAt: string
  startDate: string | null
  endDate: string | null
  source: string | null
  comment: string | null
  grantedByUserId: string | null
  isActive: boolean
}

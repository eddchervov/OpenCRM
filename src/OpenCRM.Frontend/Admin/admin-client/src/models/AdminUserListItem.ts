export interface AdminUserListItem {
  id: string
  phone: string | null
  phoneConfirmed: boolean
  email: string | null
  emailConfirmed: boolean
  hasPassword: boolean
  lockoutEnabled: boolean
  datetimeCreation: string
  lastDateOfEntry: string | null
  countVisits: number
  lastLoginDate: string | null
  countLogins: number
  firstUtmSource: string | null
  firstUtmMedium: string | null
  countVehicles: number
  countMaintenance: number
  organizationsCount: number
  canUseTestSubscriptions: boolean
  currentSubscriptionEndDate: string | null
  effectivePlanName: string | null
  effectivePlanType: string
  activeFeatureGrantsCount: number
  isDeleted: boolean
}

export interface AdminUserProfile {
  id: string
  email: string | null
  emailConfirmed: boolean
  phone: string | null
  phoneNumberConfirmed: boolean
  datetimeCreation: string
  firstUtmSource: string | null
  firstUtmMedium: string | null
  firstUtmCampaign: string | null
  firstUtmContent: string | null
  firstUtmTerm: string | null
  firstVisitAt: string | null
  lastUtmSource: string | null
  lastUtmMedium: string | null
  lastUtmCampaign: string | null
  lastUtmContent: string | null
  lastUtmTerm: string | null
  lastVisitAt: string | null
  lastDateOfEntry: string | null
  lastLoginDate: string | null
  countLogins: number
  countVisits: number
  lockoutEnabled: boolean
  lockoutEnd: string | null
  accessFailedCount: number
  hasPassword: boolean
  personalDataProcessingConsentAccepted: boolean
  personalDataProcessingConsentAcceptedAt: string | null
  advertisingConsentAccepted: boolean
  advertisingConsentAcceptedAt: string | null
  canUseTestSubscriptions: boolean
  isDeleted: boolean
  deletedAt: string | null
  roles: string[]
}

export type SiteNotificationTypeCode =
  | 'manual'
  | 'organization-invitation'
  | 'invitation-accepted'
  | 'organization-subscription-expiring'
  | 'organization-subscription-expired'
  | 'welcome'

export interface AdminSiteNotification {
  id: string
  title: string
  body: string
  typeCode: SiteNotificationTypeCode
  isTemplate: boolean
  audiences: string[]
  recipientEmail: string | null
  createdAt: string
}

export interface SendSiteNotification {
  title: string
  body: string
  recipientScope: 'all' | 'selected'
  userIds: string[]
  audiences: string[]
}

export interface SiteNotificationUser {
  id: string
  email: string
}

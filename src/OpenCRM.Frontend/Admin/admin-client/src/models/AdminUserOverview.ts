import type { AdminUserFeatureGrant } from './AdminUserFeatureGrant'
import type { AdminUserInvitation } from './AdminUserInvitation'
import type { AdminUserKpis } from './AdminUserKpis'
import type { AdminUserMaintenance } from './AdminUserMaintenance'
import type { AdminUserOrganization } from './AdminUserOrganization'
import type { AdminUserPayment } from './AdminUserPayment'
import type { AdminUserProfile } from './AdminUserProfile'
import type { AdminUserSubscriptions } from './AdminUserSubscriptions'
import type { AdminUserSupportMessage } from './AdminUserSupportMessage'
import type { AdminUserTimelineItem } from './AdminUserTimelineItem'
import type { AdminUserVehicle } from './AdminUserVehicle'

export interface AdminUserOverview {
  generatedAt: string
  profile: AdminUserProfile
  kpis: AdminUserKpis
  vehicles: AdminUserVehicle[]
  maintenances: AdminUserMaintenance[]
  organizations: AdminUserOrganization[]
  invitations: AdminUserInvitation[]
  subscriptions: AdminUserSubscriptions
  payments: AdminUserPayment[]
  featureGrants: AdminUserFeatureGrant[]
  supportMessages: AdminUserSupportMessage[]
  timeline: AdminUserTimelineItem[]
}

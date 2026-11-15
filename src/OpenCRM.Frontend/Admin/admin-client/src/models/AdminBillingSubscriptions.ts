import type { AdminBillingOrganizationSubscription } from './AdminBillingOrganizationSubscription'
import type { AdminBillingUserSubscription } from './AdminBillingUserSubscription'

export interface AdminBillingSubscriptions {
  users: AdminBillingUserSubscription[]
  organizations: AdminBillingOrganizationSubscription[]
}

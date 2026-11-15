import type { AdminUserSubscription } from './AdminUserSubscription'
import type { AdminUserSubscriptionHistory } from './AdminUserSubscriptionHistory'

export interface AdminUserSubscriptions {
  users: AdminUserSubscription[]
  organizations: AdminUserSubscription[]
  history: AdminUserSubscriptionHistory[]
}

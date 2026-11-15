import type { AdminBillingKpis } from './AdminBillingKpis'
import type { AdminBillingPayment } from './AdminBillingPayment'
import type { AdminBillingSubscriptions } from './AdminBillingSubscriptions'

export interface AdminBilling {
  generatedAt: string
  kpis: AdminBillingKpis
  payments: AdminBillingPayment[]
  subscriptions: AdminBillingSubscriptions
}

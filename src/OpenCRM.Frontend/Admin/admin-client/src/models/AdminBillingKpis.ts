export interface AdminBillingKpis {
  totalPayments: number
  payments30DaysAmount: number
  payments30DaysCount: number
  problemPayments30Days: number
  pendingPayments30Days: number
  activeUserSubscriptions: number
  activeOrganizationSubscriptions: number
  expiringSubscriptions7Days: number
}

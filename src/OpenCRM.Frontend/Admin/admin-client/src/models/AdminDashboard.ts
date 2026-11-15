export interface AdminDashboard {
  generatedAt: string
  period: {
    days: number
    newUsers: number
    newOrganizations: number
    paymentsCount: number
    paymentsAmount: number
    emails: number
    supportMessages: number
    catalogSuggestions: number
  }
  kpis: {
    usersTotal: number
    newUsersToday: number
    newUsers7Days: number
    loginsToday: number
    activeUsers7Days: number
    usersNeverLoggedIn: number
    organizationsTotal: number
    activeOrganizations: number
    newOrganizations7Days: number
    organizationMembers: number
    vehiclesTotal: number
    maintenancesTotal: number
    plannedMaintenances: number
    documentsTotal: number
    openDefects: number
    payments30DaysAmount: number
    payments30DaysCount: number
    activeOrganizationSubscriptions: number
    emailsToday: number
    emails7Days: number
    sentEmails7Days: number
    failedEmails7Days: number
    pendingEmails: number
  }
  attention: {
    newSupportMessages: number
    pendingCatalogSuggestions: number
    pendingInvitations: number
    overdueTasks: number
    lowStockItems: number
    failedPayments: number
    failedEmails7Days: number
    pendingEmails: number
  }
  charts: {
    registrations: Array<{ date: string; label: string; value: number }>
    logins: Array<{ date: string; label: string; value: number }>
    maintenances: Array<{ date: string; label: string; value: number }>
    emails: Array<{ date: string; label: string; value: number }>
    payments: Array<{ date: string; label: string; value: number }>
  }
  recent: {
    users: Array<{ id: string; title: string; createdAt: string }>
    organizations: Array<{ id: string; name: string; owner: string; createdAt: string }>
    payments: Array<{ id: number; amount: number; paymentStatus: string | null; paymentDate: string; plan: string; user: string }>
    emails: Array<{ id: number; toEmail: string; subject: string; statusCode: string; createdAt: string }>
    supportMessages: Array<{ id: number; topic: string; statusCode: string; createdAt: string }>
    catalogSuggestions: Array<{ id: number; brandName: string; modelName: string; status: number; createdAt: string }>
  }
}

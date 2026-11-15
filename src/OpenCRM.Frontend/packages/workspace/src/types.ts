export type WorkspaceMenuItem = {
  code: string
  isAvailable?: boolean
  unavailableMessage?: string | null
  sectionCodes?: string[]
  title: string
  urlTemplate: string
  iconCssClass?: string
  groupTitle?: string
  sectionLabel?: string | null
  count?: number | null
  hasDividerBefore?: boolean
  isVisible: boolean
}

export type WorkspaceQuickAction = {
  code: string
  title: string
  urlTemplate: string
  formCode?: string
  isVisible: boolean
  isPrimary?: boolean
}

export type WorkspaceAccess = {
  commercialEnabled?: boolean
  isFreePlan?: boolean
  isBaseWorkspace: boolean
  hasActiveSubscription: boolean
  isReadOnly: boolean
  exportsEnabled: boolean
  canEditVehicles?: boolean
  canArchiveVehicles?: boolean
  roleCode: string
  readOnlyMessage?: string | null
}

export type WorkspaceAnalyticsSeriesPoint = {
  date: string
  label: string
  value: number
}

export type WorkspaceAnalyticsBreakdownItem = {
  code: string
  label: string
  value: number
  percent: number
}

export type WorkspaceAnalytics = {
  kpis: {
    activeVehicles: number
    clients: number
    plannedWorks: number
    inProgressWorks: number
    completedWorks14Days: number
    revenue14Days: number
    openDefects: number
    counterEntries14Days: number
    materialsLowStock: number
    materialsOutOfStock: number
    documents14Days: number
  }
  maintenanceCreatedSeries: WorkspaceAnalyticsSeriesPoint[]
  maintenanceCompletedSeries: WorkspaceAnalyticsSeriesPoint[]
  revenueSeries: WorkspaceAnalyticsSeriesPoint[]
  statusBreakdown: WorkspaceAnalyticsBreakdownItem[]
  materialRiskBreakdown: WorkspaceAnalyticsBreakdownItem[]
}

export type WorkspaceOrganizationSwitcherItem = {
  id: string
  name: string
  scenarioName: string
  roleName: string
  url: string
  isCurrent?: boolean
}

export type WorkspaceContext = {
  user: {
    id: string
    email: string
    displayName: string
    canOpenAdminPanel?: boolean
  }
  organization: {
    id: string
    name: string
    timeZoneId?: string | null
    description?: string | null
    primaryScenarioCode?: string | null
    maintenanceWorkflowMode?: string | null
    allowWorkSelfAssignment?: boolean
    canManageMembers?: boolean
    canManageSettings?: boolean
    canManageBilling?: boolean
    members?: Array<{
      userId: string
      email?: string | null
      userName?: string | null
      employeeName?: string | null
    }>
  }
  workspaceAccess?: WorkspaceAccess
  currentSubscription?: {
    endDate?: string
    subscriptionPlan?: {
      name: string
      isDefaultPlan?: boolean
      isDemoPlan?: boolean
    } | null
  } | null
  analytics?: WorkspaceAnalytics | null
  organizationSwitcher?: {
    organizations: WorkspaceOrganizationSwitcherItem[]
    manageUrl: string
    createUrl?: string | null
    canCreateOrganization: boolean
  }
  shell: {
    scenario?: {
      modules?: {
        intake?: boolean
        assistant?: boolean
        assistantVisible?: boolean
        messaging?: boolean
        accountingExport?: boolean
        photos?: boolean
        members?: boolean
        materials?: boolean
      }
      code?: string
      title?: string
      summary?: string
      terms?: Record<string, string>
      focus?: string[]
      capabilities?: string[]
      dashboardHint?: string
      recommendedModules?: string[]
    }
    menu?: WorkspaceMenuItem[]
    quickActions?: WorkspaceQuickAction[]
  }
  links: {
    workspaceUrl: string
    adminUrl?: string
    schedulerUrl: string
    quickBookingUrl: string
    api: {
      shell: string
      calendar: string
      quickBooking: string
    }
  }
}

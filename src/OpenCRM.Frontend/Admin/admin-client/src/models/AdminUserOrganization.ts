export interface AdminUserOrganization {
  organizationId: string
  organizationName: string | null
  primaryScenarioCode: string
  scenarioName: string | null
  roleName: string
  roleCode?: string
  joinedAt: string
  isOwner: boolean
  isActive: boolean
  isArchived: boolean
  isDeleted: boolean
  membersCount: number
  vehiclesCount: number
  clientsCount: number
  currentPlanName: string
  currentSubscriptionId: number | null
  currentPlanEndDate: string | null
  currentPlanIsDemo: boolean
  hasActiveSubscription: boolean
}

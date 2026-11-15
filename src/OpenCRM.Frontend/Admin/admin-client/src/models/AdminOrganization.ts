export interface AdminOrganization {
  id: string
  name: string
  primaryScenarioCode: string
  scenarioName: string
  ownerUserId: string
  owner: string
  ownerEmail: string | null
  isActive: boolean
  isArchived: boolean
  isDeleted: boolean
  statusName: string
  clientsCount: number
  vehiclesCount: number
  membersCount: number
  activeSubscriptionName: string | null
  activeSubscriptionEndDate: string | null
}

export interface AdminOrganizationsPage {
  totalCount: number
  activeCount: number
  archivedCount: number
  deletedCount: number
  organizations: AdminOrganization[]
}

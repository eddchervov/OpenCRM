export interface AdminTestDataOrganization {
  id: string
  name: string
  ownerEmail: string | null
  scenarioCode: string
  isActive: boolean
  vehiclesCount: number
  maintenanceCount: number
}

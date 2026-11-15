export interface AdminUserOrganizationContents {
  id: string
  name: string
  members: { userId: string, name: string, roleName: string }[]
  vehicles: { id: string, name: string, clientName: string | null }[]
  clients: { id: string, name: string, vehiclesCount: number }[]
  maintenancesCount: number
  recentMaintenances: { id: string, date: string, vehicleName: string, statusName: string }[]
}

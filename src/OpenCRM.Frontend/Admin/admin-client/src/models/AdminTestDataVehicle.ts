export interface AdminTestDataVehicle {
  id: string
  displayName: string
  brandName: string
  modelName: string
  name: string | null
  yearIssue: number | null
  isArchived: boolean
  maintenanceCount: number
  defectCount: number
}

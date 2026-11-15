export interface AdminUserVehicle {
  id: string
  title: string
  kindName: string
  brand: string | null
  model: string | null
  yearIssue: number | null
  registrationPlate: string | null
  vin: string | null
  currentMileage: number | null
  mileageUnitName: string
  dateTimeCreation: string
  isArchived: boolean
  isDeleted: boolean
  organizationName: string | null
  maintenancesCount: number
  openDefectsCount: number
  lastMaintenanceAt: string | null
}

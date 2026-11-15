export interface AdminUserMaintenance {
  id: string
  date: string
  statusName: string
  typeName: string
  vehicleTitle: string
  location: string | null
  performer: string | null
  paidAmount: number | null
  materialCostAmount: number | null
  laborCostAmount: number | null
  workActNumber: string | null
  comment: string | null
}

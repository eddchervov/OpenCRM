export interface AdminUserMaintenance {
  id: string
  date: string
  status?: number
  type?: number
  statusName: string
  typeName: string
  vehicleTitle: string
  location: string | null
  performer: string | null
  currencyCode?: string
  paidAmount: number | null
  materialCostAmount: number | null
  laborCostAmount: number | null
  workActNumber: string | null
  comment: string | null
}

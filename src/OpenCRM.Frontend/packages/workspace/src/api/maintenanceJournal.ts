export type MaintenanceJournalItem = {
  id: string
  date?: string | null
  workActNumber?: number | null
  vehicleDisplayName: string
  registrationPlate?: string | null
  clientName?: string | null
  services: string[]
  serviceLines?: { name: string; employeeName?: string | null; amount: number | null }[]
  performerNames?: string[]
  status: number
  statusName: string
  plannedDate?: string | null
  plannedStartTime?: string | null
  completedDate?: string | null
  amount: number
  paidAmount?: number | null
  paymentStatus?: string | null
}

export type MaintenanceJournalData = {
  items: MaintenanceJournalItem[]
  totalCount: number
  page: number
  pageSize: number
  performers?: { id: string; name: string }[]
}

export async function loadMaintenanceJournal(
  organizationId: string,
  options: { status: number; handover?: string; dateFrom?: string; dateTo?: string; employeeId?: string; search: string; page: number; pageSize: number },
  signal?: AbortSignal
): Promise<MaintenanceJournalData> {
  const url = new URL(`/api/v1/organizations/${organizationId}/maintenance-journal`, window.location.origin)
  for (const [key, value] of Object.entries(options)) {
    if (value !== undefined && value !== '') url.searchParams.set(key, String(value).trim())
  }
  const response = await fetch(url, {
    credentials: 'same-origin', headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }, signal
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить журнал обслуживаний.')
  }

  return response.json()
}

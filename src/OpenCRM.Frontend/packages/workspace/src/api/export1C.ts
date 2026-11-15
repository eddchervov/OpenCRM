export type Export1CCapabilities = {
  canExport: boolean
  canExportBp3?: boolean
  canExportOrders: boolean
  canExportClients: boolean
  canExportVehicles: boolean
  canExportCatalog: boolean
  canExportMaterials: boolean
}

export type Export1COptions = {
  destinationCode?: 'bp3' | 'full'
  dateFrom: string
  dateTo: string
  timeZoneId: string
  includeOrders: boolean
  includeUnfinishedOrders: boolean
  includePayments: boolean
  includeClients: boolean
  includeVehicles: boolean
  includeCatalog: boolean
  includeMaterials: boolean
}

export type Export1CIssue = {
  entityType: string
  entityId: string
  orderId?: string | null
  message: string
}

export type Export1CPreview = {
  schemaVersion: number
  ordersCount: number
  completedOrdersCount: number
  ordersNeedingReviewCount: number
  paymentsCount: number
  paymentsNeedingReviewCount: number
  validationIssues: Export1CIssue[]
}

export async function loadExport1CPreview(
  organizationId: string, options: Export1COptions, signal?: AbortSignal
): Promise<Export1CPreview> {
  const url = new URL(`/api/v1/organizations/${organizationId}/accounting-export/preview`, window.location.origin)
  for (const [key, value] of Object.entries(options)) url.searchParams.set(key, String(value))
  const response = await fetch(url.toString(), { credentials: 'same-origin', headers: { Accept: 'application/json' }, signal })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось проверить данные для выгрузки.')
  }
  return response.json()
}

export async function loadExport1CCapabilities(organizationId: string, signal?: AbortSignal): Promise<Export1CCapabilities> {
  const response = await fetch(`/api/v1/organizations/${organizationId}/accounting-export/capabilities`, {
    credentials: 'same-origin',
    signal,
    headers: { Accept: 'application/json' }
  })

  if (!response.ok) {
    throw new Error('Не удалось проверить доступ к выгрузке.')
  }

  return response.json()
}

export async function downloadExport1C(organizationId: string, options: Export1COptions, signal?: AbortSignal): Promise<number> {
  const url = new URL(`/api/v1/organizations/${organizationId}/accounting-export`, window.location.origin)
  for (const [key, value] of Object.entries(options)) {
    url.searchParams.set(key, String(value))
  }

  const response = await fetch(url.toString(), {
    credentials: 'same-origin',
    signal,
    headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось сформировать выгрузку.')
  }

  const blob = await response.blob()
  signal?.throwIfAborted()
  const downloadUrl = window.URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = downloadUrl
  anchor.download = `opencrm-accounting-${organizationId}-${options.dateFrom}-${options.dateTo}.json`
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  window.setTimeout(() => window.URL.revokeObjectURL(downloadUrl), 1000)

  return Number(response.headers.get('X-Accounting-Issues') ?? 0)
}

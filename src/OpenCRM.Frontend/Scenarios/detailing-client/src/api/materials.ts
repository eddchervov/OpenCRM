export type MaterialStock = {
  id: string
  catalogItemId?: string | null
  name: string
  unit: string
  packageQuantity?: number | null
  quantityOnHand: number
  reservedQuantity: number
  availableQuantity: number
  minQuantity: number
  averageUnitCost: number
  inventoryValue: number
  updatedAt: string
  isLowStock: boolean
  isOutOfStock: boolean
}

export type MaterialMovement = {
  id: string
  stockId: string
  catalogItemId?: string | null
  maintenanceId?: string | null
  materialName: string
  typeCode: string
  quantity: number
  balanceAfter: number
  unitCost: number
  totalCost: number
  costBalanceAfter: number
  sourceEntityType?: string | null
  sourceEntityId?: string | null
  sourceTitle?: string | null
  sourceUrl?: string | null
  createdByUserId?: string | null
  createdByDisplayName?: string | null
  unit: string
  comment?: string | null
  createdAt: string
}

export type MaterialStockSnapshot = {
  stockId: string
  catalogItemId?: string | null
  name: string
  unit: string
  quantityOnHand: number
  averageUnitCost: number
  inventoryValue: number
}

export type MaterialInventory = {
  stocks: MaterialStock[]
  recentMovements: MaterialMovement[]
  stocksTotalCount: number
  allStocksCount?: number
  movementsTotalCount: number
  stocksPage: number
  stocksPageSize: number
  movementsPage: number
  movementsPageSize: number
  lowStockCount: number
  outOfStockCount: number
  totalQuantityOnHand: number
  totalInventoryValue: number
  snapshotAt?: string | null
  snapshotStocks: MaterialStockSnapshot[]
  snapshotTotalInventoryValue: number
}

export type LoadMaterialsOptions = {
  searchQuery?: string | null
  stockFilter?: string | null
  stocksPage?: number | null
  stocksPageSize?: number | null
  movementType?: string | null
  movementsPage?: number | null
  movementsPageSize?: number | null
  movementDateFrom?: string | null
  movementDateTo?: string | null
  snapshotAt?: string | null
  stockId?: string | null
}

export type MaterialReceiptRequest = {
  catalogItemId?: string | null
  name?: string | null
  unit?: string | null
  quantity: number
  packageQuantity?: number | null
  unitCost?: number | null
  comment?: string | null
}

export type MaterialIssueRequest = {
  stockId: string
  quantity: number
  comment?: string | null
}

export async function loadMaterials(organizationId: string, options: LoadMaterialsOptions = {}): Promise<MaterialInventory> {
  const params = new URLSearchParams()
  appendParam(params, 'searchQuery', options.searchQuery)
  appendParam(params, 'stockFilter', options.stockFilter)
  appendNumberParam(params, 'stocksPage', options.stocksPage)
  appendNumberParam(params, 'stocksPageSize', options.stocksPageSize)
  appendParam(params, 'movementType', options.movementType)
  appendNumberParam(params, 'movementsPage', options.movementsPage)
  appendNumberParam(params, 'movementsPageSize', options.movementsPageSize)
  appendParam(params, 'movementDateFrom', options.movementDateFrom)
  appendParam(params, 'movementDateTo', options.movementDateTo)
  appendParam(params, 'snapshotAt', options.snapshotAt)
  appendParam(params, 'stockId', options.stockId)
  const query = params.toString()
  const response = await fetch(`/api/v1/organizations/${organizationId}/materials${query ? `?${query}` : ''}`, {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить склад.')
  }

  return response.json()
}

export async function createMaterialReceipt(
  organizationId: string,
  payload: MaterialReceiptRequest
): Promise<MaterialStock> {
  return sendMaterialAction(`/api/v1/organizations/${organizationId}/materials/receipt`, 'POST', payload, 'Не удалось добавить приход.')
}

export async function createMaterialIssue(
  organizationId: string,
  payload: MaterialIssueRequest
): Promise<MaterialStock> {
  return sendMaterialAction(`/api/v1/organizations/${organizationId}/materials/issue`, 'POST', payload, 'Не удалось списать материал.')
}

export async function adjustMaterialStock(
  organizationId: string,
  stockId: string,
  payload: { quantityOnHand: number; expectedUpdatedAt: string; averageUnitCost: number | string | null; comment: string }
): Promise<MaterialStock> {
  return sendMaterialAction(`/api/v1/organizations/${organizationId}/materials/${stockId}`, 'PUT', payload, 'Не удалось сохранить корректировку.')
}

function getRequestVerificationToken() {
  return document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
}

function appendParam(params: URLSearchParams, name: string, value?: string | null) {
  const normalized = value?.trim()
  if (normalized) {
    params.set(name, normalized)
  }
}

function appendNumberParam(params: URLSearchParams, name: string, value?: number | null) {
  if (value !== null && value !== undefined && Number.isFinite(value)) {
    params.set(name, String(value))
  }
}

function getMutationHeaders() {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    'Content-Type': 'application/json',
    'X-Requested-With': 'XMLHttpRequest'
  }
  const token = getRequestVerificationToken()

  if (token) {
    headers.RequestVerificationToken = token
  }

  return headers
}

async function sendMaterialAction<T>(
  url: string,
  method: 'POST' | 'PUT',
  payload: unknown,
  errorMessage: string
): Promise<T> {
  const response = await fetch(url, {
    method,
    credentials: 'same-origin',
    headers: getMutationHeaders(),
    body: JSON.stringify(payload)
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? errorMessage)
  }

  return response.json()
}

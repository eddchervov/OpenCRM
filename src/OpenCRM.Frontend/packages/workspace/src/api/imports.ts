import { getJson, sendJson } from './workspaceAdmin'

export type OrganizationImportKind = 'client' | 'vehicle' | 'order' | 'order-line' | 'payment' | 'history'
export type OrganizationImportState = 'upload-queued' | 'mapping-queued' | 'validation-queued' | 'ready' | 'import-queued' | 'importing' | 'paused' | 'completed' | 'failed'

export type OrganizationImportCandidate = {
  id: string
  tableId: string
  sourceRow: number
  kind: OrganizationImportKind
  name: string
  excluded: boolean
  externalId?: string
  clientExternalId?: string
  clientPhone?: string
  vehicleExternalId?: string
  orderExternalId?: string
  partyName?: string
  orderStatus?: string
  occurredAt?: string
  orderComment?: string
  orderType?: string
  detailDescription?: string
  quantity?: string
  unitPrice?: string
  discountAmount?: string
  amount?: string
  paymentMethod?: string
  cancelledAt?: string
  actorName?: string
  eventType?: string
  historyComment?: string
  phone?: string
  comment?: string
  brand?: string
  model?: string
  vin?: string
  plate?: string
  year?: string
  existingClientId?: string | null
  createSeparateClient?: boolean
  status: 'create' | 'link' | 'duplicate' | 'error' | 'conflict' | 'skip'
  message: string
  matches: Array<{ id: string, name: string, phone: string }>
  linkedClientName?: string
  appliedAt?: string | null
  targetId?: string | null
}

export type OrganizationImportTable = {
  id: string
  name: string
  columns: string[]
  rows: Array<{ number: number, values: string[] }>
}

export type OrganizationImportMapping = {
  tableId: string
  kind: OrganizationImportKind | 'combined' | 'ignore'
  fields: Record<string, number[]>
  transformations?: Record<string, OrganizationImportFieldTransformation>
}

export type OrganizationImportFieldTransformation = {
  type: 'phone' | 'date-exact' | 'decimal' | 'value-map'
  format?: string
  values?: Record<string, string>
}

export type OrganizationImportDraft = {
  id: string
  fileName: string
  revision: number
  updatedAt: string
  sourceKey?: string
  importedAt: string | null
  storageVersion: number
  state: OrganizationImportState
  error: string | null
  totalRows: number
  processedRows: number
  filteredRows: number
  page: number
  pageSize: number
  counts: Record<string, number>
  data: {
    tables: OrganizationImportTable[]
    mappings: OrganizationImportMapping[]
    rows: OrganizationImportCandidate[]
    reconciliation?: {
      newClients: number
      linkedClients: number
      newVehicles: number
      existingVehicles: number
      newOrders: number
      newPayments: number
      newHistoryEvents: number
      orderTotal: number | string
      activePaymentsTotal: number | string
      outstandingTotal: number | string
    } | null
  }
  result: null | {
    createdClients: number
    createdVehicles: number
    linkedClients: number
    skipped: number
    createdOrders: number
    createdOrderLines: number
    createdPayments: number
    createdHistoryEvents: number
    clientIds: string[]
    vehicleIds: string[]
  }
}

export type OrganizationImportSummary = {
  id: string
  fileName: string
  revision: number
  updatedAt: string
  importedAt: string | null
  storageVersion: number
  state: OrganizationImportState
  totalRows: number
  processedRows: number
}

export type OrganizationImportProfile = {
  id: string
  name: string
  sourceKey: string | null
  mappings: OrganizationImportMapping[]
  updatedAt: string
  tables: Array<{ id: string, name: string, columns: string[], rows: [] }>
}

export type OrganizationImportAccess = { canImport: boolean }

function root(organizationId: string) {
  return `/api/v1/organizations/${encodeURIComponent(organizationId)}/imports`
}

export function loadOrganizationImportAccess(organizationId: string) {
  return getJson<OrganizationImportAccess>(`${root(organizationId)}/access`, 'Не удалось проверить доступ к импорту.')
}

export function loadOrganizationImports(organizationId: string) {
  return getJson<OrganizationImportSummary[]>(root(organizationId), 'Не удалось загрузить импорты организации.')
}

export function loadOrganizationImportProfiles(organizationId: string) {
  return getJson<OrganizationImportProfile[]>(`${root(organizationId)}/profiles`, 'Не удалось загрузить профили импорта.')
}

export function saveOrganizationImportProfile(
  organizationId: string,
  payload: { name: string, sourceKey: string, mappings: OrganizationImportMapping[], tables: OrganizationImportProfile['tables'] }
) {
  return sendJson<OrganizationImportProfile>(`${root(organizationId)}/profiles`, 'POST', payload, 'Не удалось сохранить профиль импорта.')
}

export function deleteOrganizationImportProfile(organizationId: string, profileId: string) {
  return sendJson<void>(`${root(organizationId)}/profiles/${encodeURIComponent(profileId)}`, 'DELETE', null, 'Не удалось удалить профиль импорта.')
}

export function loadOrganizationImportDraft(organizationId: string, draftId: string) {
  return getJson<OrganizationImportDraft>(`${root(organizationId)}/${encodeURIComponent(draftId)}`, 'Не удалось открыть черновик импорта.')
}

export type OrganizationImportPageQuery = { page: number, pageSize: number, kind?: string, status?: string, search?: string }

export function loadOrganizationImportBatchPage(organizationId: string, draftId: string, query: OrganizationImportPageQuery) {
  const parameters = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) })
  if (query.kind && query.kind !== 'all') parameters.set('kind', query.kind)
  if (query.status && query.status !== 'all') parameters.set('status', query.status)
  if (query.search?.trim()) parameters.set('search', query.search.trim())
  return getJson<OrganizationImportDraft>(`${root(organizationId)}/batches/${encodeURIComponent(draftId)}?${parameters}`, 'Не удалось загрузить страницу импорта.')
}

export async function uploadOrganizationImport(organizationId: string, file: File) {
  return uploadTo(file, root(organizationId))
}

export async function uploadOrganizationImportBatch(organizationId: string, file: File) {
  return uploadTo(file, `${root(organizationId)}/batches`)
}

async function uploadTo(file: File, url: string) {
  const form = new FormData()
  form.append('file', file)
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const headers: Record<string, string> = { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }

  if (token) {
    headers.RequestVerificationToken = token
  }

  const response = await fetch(url, {
    method: 'POST',
    credentials: 'same-origin',
    headers,
    body: form
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить файл.')
  }

  return response.json() as Promise<OrganizationImportDraft>
}

export function queueOrganizationImportBatchMapping(
  organizationId: string,
  draftId: string,
  payload: { revision: number, mappings: OrganizationImportMapping[] }
) {
  return sendJson<OrganizationImportDraft>(`${root(organizationId)}/batches/${encodeURIComponent(draftId)}/mapping`, 'POST', payload, 'Не удалось поставить сопоставление в очередь.')
}

export function saveOrganizationImportBatchSource(organizationId: string, draftId: string, payload: { revision: number, sourceKey: string }) {
  return sendJson<OrganizationImportDraft>(`${root(organizationId)}/batches/${encodeURIComponent(draftId)}/source`, 'PUT', payload, 'Не удалось сохранить источник данных.')
}

export function patchOrganizationImportBatchRows(
  organizationId: string,
  draftId: string,
  payload: { revision: number, rows: OrganizationImportCandidate[] }
) {
  return sendJson<OrganizationImportDraft>(`${root(organizationId)}/batches/${encodeURIComponent(draftId)}`, 'PUT', payload, 'Не удалось поставить правки строк в очередь.')
}

export function startOrganizationImportBatch(organizationId: string, draftId: string, revision: number, resume = false) {
  return sendJson<OrganizationImportDraft>(`${root(organizationId)}/batches/${encodeURIComponent(draftId)}/${resume ? 'resume' : 'confirm'}`, 'POST', { revision }, 'Не удалось запустить импорт.')
}

export function pauseOrganizationImportBatch(organizationId: string, draftId: string, revision: number) {
  return sendJson<OrganizationImportDraft>(`${root(organizationId)}/batches/${encodeURIComponent(draftId)}/pause`, 'POST', { revision }, 'Не удалось приостановить импорт.')
}

export function saveOrganizationImportMapping(
  organizationId: string,
  draftId: string,
  payload: { revision: number, mappings: OrganizationImportMapping[] }
) {
  return sendJson<OrganizationImportDraft>(
    `${root(organizationId)}/${encodeURIComponent(draftId)}/mapping`,
    'POST',
    payload,
    'Не удалось сохранить сопоставление столбцов.'
  )
}

export function saveOrganizationImportRows(
  organizationId: string,
  draftId: string,
  payload: { revision: number, rows: OrganizationImportCandidate[] }
) {
  return sendJson<OrganizationImportDraft>(
    `${root(organizationId)}/${encodeURIComponent(draftId)}`,
    'PUT',
    payload,
    'Не удалось сохранить и проверить строки импорта.'
  )
}

export function confirmOrganizationImport(organizationId: string, draftId: string, revision: number) {
  return sendJson<OrganizationImportDraft>(
    `${root(organizationId)}/${encodeURIComponent(draftId)}/confirm`,
    'POST',
    { revision },
    'Не удалось выполнить импорт.'
  )
}

export async function downloadOrganizationImportReport(organizationId: string, draftId: string) {
  const response = await fetch(`${root(organizationId)}/${encodeURIComponent(draftId)}/report`, {
    credentials: 'same-origin',
    headers: { Accept: 'text/csv', 'X-Requested-With': 'XMLHttpRequest' }
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось скачать отчёт импорта.')
  }

  return response.blob()
}

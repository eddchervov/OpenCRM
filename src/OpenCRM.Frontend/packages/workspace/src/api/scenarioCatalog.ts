export type WorkspaceScenarioCatalogItem = {
  id: string
  kindCode: string
  code: string
  name: string
  description?: string | null
  unitPrice?: number | null
  durationMinutes?: number | null
  serviceIds?: string[]
  sortOrder: number
  isSystem: boolean
  isUsed: boolean
  isActive: boolean
}

export type WorkspaceScenarioCatalog = {
  organizationId: string
  scenarioCode: string
  workTypes: WorkspaceScenarioCatalogItem[]
  services: WorkspaceScenarioCatalogItem[]
  serviceBundles?: WorkspaceScenarioCatalogItem[]
  workPosts: WorkspaceScenarioCatalogItem[]
  documents: WorkspaceScenarioCatalogItem[]
  planningRules: WorkspaceScenarioCatalogItem[]
  materials: WorkspaceScenarioCatalogItem[]
}

export type WorkspaceScenarioCatalogKindCode = 'service' | 'service_bundle' | 'work_post' | 'material' | 'work_type' | 'document' | 'planning_rule'

export type WorkspaceScenarioCatalogCreateRequest = {
  kindCode: WorkspaceScenarioCatalogKindCode
  name: string
  description?: string | null
  unitPrice?: number | null
  durationMinutes?: number | null
  serviceIds?: string[]
  sortOrder?: number
}

export type WorkspaceScenarioCatalogUpdateRequest = {
  name: string
  description?: string | null
  unitPrice?: number | null
  durationMinutes?: number | null
  serviceIds?: string[]
  sortOrder: number
  isActive: boolean
}

function getRequestVerificationToken() {
  return document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
}

function getMutationHeaders(options: { contentTypeJson?: boolean } = {}) {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    'X-Requested-With': 'XMLHttpRequest'
  }
  const token = getRequestVerificationToken()

  if (token) {
    headers.RequestVerificationToken = token
  }

  if (options.contentTypeJson) {
    headers['Content-Type'] = 'application/json'
  }

  return headers
}

export async function loadScenarioCatalog(organizationId: string, signal?: AbortSignal): Promise<WorkspaceScenarioCatalog> {
  const response = await fetch(`/api/v1/organizations/${organizationId}/scenario-catalog`, {
    method: 'GET',
    signal,
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить справочники.')
  }

  return response.json()
}

export async function createScenarioCatalogItem(
  organizationId: string,
  payload: WorkspaceScenarioCatalogCreateRequest
): Promise<WorkspaceScenarioCatalogItem> {
  const response = await fetch(`/api/v1/organizations/${organizationId}/scenario-catalog`, {
    method: 'POST',
    credentials: 'same-origin',
    headers: getMutationHeaders({ contentTypeJson: true }),
    body: JSON.stringify(payload)
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось добавить элемент справочника.')
  }

  return response.json()
}

export async function updateScenarioCatalogItem(
  organizationId: string,
  itemId: string,
  payload: WorkspaceScenarioCatalogUpdateRequest
): Promise<WorkspaceScenarioCatalogItem> {
  const response = await fetch(`/api/v1/organizations/${organizationId}/scenario-catalog/${itemId}`, {
    method: 'PUT',
    credentials: 'same-origin',
    headers: getMutationHeaders({ contentTypeJson: true }),
    body: JSON.stringify(payload)
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось сохранить элемент справочника.')
  }

  return response.json()
}

export async function deleteScenarioCatalogItem(
  organizationId: string,
  itemId: string
): Promise<void> {
  const response = await fetch(`/api/v1/organizations/${organizationId}/scenario-catalog/${itemId}`, {
    method: 'DELETE',
    credentials: 'same-origin',
    headers: getMutationHeaders()
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось удалить элемент справочника.')
  }
}

export type WorkPostAffectedMaintenances = {
  totalCount: number
  canViewMaintenances?: boolean
  items: { maintenanceId: string, date: string, vehicleName?: string | null, registrationPlate?: string | null, status: number }[]
}

export async function loadWorkPostAffectedMaintenances(organizationId: string, itemId: string): Promise<WorkPostAffectedMaintenances> {
  const response = await fetch(
    `/api/v1/organizations/${organizationId}/scenario-catalog/${itemId}/affected-maintenance`,
    { credentials: 'same-origin', headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' } }
  )
  if (response.status === 403) {
    return { totalCount: 0, items: [], canViewMaintenances: false }
  }
  if (!response.ok) {
    throw new Error('Не удалось проверить назначения на пост. Повторите попытку.')
  }

  return response.json()
}

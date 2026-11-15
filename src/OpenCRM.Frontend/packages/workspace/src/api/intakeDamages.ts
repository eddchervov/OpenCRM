import { getMutationHeaders } from './maintenance'

export type IntakeDamageView = 'front' | 'rear' | 'left' | 'right'
export type IntakeDamageType = 'scratch' | 'chip' | 'dent' | 'other'

export type IntakeDamage = {
  id: string
  number: number
  viewCode: IntakeDamageView
  damageType: IntakeDamageType
  x: number
  y: number
  description: string | null
  photoIds: string[]
}

export type IntakeDamagesResponse = {
  version: number
  diagramVersion: string
  damages: IntakeDamage[]
}

export type IntakeDamagePayload = Omit<IntakeDamage, 'id' | 'number'>

export class IntakeDamageApiError extends Error {
  constructor(message: string, readonly status: number) {
    super(message)
    this.name = 'IntakeDamageApiError'
  }
}

async function readErrorMessage(response: Response) {
  const contentType = response.headers.get('content-type') ?? ''
  if (contentType.includes('application/json')) {
    const error = await response.json().catch(() => null)
    const validationMessages = error?.errors && typeof error.errors === 'object'
      ? Object.values(error.errors).flat().filter(Boolean).join('\n')
      : ''
    return validationMessages || error?.message || error?.title || 'Не удалось сохранить повреждения.'
  }

  return (await response.text().catch(() => '')).trim() || 'Не удалось сохранить повреждения.'
}

function getUrl(maintenanceId: string, organizationId: string, damageId?: string) {
  const path = `/api/v1/maintenance/${encodeURIComponent(maintenanceId)}/intake/damages${damageId ? `/${encodeURIComponent(damageId)}` : ''}`
  const url = new URL(path, window.location.origin)
  url.searchParams.set('organizationId', organizationId)
  return url
}

export async function loadIntakeDamages(
  organizationId: string,
  maintenanceId: string,
  signal?: AbortSignal
): Promise<IntakeDamagesResponse> {
  const response = await fetch(getUrl(maintenanceId, organizationId), {
    method: 'GET', credentials: 'same-origin', signal,
    headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
  })
  if (!response.ok) throw new IntakeDamageApiError(await readErrorMessage(response), response.status)
  return response.json()
}

export async function saveIntakeDamage(
  organizationId: string,
  maintenanceId: string,
  damageId: string,
  version: number,
  diagramVersion: string,
  damage: IntakeDamagePayload
): Promise<void> {
  const response = await fetch(getUrl(maintenanceId, organizationId, damageId), {
    method: 'PUT', credentials: 'same-origin', headers: getMutationHeaders({ acceptJson: true, contentTypeJson: true }),
    body: JSON.stringify({ version, diagramVersion, ...damage })
  })
  if (!response.ok) throw new IntakeDamageApiError(await readErrorMessage(response), response.status)
}

export async function deleteIntakeDamage(
  organizationId: string,
  maintenanceId: string,
  damageId: string,
  version: number
): Promise<void> {
  const url = getUrl(maintenanceId, organizationId, damageId)
  url.searchParams.set('version', String(version))
  const response = await fetch(url, {
    method: 'DELETE', credentials: 'same-origin', headers: getMutationHeaders()
  })
  if (!response.ok) throw new IntakeDamageApiError(await readErrorMessage(response), response.status)
}


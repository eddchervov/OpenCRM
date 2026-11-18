import { translate as t } from '@opencrm/ui/i18n'
import '@opencrm/workspace/vehicleApiMessages'
import type { WorkspaceMaintenance } from './maintenance'

export type WorkspaceVehicleBrandFilter = {
  id: string
  name: string
}

export type WorkspaceVehicleCatalogOption = {
  id: number
  name: string
}

export type WorkspaceVehicle = {
  id: string
  kind: number
  mileageUnit: number
  brandId?: number | null
  brandName: string
  brandLogoKey?: string | null
  displayName?: string
  modelId?: number | null
  modelName: string
  yearIssue?: number | null
  photoId?: string | null
  photoUrl?: string | null
  isArchived: boolean
  archiveDate?: string | null
  daysLeft?: number | null
  maintenanceCount: number
  lastMaintenanceDate?: string | null
  currentMaintenanceStatus?: number | null
  currentMaintenanceId?: string | null
  currentMaintenanceName?: string | null
  completedWorkCount?: number
  totalWorkCount?: number
  currentMaintenanceDate?: string | null
  name?: string | null
  vin?: string | null
  registrationPlate?: string | null
  registrationCountryCode?: string | null
  bodyNumber?: string | null
  engineNumber?: string | null
  color?: string | null
  registrationCertificate?: string | null
  commissioningDate?: string | null
  description?: string | null
  currentMileage?: number | null
  currentMileageUpdatedAt?: string | null
  manualBrandName?: string | null
  manualModelName?: string | null
  isManualEntry: boolean
  dateTimeCreation: string
  organizationId?: string | null
  organizationClientId?: string | null
  organizationClientName?: string | null
}

export function getVehicleDisplayName(vehicle: Pick<WorkspaceVehicle, 'brandName' | 'modelName' | 'manualBrandName' | 'manualModelName' | 'registrationPlate'>, includeRegistrationPlate = false) {
  const identity = [vehicle.brandName || vehicle.manualBrandName, vehicle.modelName || vehicle.manualModelName]
    .map(value => value?.trim()).filter(Boolean).join(' ')

  return [identity || t('vehicleApi.vehicle'), !identity || includeRegistrationPlate ? vehicle.registrationPlate?.trim() : null]
    .filter(Boolean).join(' · ')
}

export type WorkspaceVehiclesPage = {
  canViewMaintenance?: boolean
  items: WorkspaceVehicle[]
  userBrands: WorkspaceVehicleBrandFilter[]
  userKinds: number[]
  page: number
  pageSize: number
  totalCount: number
  allCount: number
  totalPages: number
}

export type WorkspaceVehiclePhoto = {
  photoId: string
  url: string
  isPrimary: boolean
  albumId?: string | null
  originalFileName?: string | null
  size?: number | null
  createdAt?: string | null
}

export type WorkspaceVehicleHistoryEvent = {
  id: string
  eventType: string
  title: string
  description?: string | null
  actorDisplayName?: string | null
  relatedEntityType?: string | null
  relatedEntityId?: string | null
  relatedUrl?: string | null
  createdAt: string
}

export type WorkspaceVehicleMaintenancePage = {
  items: WorkspaceMaintenance[]
  totalCount: number
  page: number
  pageSize: number
  hasMore: boolean
}

export type WorkspaceVehicleAlbum = {
  albumId: string
  vehicleId: string
  title: string
  createdAt: string
  photoCount: number
  primaryPhotoId?: string | null
  photos: WorkspaceVehiclePhoto[]
}

export type WorkspaceVehicleAlbumsResponse = {
  albums: WorkspaceVehicleAlbum[]
  limit?: {
    currentCount?: number
    maxCount?: number
    isLimitedByPlan?: boolean
  } | null
}

type WorkspaceVehiclePhotosGroup = {
  vehicleId: string
  photos: WorkspaceVehiclePhoto[]
}

function getRequestVerificationToken() {
  return document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
}

function getMutationHeaders(options: { acceptJson?: boolean, contentTypeJson?: boolean } = {}) {
  const headers: Record<string, string> = {
    'X-Requested-With': 'XMLHttpRequest'
  }
  const token = getRequestVerificationToken()

  if (token) {
    headers.RequestVerificationToken = token
  }

  if (options.acceptJson) {
    headers.Accept = 'application/json'
  }

  if (options.contentTypeJson) {
    headers['Content-Type'] = 'application/json'
  }

  return headers
}

async function readErrorMessage(response: Response, fallbackMessage: string) {
  const contentType = response.headers.get('content-type') ?? ''

  if (contentType.includes('application/json')) {
    const error = await response.json().catch(() => null)
    const validationMessages = error?.errors && typeof error.errors === 'object'
      ? Object.values(error.errors).flat().filter(Boolean).join('\n')
      : ''

    return validationMessages || error?.message || error?.title || fallbackMessage
  }

  const text = await response.text().catch(() => '')
  return text.trim() || fallbackMessage
}

export type WorkspaceVehicleSaveRequest = {
  kind: number
  mileageUnit: number
  modelId?: number | null
  yearIssue?: number | null
  manualBrandName?: string | null
  manualModelName?: string | null
  name?: string | null
  vin?: string | null
  registrationPlate?: string | null
  allowOtherRegistrationPlateFormat?: boolean
  registrationCountryCode?: string | null
  bodyNumber?: string | null
  engineNumber?: string | null
  color?: string | null
  registrationCertificate?: string | null
  commissioningDate?: string | null
  description?: string | null
  organizationId?: string | null
  organizationClientId?: string | null
}

export async function loadVehicleBrandOptions(kind?: string | number | null): Promise<WorkspaceVehicleCatalogOption[]> {
  const url = new URL('/api/v1/catalog/brands/options', window.location.origin)

  if (kind) {
    url.searchParams.set('kind', String(kind))
  }

  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, t('vehicleApi.brands')))
  }

  return response.json()
}

export async function loadVehicleModelOptions(
  brandId: string | number,
  kind?: string | number | null
): Promise<WorkspaceVehicleCatalogOption[]> {
  const url = new URL('/api/v1/catalog/models/options', window.location.origin)
  url.searchParams.set('brandId', String(brandId))

  if (kind) {
    url.searchParams.set('kind', String(kind))
  }

  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, t('vehicleApi.models')))
  }

  return response.json()
}

export async function loadVehiclesPage(
  organizationId: string,
  page: number,
  pageSize: number,
  options: {
    isArchived?: boolean
    brandFilter?: string
    kind?: string
    searchQuery?: string
    organizationClientId?: string
  } = {}
): Promise<WorkspaceVehiclesPage> {
  const url = new URL('/api/v1/vehicles', window.location.origin)
  url.searchParams.set('organizationId', organizationId)
  url.searchParams.set('page', String(page))
  url.searchParams.set('pageSize', String(pageSize))
  url.searchParams.set('isArchived', options.isArchived ? 'true' : 'false')

  if (options.brandFilter && options.brandFilter !== 'all') {
    url.searchParams.set('brandFilter', options.brandFilter)
  }

  if (options.kind && options.kind !== 'all') {
    url.searchParams.set('kind', options.kind)
  }

  if (options.searchQuery) {
    url.searchParams.set('searchQuery', options.searchQuery)
  }

  if (options.organizationClientId) {
    url.searchParams.set('organizationClientId', options.organizationClientId)
  }

  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, t('vehicleApi.vehicles')))
  }

  return response.json()
}

export async function loadVehicle(
  organizationId: string,
  vehicleId: string
): Promise<WorkspaceVehicle> {
  const url = new URL(`/api/v1/vehicles/${vehicleId}`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, t('vehicleApi.card')))
  }

  return response.json()
}

export async function loadVehicleHistory(
  organizationId: string,
  vehicleId: string
): Promise<WorkspaceVehicleHistoryEvent[]> {
  const url = new URL(`/api/v1/vehicles/${vehicleId}/history`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)
  url.searchParams.set('take', '25')

  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    return []
  }

  return response.json()
}

export async function loadVehicleMaintenancePage(
  organizationId: string,
  vehicleId: string,
  page = 1,
  pageSize = 8,
  status?: number
): Promise<WorkspaceVehicleMaintenancePage> {
  const url = new URL(`/api/v1/vehicles/${vehicleId}/maintenance/${page}/${pageSize}`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)
  url.searchParams.set('sort', 'date_desc')
  if (status) url.searchParams.set('status', String(status))

  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, t('vehicleApi.history')))
  }

  return response.json()
}

export async function loadVehicleAlbums(
  organizationId: string,
  vehicleId: string
): Promise<WorkspaceVehicleAlbumsResponse> {
  const url = new URL(`/api/v1/media/vehicles/${vehicleId}/albums`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    return { albums: [] }
  }

  return response.json()
}

export async function loadVehicleAlbum(
  organizationId: string,
  vehicleId: string,
  albumId: string
): Promise<WorkspaceVehicleAlbum> {
  const url = new URL(`/api/v1/media/vehicles/${vehicleId}/albums/${albumId}`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, t('vehicleApi.album')))
  }

  return response.json()
}

export async function loadVehiclePhotos(
  organizationId: string,
  vehicleIds: string[]
): Promise<Record<string, WorkspaceVehiclePhoto[]>> {
  if (vehicleIds.length === 0) {
    return {}
  }

  const url = new URL('/api/v1/media/vehicles/photos', window.location.origin)
  url.searchParams.set('organizationId', organizationId)
  vehicleIds.forEach(vehicleId => url.searchParams.append('vehicleIds', vehicleId))

  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    return {}
  }

  const groups = await response.json() as WorkspaceVehiclePhotosGroup[]
  return Object.fromEntries(groups.map(group => [
    group.vehicleId,
    [...group.photos].sort((left, right) => Number(right.isPrimary) - Number(left.isPrimary))
  ]))
}

export async function loadVehiclePhotoList(
  organizationId: string,
  vehicleId: string
): Promise<WorkspaceVehiclePhoto[]> {
  const url = new URL(`/api/v1/media/vehicles/${vehicleId}/photos`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    return []
  }

  return response.json()
}

export async function uploadVehiclePhotos(
  organizationId: string,
  vehicleId: string,
  files: File[]
): Promise<string[]> {
  const url = new URL(`/api/v1/media/vehicles/${vehicleId}/photos`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const formData = new FormData()
  files.forEach(file => formData.append('formFiles', file))

  const response = await fetch(url.toString(), {
    method: 'POST',
    credentials: 'same-origin',
    headers: getMutationHeaders({ acceptJson: true }),
    body: formData
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, t('vehicleApi.uploadPhoto')))
  }

  return response.json()
}

export async function deleteVehiclePhoto(
  organizationId: string,
  vehicleId: string,
  photoId: string
): Promise<void> {
  const url = new URL(`/api/v1/media/vehicles/${vehicleId}/photos/${photoId}`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'DELETE',
    credentials: 'same-origin',
    headers: getMutationHeaders()
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, t('vehicleApi.deletePhoto')))
  }
}

export async function setPrimaryVehiclePhoto(
  organizationId: string,
  vehicleId: string,
  photoId: string
): Promise<void> {
  const url = new URL(`/api/v1/media/vehicles/${vehicleId}/photos/${photoId}/primary`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'POST',
    credentials: 'same-origin',
    headers: getMutationHeaders()
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? t('vehicleApi.mainPhoto'))
  }
}

export async function createVehicleAlbum(
  organizationId: string,
  vehicleId: string,
  title: string
): Promise<WorkspaceVehicleAlbum> {
  const url = new URL(`/api/v1/media/vehicles/${vehicleId}/albums`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'POST',
    credentials: 'same-origin',
    headers: getMutationHeaders({ acceptJson: true, contentTypeJson: true }),
    body: JSON.stringify({ title })
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, t('vehicleApi.createAlbum')))
  }

  return response.json()
}

export async function deleteVehicleAlbum(
  organizationId: string,
  vehicleId: string,
  albumId: string
): Promise<void> {
  const url = new URL(`/api/v1/media/vehicles/${vehicleId}/albums/${albumId}`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'DELETE',
    credentials: 'same-origin',
    headers: getMutationHeaders()
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, t('vehicleApi.deleteAlbum')))
  }
}

export async function uploadVehicleAlbumPhotos(
  organizationId: string,
  vehicleId: string,
  albumId: string,
  files: File[]
): Promise<string[]> {
  const url = new URL(`/api/v1/media/vehicles/${vehicleId}/albums/${albumId}/photos`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const formData = new FormData()
  files.forEach(file => formData.append('formFiles', file))

  const response = await fetch(url.toString(), {
    method: 'POST',
    credentials: 'same-origin',
    headers: getMutationHeaders({ acceptJson: true }),
    body: formData
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, t('vehicleApi.uploadAlbumPhoto')))
  }

  return response.json()
}

export async function deleteVehicleAlbumPhoto(
  organizationId: string,
  vehicleId: string,
  albumId: string,
  photoId: string
): Promise<void> {
  const url = new URL(`/api/v1/media/vehicles/${vehicleId}/albums/${albumId}/photos/${photoId}`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'DELETE',
    credentials: 'same-origin',
    headers: getMutationHeaders()
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, t('vehicleApi.deleteAlbumPhoto')))
  }
}

export async function setPrimaryVehicleAlbumPhoto(
  organizationId: string,
  vehicleId: string,
  albumId: string,
  photoId: string
): Promise<void> {
  const url = new URL(`/api/v1/media/vehicles/${vehicleId}/albums/${albumId}/photos/${photoId}/primary`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'POST',
    credentials: 'same-origin',
    headers: getMutationHeaders()
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? t('vehicleApi.mainAlbumPhoto'))
  }
}

export async function createVehicle(payload: WorkspaceVehicleSaveRequest): Promise<string> {
  const url = new URL('/api/v1/vehicles', window.location.origin)
  const response = await fetch(url.toString(), {
    method: 'POST',
    credentials: 'same-origin',
    headers: getMutationHeaders({ acceptJson: true, contentTypeJson: true }),
    body: JSON.stringify(payload)
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, t('vehicleApi.save')))
  }

  return response.json()
}

export async function updateVehicle(
  organizationId: string,
  vehicleId: string,
  payload: WorkspaceVehicleSaveRequest
): Promise<void> {
  const url = new URL(`/api/v1/vehicles/${vehicleId}`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'PUT',
    credentials: 'same-origin',
    headers: getMutationHeaders({ acceptJson: true, contentTypeJson: true }),
    body: JSON.stringify(payload)
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, t('vehicleApi.save')))
  }
}

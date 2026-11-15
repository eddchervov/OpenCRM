export type WorkspaceMaintenanceDetail = {
  detailId?: string | null
  name: string
  description?: string | null
  price?: number | null
  vatRateCode?: string | null
  employeeId?: string | null
  employeeName?: string | null
  catalogItemId?: string | null
  quantity?: number
  materialCatalogItemId?: string | null
  materialName?: string | null
  materialQuantity?: number | null
  materialUnit?: string | null
  materialUnitCost?: number | null
  materialTotalCost?: number | null
}

export type WorkspaceMaintenanceDefect = {
  id: string
  title: string
  description?: string | null
  severityCode: string
  statusCode: string
  createdAt: string
  closedAt?: string | null
}

export type WorkspaceMaintenance = {
  version?: number
  canManagePayments?: boolean
  canEditServices?: boolean
  id: string
  status: number
  type: number
  date: string
  plannedDate?: string | null
  plannedStartTime?: string | null
  plannedEndDate?: string | null
  plannedEndTime?: string | null
  readyForHandoverAt?: string | null
  handedOverAt?: string | null
  completedDate?: string | null
  location?: string | null
  employeeId?: string | null
  performer?: string | null
  mileage?: number | null
  comment?: string | null
  intakeMileage?: number | null
  intakeCompleteness?: string | null
  intakeVisibleDamage?: string | null
  intakeNotes?: string | null
  intakeRecordedAt?: string | null
  price?: number | null
  grossAmount: number
  discountAmount: number
  paidAmount: number
  debtAmount: number
  materialCostAmount: number
  laborCostAmount: number
  profitAmount: number
  paymentStatus: string
  vehicleId: string
  buyerClientId?: string | null
  buyerName?: string | null
  buyerPartyTypeCode?: string | null
  buyerInn?: string | null
  buyerKpp?: string | null
  buyerLegalAddress?: string | null
  photoIds: string[]
  maintenanceDetails: WorkspaceMaintenanceDetail[]
  linkedDefects: WorkspaceMaintenanceDefect[]
}

export type MaintenancePhotoStage = 0 | 1 | 2

export type WorkspaceMaintenancePhoto = {
  stage: MaintenancePhotoStage
  createdAt: string
  photoId: string
  url: string
}

export type WorkspaceMaintenanceSaveRequest = {
  buyerClientId?: string | null
  hasBuyerSelection?: boolean
  changeBuyer?: boolean
  version?: number
  status: number
  type: number
  plannedDate?: string | null
  plannedStartTime?: string | null
  plannedEndDate?: string | null
  plannedEndTime?: string | null
  completedDate?: string | null
  location?: string | null
  employeeId?: string | null
  performer?: string | null
  mileage?: number | null
  comment?: string | null
  discountAmount?: number | null
  paidAmount?: number | null
  laborCostAmount?: number | null
  details: WorkspaceMaintenanceDetail[]
  linkedDefectIds?: string[] | null
}

export type WorkspaceMaintenanceWorkflowAction = {
  action: 'start' | 'complete' | 'cancel' | 'reschedule' | 'reopen' | 'return_to_planned' | 'return_to_work' | 'mark_ready' | 'handover' | 'undo_handover' | 'simple_handover'
  version?: number
  location?: string
  plannedDate?: string | null
  plannedStartTime?: string | null
  plannedEndDate?: string | null
  plannedEndTime?: string | null
  completedDate?: string | null
  mileage?: number | null
  reason?: string | null
}

function getRequestVerificationToken() {
  return document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
}

export function getMutationHeaders(options: { acceptJson?: boolean, contentTypeJson?: boolean } = {}) {
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

  if (contentType.includes('application/json') || contentType.includes('application/problem+json')) {
    const error = await response.json().catch(() => null)
    const validationMessages = error?.errors && typeof error.errors === 'object'
      ? Object.values(error.errors).flat().filter(Boolean).join('\n')
      : ''

    return validationMessages || error?.message || error?.title || fallbackMessage
  }

  const text = await response.text().catch(() => '')
  if (contentType.includes('text/html') || /^\s*</u.test(text)) return fallbackMessage
  return text.trim() || fallbackMessage
}

export type MaintenanceHistoryEvent = {
  id: string
  eventType: string
  title: string
  description?: string | null
  actorDisplayName?: string | null
  sourceInquiryId?: string | null
  timeZoneId?: string | null
  createdAt: string
}

export async function loadMaintenanceHistory(organizationId: string, maintenanceId: string, signal?: AbortSignal): Promise<MaintenanceHistoryEvent[]> {
  const url = new URL(`/api/v1/maintenance/${encodeURIComponent(maintenanceId)}/history`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)
  const response = await fetch(url.toString(), {
    method: 'GET', credentials: 'same-origin', signal,
    headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
  })
  if (!response.ok) throw new Error(await readErrorMessage(response, 'Не удалось загрузить историю обслуживания.'))
  return response.json()
}

export async function loadMaintenance(
  organizationId: string,
  maintenanceId: string
): Promise<WorkspaceMaintenance> {
  const url = new URL(`/api/v1/maintenance/${maintenanceId}`, window.location.origin)
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
    throw new Error(await readErrorMessage(response, 'Не удалось загрузить карточку записи.'))
  }

  return response.json()
}

export async function loadMaintenancePhotos(
  organizationId: string,
  maintenanceId: string
): Promise<WorkspaceMaintenancePhoto[]> {
  const url = new URL(`/api/v1/media/maintenance/${maintenanceId}/photos`, window.location.origin)
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
    throw new Error(await readErrorMessage(response, 'Не удалось загрузить фотографии.'))
  }

  return response.json()
}

export async function uploadMaintenancePhotos(
  organizationId: string,
  maintenanceId: string,
  files: File[],
  stage: MaintenancePhotoStage = 0
): Promise<void> {
  const url = new URL(`/api/v1/media/maintenance/${maintenanceId}/photos`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)
  url.searchParams.set('stage', String(stage))

  const formData = new FormData()
  files.forEach(file => formData.append('formFiles', file))

  const response = await fetch(url.toString(), {
    method: 'POST',
    credentials: 'same-origin',
    headers: getMutationHeaders({ acceptJson: true }),
    body: formData
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Не удалось загрузить фото услуги.'))
  }
}

export async function deleteMaintenancePhoto(
  organizationId: string,
  maintenanceId: string,
  photoId: string
): Promise<void> {
  const url = new URL(`/api/v1/media/maintenance/${maintenanceId}/photos/${photoId}`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'DELETE',
    credentials: 'same-origin',
    headers: getMutationHeaders()
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Не удалось удалить фото услуги.'))
  }
}

export async function createMaintenance(
  organizationId: string,
  vehicleId: string,
  payload: WorkspaceMaintenanceSaveRequest
): Promise<string> {
  const url = new URL(`/api/v1/vehicles/${vehicleId}/maintenance`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'POST',
    credentials: 'same-origin',
    headers: getMutationHeaders({ acceptJson: true, contentTypeJson: true }),
    body: JSON.stringify(payload)
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Не удалось сохранить услугу.'))
  }

  return response.json()
}

export async function updateMaintenance(
  organizationId: string,
  maintenanceId: string,
  payload: WorkspaceMaintenanceSaveRequest
): Promise<void> {
  const url = new URL(`/api/v1/maintenance/${maintenanceId}`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'PUT',
    credentials: 'same-origin',
    headers: getMutationHeaders({ acceptJson: true, contentTypeJson: true }),
    body: JSON.stringify(payload)
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Не удалось сохранить услугу.'))
  }
}

export async function changeMaintenanceWorkflow(
  organizationId: string,
  maintenanceId: string,
  payload: WorkspaceMaintenanceWorkflowAction
): Promise<void> {
  const url = new URL(`/api/v1/maintenance/${maintenanceId}/workflow`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)

  const response = await fetch(url.toString(), {
    method: 'POST',
    credentials: 'same-origin',
    headers: getMutationHeaders({ acceptJson: true, contentTypeJson: true }),
    body: JSON.stringify(payload)
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Не удалось изменить статус записи.'))
  }
}

export type WorkspaceMaintenanceIntakeRequest = {
  mileage: number | null
  completeness: string
  visibleDamage: string
  notes: string
}

export async function saveMaintenanceIntake(
  organizationId: string, maintenanceId: string, intake: WorkspaceMaintenanceIntakeRequest
): Promise<void> {
  const url = new URL(`/api/v1/maintenance/${maintenanceId}/intake`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)
  const response = await fetch(url, {
    method: 'PUT',
    credentials: 'same-origin',
    headers: { ...getMutationHeaders({ acceptJson: true }), 'Content-Type': 'application/json' },
    body: JSON.stringify(intake)
  })
  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Не удалось сохранить приёмку.'))
  }
}

export async function setMaintenancePhotoStage(
  organizationId: string, maintenanceId: string, photoId: string, stage: MaintenancePhotoStage
): Promise<void> {
  const url = new URL(`/api/v1/media/maintenance/${maintenanceId}/photos/${photoId}/stage`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)
  url.searchParams.set('stage', String(stage))
  const response = await fetch(url, { method: 'PUT', credentials: 'same-origin', headers: getMutationHeaders() })
  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Не удалось изменить этап фотографии.'))
  }
}

export async function loadMaintenancePhotoContent(
  organizationId: string, maintenanceId: string, photoId: string, signal: AbortSignal
): Promise<Blob> {
  const url = new URL(`/api/v1/media/maintenance/${maintenanceId}/photos/${photoId}/content`, window.location.origin)
  url.searchParams.set('organizationId', organizationId)
  const response = await fetch(url, { credentials: 'same-origin', signal })
  if (!response.ok || !response.headers.get('content-type')?.startsWith('image/')) {
    throw new Error('Не удалось загрузить фотографии. Повторите попытку или обновите карточку записи.')
  }
  return response.blob()
}

export type WorkspaceMaintenancePayment = {
  id: string
  amount: number
  methodCode?: string | null
  payerClientId?: string | null
  payerName?: string | null
  createdAt: string
  createdBy: string
  comment?: string | null
  isOpeningBalance: boolean
  cancelledAt?: string | null
  cancelledBy?: string | null
  cancellationReason?: string | null
}

export async function loadMaintenancePayments(organizationId: string, maintenanceId: string): Promise<WorkspaceMaintenancePayment[]> {
  const response = await fetch(`/api/v1/maintenance/${maintenanceId}/payments?organizationId=${organizationId}`, {
    credentials: 'same-origin', headers: getMutationHeaders({ acceptJson: true })
  })
  if (!response.ok) throw new Error(await readErrorMessage(response, 'Не удалось загрузить оплаты.'))
  return response.json()
}

export async function saveMaintenanceCommerce(
  organizationId: string, maintenanceId: string, path: string, payload: unknown, method = 'POST'
): Promise<void> {
  const response = await fetch(`/api/v1/maintenance/${maintenanceId}/${path}?organizationId=${organizationId}`, {
    method, credentials: 'same-origin', headers: getMutationHeaders({ contentTypeJson: true, acceptJson: true }),
    body: JSON.stringify(payload)
  })
  if (!response.ok) throw new Error(await readErrorMessage(response, 'Не удалось сохранить изменения.'))
}

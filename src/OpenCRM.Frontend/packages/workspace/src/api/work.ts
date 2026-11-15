export type AssignedWork = {
  maintenanceId: string
  detailId: string
  name: string
  description?: string | null
  price?: number | null
  quantity: number
  vehicleName?: string | null
  registrationPlate?: string | null
  location?: string | null
  plannedDate?: string | null
  plannedStartTime?: string | null
  plannedEndDate?: string | null
  plannedEndTime?: string | null
  intakeNotes?: string | null
  intakeCompleteness?: string | null
  intakeVisibleDamage?: string | null
  workComment?: string | null
  reworkReason?: string | null
  status: number
  version: number
  startedAt?: string | null
  completedAt?: string | null
  activeTime: string
}

export type WorkProgress = {
  detailId: string
  name: string
  employeeName?: string | null
  hasAssignedEmployee: boolean
  status: number
  version: number
  activeTime: string
  pauseReason?: string | null
  completedBy?: string | null
  completionReason?: string | null
}

export type OperationMaterialStock = {
  id: string
  name: string
  unit: string
  packageQuantity?: number | null
  availableQuantity: number
}

export type OperationMaterialIssue = {
  id: string
  stockId: string
  materialName: string
  quantity: number
  unit: string
  createdAt: string
  isReturn: boolean
  sourceMovementId?: string | null
}

export type WorkReport = {
  id: string
  maintenanceId: string
  detailId: string
  kind: 'comment' | 'defect' | 'extra' | 'time'
  text: string
  employeeName: string
  createdAt: string
  decision?: 'approved' | 'rejected' | null
  decisionReason?: string | null
  decidedAt?: string | null
  approvedPrice?: number | null
  timeAdjustmentMinutes?: number | null
}

export type WorkPhoto = {
  photoId: string
  stage: number
  createdAt: string
  detailId?: string | null
  url: string
}

export type ProductionReport = {
  maintenanceId: string
  totalOperations: number
  completedOperations: number
  activeTime: string
  timeAdjustmentsCount: number
  materials: Array<{ name: string; unit: string; quantity: number; cost?: number | null }>
  materialCostAmount?: number | null
  laborCostAmount?: number | null
  revenueAmount?: number | null
  grossMarginAmount?: number | null
}

export async function loadProductionReport(
  organizationId: string, maintenanceId: string
): Promise<ProductionReport> {
  const response = await fetch(
    `/api/v1/organizations/${organizationId}/work/${maintenanceId}/production-report`,
    { headers: { Accept: 'application/json' } }
  )
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить производственный отчёт.')
  }

  return response.json()
}

export async function loadWorkPhotos(organizationId: string, work: AssignedWork): Promise<WorkPhoto[]> {
  const response = await fetch(
    `/api/v1/organizations/${organizationId}/work/${work.maintenanceId}/operations/${work.detailId}/photos`,
    { headers: { Accept: 'application/json' } }
  )
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить фотографии.')
  }

  return response.json()
}

export async function uploadWorkPhotos(
  organizationId: string, work: AssignedWork, files: File[], stage: number
): Promise<void> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const form = new FormData()
  files.forEach(file => form.append('files', file))
  const response = await fetch(
    `/api/v1/organizations/${organizationId}/work/${work.maintenanceId}/operations/${work.detailId}/photos?stage=${stage}`,
    { method: 'POST', headers: { RequestVerificationToken: token }, body: form }
  )
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить фотографии.')
  }
}

export async function loadWorkReports(
  organizationId: string, maintenanceId: string, detailId?: string
): Promise<WorkReport[]> {
  const query = detailId ? `?detailId=${detailId}` : ''
  const response = await fetch(`/api/v1/organizations/${organizationId}/work/${maintenanceId}/reports${query}`, {
    headers: { Accept: 'application/json' }
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить замечания.')
  }

  return response.json()
}

export async function addWorkReport(
  organizationId: string, work: AssignedWork, kind: WorkReport['kind'], reportText: string
): Promise<WorkReport> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(
    `/api/v1/organizations/${organizationId}/work/${work.maintenanceId}/operations/${work.detailId}/reports`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
      body: JSON.stringify({ commandId: crypto.randomUUID(), kind, text: reportText })
    }
  )
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось сохранить сообщение.')
  }

  return response.json()
}

export async function decideWorkReport(
  organizationId: string, report: WorkReport, decision: 'approved' | 'rejected',
  reason?: string, price?: number, vatRateCode?: string
): Promise<WorkReport> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(
    `/api/v1/organizations/${organizationId}/work/${report.maintenanceId}/reports/${report.id}/decision`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
      body: JSON.stringify({ decision, reason: reason || null, price: price ?? null, vatRateCode: vatRateCode || null })
    }
  )
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось сохранить решение.')
  }

  return response.json()
}

export async function adjustWorkTime(
  organizationId: string, maintenanceId: string, detailId: string, minutes: number, reason: string
): Promise<WorkReport> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(
    `/api/v1/organizations/${organizationId}/work/${maintenanceId}/operations/${detailId}/time-adjustments`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
      body: JSON.stringify({ commandId: crypto.randomUUID(), minutes, reason })
    }
  )
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось исправить время работы.')
  }

  return response.json()
}

export async function loadAvailableOperationMaterials(
  organizationId: string, work: AssignedWork
): Promise<OperationMaterialStock[]> {
  const response = await fetch(
    `/api/v1/organizations/${organizationId}/work/${work.maintenanceId}/operations/${work.detailId}/materials/available`,
    { headers: { Accept: 'application/json' } }
  )
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить материалы.')
  }

  return response.json()
}

export async function loadIssuedOperationMaterials(
  organizationId: string, work: AssignedWork
): Promise<OperationMaterialIssue[]> {
  const response = await fetch(
    `/api/v1/organizations/${organizationId}/work/${work.maintenanceId}/operations/${work.detailId}/materials`,
    { headers: { Accept: 'application/json' } }
  )
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить списанные материалы.')
  }

  return response.json()
}

export async function issueOperationMaterial(
  organizationId: string, work: AssignedWork, stock: OperationMaterialStock, quantity: number, unit: string
): Promise<OperationMaterialIssue> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(
    `/api/v1/organizations/${organizationId}/work/${work.maintenanceId}/operations/${work.detailId}/materials`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
      body: JSON.stringify({ stockId: stock.id, quantity, unit, commandId: crypto.randomUUID() })
    }
  )
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось списать материал.')
  }

  return response.json()
}

export async function returnOperationMaterial(
  organizationId: string, work: AssignedWork, issue: OperationMaterialIssue, reason: string
): Promise<OperationMaterialIssue> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(
    `/api/v1/organizations/${organizationId}/work/${work.maintenanceId}/operations/${work.detailId}/materials/${issue.id}/return`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
      body: JSON.stringify({ commandId: crypto.randomUUID(), reason })
    }
  )
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось вернуть материал.')
  }

  return response.json()
}

export async function loadWorkProgress(organizationId: string, maintenanceId: string): Promise<WorkProgress[]> {
  const response = await fetch(`/api/v1/organizations/${organizationId}/work/${maintenanceId}/progress`, {
    headers: { Accept: 'application/json' }
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить состояние работ.')
  }

  return response.json()
}

export async function completeWorkForEmployee(
  organizationId: string, maintenanceId: string, work: WorkProgress, reason: string, commandId: string
): Promise<void> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(`/api/v1/organizations/${organizationId}/work/${maintenanceId}/operations/${work.detailId}/complete-for-employee`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
    body: JSON.stringify({ commandId, version: work.version, action: 'complete-for-employee', reason })
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось завершить работу.')
  }
}

export async function returnWorkForRework(
  organizationId: string, maintenanceId: string, work: WorkProgress, reason: string
): Promise<void> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(`/api/v1/organizations/${organizationId}/work/${maintenanceId}/operations/${work.detailId}/return`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
    body: JSON.stringify({ commandId: crypto.randomUUID(), version: work.version, action: 'return', reason })
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось вернуть работу.')
  }
}

export async function loadAssignedWork(organizationId: string): Promise<AssignedWork[]> {
  const response = await fetch(`/api/v1/organizations/${organizationId}/work/assigned`, {
    headers: { Accept: 'application/json' }
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить работы.')
  }

  return response.json()
}

export async function loadAvailableWork(organizationId: string): Promise<AssignedWork[]> {
  const response = await fetch(`/api/v1/organizations/${organizationId}/work/available`, {
    headers: { Accept: 'application/json' }
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось загрузить свободные работы.')
  }

  return response.json()
}

export async function claimAvailableWork(organizationId: string, work: AssignedWork): Promise<AssignedWork> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(
    `/api/v1/organizations/${organizationId}/work/${work.maintenanceId}/operations/${work.detailId}/claim`,
    { method: 'POST', headers: { RequestVerificationToken: token } }
  )
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось взять работу.')
  }

  return response.json()
}

export async function transitionAssignedWork(
  organizationId: string,
  work: AssignedWork,
  action: 'start' | 'pause' | 'resume' | 'complete',
  reason?: string
): Promise<AssignedWork> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(`/api/v1/organizations/${organizationId}/work/${work.maintenanceId}/operations/${work.detailId}/transition`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
    body: JSON.stringify({ commandId: crypto.randomUUID(), version: work.version, action, reason: reason || null })
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? 'Не удалось изменить состояние работы.')
  }

  return response.json()
}

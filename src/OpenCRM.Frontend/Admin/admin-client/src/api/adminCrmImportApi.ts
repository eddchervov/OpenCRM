import { request } from './httpClient'

export interface CrmImportUser {
  id: string
  email: string | null
  phone: string | null
}

export interface CrmImportOrganization {
  id: string
  name: string
  ownerEmail: string | null
}

export interface CrmImportPreviewRow {
  number: number
  type: string
  name: string
  phone?: string | null
  brand?: string | null
  model?: string | null
  vin?: string | null
  plate?: string | null
  status: 'create' | 'duplicate' | 'error'
  message: string | null
}

export interface CrmImportPreview {
  id: string
  fileName: string
  alreadyImported: boolean
  rows: CrmImportPreviewRow[]
  summary: {
    clients: number
    vehicles: number
    create: number
    duplicate: number
    error: number
  }
}

export interface CrmImportResult {
  createdClients: number
  createdVehicles: number
  skippedDuplicates: number
  errors: number
  reportId: string
}

const baseUrl = '/api/v1/admin/crm-import'
const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

export const getCrmImportUsers = (search: string) =>
  request<CrmImportUser[]>(`${baseUrl}/users?search=${encodeURIComponent(search)}`)

export const getCrmImportOrganizations = (userId: string) =>
  request<CrmImportOrganization[]>(`${baseUrl}/users/${encodeURIComponent(userId)}/organizations`)

export const createCrmImportPreview = (userId: string, organizationId: string, file: File) => {
  const formData = new FormData()
  formData.append('userId', userId)
  formData.append('organizationId', organizationId)
  formData.append('file', file)

  return request<CrmImportPreview>(`${baseUrl}/previews`, {
    method: 'POST',
    body: formData
  })
}

export const confirmCrmImport = (previewId: string) =>
  request<CrmImportResult>(`${baseUrl}/previews/${encodeURIComponent(previewId)}/confirm`, { method: 'POST' })

async function downloadReport(path: string): Promise<Blob> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    credentials: 'include',
    headers: { Accept: 'text/csv, application/octet-stream' }
  })

  if (!response.ok) {
    const payload = await response.json().catch(() => null) as { message?: string } | null
    throw new Error(payload?.message ?? `Не удалось скачать отчёт (${response.status}).`)
  }

  return await response.blob()
}

export const getCrmImportPreviewReport = (previewId: string) =>
  downloadReport(`${baseUrl}/previews/${encodeURIComponent(previewId)}/report`)

export const getCrmImportResultReport = (previewId: string) =>
  downloadReport(`${baseUrl}/previews/${encodeURIComponent(previewId)}/result-report`)

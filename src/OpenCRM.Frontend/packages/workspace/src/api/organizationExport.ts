export interface OrganizationExportStatus {
  id: string | null
  state: 'idle' | 'queued' | 'running' | 'ready' | 'failed' | 'expired'
  requestedAtUtc?: string | null
  completedAtUtc?: string | null
  expiresAtUtc?: string | null
  retryAtUtc?: string | null
  sizeBytes?: number | null
  errorMessage?: string | null
}

async function readStatus(response: Response): Promise<OrganizationExportStatus> {
  const result = await response.json().catch(() => null)
  if (!response.ok || !result || typeof result.state !== 'string') {
    throw new Error(typeof result?.message === 'string' && result.message.trim()
      ? result.message
      : 'Не удалось получить выгрузку. Попробуйте позже.')
  }

  return result
}

export async function requestOrganizationExport(organizationId: string): Promise<OrganizationExportStatus> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content
    ?? document.querySelector<HTMLInputElement>('input[name="__RequestVerificationToken"]')?.value
  return readStatus(await fetch(`/api/v1/organizations/${encodeURIComponent(organizationId)}/data-export`, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest', ...(token ? { RequestVerificationToken: token } : {}) }
  }))
}

export async function loadOrganizationExportStatus(organizationId: string): Promise<OrganizationExportStatus> {
  return readStatus(await fetch(`/api/v1/organizations/${encodeURIComponent(organizationId)}/data-export/status`, {
    credentials: 'same-origin', headers: { Accept: 'application/json' }
  }))
}

export function organizationExportDownloadUrl(organizationId: string, jobId: string): string {
  return `/api/v1/organizations/${encodeURIComponent(organizationId)}/data-export/${encodeURIComponent(jobId)}/file`
}

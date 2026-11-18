import { translate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/workspaceApiMessages'
export type WorkspaceClientLookupItem = {
  id: string
  name: string
  phone: string | null
  partyTypeCode: string | null
}

export type WorkspaceClientLookupPage = {
  items: WorkspaceClientLookupItem[]
  totalCount: number
}

export async function loadClientLookup(
  organizationId: string,
  searchQuery: string,
  page: number,
  signal?: AbortSignal
): Promise<WorkspaceClientLookupPage> {
  const url = new URL(`/api/v1/organizations/${organizationId}/clients/lookup`, window.location.origin)
  url.searchParams.set('searchQuery', searchQuery)
  url.searchParams.set('page', String(page))

  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    signal,
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? translate('workspaceApi.clientLookup'))
  }

  return response.json()
}

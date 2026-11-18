import { translate as t } from '@opencrm/ui/i18n'
import '@opencrm/workspace/timeZoneMessages'
import { withPathBase } from '../utils/pathBase.mjs'
import { getMutationHeaders } from './maintenance'

export type OrganizationTimeZoneSettings = { timeZoneId: string | null; canManage: boolean; canChange?: boolean }

async function request(organizationId: string, body?: { timeZoneId: string; expectedTimeZoneId: string | null }, signal?: AbortSignal): Promise<OrganizationTimeZoneSettings> {
  let response: Response
  try {
    response = await fetch(withPathBase(`/api/v1/organizations/${encodeURIComponent(organizationId)}/time-zone`), {
      method: body ? 'PUT' : 'GET', credentials: 'same-origin', signal,
      headers: getMutationHeaders({ acceptJson: true, contentTypeJson: Boolean(body) }),
      body: body ? JSON.stringify(body) : undefined
    })
  } catch (caught) {
    if (caught instanceof DOMException && caught.name === 'AbortError') throw caught
    throw new Error(t('timeZone.networkError'))
  }
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    if (response.status === 401) throw new Error(t('timeZone.signIn'))
    if (response.status === 403) throw new Error(t('timeZone.forbidden'))
    if (response.status === 409) throw new Error(t('timeZone.conflict'))
    throw new Error(error?.message ?? t('timeZone.requestError'))
  }

  return response.json()
}

export const loadOrganizationTimeZone = (organizationId: string, signal?: AbortSignal) => request(organizationId, undefined, signal)
export const saveOrganizationTimeZone = (organizationId: string, timeZoneId: string, expectedTimeZoneId: string | null) => request(organizationId, { timeZoneId, expectedTimeZoneId })

import { getMutationHeaders } from './maintenance'
import { translate } from '@opencrm/ui/i18n'
import { withPathBase } from '../utils/pathBase.mjs'
import '../currencyMessages'

export type CurrencySettings = {
  currencyCode: string
  canChange: boolean
  canManage: boolean
  supportedCurrencies: string[]
}

async function requestCurrency(
  organizationId: string | undefined,
  body?: { currencyCode: string; expectedCurrencyCode: string },
  signal?: AbortSignal
): Promise<CurrencySettings> {
  const path = organizationId
    ? `/api/v1/organizations/${encodeURIComponent(organizationId)}/currency`
    : '/api/v1/users/me/currency'
  let response: Response
  try {
    response = await fetch(withPathBase(path), {
      method: body ? 'PUT' : 'GET', credentials: 'same-origin', signal,
      headers: getMutationHeaders({ acceptJson: true, contentTypeJson: Boolean(body) }),
      body: body ? JSON.stringify(body) : undefined
    })
  } catch (error) {
    if (signal?.aborted) throw error
    throw new Error(translate('currency.networkError'))
  }

  if (response.status === 401) throw new Error(translate('currency.signedOut'))
  if (response.status === 403) throw new Error(translate('currency.forbidden'))
  if (response.status === 409) throw new Error(translate('currency.conflict'))
  if (!response.ok) throw new Error(translate(body ? 'currency.saveError' : 'currency.loadError'))

  return response.json()
}

export const loadCurrencySettings = (organizationId?: string, signal?: AbortSignal) => requestCurrency(organizationId, undefined, signal)
export const saveCurrencySettings = (organizationId: string | undefined, currencyCode: string, expectedCurrencyCode: string) =>
  requestCurrency(organizationId, { currencyCode, expectedCurrencyCode })

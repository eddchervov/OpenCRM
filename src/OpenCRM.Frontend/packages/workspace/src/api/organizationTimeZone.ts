import { getMutationHeaders } from './maintenance'

export type OrganizationTimeZoneSettings = { timeZoneId: string | null; canManage: boolean }

async function request(organizationId: string, body?: { timeZoneId: string; expectedTimeZoneId: string | null }, signal?: AbortSignal): Promise<OrganizationTimeZoneSettings> {
  let response: Response
  try {
    response = await fetch(`/api/v1/organizations/${encodeURIComponent(organizationId)}/time-zone`, {
      method: body ? 'PUT' : 'GET', credentials: 'same-origin', signal,
      headers: getMutationHeaders({ acceptJson: true, contentTypeJson: Boolean(body) }),
      body: body ? JSON.stringify(body) : undefined
    })
  } catch (caught) {
    if (caught instanceof DOMException && caught.name === 'AbortError') throw caught
    throw new Error('Нет связи с CRM. Проверьте соединение и повторите попытку.')
  }
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    if (response.status === 401) throw new Error('Войдите снова, чтобы открыть настройки часового пояса.')
    if (response.status === 403) throw new Error('Недостаточно прав для настройки часового пояса организации.')
    if (response.status === 409) throw new Error('Часовой пояс уже изменил другой сотрудник. Обновите данные и повторите изменение.')
    throw new Error(error?.message ?? 'Не удалось загрузить или сохранить часовой пояс организации.')
  }

  return response.json()
}

export const loadOrganizationTimeZone = (organizationId: string, signal?: AbortSignal) => request(organizationId, undefined, signal)
export const saveOrganizationTimeZone = (organizationId: string, timeZoneId: string, expectedTimeZoneId: string | null) => request(organizationId, { timeZoneId, expectedTimeZoneId })

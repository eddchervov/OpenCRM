import { request } from './httpClient'

export function logoutAdminSession(): Promise<void> {
  return request<void>('/api/v1/admin/session/logout', {
    method: 'POST'
  })
}

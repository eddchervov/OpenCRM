import type { AdminCurrentUser } from '@/models/AdminCurrentUser'
import { request } from './httpClient'

export function getAdminCurrentUser(): Promise<AdminCurrentUser> {
  return request<AdminCurrentUser>('/api/v1/admin/current-user')
}

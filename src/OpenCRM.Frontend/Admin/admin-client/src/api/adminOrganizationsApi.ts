import type { AdminOrganizationsPage } from '@/models/AdminOrganization'
import { request } from './httpClient'

export function getAdminOrganizations(search: string, user: string, page: number): Promise<AdminOrganizationsPage> {
  const query = new URLSearchParams({ search, user, page: String(page), pageSize: '25' })
  return request<AdminOrganizationsPage>(`/api/v1/admin/organizations?${query}`)
}

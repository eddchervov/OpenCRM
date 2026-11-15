import type { AdminUserOverview } from '@/models/AdminUserOverview'
import type { AdminUserOrganizationContents } from '@/models/AdminUserOrganizationContents'
import type { AdminUsersResponse } from '@/models/AdminUsersResponse'
import { request } from './httpClient'

export function getAdminUsers(
  search: string,
  sort: number,
  includeDeleted: boolean,
  page: number,
  pageSize: number,
  utmSource: string | null = null
): Promise<AdminUsersResponse> {
  const query = new URLSearchParams({
    sort: sort.toString(),
    includeDeleted: includeDeleted.toString(),
    page: page.toString(),
    pageSize: pageSize.toString()
  })
  if (search) {
    query.set('search', search)
  }
  if (utmSource) {
    query.set('utmSource', utmSource)
  }

  return request<AdminUsersResponse>(`/api/v1/admin/users?${query.toString()}`)
}

export function getAdminUserOverview(userId: string): Promise<AdminUserOverview> {
  return request<AdminUserOverview>(`/api/v1/admin/users/${encodeURIComponent(userId)}/overview`)
}

export function getAdminUserOrganizationContents(userId: string, organizationId: string): Promise<AdminUserOrganizationContents> {
  return request<AdminUserOrganizationContents>(
    `/api/v1/admin/users/${encodeURIComponent(userId)}/organizations/${encodeURIComponent(organizationId)}/contents`
  )
}

export function impersonateAdminUser(userId: string): Promise<{ success: boolean; message: string }> {
  return request<{ success: boolean; message: string }>('/Auth/ImpersonateUser', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userId })
  })
}

export function updateAdminUserTestSubscriptions(userId: string, canUseTestSubscriptions: boolean): Promise<{ message: string }> {
  return request<{ message: string }>(`/api/v1/admin/users/${encodeURIComponent(userId)}/test-subscriptions`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ canUseTestSubscriptions })
  })
}

export function grantAdminUserDemo(userId: string): Promise<{ message: string }> {
  return request<{ message: string }>(`/api/v1/admin/users/${encodeURIComponent(userId)}/grant-demo`, {
    method: 'POST'
  })
}

export function returnAdminUserToDefaultPlan(userId: string): Promise<{ message: string }> {
  return request<{ message: string }>(`/api/v1/admin/users/${encodeURIComponent(userId)}/active-subscription`, {
    method: 'DELETE'
  })
}

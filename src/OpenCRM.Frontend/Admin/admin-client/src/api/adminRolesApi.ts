import type { AdminRoleMembers } from '@/models/AdminRoleMembers'
import { request } from './httpClient'

const basePath = '/api/v1/admin/roles'

export function getAdminRoleMembers(roleId: string): Promise<AdminRoleMembers> {
  return request<AdminRoleMembers>(`${basePath}/${encodeURIComponent(roleId)}/members`)
}

export function addAdminRoleMember(roleId: string, userId: string): Promise<{ message: string }> {
  return request<{ message: string }>(`${basePath}/${encodeURIComponent(roleId)}/members`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userId })
  })
}

export function removeAdminRoleMember(roleId: string, userId: string): Promise<{ message: string }> {
  return request<{ message: string }>(
    `${basePath}/${encodeURIComponent(roleId)}/members/${encodeURIComponent(userId)}`,
    { method: 'DELETE' }
  )
}

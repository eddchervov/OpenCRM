import type { AdminPermissions } from '@/models/AdminPermissions'
import { request } from './httpClient'

export function getAdminPermissions(): Promise<AdminPermissions> {
  return request<AdminPermissions>('/api/v1/admin/permissions')
}

export function updateAdminRolePermissions(
  roleId: string,
  permissionCodes: string[]
): Promise<{ message: string }> {
  return request<{ message: string }>(
    `/api/v1/admin/permissions/roles/${encodeURIComponent(roleId)}`,
    {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ permissionCodes })
    }
  )
}

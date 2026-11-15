import type { AdminPermission } from './AdminPermission'
import type { AdminRolePermissions } from './AdminRolePermissions'

export interface AdminPermissions {
  permissions: AdminPermission[]
  roles: AdminRolePermissions[]
}

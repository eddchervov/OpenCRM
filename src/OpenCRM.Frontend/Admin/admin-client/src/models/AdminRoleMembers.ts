import type { AdminRoleMember } from './AdminRoleMember'

export interface AdminRoleMembers {
  roleId: string
  roleName: string
  isProtected: boolean
  users: AdminRoleMember[]
}

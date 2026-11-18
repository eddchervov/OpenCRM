export interface AdminUserInvitation {
  id: string
  organizationName: string
  email: string
  roleName: string
  roleCode?: string
  statusCode: string
  createdAt: string
  acceptedAt: string | null
  revokedAt: string | null
  invitedBy: string
}

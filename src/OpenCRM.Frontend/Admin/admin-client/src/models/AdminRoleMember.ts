export interface AdminRoleMember {
  userId: string
  displayName: string
  email: string | null
  phone: string | null
  hasRole: boolean
  isCurrentUser: boolean
}

export interface AdminCurrentUser {
  id: string
  email: string
  roles: string[]
  permissions: string[]
  isSystem: boolean
  commercialEnabled: boolean
}

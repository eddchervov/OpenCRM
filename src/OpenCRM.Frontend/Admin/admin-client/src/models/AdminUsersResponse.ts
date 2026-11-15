import type { AdminUserListItem } from './AdminUserListItem'

export interface AdminUsersResponse {
  users: AdminUserListItem[]
  allCount: number
  page: number
  pageSize: number
}

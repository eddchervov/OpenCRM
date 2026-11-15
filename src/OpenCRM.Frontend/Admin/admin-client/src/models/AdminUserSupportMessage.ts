export interface AdminUserSupportMessage {
  id: number
  typeName: string
  statusCode: string
  statusName: string
  topic: string | null
  text: string
  sourcePage: string | null
  adminComment: string | null
  createdAt: string
  updatedAt: string | null
  closedAt: string | null
}

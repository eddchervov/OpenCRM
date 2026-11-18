export interface AdminSupportMessage {
  id: number
  typeName: string
  typeCode?: string
  statusCode: string
  statusName: string
  userId: string | null
  userEmail: string | null
  name: string | null
  email: string | null
  phone: string | null
  topic: string
  text: string
  adminComment: string | null
  createdAt: string
  organizationId: string | null
  organizationName: string | null
  entries: AdminSupportMessageEntry[]
}

export interface AdminSupportMessageEntry {
  id: number
  isStaff: boolean
  kind: string
  text: string
  createdAt: string
  deliveryStatus: string | null
  deliveryError: string | null
  attachments: AdminSupportMessageAttachment[]
}

export interface AdminSupportMessageAttachment {
  id: string
  fileName: string
  sizeBytes: number
}

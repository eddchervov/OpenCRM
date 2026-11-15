export interface AdminSmsHistory {
  id: number
  text: string
  dateTime: string
  response: string | null
  smsCode: string
  phone: string
  type: number
}

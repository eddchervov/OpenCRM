import { request } from './httpClient'
import type { AdminSiteNotification, SendSiteNotification, SiteNotificationUser } from '@/models/AdminSiteNotification'

export const getSiteNotifications = () => request<AdminSiteNotification[]>('/api/v1/admin/site-notifications')

export const getSiteNotificationUsers = (search: string) => request<SiteNotificationUser[]>(
  `/api/v1/admin/site-notifications/users?search=${encodeURIComponent(search)}`
)

export const updateSiteNotificationTemplate = (id: string, template: { title: string; body: string }) =>
  request<AdminSiteNotification>(`/api/v1/admin/site-notifications/templates/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(template)
  })

export const sendSiteNotification = (notification: SendSiteNotification) => request<void>(
  '/api/v1/admin/site-notifications/send',
  { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(notification) }
)

export const deleteSiteNotification = (id: string) => request<void>(`/api/v1/admin/site-notifications/${id}`, {
  method: 'DELETE'
})
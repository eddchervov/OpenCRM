import { request } from './httpClient'
import type { AdminCatalogSuggestion } from '@/models/AdminCatalogSuggestion'
import type { AdminCatalogBrand } from '@/models/AdminCatalogBrand'
import type { AdminCatalogModel } from '@/models/AdminCatalogModel'
import type { AdminCatalogSeedResult } from '@/models/AdminCatalogSeedResult'
import type { AdminEmailCampaign } from '@/models/AdminEmailCampaign'
import type { AdminEmailComposeData } from '@/models/AdminEmailComposeData'
import type { AdminEmailMessage } from '@/models/AdminEmailMessage'
import type { AdminEmailMessageDetails } from '@/models/AdminEmailMessageDetails'
import type { AdminFeatureDefinition } from '@/models/AdminFeatureDefinition'
import type { AdminSmsHistory } from '@/models/AdminSmsHistory'
import type { AdminSupportMessage } from '@/models/AdminSupportMessage'
import type { AdminTestDataOrganization } from '@/models/AdminTestDataOrganization'
import type { AdminTestDataVehicle } from '@/models/AdminTestDataVehicle'

const jsonHeaders = { 'Content-Type': 'application/json' }

export const getFeatures = () => request<AdminFeatureDefinition[]>('/api/v1/admin/features')
export const saveFeature = (feature: Omit<AdminFeatureDefinition, 'id' | 'isSystem'>, id?: number) =>
  request<AdminFeatureDefinition>(id ? `/api/v1/admin/features/${id}` : '/api/v1/admin/features', {
    method: id ? 'PUT' : 'POST', headers: jsonHeaders, body: JSON.stringify(feature)
  })
export const toggleFeature = (id: number) => request<void>(`/api/v1/admin/features/${id}/toggle-status`, { method: 'PATCH' })

export const getSupportMessages = (filters: { status?: string; search?: string; source?: string; page?: number } = {}) => {
  const query = new URLSearchParams({
    status: filters.status ?? '',
    search: filters.search ?? '',
    source: filters.source ?? '',
    page: String(filters.page ?? 1)
  })

  return request<{ items: AdminSupportMessage[]; totalCount: number; pageSize: number }>(
    `/api/v1/admin/support-messages?${query.toString()}`)
}
export const getSupportMessage = (id: number) =>
  request<AdminSupportMessage>(`/api/v1/admin/support-messages/${id}`)
export const replyToSupportMessage = (id: number, text: string) =>
  request<void>(`/api/v1/admin/support-messages/${id}/replies`, {
    method: 'POST', headers: jsonHeaders, body: JSON.stringify({ text })
  })
export const updateSupportMessage = (id: number, statusCode: string, adminComment: string) =>
  request<void>(`/api/v1/admin/support-messages/${id}`, {
    method: 'PUT', headers: jsonHeaders, body: JSON.stringify({ statusCode, adminComment })
  })

export const getCatalogSuggestions = (status = '') => request<AdminCatalogSuggestion[]>(
  `/api/v1/admin/catalog-suggestions${status ? `?status=${status}` : ''}`)
export const reviewCatalogSuggestion = (id: number, action: 'approve' | 'reject', comment: string) =>
  request<void>(`/api/v1/admin/catalog-suggestions/${id}/${action}`, {
    method: 'POST', headers: jsonHeaders, body: JSON.stringify({ comment })
  })

export const getCatalogBrands = (kind = '', search = '') => request<AdminCatalogBrand[]>(
  `/api/v1/admin/catalog/brands?kind=${kind}&search=${encodeURIComponent(search)}`)
export const getCatalogModels = (brandId: number) => request<AdminCatalogModel[]>(`/api/v1/admin/catalog/brands/${brandId}/models`)
export const saveCatalogBrand = (name: string, kind: number, id?: number) => request<void>(
  id ? `/api/v1/admin/catalog/brands/${id}` : '/api/v1/admin/catalog/brands',
  { method: id ? 'PUT' : 'POST', headers: jsonHeaders, body: JSON.stringify({ name, kind }) })
export const saveCatalogModel = (brandId: number, name: string, id?: number) => request<void>(
  id ? `/api/v1/admin/catalog/models/${id}` : `/api/v1/admin/catalog/brands/${brandId}/models`,
  { method: id ? 'PUT' : 'POST', headers: jsonHeaders, body: JSON.stringify({ name }) })
export const deleteCatalogBrand = (id: number) => request<void>(`/api/v1/admin/catalog/brands/${id}`, { method: 'DELETE' })
export const deleteCatalogModel = (id: number) => request<void>(`/api/v1/admin/catalog/models/${id}`, { method: 'DELETE' })
export const seedVehicleCatalog = () => request<AdminCatalogSeedResult>('/api/v1/admin/catalog/seed', { method: 'POST' })

export const getEmailMessages = (search = '', status = '') => request<{ items: AdminEmailMessage[]; totalCount: number }>(
  `/api/v1/admin/email-messages?search=${encodeURIComponent(search)}&status=${encodeURIComponent(status)}`)
export const getEmailMessage = (id: number) => request<AdminEmailMessageDetails>(`/api/v1/admin/email-messages/${id}`)
export const getEmailComposeData = (search = '') => request<AdminEmailComposeData>(
  `/api/v1/admin/email-messages/compose-data?userSearch=${encodeURIComponent(search)}`)
export const previewEmailRecipients = (payload: object) => request<{ totalCount: number; items: Array<{ email: string; displayName: string }> }>('/api/v1/admin/email-messages/preview-recipients', {
  method: 'POST', headers: jsonHeaders, body: JSON.stringify(payload)
})
export const scheduleEmailCampaign = (payload: object) => request<{ scheduledCount: number }>('/api/v1/admin/email-messages/schedule', {
  method: 'POST', headers: jsonHeaders, body: JSON.stringify(payload)
})
export const getEmailCampaigns = () => request<{ items: AdminEmailCampaign[]; totalCount: number }>('/api/v1/admin/email-messages/campaigns')
export const changeEmailCampaignStatus = (id: string, action: 'pause' | 'resume' | 'cancel') =>
  request<void>(`/api/v1/admin/email-messages/campaigns/${id}/${action}`, { method: 'POST' })

export const getSmsHistory = () => request<{ histories: AdminSmsHistory[]; totalCount: number }>('/api/v1/admin/sms-sending-histories')

export const getTestOrganizations = (search = '') => request<AdminTestDataOrganization[]>(`/api/v1/admin/test-data/organizations?search=${encodeURIComponent(search)}`)
export const getTestVehicles = (organizationId: string) => request<AdminTestDataVehicle[]>(`/api/v1/admin/test-data/organizations/${organizationId}/vehicles`)
export const generateTestData = (path: string, count: number, mode?: string) => request<{ message: string; createdCount: number }>(path, {
  method: 'POST', headers: jsonHeaders, body: JSON.stringify({ count, mode })
})

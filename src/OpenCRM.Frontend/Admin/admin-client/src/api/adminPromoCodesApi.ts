import type { AdminPromoCode } from '@/models/AdminPromoCode'
import type { AdminPromoCodeForm } from '@/models/AdminPromoCodeForm'
import type { AdminPromoCodeUsage } from '@/models/AdminPromoCodeUsage'
import { request } from './httpClient'

const basePath = '/api/v1/admin/promo-codes'

export function getAdminPromoCodes(): Promise<AdminPromoCode[]> {
  return request<AdminPromoCode[]>(basePath)
}

export function createAdminPromoCode(form: AdminPromoCodeForm): Promise<AdminPromoCode> {
  return request<AdminPromoCode>(basePath, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(form)
  })
}

export function updateAdminPromoCode(promoCodeId: number, form: AdminPromoCodeForm): Promise<AdminPromoCode> {
  return request<AdminPromoCode>(`${basePath}/${promoCodeId}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(form)
  })
}

export function setAdminPromoCodeStatus(promoCodeId: number, isActive: boolean): Promise<void> {
  return request<void>(`${basePath}/${promoCodeId}/status`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ isActive })
  })
}

export function deleteAdminPromoCode(promoCodeId: number): Promise<void> {
  return request<void>(`${basePath}/${promoCodeId}`, { method: 'DELETE' })
}

export function getAdminPromoCodeUsages(promoCodeId: number): Promise<AdminPromoCodeUsage[]> {
  return request<AdminPromoCodeUsage[]>(`${basePath}/${promoCodeId}/usages`)
}

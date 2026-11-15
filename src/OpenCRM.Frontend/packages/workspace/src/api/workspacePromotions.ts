import { getJson, sendJson } from './workspaceAdmin'

export type WorkspacePromoApplication = {
  id: number
  promoCodeId: number
  code: string
  name: string
  benefitTypeCode: 'bonus_days' | 'percentage' | 'fixed_amount' | string
  bonusDays?: number | null
  discountValue?: number | null
  discountValidUntil?: string | null
  status: string
  appliedAt: string
  consumedAt?: string | null
  organizationId: string
  accessUntil?: string | null
}

export function loadWorkspacePromoApplications(organizationId: string): Promise<WorkspacePromoApplication[]> {
  return getJson(`/api/v1/organizations/${organizationId}/workspace/billing/promocodes`, 'Не удалось загрузить историю промокодов.')
}

export type WorkspacePromoHistoryPage = {
  items: WorkspacePromoApplication[]
  totalCount: number
  page: number
  pageSize: number
}

export function loadWorkspacePromoHistory(organizationId: string, page: number): Promise<WorkspacePromoHistoryPage> {
  return getJson(
    `/api/v1/organizations/${organizationId}/workspace/billing/promocodes/history?page=${page}&pageSize=20`,
    'Не удалось загрузить историю промокодов.'
  )
}

export function applyWorkspacePromoCode(organizationId: string, code: string): Promise<WorkspacePromoApplication> {
  return sendJson(`/api/v1/organizations/${organizationId}/workspace/billing/promocodes`, 'POST', { code }, 'Не удалось применить промокод.')
}

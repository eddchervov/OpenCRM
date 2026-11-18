import { translate as t } from '@opencrm/ui/i18n'
import '@opencrm/workspace/apiMessages'
export type WorkspaceFinanceKpis = {
  ordersCount: number
  grossAmount: number
  netAmount: number
  paidAmount: number
  debtAmount: number
  discountAmount: number
  materialCostAmount: number
  laborCostAmount: number
  costAmount: number
  profitAmount: number
  averageCheckAmount: number
}

export type WorkspaceFinanceMoneySeriesPoint = {
  date: string
  label: string
  value: number
}

export type WorkspaceFinanceBreakdownItem = {
  code: string
  label: string
  value: number
  percent: number
}

export type WorkspaceFinanceServiceItem = {
  name: string
  count: number
  amount: number
}

export type WorkspaceFinanceClientDebtItem = {
  clientId?: string | null
  clientName: string
  ordersCount: number
  debtAmount: number
}

export type WorkspaceFinanceOrderItem = {
  currencyCode?: string
  id: string
  isCancelled?: boolean
  date: string
  vehicleId: string
  vehicleDisplayName: string
  vehicleSubtitle?: string | null
  clientId?: string | null
  clientName?: string | null
  grossAmount: number
  netAmount: number
  paidAmount: number
  debtAmount: number
  discountAmount: number
  materialCostAmount: number
  laborCostAmount: number
  profitAmount: number
  paymentStatus: string
  paymentStatusName: string
  detailUrl: string
}

export type WorkspaceFinanceSummary = {
  currencyCode?: string
  otherCurrencyTotals?: {
    currencyCode: string
    grossAmount: number
    netAmount: number
    paidAmount: number
    debtAmount: number
    costAmount: number
    profitAmount: number
  }[]
  dateFrom: string
  dateTo: string
  kpis: WorkspaceFinanceKpis
  revenueSeries: WorkspaceFinanceMoneySeriesPoint[]
  accruedSeries: WorkspaceFinanceMoneySeriesPoint[]
  costBreakdown: WorkspaceFinanceBreakdownItem[]
  paymentBreakdown: WorkspaceFinanceBreakdownItem[]
  topServices: WorkspaceFinanceServiceItem[]
  topDebtors: WorkspaceFinanceClientDebtItem[]
  orders: WorkspaceFinanceOrderItem[]
}

export async function loadWorkspaceFinance(
  organizationId: string,
  dateFrom: string,
  dateTo: string
): Promise<WorkspaceFinanceSummary> {
  const url = new URL(`/api/v1/organizations/${organizationId}/finance`, window.location.origin)
  url.searchParams.set('dateFrom', dateFrom)
  url.searchParams.set('dateTo', dateTo)

  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (response.status === 401) {
    window.location.href = `/auth/login?returnUrl=${encodeURIComponent(window.location.pathname + window.location.search + window.location.hash)}`
    throw new Error(t('apiErrors.signIn'))
  }

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? t('apiErrors.finance'))
  }

  return response.json()
}

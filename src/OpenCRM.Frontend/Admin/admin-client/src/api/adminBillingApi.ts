import type { AdminBilling } from '@/models/AdminBilling'
import type { AdminBillingPayment } from '@/models/AdminBillingPayment'
import type { AdminOperationMessage } from '@/models/AdminOperationMessage'
import type { AdminPaymentStatusAudit } from '@/models/AdminPaymentStatusAudit'
import type { AdminSubscriptionAudit } from '@/models/AdminSubscriptionAudit'
import type { AdminAvailablePlan } from '@/models/AdminAvailablePlan'
import type { AdminBillingPurchase } from '@/models/AdminBillingPurchase'
import type { AdminPaymentServiceActions, AdminPaymentServiceActionRequest, AdminPaymentServiceAction } from '@/models/AdminPaymentServiceActions'
import { request } from './httpClient'

export function getAdminBilling(): Promise<AdminBilling> {
  return request<AdminBilling>('/api/v1/admin/billing')
}

export function getAdminBillingPayment(paymentId: number): Promise<AdminBillingPayment> {
  return request<AdminBillingPayment>(`/api/v1/admin/billing/payments/${paymentId}`)
}

export function getAdminBillingAttention(): Promise<{ overdueReceiptCount: number; failedActivationCount: number }> {
  return request('/api/v1/admin/billing/attention')
}

export function getAdminBillingPurchases(): Promise<AdminBillingPurchase[]> {
  return request<AdminBillingPurchase[]>('/api/v1/admin/billing/purchases')
}

export function markAdminNpdReceipt(
  paymentId: number,
  issued: boolean,
  expectedIssued: boolean,
  reason: string
): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(`/api/v1/admin/billing/payments/${paymentId}/npd-receipt`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ issued, expectedIssued, reason })
  })
}

export function sendAdminNpdReceipt(paymentId: number): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(`/api/v1/admin/billing/payments/${paymentId}/npd-receipt/send`, {
    method: 'POST'
  })
}

export function saveAdminNpdReceiptDetails(
  paymentId: number,
  receiptUrl: string,
  comment: string
): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(`/api/v1/admin/billing/payments/${paymentId}/npd-receipt/details`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ receiptUrl, comment })
  })
}

export function uploadAdminNpdReceiptFile(paymentId: number, file: File): Promise<AdminOperationMessage> {
  const formData = new FormData()
  formData.append('file', file)

  return request<AdminOperationMessage>(`/api/v1/admin/billing/payments/${paymentId}/npd-receipt/file`, {
    method: 'POST',
    body: formData
  })
}

export function deleteAdminNpdReceiptFile(paymentId: number): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(`/api/v1/admin/billing/payments/${paymentId}/npd-receipt/file`, {
    method: 'DELETE'
  })
}

export function getAdminAvailablePlans(): Promise<AdminAvailablePlan[]> {
  return request<AdminAvailablePlan[]>('/api/v1/admin/billing/available-plans')
}

export function grantAdminPlan(
  scope: 'users' | 'organizations',
  id: string,
  planId: number,
  days: number,
  reason: string
): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(`/api/v1/admin/billing/${scope}/${encodeURIComponent(id)}/plan`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ planId, days, reason })
  })
}

export function getAdminPaymentStatusAudits(paymentId: number): Promise<AdminPaymentStatusAudit[]> {
  return request<AdminPaymentStatusAudit[]>('/api/v1/admin/billing/payments/' + paymentId + '/status-audits')
}

export function syncAdminPayment(paymentId: number): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(`/api/v1/admin/billing/payments/${paymentId}/sync-yookassa`, {
    method: 'POST'
  })
}

export function markAdminPaymentStatus(
  paymentId: number,
  status: string,
  expectedCurrentStatus: string | null,
  reason: string
): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(`/api/v1/admin/billing/payments/${paymentId}/mark-status`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ status, expectedCurrentStatus, reason })
  })
}

export function getAdminSubscriptionAudits(scope: string, subscriptionId: number): Promise<AdminSubscriptionAudit[]> {
  return request<AdminSubscriptionAudit[]>(`/api/v1/admin/billing/subscriptions/${scope}/${subscriptionId}/audits`)
}

export function stopAdminSubscription(scope: string, subscriptionId: number, reason: string, expectedEndDate: string): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(
    `/api/v1/admin/billing/subscriptions/${scope}/${subscriptionId}/stop`,
    { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ reason, expectedEndDate }) }
  )
}

export function grantAdminOrganizationDemo(userId: string, organizationId: string, reason: string): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(
    `/api/v1/admin/billing/users/${encodeURIComponent(userId)}/organizations/${encodeURIComponent(organizationId)}/demo`,
    { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ reason }) }
  )
}

export function revokeAdminOrganizationDemo(userId: string, organizationId: string, reason: string): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(
    `/api/v1/admin/billing/users/${encodeURIComponent(userId)}/organizations/${encodeURIComponent(organizationId)}/demo`,
    { method: 'DELETE', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ reason }) }
  )
}

export function grantAdminUserDemo(userId: string, reason: string): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(`/api/v1/admin/billing/users/${encodeURIComponent(userId)}/demo`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ reason })
  })
}

export function revokeAdminUserDemo(userId: string, reason: string): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(`/api/v1/admin/billing/users/${encodeURIComponent(userId)}/demo`, {
    method: 'DELETE', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ reason })
  })
}

export function extendAdminSubscription(
  scope: string,
  subscriptionId: number,
  days: number,
  reason: string,
  expectedEndDate: string
): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(
    `/api/v1/admin/billing/subscriptions/${scope}/${subscriptionId}/extend`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ days, reason, expectedEndDate })
    }
  )
}

export function getAdminPaymentServiceActions(paymentId: number): Promise<AdminPaymentServiceActions> {
  return request<AdminPaymentServiceActions>('/api/v1/admin/billing/payments/' + paymentId + '/service-actions', {
    signal: AbortSignal.timeout(20000)
  })
}

export function submitAdminPaymentServiceAction(paymentId: number, action: AdminPaymentServiceActionRequest): Promise<AdminPaymentServiceAction> {
  return request<AdminPaymentServiceAction>('/api/v1/admin/billing/payments/' + paymentId + '/service-actions', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(action),
    signal: AbortSignal.timeout(20000)
  })
}

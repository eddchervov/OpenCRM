import type { AdminOperationMessage } from '@/models/AdminOperationMessage'
import type { AdminPlanFeature } from '@/models/AdminPlanFeature'
import type { AdminSubscriptionPlan } from '@/models/AdminSubscriptionPlan'
import type { AdminSubscriptionPlanForm } from '@/models/AdminSubscriptionPlanForm'
import { request } from './httpClient'

const basePath = '/api/v1/admin/subscription-plans'

export function getAdminSubscriptionPlans(): Promise<AdminSubscriptionPlan[]> {
  return request<AdminSubscriptionPlan[]>(basePath)
}

export function createAdminSubscriptionPlan(form: AdminSubscriptionPlanForm): Promise<AdminSubscriptionPlan> {
  return request<AdminSubscriptionPlan>(basePath, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(form)
  })
}

export function updateAdminSubscriptionPlan(
  planId: number,
  form: AdminSubscriptionPlanForm
): Promise<AdminSubscriptionPlan> {
  return request<AdminSubscriptionPlan>(`${basePath}/${planId}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(form)
  })
}

export function toggleAdminSubscriptionPlan(planId: number): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(`${basePath}/${planId}/toggle-status`, { method: 'PATCH' })
}

export function deleteAdminSubscriptionPlan(planId: number): Promise<void> {
  return request<void>(`${basePath}/${planId}`, { method: 'DELETE' })
}

export function getAdminPlanFeatures(planId: number): Promise<AdminPlanFeature[]> {
  return request<AdminPlanFeature[]>(`${basePath}/${planId}/features`)
}

export function saveAdminPlanFeatures(planId: number, features: AdminPlanFeature[]): Promise<AdminOperationMessage> {
  return request<AdminOperationMessage>(`${basePath}/${planId}/features`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      features: features.map(feature => ({
        featureId: feature.featureId,
        isAssigned: feature.isAssigned,
        boolValue: feature.boolValue,
        intValue: feature.intValue
      }))
    })
  })
}

import { request } from './httpClient'
import type { AdminDashboard } from '@/models/AdminDashboard'

export function getAdminDashboard(periodDays: 1 | 7 | 30, signal?: AbortSignal): Promise<AdminDashboard> {
  return request<AdminDashboard>('/api/v1/admin/dashboard?periodDays=' + periodDays, { signal })
}


import { translate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/setupMessages'

import { type WorkspaceScenarioCatalogItem } from './scenarioCatalog'

export type WorkspaceWorkingDayBreak = { startTime: string, endTime: string }
export type WorkspaceWorkingDay = {
  dayOfWeek: number
  isWorking: boolean
  startTime: string | null
  endTime: string | null
  breaks: WorkspaceWorkingDayBreak[]
}

export type WorkspaceSetup = {
  shouldAutoOpen: boolean
  step: number
  revision: number
  completedAt: string | null
  canManage: boolean
  workdayStart: string | null
  workdayEnd: string | null
  workingDays: number[]
  weeklySchedule: WorkspaceWorkingDay[]
}

export type WorkspaceSetupItem = Pick<WorkspaceScenarioCatalogItem, 'id' | 'name' | 'unitPrice' | 'durationMinutes' | 'isActive'> & { isUsed?: boolean }

export async function loadWorkspaceSetup(organizationId: string): Promise<WorkspaceSetup> {
  const response = await fetch(`/api/v1/organizations/${organizationId}/setup`, { credentials: 'same-origin' })
  if (!response.ok) throw new Error(translate('setup.loadApiError'))
  return response.json()
}

export async function openWorkspaceSetup(organizationId: string): Promise<boolean> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(`/api/v1/organizations/${organizationId}/setup/open`, {
    method: 'POST', credentials: 'same-origin', headers: { RequestVerificationToken: token }
  })
  if (!response.ok) throw new Error(translate('setup.openApiError'))
  return (await response.json()).shouldOpen === true
}

export async function saveWorkspaceSetup(organizationId: string, payload: {
  step: number, revision: number, skip: boolean, items: WorkspaceSetupItem[],
  workdayStart?: string, workdayEnd?: string, workingDays?: number[], weeklySchedule?: WorkspaceWorkingDay[]
}): Promise<WorkspaceSetup> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(`/api/v1/organizations/${organizationId}/setup`, {
    method: 'PUT', credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
    body: JSON.stringify(payload)
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? translate('setup.saveApiError'))
  }
  return response.json()
}

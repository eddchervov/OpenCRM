import { translate as t } from '@opencrm/ui/i18n'
import '../shiftMessages'

export type Shift = {
  id: string
  employeeId: string
  employeeName: string
  localStartDate: string
  timeZoneId: string
  startLocal: string
  endLocal: string
  plannedBreakMinutes: number
  isPublished: boolean
  isCancelled: boolean
  version: number
}

export type ShiftAssignment = {
  maintenanceId: string
  employeeId: string
  vehicleName: string
  localStartDate: string
  serviceCount: number
  segments: Array<{ startLocal: string, endLocal: string, isOutsideShift: boolean }>
}
export type ShiftWeek = {
  timeZoneId: string | null
  canManage: boolean
  canViewAssignments: boolean
  employees: Array<{ id: string, name: string, isActive: boolean }>
  shifts: Shift[]
  assignments: ShiftAssignment[]
}

export type ShiftInput = {
  employeeId: string
  startLocal: string
  endLocal: string
  plannedBreakMinutes: number
  version?: number
  reason?: string
}

const base = (organizationId: string) => `/api/v1/organizations/${organizationId}/shifts`
const token = () => document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''

async function read<T>(responsePromise: Promise<Response>): Promise<T> {
  const response = await responsePromise
  if (!response.ok) {
    const detail = await response.json().catch(() => null)
    throw new Error(detail?.message ?? t('shiftsApi.actionError'))
  }
  return response.json() as Promise<T>
}

async function send<T>(url: string, method: string, body: unknown): Promise<T> {
  return read<T>(fetch(url, {
    method,
    headers: { 'Content-Type': 'application/json', RequestVerificationToken: token() },
    body: JSON.stringify(body)
  }))
}

export const loadShiftWeek = (organizationId: string, weekStart: string) =>
  read<ShiftWeek>(fetch(base(organizationId) + `?weekStart=${weekStart}`, { headers: { Accept: 'application/json' } }))
export const loadOwnShifts = (organizationId: string) =>
  read<Shift[]>(fetch(base(organizationId) + '/mine', { headers: { Accept: 'application/json' } }))
export const setShiftTimeZone = (organizationId: string, timeZoneId: string, expectedTimeZoneId: string | null = null) =>
  send(base(organizationId) + '/settings', 'PUT', { timeZoneId, expectedTimeZoneId })
export const saveShift = (organizationId: string, shiftId: string | null, input: ShiftInput) =>
  send(base(organizationId) + (shiftId ? `/${shiftId}` : ''), shiftId ? 'PUT' : 'POST', input)
export const cancelShift = (organizationId: string, shift: Shift, reason: string) =>
  send(base(organizationId) + `/${shift.id}`, 'DELETE', { version: shift.version, reason })
export const publishShiftWeek = (organizationId: string, weekStart: string) =>
  send(base(organizationId) + '/publish', 'POST', { weekStart })
export const copyPreviousShiftWeek = (organizationId: string, weekStart: string) =>
  send<{ count: number }>(base(organizationId) + '/copy-previous-week', 'POST', { weekStart })

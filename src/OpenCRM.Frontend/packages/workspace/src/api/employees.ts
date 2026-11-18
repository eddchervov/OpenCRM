import { translate as t } from '@opencrm/ui/i18n'
import '../employeeMessages'

export type OrganizationEmployee = {
  id: string
  name: string
  phone?: string | null
  memberId?: string | null
  isActive: boolean
  version: number
}

export type EmployeeMemberOption = { id: string; email?: string | null; userName?: string | null }

export async function loadEmployees(organizationId: string): Promise<{
  items: OrganizationEmployee[]
  canManage: boolean
  canLinkMembers: boolean
  members: EmployeeMemberOption[]
}> {
  const response = await fetch(`/api/v1/organizations/${organizationId}/employees`, { headers: { Accept: 'application/json' } })
  if (!response.ok) {
    const error = new Error(t('employeeApi.loadEmployees')) as Error & { status: number }
    error.status = response.status
    throw error
  }
  return response.json()
}

export async function linkEmployeeMember(organizationId: string, employee: OrganizationEmployee, memberId: string | null) {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(`/api/v1/organizations/${organizationId}/employees/${employee.id}/member`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
    body: JSON.stringify({ memberId, version: employee.version })
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? t('employeeApi.linkMember'))
  }
}

export async function saveEmployee(organizationId: string, employee: OrganizationEmployee & { memberIdToLink?: string }) {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(`/api/v1/organizations/${organizationId}/employees${employee.id ? `/${employee.id}` : ''}`, {
    method: employee.id ? 'PUT' : 'POST',
    headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
    body: JSON.stringify({ ...employee, id: employee.id || undefined })
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? t('employeeApi.saveEmployee'))
  }
}

export async function createEmployeeWithInvitation(
  organizationId: string,
  payload: { name: string, phone?: string | null, email: string, roleCode: string, isActive?: boolean }
): Promise<{ employeeId: string, invitationId: string }> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch('/api/v1/organizations/' + organizationId + '/employees/with-invitation', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
    body: JSON.stringify(payload)
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? t('employeeApi.inviteEmployee'))
  }

  return response.json()
}

export async function createEmployeeForMember(
  organizationId: string,
  payload: { memberId: string, name: string, phone?: string | null, isActive?: boolean }
): Promise<{ id: string }> {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(`/api/v1/organizations/${organizationId}/employees/for-member`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
    body: JSON.stringify(payload)
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? t('employeeApi.createPerformer'))
  }

  return response.json()
}
export async function assignMaintenanceEmployee(organizationId: string, maintenanceId: string, employeeId: string, version?: number) {
  const token = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  const response = await fetch(`/api/v1/maintenance/${maintenanceId}/employee?organizationId=${organizationId}`, {
    method: 'PUT', headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
    body: JSON.stringify({ employeeId: employeeId || null, version })
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? t('employeeApi.assignEmployee'))
  }
}

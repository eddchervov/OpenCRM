import type { WorkspaceScenarioCatalog } from './scenarioCatalog'

import type { WorkspaceAnalytics } from '../types'

export type WorkspaceOrganizationMember = {
  id: string
  userId: string
  email?: string | null
  userName?: string | null
  employeeName?: string | null
  roleCode: string
  roleName: string
  joinedAt: string
  isOwner: boolean
}

export type WorkspaceOrganizationInvitation = {
  id: string
  email: string
  employeeId?: string | null
  employeeName?: string | null
  roleCode: string
  roleName: string
  statusCode: string
  statusName: string
  createdAt: string
}

export type WorkspaceOrganization = {
  id: string
  name: string
  timeZoneId?: string | null
  description?: string | null
  legalName?: string | null
  inn?: string | null
  kpp?: string | null
  ogrn?: string | null
  legalAddress?: string | null
  actualAddress?: string | null
  phone?: string | null
  email?: string | null
  signatoryPosition?: string | null
  signatoryName?: string | null
  signatoryBasis?: string | null
  documentStyleCode: string
  maintenanceWorkflowMode: string
  allowWorkSelfAssignment: boolean
  primaryScenarioCode: string
  primaryScenarioName: string
  currentUserRoleCode: string
  currentUserRoleName: string
  canManageMembers: boolean
  canManageSettings: boolean
  canManageBilling: boolean
  membersCount: number
  createdAt: string
  isArchived: boolean
  members: WorkspaceOrganizationMember[]
  pendingInvitations: WorkspaceOrganizationInvitation[]
  invitations: WorkspaceOrganizationInvitation[]
}

export type WorkspaceFeature = {
  code: string
  name?: string
  boolValue?: boolean | null
  intValue?: number | null
  stringValue?: string | null
}

export type WorkspacePlan = {
  id?: number
  name: string
  group?: string | null
  price?: number
  effectivePrice?: number
  originalEffectivePrice?: number
  hasPromoDiscount?: boolean
  promoCode?: string | null
  pricePerMonth?: number
  durationDays?: number
  isDefaultPlan?: boolean
  isDemoPlan?: boolean
  features?: WorkspaceFeature[]
  vehicleLimit?: number | null
  hasDiscount?: boolean
}

export type WorkspaceSubscription = {
  endDate?: string
  sourceCode?: string
  promoDaysStartDate?: string | null
  subscriptionPlan?: WorkspacePlan | null
}

export type WorkspaceOverviewData = {
  organization: WorkspaceOrganization
  workspaceFeatures: WorkspaceFeature[]
  currentSubscription?: WorkspaceSubscription | null
  effectivePlan?: WorkspacePlan | null
  workspaceVehicleLimit: number
  workspaceMembersLimit: number
  activeVehiclesCount: number
  archivedVehiclesCount: number
  clientsCount: number
  metrics: {
    maintenanceTotalCount: number
    maintenancePlannedCount: number
    maintenanceInProgressCount: number
    maintenanceCompletedCount: number
    openDefectsCount: number
    resolvedDefectsCount: number
    counterEntriesCount: number
    documentsCount: number
  }
  planning: {
    overdueCount: number
    soonCount: number
    attentionCount: number
    defectsCount: number
    firstServiceCount: number
    counterUpdateCount: number
  }
}

export type WorkspaceRolePermission = {
  code: string
  name: string
  groupName: string
  description: string
  isWritePermission: boolean
  isEnabled: boolean
}

export type WorkspaceRolePermissionMatrix = {
  roleCode: string
  roleName: string
  canEdit: boolean
  permissions: WorkspaceRolePermission[]
}

export type WorkspaceMembersData = {
  organization: WorkspaceOrganization
  rolePermissionMatrix: WorkspaceRolePermissionMatrix[]
  canManageMembers: boolean
  canInviteMembers: boolean
  canManageRoles: boolean
  canRemoveMembers: boolean
  roleOptions: Array<{ code: string, name: string }>
}

export type WorkspaceWikiArticle = {
  slug: string
  title: string
  summary: string
  screenshotUrl?: string | null
  screenshotAlt?: string | null
  sections: Array<{
    title: string
    paragraphs: string[]
    bullets: string[]
    note?: string | null
  }>
}

export type WorkspaceWikiData = {
  articles: WorkspaceWikiArticle[]
  selectedArticle?: WorkspaceWikiArticle | null
  searchQuery: string
  hasSearchResults: boolean
}

export type WorkspaceSettingsData = {
  organization: WorkspaceOrganization
  workspaceFeatures: WorkspaceFeature[]
  canManageProfile: boolean
  canManageRequisites: boolean
  canArchive: boolean
  scenarioCatalog?: {
    statuses?: unknown[]
  } & WorkspaceScenarioCatalog
}

export type WorkspaceBillingData = {
  organization: WorkspaceOrganization
  workspaceFeatures: WorkspaceFeature[]
  currentSubscription?: WorkspaceSubscription | null
  effectivePlan?: WorkspacePlan | null
  workspaceVehicleLimit: number
  workspaceMembersLimit: number
  availableBusinessPlans: WorkspacePlan[]
  testDemoPlans?: WorkspacePlan[]
  canUseTestSubscriptions?: boolean
  demoBusinessAvailable: boolean
  demoBusinessAvailabilityMessage?: string | null
  canExtendBusinessSubscription: boolean
  businessDaysUntilExpiry: number
  businessExtensionMessage?: string | null
  canManageBilling: boolean
}

export type WorkspaceStatisticsData = {
  organization: WorkspaceOrganization
  workspaceFeatures: WorkspaceFeature[]
  analytics: WorkspaceAnalytics
}

export type WorkspaceProfileSaveRequest = {
  name: string
  description?: string | null
  primaryScenarioCode: string
  documentStyleCode?: string | null
  maintenanceWorkflowMode?: string | null
  allowWorkSelfAssignment: boolean
}

export type WorkspaceRequisitesSaveRequest = {
  legalName?: string | null
  inn?: string | null
  kpp?: string | null
  ogrn?: string | null
  legalAddress?: string | null
  actualAddress?: string | null
  phone?: string | null
  email?: string | null
  signatoryPosition?: string | null
  signatoryName?: string | null
  signatoryBasis?: string | null
}

export type WorkspaceActionResult = {
  code?: string | null
  operationId?: string | null
  message?: string | null
  paymentUrl?: string | null
  requiresPayment?: boolean
  purchaseId?: string | null
  isPaid?: boolean
  primaryActionUrl?: string | null
}

export type WorkspacePaymentBuyer = {
  typeCode: 'individual' | 'business'
  name: string | null
  inn: string | null
}

export function loadWorkspaceOverview(organizationId: string): Promise<WorkspaceOverviewData> {
  return getJson(`/api/v1/organizations/${organizationId}/workspace/overview`, 'Не удалось загрузить организацию.')
}

export function loadWorkspaceMembers(organizationId: string): Promise<WorkspaceMembersData> {
  return getJson(`/api/v1/organizations/${organizationId}/workspace/members`, 'Не удалось загрузить участников.')
}

export function loadWorkspaceWiki(
  organizationId: string,
  options: { slug?: string, searchQuery?: string } = {}
): Promise<WorkspaceWikiData> {
  const url = new URL(`/api/v1/organizations/${organizationId}/workspace/wiki`, window.location.origin)

  if (options.slug) {
    url.searchParams.set('slug', options.slug)
  }

  if (options.searchQuery) {
    url.searchParams.set('q', options.searchQuery)
  }

  return getJson(url, 'Не удалось загрузить вики.')
}

export function loadWorkspaceWikiArticle(organizationId: string, slug: string): Promise<WorkspaceWikiArticle> {
  return getJson(
    `/api/v1/organizations/${organizationId}/workspace/wiki/articles/${encodeURIComponent(slug)}`,
    'Не удалось загрузить статью.'
  )
}

export function loadWorkspaceSettings(organizationId: string): Promise<WorkspaceSettingsData> {
  return getJson(`/api/v1/organizations/${organizationId}/workspace/settings`, 'Не удалось загрузить настройки.')
}

export function loadWorkspaceBilling(organizationId: string): Promise<WorkspaceBillingData> {
  return getJson(`/api/v1/organizations/${organizationId}/workspace/billing`, 'Не удалось загрузить тарифы.')
}

export function loadWorkspaceStatistics(organizationId: string): Promise<WorkspaceStatisticsData> {
  return getJson(`/api/v1/organizations/${organizationId}/workspace/statistics`, 'Не удалось загрузить статистику.')
}

export function updateWorkspaceProfile(
  organizationId: string,
  payload: WorkspaceProfileSaveRequest
): Promise<WorkspaceActionResult> {
  return sendJson(`/api/v1/organizations/${organizationId}/workspace/settings/profile`, 'PUT', payload, 'Не удалось сохранить настройки.')
}

export function updateWorkspaceRequisites(
  organizationId: string,
  payload: WorkspaceRequisitesSaveRequest
): Promise<WorkspaceActionResult> {
  const normalizedPayload = Object.fromEntries(Object.entries(payload).map(([field, value]) => [field, value?.trim() || null]))
  if (normalizedPayload.kpp) normalizedPayload.kpp = normalizedPayload.kpp.toUpperCase()
  const errors: Record<string, string[]> = {}
  for (const [field, pattern, message] of [
    ['inn', /^(?:[0-9]{10}|[0-9]{12})$/, 'ИНН должен содержать 10 или 12 цифр.'],
    ['kpp', /^[0-9]{4}[0-9A-Z]{2}[0-9]{3}$/, 'КПП должен содержать 9 знаков: цифры, в 5–6-й позиции допустимы латинские буквы.'],
    ['ogrn', /^(?:[0-9]{13}|[0-9]{15})$/, 'ОГРН / ОГРНИП должен содержать 13 или 15 цифр.']
  ] as const) {
    const value = normalizedPayload[field]
    if (value && !pattern.test(value)) errors[field] = [message]
  }
  if (Object.keys(errors).length) return Promise.reject(new WorkspaceActionError('Проверьте данные формы.', undefined, errors))

  return sendJson(`/api/v1/organizations/${organizationId}/workspace/settings/requisites`, 'PUT', normalizedPayload, 'Не удалось сохранить реквизиты.')
}

export function archiveWorkspaceOrganization(organizationId: string): Promise<WorkspaceActionResult> {
  return sendJson(`/api/v1/organizations/${organizationId}/workspace/settings/archive`, 'POST', null, 'Не удалось архивировать организацию.')
}

export function inviteWorkspaceMember(
  organizationId: string,
  payload: { email: string, roleCode: string, employeeId?: string | null }
): Promise<WorkspaceActionResult> {
  return sendJson(`/api/v1/organizations/${organizationId}/workspace/members/invitations`, 'POST', payload, 'Не удалось добавить участника.')
}

export function updateWorkspaceMemberRole(
  organizationId: string,
  memberId: string,
  payload: { roleCode: string }
): Promise<WorkspaceActionResult> {
  return sendJson(`/api/v1/organizations/${organizationId}/workspace/members/${memberId}/role`, 'PUT', payload, 'Не удалось изменить роль.')
}

export function removeWorkspaceMember(organizationId: string, memberId: string): Promise<WorkspaceActionResult> {
  return sendJson(`/api/v1/organizations/${organizationId}/workspace/members/${memberId}`, 'DELETE', null, 'Не удалось удалить участника.')
}

export function transferWorkspaceOwnership(organizationId: string, memberId: string): Promise<WorkspaceActionResult> {
  return sendJson(`/api/v1/organizations/${organizationId}/workspace/members/${memberId}/transfer-ownership`, 'POST', null, 'Не удалось передать владение.')
}

export function revokeWorkspaceInvitation(organizationId: string, invitationId: string): Promise<WorkspaceActionResult> {
  return sendJson(`/api/v1/organizations/${organizationId}/workspace/members/invitations/${invitationId}`, 'DELETE', null, 'Не удалось отозвать приглашение.')
}

export function updateWorkspaceRolePermissions(
  organizationId: string,
  roleCode: string,
  payload: { enabledPermissionCodes: string[] }
): Promise<WorkspaceActionResult> {
  return sendJson(`/api/v1/organizations/${organizationId}/workspace/members/permissions/${roleCode}`, 'PUT', payload, 'Не удалось сохранить права.')
}

export function purchaseWorkspaceBusinessPlan(
  organizationId: string,
  planId: number,
  buyer?: WorkspacePaymentBuyer,
  operationId?: string
): Promise<WorkspaceActionResult> {
  return sendJson(`/api/v1/organizations/${organizationId}/workspace/billing/plans/${planId}/purchase`, 'POST', buyer ?? null, 'Не удалось подключить тариф.', operationId ? { 'Idempotency-Key': operationId } : {})
}

export function activateWorkspaceDemoPlan(organizationId: string, planId: number): Promise<WorkspaceActionResult> {
  return sendJson(`/api/v1/organizations/${organizationId}/workspace/billing/plans/${planId}/activate-demo`, 'POST', null, 'Не удалось подключить демо.')
}

export function extendWorkspaceBusinessPlan(
  organizationId: string,
  buyer?: WorkspacePaymentBuyer,
  operationId?: string
): Promise<WorkspaceActionResult> {
  return sendJson(`/api/v1/organizations/${organizationId}/workspace/billing/extend`, 'POST', buyer ?? null, 'Не удалось продлить тариф.', operationId ? { 'Idempotency-Key': operationId } : {})
}

export function loadPaymentPurchase(purchaseId: string, signal?: AbortSignal): Promise<WorkspaceActionResult> {
  return getJson(`/api/v1/payments/purchases/${encodeURIComponent(purchaseId)}`, 'Не удалось проверить состояние оплаты.', signal)
}

function getRequestVerificationToken() {
  return document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
}

function getMutationHeaders() {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    'Content-Type': 'application/json',
    'X-Requested-With': 'XMLHttpRequest'
  }
  const token = getRequestVerificationToken()

  if (token) {
    headers.RequestVerificationToken = token
  }

  return headers
}

export async function getJson<T>(url: string | URL, errorMessage: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url.toString(), {
    method: 'GET',
    credentials: 'same-origin',
    signal,
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message ?? errorMessage)
  }

  return response.json()
}

export async function sendJson<T>(
  url: string | URL,
  method: 'POST' | 'PUT' | 'DELETE',
  payload: unknown,
  errorMessage: string,
  extraHeaders: Record<string, string> = {}
): Promise<T> {
  const response = await fetch(url.toString(), {
    method,
    credentials: 'same-origin',
    headers: { ...getMutationHeaders(), ...extraHeaders },
    body: payload === null ? undefined : JSON.stringify(payload)
  })

  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new WorkspaceActionError(error?.errors ? 'Проверьте данные формы.' : error?.message ?? errorMessage, error?.code ?? error?.errorCode, error?.errors)
  }

  return response.json().catch(() => ({} as T))
}

export class WorkspaceActionError extends Error {
  readonly code?: string
  readonly errors?: Record<string, string[]>

  constructor(message: string, code?: string, errors?: Record<string, string[]>) {
    super(message)
    this.code = code
    this.errors = errors
  }
}

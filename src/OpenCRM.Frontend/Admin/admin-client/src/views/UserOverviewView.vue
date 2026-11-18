<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ApiError } from '@/api/ApiError'
import { usesFreePro } from '@/utils/subscriptionPolicy'
import { extendAdminSubscription, getAdminAvailablePlans, grantAdminOrganizationDemo, grantAdminPlan, grantAdminUserDemo, revokeAdminOrganizationDemo, revokeAdminUserDemo, stopAdminSubscription } from '@/api/adminBillingApi'
import type { AdminAvailablePlan } from '@/models/AdminAvailablePlan'
import {
  getAdminUserOverview,
  getAdminUserOrganizationContents,
  impersonateAdminUser,
  updateAdminUserTestSubscriptions
} from '@/api/adminUsersApi'
import type { AdminUserFeatureGrant } from '@/models/AdminUserFeatureGrant'
import type { AdminUserOverview } from '@/models/AdminUserOverview'
import type { AdminUserOrganization } from '@/models/AdminUserOrganization'
import type { AdminUserOrganizationContents } from '@/models/AdminUserOrganizationContents'
import { useAdminCurrentUserStore } from '@/stores/adminCurrentUserStore'
import { formattingCulture, useLocalization } from '@opencrm/ui/i18n'
import '../adminUsersMessages'
import { getAdminRecordLabel } from '@/utils/adminRecordLabels'
import { getOrganizationScenarioLabel, getVehicleKindLabel } from '@/utils/adminSystemLabels'

type TabName = 'overview' | 'vehicles' | 'organizations' | 'payments' | 'support' | 'features'

const route = useRoute()
const currentUserStore = useAdminCurrentUserStore()
const { t, locale } = useLocalization()
const overview = ref<AdminUserOverview | null>(null)
const activeTab = ref<TabName>('overview')
const isLoading = ref(false)
const errorMessage = ref<string | null>(null)
const isImpersonationModalOpen = ref(false)
const isImpersonating = ref(false)
const impersonationError = ref<string | null>(null)
const isManagementModalOpen = ref(false)
const isManaging = ref(false)
const managementMessage = ref<string | null>(null)
const managementError = ref<string | null>(null)
const managementConfirmation = ref<'demo' | 'revoke-demo' | 'default-plan' | 'grant-plan' | 'extend' | null>(null)
const managementReason = ref('')
const managementPlanId = ref(0)
const managementPlanDays = ref(30)
const availablePlans = ref<AdminAvailablePlan[]>([])
const organizationConfirmation = ref<{ organization: AdminUserOrganization, action: 'grant' | 'revoke' | 'grant-plan' | 'stop' | 'extend' } | null>(null)
const organizationActionReason = ref('')
const organizationPlanId = ref(0)
const organizationPlanDays = ref(30)
const organizationMessage = ref<string | null>(null)
const organizationError = ref<string | null>(null)
const isManagingOrganization = ref(false)
const expandedOrganizationId = ref<string | null>(null)
const organizationContents = ref<Record<string, AdminUserOrganizationContents>>({})
const organizationContentsError = ref<string | null>(null)
const isLoadingOrganizationContents = ref(false)

const userId = computed(() => route.params.userId?.toString() ?? '')
const profile = computed(() => overview.value?.profile ?? null)
const commercialEnabled = computed(() => currentUserStore.currentUser?.commercialEnabled === true)
const canImpersonate = computed(() =>
  currentUserStore.isSystem ||
  currentUserStore.currentUser?.permissions.includes('Admin.Users.Impersonate') === true)
const canManage = computed(() =>
  currentUserStore.isSystem ||
  currentUserStore.currentUser?.permissions.includes('Admin.Users.Manage') === true)
const canManageBilling = computed(() => commercialEnabled.value && (
  currentUserStore.isSystem ||
  currentUserStore.currentUser?.permissions.includes('Admin.Billing.Manage') === true))
const hasActiveUserSubscription = computed(() =>
  overview.value?.subscriptions.users.some(subscription => subscription.isActive) === true)
const hasActiveUserDemo = computed(() =>
  overview.value?.subscriptions.users.some(subscription => subscription.isActive && subscription.isDemoPlan) === true)
const activeUserSubscriptionsCount = computed(() =>
  overview.value?.subscriptions.users.filter(subscription => subscription.isActive).length ?? 0)
const personalPlans = computed(() => availablePlans.value.filter(plan => plan.planTypeCode === 'personal'))
const businessPlans = computed(() => availablePlans.value.filter(plan => plan.planTypeCode === 'business'))
const managementPlanEndDate = computed(() => previewPlanEndDate(managementPlanId.value, managementPlanDays.value, personalPlans.value))
const organizationPlanEndDate = computed(() => previewPlanEndDate(organizationPlanId.value, organizationPlanDays.value, businessPlans.value))
const activeUserSubscription = computed(() => overview.value?.subscriptions.users.find(subscription => subscription.isActive) ?? null)
const managementExtensionEndDate = computed(() => previewExtensionEndDate(activeUserSubscription.value?.endDate, managementPlanDays.value))
const organizationExtensionEndDate = computed(() => previewExtensionEndDate(organizationConfirmation.value?.organization.currentPlanEndDate, organizationPlanDays.value))

const tabs: { value: TabName, label: string }[] = [
  { value: 'overview', label: t('adminUsers.overview.tabOverview') },
  { value: 'vehicles', label: t('adminUsers.overview.tabVehicles') },
  { value: 'organizations', label: t('adminUsers.overview.tabOrganizationsPlans') },
  { value: 'payments', label: t('adminUsers.overview.tabPayments') },
  { value: 'support', label: t('adminUsers.overview.tabSupport') },
  { value: 'features', label: t('adminUsers.overview.tabFeatures') }
]
const visibleTabs = computed(() => tabs
  .filter(tab => commercialEnabled.value || (tab.value !== 'payments' && tab.value !== 'features'))
  .map(tab => tab.value === 'organizations' && !commercialEnabled.value
    ? { ...tab, label: t('adminUsers.overview.tabOrganizations') }
    : { ...tab, label: t(tab.value === 'overview' ? 'adminUsers.overview.tabOverview'
      : tab.value === 'vehicles' ? 'adminUsers.overview.tabVehicles'
        : tab.value === 'organizations' ? 'adminUsers.overview.tabOrganizationsPlans'
          : tab.value === 'payments' ? 'adminUsers.overview.tabPayments'
            : tab.value === 'support' ? 'adminUsers.overview.tabSupport'
              : 'adminUsers.overview.tabFeatures') }))

function adminErrorMessage(error: unknown, fallbackKey: string): string {
  if (error instanceof ApiError) {
    return error.message.startsWith('API request failed with status ')
      ? t('adminUsers.api.requestFailed', { status: error.status })
      : error.message
  }

  return t(fallbackKey)
}

watch(userId, () => {
  activeTab.value = route.query.tab === 'organizations' ? 'organizations' : 'overview'
  organizationConfirmation.value = null
  organizationMessage.value = null
  organizationError.value = null
  expandedOrganizationId.value = null
  organizationContents.value = {}
  void loadUser()
}, { immediate: true })

async function loadUser(): Promise<void> {
  if (!userId.value) {
    return
  }

  isLoading.value = true
  errorMessage.value = null

  try {
    overview.value = await getAdminUserOverview(userId.value)
  } catch (error) {
    overview.value = null
    errorMessage.value = adminErrorMessage(error, 'adminUsers.overview.loadError')
  } finally {
    isLoading.value = false
  }
}

function openImpersonationModal(): void {
  impersonationError.value = null
  isImpersonationModalOpen.value = true
}

async function impersonate(): Promise<void> {
  if (!profile.value) {
    return
  }

  isImpersonating.value = true
  impersonationError.value = null

  try {
    await impersonateAdminUser(profile.value.id)
    window.location.assign('/')
  } catch (error) {
    impersonationError.value = adminErrorMessage(error, 'adminUsers.overview.impersonateError')
  } finally {
    isImpersonating.value = false
  }
}

function openManagementModal(): void {
  if (!commercialEnabled.value) {
    return
  }
  managementMessage.value = null
  managementError.value = null
  managementConfirmation.value = null
  managementReason.value = ''
  isManagementModalOpen.value = true
  void loadAvailablePlans()
}

function openPersonalSubscriptionAction(action: 'extend' | 'default-plan'): void {
  openManagementModal()
  managementConfirmation.value = action
}

async function loadAvailablePlans(): Promise<void> {
  if (!canManageBilling.value) {
    return
  }

  try {
    availablePlans.value = await getAdminAvailablePlans()
  } catch (error) {
    managementError.value = adminErrorMessage(error, 'adminUsers.overview.plansLoadError')
  }
}

function previewPlanEndDate(planId: number, days: number, plans: AdminAvailablePlan[]): string | null {
  if (!plans.some(plan => plan.id === planId) || !Number.isInteger(days) || days < 1 || days > 366) {
    return null
  }

  return new Date(Date.now() + days * 24 * 60 * 60 * 1000).toISOString()
}

function previewExtensionEndDate(endDate: string | null | undefined, days: number): string | null {
  if (!endDate || !Number.isInteger(days) || days < 1 || days > 366) {
    return null
  }

  return new Date(Math.max(Date.now(), new Date(endDate).getTime()) + days * 24 * 60 * 60 * 1000).toISOString()
}

function activeOrganizationSubscriptionsCount(organizationId: string): number {
  return overview.value?.subscriptions.organizations.filter(subscription =>
    subscription.organizationId === organizationId && subscription.isActive).length ?? 0
}

async function toggleOrganizationContents(organizationId: string): Promise<void> {
  if (expandedOrganizationId.value === organizationId) {
    expandedOrganizationId.value = null
    return
  }

  expandedOrganizationId.value = organizationId
  organizationContentsError.value = null
  if (organizationContents.value[organizationId]) {
    return
  }

  isLoadingOrganizationContents.value = true
  try {
    organizationContents.value[organizationId] = await getAdminUserOrganizationContents(userId.value, organizationId)
  } catch (error) {
    organizationContentsError.value = adminErrorMessage(error, 'adminUsers.overview.contentsLoadError')
  } finally {
    isLoadingOrganizationContents.value = false
  }
}

async function toggleTestSubscriptions(): Promise<void> {
  if (!profile.value) {
    return
  }

  await runManagementOperation(() => updateAdminUserTestSubscriptions(
    profile.value!.id,
    !profile.value!.canUseTestSubscriptions
  ))
}

async function grantDemo(): Promise<void> {
  if (!profile.value || !managementReason.value.trim()) {
    return
  }

  await runManagementOperation(() => grantAdminUserDemo(profile.value!.id, managementReason.value.trim()))
}

async function revokeDemo(): Promise<void> {
  if (!profile.value || !managementReason.value.trim()) {
    return
  }

  await runManagementOperation(() => revokeAdminUserDemo(profile.value!.id, managementReason.value.trim()))
}

async function grantUserPlan(): Promise<void> {
  if (!profile.value || hasActiveUserSubscription.value || !managementPlanEndDate.value || !managementReason.value.trim()) {
    return
  }

  await runManagementOperation(() => grantAdminPlan(
    'users', profile.value!.id, managementPlanId.value, managementPlanDays.value, managementReason.value.trim()))
}

async function returnToDefaultPlan(): Promise<void> {
  const subscription = activeUserSubscription.value
  if (!subscription || !managementReason.value.trim()) {
    return
  }

  await runManagementOperation(() => stopAdminSubscription('users', subscription.id, managementReason.value.trim(), subscription.endDate))
}

async function extendUserPlan(): Promise<void> {
  const subscription = activeUserSubscription.value
  if (!subscription || !managementExtensionEndDate.value || !managementReason.value.trim()) {
    return
  }

  await runManagementOperation(() => extendAdminSubscription(
    'users', subscription.id, managementPlanDays.value, managementReason.value.trim(), subscription.endDate))
}

async function confirmManagementAction(): Promise<void> {
  if (!commercialEnabled.value) {
    return
  }

  if (managementConfirmation.value === 'demo') {
    await grantDemo()
  } else if (managementConfirmation.value === 'revoke-demo') {
    await revokeDemo()
  } else if (managementConfirmation.value === 'grant-plan') {
    await grantUserPlan()
  } else if (managementConfirmation.value === 'default-plan') {
    await returnToDefaultPlan()
  } else if (managementConfirmation.value === 'extend') {
    await extendUserPlan()
  }

  managementConfirmation.value = null
}

async function runManagementOperation(operation: () => Promise<{ message: string }>): Promise<void> {
  if (!commercialEnabled.value) {
    return
  }

  isManaging.value = true
  managementMessage.value = null
  managementError.value = null

  try {
    const response = await operation()
    await loadUser()
    managementMessage.value = response.message
  } catch (error) {
    managementError.value = adminErrorMessage(error, 'adminUsers.overview.operationError')
  } finally {
    isManaging.value = false
  }
}

function openOrganizationConfirmation(organization: AdminUserOrganization, action: 'grant' | 'revoke' | 'grant-plan' | 'stop' | 'extend'): void {
  if (!commercialEnabled.value || (action === 'grant' && usesFreePro(organization.primaryScenarioCode))) {
    return
  }

  organizationError.value = null
  organizationActionReason.value = ''
  organizationPlanId.value = 0
  organizationPlanDays.value = 30
  organizationConfirmation.value = { organization, action }
  void loadAvailablePlans()
}

async function confirmOrganizationAction(): Promise<void> {
  if (!commercialEnabled.value || !profile.value || !organizationConfirmation.value || !organizationActionReason.value.trim() ||
    (organizationConfirmation.value.action === 'grant-plan' && !organizationPlanEndDate.value) ||
    (organizationConfirmation.value.action === 'extend' && !organizationExtensionEndDate.value)) {
    return
  }

  const { organization, action } = organizationConfirmation.value
  isManagingOrganization.value = true
  organizationError.value = null
  organizationMessage.value = null

  try {
    let response: { message: string }
    if (action === 'grant-plan') {
      response = await grantAdminPlan('organizations', organization.organizationId, organizationPlanId.value, organizationPlanDays.value, organizationActionReason.value.trim())
    } else if (action === 'grant' && !usesFreePro(organization.primaryScenarioCode)) {
      response = await grantAdminOrganizationDemo(profile.value.id, organization.organizationId, organizationActionReason.value.trim())
    } else if (action === 'revoke') {
      response = await revokeAdminOrganizationDemo(profile.value.id, organization.organizationId, organizationActionReason.value.trim())
    } else if (action === 'stop' && organization.currentSubscriptionId && organization.currentPlanEndDate) {
      response = await stopAdminSubscription('organizations', organization.currentSubscriptionId, organizationActionReason.value.trim(), organization.currentPlanEndDate)
    } else if (action === 'extend' && organization.currentSubscriptionId && organization.currentPlanEndDate) {
      response = await extendAdminSubscription('organizations', organization.currentSubscriptionId, organizationPlanDays.value, organizationActionReason.value.trim(), organization.currentPlanEndDate)
    } else {
      return
    }
    await loadUser()
    organizationMessage.value = response.message
    organizationConfirmation.value = null
  } catch (error) {
    organizationError.value = adminErrorMessage(error, 'adminUsers.overview.organizationSubscriptionError')
  } finally {
    isManagingOrganization.value = false
  }
}

function formatDate(value: string | null): string {
  if (!value) {
    return '—'
  }

  return new Intl.DateTimeFormat(formattingCulture.value, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric'
  }).format(new Date(value))
}

function formatDateTime(value: string | null): string {
  if (!value || new Date(value).getFullYear() <= 1) {
    return '—'
  }

  return new Intl.DateTimeFormat(formattingCulture.value, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  }).format(new Date(value))
}

function formatMaintenanceDate(value: string | null): string {
  if (!value) return '—'

  const date = new Date(value.slice(0, 10) + 'T00:00:00Z')
  if (Number.isNaN(date.getTime())) return '—'

  return new Intl.DateTimeFormat(formattingCulture.value, {
    day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC'
  }).format(date)
}

function formatMoney(value: number | null, currencyCode = 'RUB'): string {
  if (value === null) {
    return '—'
  }

  return new Intl.NumberFormat(formattingCulture.value, {
    style: 'currency',
    currency: currencyCode || 'RUB',
    currencyDisplay: 'code',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
  }).format(value)
}

function formatFeatureValue(grant: AdminUserFeatureGrant): string {
  if (grant.valueType.toLowerCase().includes('bool')) {
    return grant.boolValue ? t('adminUsers.overview.allowed') : t('adminUsers.overview.denied')
  }

  return grant.intValue?.toString() ?? t('adminUsers.overview.noValue')
}

function organizationPlanStatus(organization: AdminUserOrganization): string {
  if (organization.currentPlanIsDemo) return t('adminUsers.overview.demoActive')
  if (organization.hasActiveSubscription) {
    return usesFreePro(organization.primaryScenarioCode)
      ? t('adminUsers.overview.proActive')
      : t('adminUsers.overview.planActive')
  }
  if (organization.isActive && !organization.isArchived && !organization.isDeleted) {
    return usesFreePro(organization.primaryScenarioCode)
      ? t('adminUsers.overview.freeForever')
      : t('adminUsers.overview.basePlan')
  }

  return t('adminUsers.overview.inactive')
}

function organizationStatus(organization: AdminUserOrganization): string {
  if (organization.isDeleted) return t('adminUsers.overview.deletedOrganization')
  if (organization.isArchived) return t('adminUsers.overview.archivedStatus')
  return organization.isActive ? t('adminUsers.overview.activeOrganization') : t('adminUsers.overview.inactive')
}

function organizationActionTitle(action: 'grant' | 'revoke' | 'grant-plan' | 'stop' | 'extend'): string {
  const key = action === 'grant-plan' ? 'orgGrantPlanTitle'
    : action === 'grant' ? 'orgGrantDemoTitle'
      : action === 'extend' ? 'orgExtendTitle'
        : action === 'revoke' ? 'orgRevokeTitle'
          : 'orgStopTitle'
  return t(`adminUsers.overview.${key}`)
}

function managementConfirmationTitle(action: NonNullable<typeof managementConfirmation.value>): string {
  const key = action === 'grant-plan' ? 'grantPlanQuestion'
    : action === 'demo' ? 'grantDemoQuestion'
      : action === 'revoke-demo' ? 'revokeDemoQuestion'
        : action === 'extend' ? 'extendPersonalQuestion'
          : 'stopPersonalQuestion'
  return t(`adminUsers.overview.${key}`)
}

function subscriptionFallbackText(scenarioCode: string | null | undefined): string {
  return t(usesFreePro(scenarioCode)
    ? 'adminUsers.overview.freeProFallback'
    : 'adminUsers.overview.otherScenarioFallback')
}
</script>

<template>
  <section class="user-overview-page">
    <div class="user-overview-heading">
      <div>
        <RouterLink class="back-link" :to="{ name: 'users' }">{{ t('adminUsers.overview.backToUsers') }}</RouterLink>
        <h1>{{ t('adminUsers.overview.title') }}</h1>
      </div>
      <div class="user-overview-actions">
        <button
          v-if="commercialEnabled && (canManage || canManageBilling) && profile && !profile.isDeleted"
          class="secondary-button"
          type="button"
          @click="openManagementModal"
        >
          {{ t('adminUsers.overview.manage') }}
        </button>
        <button
          v-if="canImpersonate && profile && !profile.isDeleted && profile.id !== currentUserStore.currentUser?.id"
          class="primary-button"
          type="button"
          @click="openImpersonationModal"
        >
          {{ t('adminUsers.overview.impersonate') }}
        </button>
        <button class="secondary-button" type="button" :disabled="isLoading" @click="loadUser">
          {{ t('adminUsers.overview.refresh') }}
        </button>
      </div>
    </div>

    <div v-if="isLoading && !overview" class="panel-state">
      {{ t('adminUsers.overview.loading') }}
    </div>

    <div v-else-if="errorMessage" class="panel-state panel-state--error">
      {{ errorMessage }}
    </div>

    <template v-else-if="overview && profile">
      <div class="user-hero">
        <div class="user-avatar user-avatar--large">
          {{ (profile.email || profile.phone || '?').charAt(0).toUpperCase() }}
        </div>
        <div class="user-hero__identity">
          <strong>{{ profile.email || t('adminUsers.overview.noEmail') }}</strong>
          <span>{{ profile.phone || profile.id }}</span>
        </div>
        <div class="user-hero__badges">
          <span class="status-badge" :class="profile.isDeleted ? 'status-badge--danger' : 'status-badge--success'">
            {{ profile.isDeleted ? t('adminUsers.overview.deleted') : t('adminUsers.overview.active') }}
          </span>
          <span v-for="role in profile.roles" :key="role" class="soft-badge">{{ role }}</span>
        </div>
      </div>

      <div class="kpi-grid">
        <div><span>{{ t('adminUsers.overview.vehicles') }}</span><strong>{{ overview.kpis.vehiclesTotal }}</strong></div>
        <div><span>{{ t('adminUsers.overview.maintenances') }}</span><strong>{{ overview.kpis.maintenancesTotal }}</strong></div>
        <div><span>{{ t('adminUsers.overview.organizations') }}</span><strong>{{ overview.kpis.organizationsTotal }}</strong></div>
        <div v-if="commercialEnabled"><span>{{ t('adminUsers.overview.paymentAmount') }}</span><strong>{{ formatMoney(overview.kpis.paymentsAmount) }}</strong></div>
        <div v-if="commercialEnabled"><span>{{ t('adminUsers.overview.activeSubscriptions') }}</span><strong>{{ overview.kpis.activeUserSubscriptions + overview.kpis.activeOrganizationSubscriptions }}</strong></div>
        <div><span>{{ t('adminUsers.overview.openRequests') }}</span><strong>{{ overview.kpis.supportOpen }}</strong></div>
      </div>

      <div class="detail-tabs" role="tablist">
        <button
          v-for="tab in visibleTabs"
          :key="tab.value"
          type="button"
          :class="{ active: activeTab === tab.value }"
          @click="activeTab = tab.value"
        >
          {{ tab.label }}
        </button>
      </div>

      <div v-if="activeTab === 'overview'" class="overview-grid">
        <section class="detail-panel">
          <div class="detail-panel__title">{{ t('adminUsers.overview.basicInfo') }}</div>
          <dl class="detail-list">
            <div><dt>Email</dt><dd>{{ profile.email || '—' }}</dd></div>
            <div><dt>{{ t('adminUsers.overview.emailConfirmed') }}</dt><dd>{{ profile.emailConfirmed ? t('adminUsers.overview.yes') : t('adminUsers.overview.no') }}</dd></div>
            <div><dt>{{ t('adminUsers.overview.phone') }}</dt><dd>{{ profile.phone || '—' }}</dd></div>
            <div><dt>{{ t('adminUsers.overview.phoneConfirmed') }}</dt><dd>{{ profile.phoneNumberConfirmed ? t('adminUsers.overview.yes') : t('adminUsers.overview.no') }}</dd></div>
            <div><dt>{{ t('adminUsers.overview.registration') }}</dt><dd>{{ formatDateTime(profile.datetimeCreation) }}</dd></div>
            <div><dt>{{ t('adminUsers.overview.password') }}</dt><dd>{{ profile.hasPassword ? t('adminUsers.overview.passwordSet') : t('adminUsers.overview.passwordNotSet') }}</dd></div>
          </dl>
        </section>

        <section class="detail-panel">
          <div class="detail-panel__title">{{ t('adminUsers.overview.securityConsent') }}</div>
          <dl class="detail-list">
            <div><dt>{{ t('adminUsers.overview.lockoutAllowed') }}</dt><dd>{{ profile.lockoutEnabled ? t('adminUsers.overview.yes') : t('adminUsers.overview.no') }}</dd></div>
            <div><dt>{{ t('adminUsers.overview.lockedUntil') }}</dt><dd>{{ formatDateTime(profile.lockoutEnd) }}</dd></div>
            <div><dt>{{ t('adminUsers.overview.failedLogins') }}</dt><dd>{{ profile.accessFailedCount }}</dd></div>
            <div><dt>{{ t('adminUsers.overview.dataProcessing') }}</dt><dd>{{ profile.personalDataProcessingConsentAccepted ? t('adminUsers.overview.accepted') : t('adminUsers.overview.notAccepted') }}</dd></div>
            <div><dt>{{ t('adminUsers.overview.consentDate') }}</dt><dd>{{ formatDateTime(profile.personalDataProcessingConsentAcceptedAt) }}</dd></div>
            <div v-if="commercialEnabled"><dt>{{ t('adminUsers.overview.testPlans') }}</dt><dd>{{ profile.canUseTestSubscriptions ? t('adminUsers.overview.allowedPlural') : t('adminUsers.overview.notAllowed') }}</dd></div>
          </dl>
        </section>

        <section class="detail-panel detail-panel--wide">
          <div class="detail-panel__title">{{ t('adminUsers.overview.history') }}</div>
          <div v-if="overview.timeline.length" class="timeline">
            <div v-for="item in overview.timeline" :key="`${item.type}-${item.key}-${item.date}`">
              <span class="timeline__dot"></span>
              <strong>{{ getAdminRecordLabel('timeline', item.type, item.label, locale) }}</strong>
              <span>{{ getAdminRecordLabel('title', item.type, item.title, locale) }}</span>
              <time>{{ formatDateTime(item.date) }}</time>
            </div>
          </div>
          <div v-else class="inline-empty">{{ t('adminUsers.overview.noEvents') }}</div>
        </section>
      </div>

      <div v-if="activeTab === 'vehicles'" class="detail-stack">
        <section class="detail-panel">
          <div class="detail-panel__title">{{ t('adminUsers.overview.equipment') }}</div>
          <div v-if="overview.vehicles.length" class="record-grid">
            <article v-for="vehicle in overview.vehicles" :key="vehicle.id" class="record-card">
              <div class="record-card__top">
                <strong>{{ vehicle.title }}</strong>
                <span class="soft-badge">{{ vehicle.isArchived ? t('adminUsers.overview.archived') : t('adminUsers.overview.equipmentActive') }}</span>
              </div>
              <span>{{ [vehicle.brand, vehicle.model, vehicle.yearIssue].filter(Boolean).join(' · ') || getVehicleKindLabel(vehicle.kind ?? 0, vehicle.kindName, locale) }}</span>
              <dl>
                <div><dt>{{ t('adminUsers.overview.plateVin') }}</dt><dd>{{ vehicle.registrationPlate || vehicle.vin || '—' }}</dd></div>
                <div><dt>{{ t('adminUsers.overview.organization') }}</dt><dd>{{ vehicle.organizationName || t('adminUsers.overview.personal') }}</dd></div>
                <div><dt>{{ t('adminUsers.overview.maintenances') }}</dt><dd>{{ vehicle.maintenancesCount }}</dd></div>
                <div><dt>{{ t('adminUsers.overview.openDefects') }}</dt><dd>{{ vehicle.openDefectsCount }}</dd></div>
              </dl>
            </article>
          </div>
          <div v-else class="inline-empty">{{ t('adminUsers.overview.noVehicles') }}</div>
        </section>

        <section class="detail-panel">
          <div class="detail-panel__title">{{ t('adminUsers.overview.recentMaintenances') }}</div>
          <div v-if="overview.maintenances.length" class="table-wrap">
            <table class="data-table">
              <thead><tr><th>{{ t('adminUsers.overview.date') }}</th><th>{{ t('adminUsers.overview.equipment') }}</th><th>{{ t('adminUsers.overview.type') }}</th><th>{{ t('adminUsers.overview.status') }}</th><th>{{ t('adminUsers.overview.cost') }}</th></tr></thead>
              <tbody>
                <tr v-for="maintenance in overview.maintenances" :key="maintenance.id">
                  <td>{{ formatMaintenanceDate(maintenance.date) }}</td>
                  <td>{{ maintenance.vehicleTitle }}</td>
                  <td>{{ getAdminRecordLabel('maintenanceType', maintenance.type, maintenance.typeName, locale) }}</td>
                  <td>{{ getAdminRecordLabel('maintenanceStatus', maintenance.status, maintenance.statusName, locale) }}</td>
                  <td>{{ formatMoney(maintenance.paidAmount, maintenance.currencyCode) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div v-else class="inline-empty">{{ t('adminUsers.overview.noMaintenances') }}</div>
        </section>
      </div>

      <div v-if="activeTab === 'organizations'" class="detail-stack">
        <section class="detail-panel">
          <div class="detail-panel__title">{{ t(commercialEnabled ? 'adminUsers.overview.organizationsAndPlans' : 'adminUsers.overview.tabOrganizations') }}</div>
          <p v-if="commercialEnabled" class="muted">{{ t('adminUsers.overview.freeProInfo') }}</p>
          <div v-if="organizationMessage" class="form-message form-message--success">{{ organizationMessage }}</div>
          <div v-if="overview.organizations.length" class="organization-cards">
            <article v-for="organization in overview.organizations" :key="organization.organizationId" class="record-card">
              <div class="record-card__top">
                <strong>{{ organization.organizationName || t('adminUsers.overview.noName') }}</strong>
                <span v-if="commercialEnabled" class="status-badge" :class="organization.hasActiveSubscription ? 'status-badge--success' : ''">
                  {{ organizationPlanStatus(organization) }}
                </span>
                <span v-else class="status-badge" :class="organization.isActive && !organization.isArchived && !organization.isDeleted ? 'status-badge--success' : ''">{{ organizationStatus(organization) }}</span>
              </div>
              <span>{{ getOrganizationScenarioLabel(organization.primaryScenarioCode, organization.scenarioName ?? '', locale) }} · {{ getAdminRecordLabel('role', organization.roleCode, organization.roleName, locale) }}</span>
              <dl>
                <div><dt>{{ t('adminUsers.overview.participants') }}</dt><dd>{{ organization.membersCount }}</dd></div>
                <div><dt>{{ t('adminUsers.overview.equipment') }}</dt><dd>{{ organization.vehiclesCount }}</dd></div>
                <div><dt>{{ t('adminUsers.overview.clients') }}</dt><dd>{{ organization.clientsCount }}</dd></div>
                <div><dt>{{ t('adminUsers.overview.joined') }}</dt><dd>{{ formatDate(organization.joinedAt) }}</dd></div>
                <div v-if="commercialEnabled"><dt>{{ t('adminUsers.overview.currentPlan') }}</dt><dd>{{ organization.currentPlanName }}</dd></div>
                <div v-if="commercialEnabled && organization.currentPlanEndDate"><dt>{{ t('adminUsers.overview.validUntil') }}</dt><dd>{{ formatDate(organization.currentPlanEndDate) }}</dd></div>
              </dl>
              <div class="record-card__actions">
                <button class="secondary-button" type="button" :disabled="isLoadingOrganizationContents" @click="toggleOrganizationContents(organization.organizationId)">
                  {{ t(expandedOrganizationId === organization.organizationId ? 'adminUsers.overview.hideContents' : 'adminUsers.overview.showContents') }}
                </button>
                <template v-if="canManageBilling">
                  <button v-if="!usesFreePro(organization.primaryScenarioCode) && !organization.hasActiveSubscription && organization.isActive && !organization.isArchived && !organization.isDeleted" class="secondary-button" type="button" :disabled="isManagingOrganization" @click="openOrganizationConfirmation(organization, 'grant')">{{ t('adminUsers.overview.grantDemo') }}</button>
                  <button v-if="!organization.hasActiveSubscription && organization.isActive && !organization.isArchived && !organization.isDeleted" class="secondary-button" type="button" :disabled="isManagingOrganization" @click="openOrganizationConfirmation(organization, 'grant-plan')">{{ t(usesFreePro(organization.primaryScenarioCode) ? 'adminUsers.overview.grantPro' : 'adminUsers.overview.grantPlan') }}</button>
                  <button v-if="activeOrganizationSubscriptionsCount(organization.organizationId) === 1" class="secondary-button" type="button" :disabled="isManagingOrganization" @click="openOrganizationConfirmation(organization, 'extend')">{{ t('adminUsers.overview.extend') }}</button>
                  <button v-if="organization.currentPlanIsDemo" class="danger-button" type="button" :disabled="isManagingOrganization" @click="openOrganizationConfirmation(organization, 'revoke')">{{ t('adminUsers.overview.revokeDemo') }}</button>
                  <button v-if="organization.hasActiveSubscription" class="danger-button" type="button" :disabled="isManagingOrganization" @click="openOrganizationConfirmation(organization, 'stop')">{{ t('adminUsers.overview.stopAll') }}</button>
                </template>
              </div>
              <div v-if="commercialEnabled && overview.subscriptions.organizations.some(subscription => subscription.organizationId === organization.organizationId)" class="organization-subscriptions">
                <strong>{{ t('adminUsers.overview.organizationSubscriptions') }}</strong>
                <div v-for="subscription in overview.subscriptions.organizations.filter(item => item.organizationId === organization.organizationId)" :key="subscription.id" class="organization-subscriptions__row">
                  <span>{{ subscription.planName }} · {{ formatDate(subscription.startDate) }} — {{ formatDate(subscription.endDate) }}</span>
                  <span class="status-badge" :class="subscription.isActive ? 'status-badge--success' : ''">{{ subscription.isActive ? t('adminUsers.overview.active') : t('adminUsers.overview.expired') }}</span>
                </div>
              </div>
              <div v-if="expandedOrganizationId === organization.organizationId" class="organization-contents">
                <div v-if="isLoadingOrganizationContents" class="inline-empty">{{ t('adminUsers.overview.loadingContents') }}</div>
                <div v-else-if="organizationContentsError" class="form-message form-message--error">{{ organizationContentsError }}</div>
                <div v-else-if="organizationContents[organization.organizationId]" class="organization-contents__grid">
                  <div>
                    <strong>{{ t('adminUsers.overview.participants') }} ({{ organizationContents[organization.organizationId].members.length }})</strong>
                    <ul><li v-for="member in organizationContents[organization.organizationId].members" :key="member.userId">{{ member.name }} · {{ getAdminRecordLabel('role', undefined, member.roleName, locale) }}</li></ul>
                    <span v-if="!organizationContents[organization.organizationId].members.length">{{ t('adminUsers.overview.noParticipants') }}</span>
                  </div>
                  <div>
                    <strong>{{ t('adminUsers.overview.equipment') }} ({{ organizationContents[organization.organizationId].vehicles.length }})</strong>
                    <ul><li v-for="vehicle in organizationContents[organization.organizationId].vehicles" :key="vehicle.id">{{ vehicle.name }}<template v-if="vehicle.clientName"> · {{ vehicle.clientName }}</template></li></ul>
                    <span v-if="!organizationContents[organization.organizationId].vehicles.length">{{ t('adminUsers.overview.noEquipment') }}</span>
                  </div>
                  <div>
                    <strong>{{ t('adminUsers.overview.clients') }} ({{ organizationContents[organization.organizationId].clients.length }})</strong>
                    <ul><li v-for="client in organizationContents[organization.organizationId].clients" :key="client.id">{{ t('adminUsers.overview.clientVehicles', { name: client.name, count: client.vehiclesCount }) }}</li></ul>
                    <span v-if="!organizationContents[organization.organizationId].clients.length">{{ t('adminUsers.overview.noClients') }}</span>
                  </div>
                  <div>
                    <strong>{{ t('adminUsers.overview.recentMaintenances') }} ({{ organizationContents[organization.organizationId].maintenancesCount }})</strong>
                    <ul><li v-for="maintenance in organizationContents[organization.organizationId].recentMaintenances" :key="maintenance.id">{{ formatMaintenanceDate(maintenance.date) }} · {{ maintenance.vehicleName }} · {{ getAdminRecordLabel('maintenanceStatus', undefined, maintenance.statusName, locale) }}</li></ul>
                    <span v-if="organizationContents[organization.organizationId].maintenancesCount > organizationContents[organization.organizationId].recentMaintenances.length">{{ t('adminUsers.overview.lastTwenty') }}</span>
                    <span v-if="!organizationContents[organization.organizationId].recentMaintenances.length">{{ t('adminUsers.overview.noOrganizationMaintenances') }}</span>
                  </div>
                </div>
              </div>
            </article>
          </div>
          <div v-else class="inline-empty">{{ t('adminUsers.overview.noOrganizations') }}</div>
        </section>

        <section class="detail-panel">
          <div class="detail-panel__title">{{ t('adminUsers.overview.invitations') }}</div>
          <div v-if="overview.invitations.length" class="table-wrap">
            <table class="data-table">
              <thead><tr><th>{{ t('adminUsers.overview.organization') }}</th><th>{{ t('adminUsers.overview.role') }}</th><th>{{ t('adminUsers.overview.status') }}</th><th>{{ t('adminUsers.overview.invitedBy') }}</th><th>{{ t('adminUsers.overview.created') }}</th></tr></thead>
              <tbody>
                <tr v-for="invitation in overview.invitations" :key="invitation.id">
                  <td>{{ invitation.organizationName }}</td>
                  <td>{{ getAdminRecordLabel('role', invitation.roleCode, invitation.roleName, locale) }}</td>
                  <td>{{ invitation.statusCode }}</td>
                  <td>{{ invitation.invitedBy }}</td>
                  <td>{{ formatDateTime(invitation.createdAt) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div v-else class="inline-empty">{{ t('adminUsers.overview.noInvitations') }}</div>
        </section>
      </div>

      <div v-if="commercialEnabled && activeTab === 'organizations'" class="detail-stack">
        <section class="detail-panel">
          <div class="detail-panel__title">{{ t('adminUsers.overview.legacySubscriptions') }}</div>
          <div v-if="overview.subscriptions.users.length" class="record-grid">
            <article v-for="subscription in overview.subscriptions.users" :key="subscription.id" class="record-card">
              <div class="record-card__top">
                <strong>{{ subscription.planName }}</strong>
                <span class="status-badge" :class="subscription.isActive ? 'status-badge--success' : ''">
                  {{ subscription.isActive ? t('adminUsers.overview.active') : t('adminUsers.overview.expired') }}
                </span>
              </div>
              <dl>
                <div><dt>{{ t('adminUsers.overview.start') }}</dt><dd>{{ formatDate(subscription.startDate) }}</dd></div>
                <div><dt>{{ t('adminUsers.overview.end') }}</dt><dd>{{ formatDate(subscription.endDate) }}</dd></div>
                <div><dt>{{ t('adminUsers.overview.daysLeft') }}</dt><dd>{{ Math.max(0, subscription.daysLeft) }}</dd></div>
                <div><dt>{{ t('adminUsers.overview.autoRenewal') }}</dt><dd>{{ subscription.autoRenewal ? t('adminUsers.overview.yes') : t('adminUsers.overview.no') }}</dd></div>
              </dl>
              <div v-if="canManageBilling && subscription.id === activeUserSubscription?.id" class="record-card__actions">
                <button v-if="activeUserSubscriptionsCount === 1" class="secondary-button" type="button" @click="openPersonalSubscriptionAction('extend')">{{ t('adminUsers.overview.extend') }}</button>
                <button class="danger-button" type="button" @click="openPersonalSubscriptionAction('default-plan')">{{ t('adminUsers.overview.stopAll') }}</button>
              </div>
            </article>
          </div>
          <div v-else class="inline-empty">{{ t('adminUsers.overview.noSubscriptions') }}</div>
        </section>

        <section class="detail-panel">
          <div class="detail-panel__title">{{ t('adminUsers.overview.subscriptionHistory') }}</div>
          <div v-if="overview.subscriptions.history.length" class="table-wrap">
            <table class="data-table">
              <thead><tr><th>{{ t('adminUsers.overview.plan') }}</th><th>{{ t('adminUsers.overview.status') }}</th><th>{{ t('adminUsers.overview.start') }}</th><th>{{ t('adminUsers.overview.end') }}</th><th>{{ t('adminUsers.overview.comment') }}</th></tr></thead>
              <tbody>
                <tr v-for="history in overview.subscriptions.history" :key="history.id">
                  <td>{{ history.planName }}</td>
                  <td>{{ history.status }}</td>
                  <td>{{ formatDate(history.startDate) }}</td>
                  <td>{{ formatDate(history.actualEndDate || history.endDate) }}</td>
                  <td>{{ history.comment || '—' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div v-else class="inline-empty">{{ t('adminUsers.overview.noHistory') }}</div>
        </section>
      </div>

      <section v-if="commercialEnabled && activeTab === 'payments'" class="detail-panel">
        <div class="detail-panel__title">{{ t('adminUsers.overview.payment') }}</div>
        <div v-if="overview.payments.length" class="table-wrap">
          <table class="data-table">
            <thead><tr><th>{{ t('adminUsers.overview.date') }}</th><th>{{ t('adminUsers.overview.plan') }}</th><th>{{ t('adminUsers.overview.amount') }}</th><th>{{ t('adminUsers.overview.method') }}</th><th>{{ t('adminUsers.overview.status') }}</th><th>{{ t('adminUsers.overview.transaction') }}</th></tr></thead>
            <tbody>
              <tr v-for="payment in overview.payments" :key="payment.id">
                <td>{{ formatDateTime(payment.paymentDate) }}</td>
                <td>{{ payment.planName }}</td>
                <td>{{ formatMoney(payment.amount) }}</td>
                <td>{{ getAdminRecordLabel('paymentMethod', payment.paymentMethod, payment.paymentMethodName, locale) }}</td>
                <td><span class="status-badge" :class="`status-badge--${payment.paymentStatusGroup}`">{{ getAdminRecordLabel('paymentStatus', payment.paymentStatus, payment.paymentStatusName, locale) }}</span></td>
                <td>{{ payment.transactionId || payment.externalPaymentId || '—' }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div v-else class="inline-empty">{{ t('adminUsers.overview.noPayments') }}</div>
      </section>

      <section v-if="activeTab === 'support'" class="detail-panel">
        <div class="detail-panel__title">{{ t('adminUsers.overview.support') }}</div>
        <div v-if="overview.supportMessages.length" class="record-grid">
          <article v-for="message in overview.supportMessages" :key="message.id" class="record-card">
            <div class="record-card__top">
              <strong>{{ message.topic || t('adminUsers.overview.noSubject') }}</strong>
              <span class="soft-badge">{{ getAdminRecordLabel('supportStatus', message.statusCode, message.statusName, locale) }}</span>
            </div>
            <span>{{ getAdminRecordLabel('supportType', message.typeCode, message.typeName, locale) }} · {{ formatDateTime(message.createdAt) }}</span>
            <p>{{ message.text }}</p>
            <p v-if="message.adminComment"><strong>{{ t('adminUsers.overview.comment') }}:</strong> {{ message.adminComment }}</p>
          </article>
        </div>
        <div v-else class="inline-empty">{{ t('adminUsers.overview.noSupport') }}</div>
      </section>

      <section v-if="commercialEnabled && activeTab === 'features'" class="detail-panel">
        <div class="detail-panel__title">{{ t('adminUsers.overview.individualFeatures') }}</div>
        <div v-if="overview.featureGrants.length" class="record-grid">
          <article v-for="grant in overview.featureGrants" :key="grant.id" class="record-card">
            <div class="record-card__top">
              <strong>{{ grant.featureName }}</strong>
              <span class="status-badge" :class="grant.isActive ? 'status-badge--success' : ''">
                {{ grant.isActive ? t('adminUsers.overview.active') : t('adminUsers.overview.inactive') }}
              </span>
            </div>
            <span>{{ grant.featureCode }}</span>
            <dl>
              <div><dt>{{ t('adminUsers.overview.value') }}</dt><dd>{{ formatFeatureValue(grant) }}</dd></div>
              <div><dt>{{ t('adminUsers.overview.source') }}</dt><dd>{{ grant.source || t('adminUsers.overview.manualGrant') }}</dd></div>
              <div><dt>{{ t('adminUsers.overview.start') }}</dt><dd>{{ formatDate(grant.startDate) }}</dd></div>
              <div><dt>{{ t('adminUsers.overview.end') }}</dt><dd>{{ formatDate(grant.endDate) }}</dd></div>
            </dl>
            <p v-if="grant.comment">{{ grant.comment }}</p>
          </article>
        </div>
        <div v-else class="inline-empty">{{ t('adminUsers.overview.noFeatures') }}</div>
      </section>
    </template>

    <div v-if="isImpersonationModalOpen && profile" class="modal-backdrop" @click.self="isImpersonationModalOpen = false">
      <form class="modal-card modal-card--compact" @submit.prevent="impersonate">
        <div class="modal-heading">
          <strong>{{ t('adminUsers.overview.impersonationTitle') }}</strong>
          <button type="button" :disabled="isImpersonating" @click="isImpersonationModalOpen = false">×</button>
        </div>
        <p>{{ profile.email || profile.phone || profile.id }}</p>
        <div v-if="impersonationError" class="form-message form-message--error">{{ impersonationError }}</div>
        <div class="modal-actions">
          <button class="primary-button" type="submit" :disabled="isImpersonating">
            {{ isImpersonating ? t('adminUsers.overview.signingIn') : t('adminUsers.overview.continue') }}
          </button>
          <button class="secondary-button" type="button" :disabled="isImpersonating" @click="isImpersonationModalOpen = false">
            {{ t('adminUsers.overview.cancel') }}
          </button>
        </div>
      </form>
    </div>

    <div v-if="commercialEnabled && organizationConfirmation" class="modal-backdrop" @click.self="!isManagingOrganization && (organizationConfirmation = null)">
      <div class="modal-card modal-card--compact">
        <div class="modal-heading">
          <strong>{{ organizationActionTitle(organizationConfirmation.action) }}</strong>
          <button type="button" :disabled="isManagingOrganization" @click="organizationConfirmation = null">×</button>
        </div>
        <p>{{ organizationConfirmation.organization.organizationName || t('adminUsers.overview.noName') }}</p>
        <p v-if="organizationConfirmation.action === 'stop'">{{ t('adminUsers.overview.stopOrgWarning') }}</p>
        <p v-if="organizationConfirmation.action === 'stop' || organizationConfirmation.action === 'revoke'">{{ subscriptionFallbackText(organizationConfirmation.organization.primaryScenarioCode) }}</p>
        <p v-if="organizationConfirmation.action === 'revoke'">{{ t('adminUsers.overview.revokeOrgWarning') }}</p>
        <template v-if="organizationConfirmation.action === 'grant-plan'">
          <label><span>{{ t('adminUsers.overview.plan') }}</span><SearchSelect v-model="organizationPlanId" :options="[{ value: 0, label: t('adminUsers.overview.selectPlan') }, ...businessPlans.map(plan => ({ value: plan.id, label: plan.name }))]" /></label>
          <label><span>{{ t('adminUsers.overview.termDays') }}</span><input v-model.number="organizationPlanDays" type="number" min="1" max="366" required></label>
          <p>{{ t('adminUsers.overview.planStartsAndEnds', { date: organizationPlanEndDate ? formatDateTime(organizationPlanEndDate) : t('adminUsers.overview.choosePlanAndTerm') }) }}</p>
        </template>
        <template v-if="organizationConfirmation.action === 'extend'">
          <label><span>{{ t('adminUsers.overview.extendByDays') }}</span><input v-model.number="organizationPlanDays" type="number" min="1" max="366" required></label>
          <p>{{ t('adminUsers.overview.newEndDate', { date: organizationExtensionEndDate ? formatDateTime(organizationExtensionEndDate) : t('adminUsers.overview.specifyTerm') }) }}</p>
        </template>
        <label><span>{{ t('adminUsers.overview.reason') }}</span><textarea v-model="organizationActionReason" maxlength="500" rows="3" required></textarea></label>
        <div v-if="organizationError" class="form-message form-message--error">{{ organizationError }}</div>
        <div class="modal-actions">
          <button :class="organizationConfirmation.action === 'stop' || organizationConfirmation.action === 'revoke' ? 'danger-button' : 'primary-button'" type="button" :disabled="isManagingOrganization || !organizationActionReason.trim() || (organizationConfirmation.action === 'grant-plan' && !organizationPlanEndDate) || (organizationConfirmation.action === 'extend' && !organizationExtensionEndDate)" @click="confirmOrganizationAction">
            {{ isManagingOrganization ? t('adminUsers.overview.saving') : t('adminUsers.overview.confirm') }}
          </button>
          <button class="secondary-button" type="button" :disabled="isManagingOrganization" @click="organizationConfirmation = null">{{ t('adminUsers.overview.cancel') }}</button>
        </div>
      </div>
    </div>

    <div v-if="commercialEnabled && isManagementModalOpen && profile" class="modal-backdrop" @click.self="isManagementModalOpen = false">
      <div class="modal-card modal-card--compact">
        <div class="modal-heading">
          <strong>{{ t('adminUsers.overview.managingUser') }}</strong>
          <button type="button" :disabled="isManaging" @click="isManagementModalOpen = false">×</button>
        </div>
        <div v-if="managementMessage" class="form-message form-message--success">{{ managementMessage }}</div>
        <div v-if="managementError" class="form-message form-message--error">{{ managementError }}</div>
        <div v-if="managementConfirmation" class="user-management-confirmation">
          <strong>{{ managementConfirmationTitle(managementConfirmation) }}</strong>
          <p v-if="managementConfirmation === 'demo'">{{ t('adminUsers.overview.demoDurationInfo') }}</p>
          <p v-if="managementConfirmation === 'default-plan'">{{ t('adminUsers.overview.stopPersonalWarning') }}</p>
          <template v-if="managementConfirmation === 'grant-plan'">
            <label><span>{{ t('adminUsers.overview.plan') }}</span><SearchSelect v-model="managementPlanId" :options="[{ value: 0, label: t('adminUsers.overview.selectPlan') }, ...personalPlans.map(plan => ({ value: plan.id, label: plan.name }))]" /></label>
            <label><span>{{ t('adminUsers.overview.termDays') }}</span><input v-model.number="managementPlanDays" type="number" min="1" max="366" required></label>
            <p>{{ t('adminUsers.overview.planStartsAndEnds', { date: managementPlanEndDate ? formatDateTime(managementPlanEndDate) : t('adminUsers.overview.choosePlanAndTerm') }) }}</p>
          </template>
          <template v-if="managementConfirmation === 'extend'">
            <label><span>{{ t('adminUsers.overview.extendByDays') }}</span><input v-model.number="managementPlanDays" type="number" min="1" max="366" required></label>
            <p>{{ t('adminUsers.overview.newEndDate', { date: managementExtensionEndDate ? formatDateTime(managementExtensionEndDate) : t('adminUsers.overview.specifyTerm') }) }}</p>
          </template>
          <label><span>{{ t('adminUsers.overview.reason') }}</span><textarea v-model="managementReason" maxlength="500" rows="3" required></textarea></label>
          <div class="modal-actions">
            <button class="primary-button" type="button" :disabled="isManaging || !managementReason.trim() || (managementConfirmation === 'grant-plan' && !managementPlanEndDate) || (managementConfirmation === 'extend' && !managementExtensionEndDate)" @click="confirmManagementAction">{{ t('adminUsers.overview.confirm') }}</button>
            <button class="secondary-button" type="button" :disabled="isManaging" @click="managementConfirmation = null">{{ t('adminUsers.overview.cancel') }}</button>
          </div>
        </div>
        <div v-else class="user-management-actions">
          <button v-if="canManage" class="secondary-button" type="button" :disabled="isManaging" @click="toggleTestSubscriptions">
            {{ t(profile.canUseTestSubscriptions ? 'adminUsers.overview.disableTestPlans' : 'adminUsers.overview.allowTestPlans') }}
          </button>
          <button v-if="canManageBilling" class="secondary-button" type="button" :disabled="isManaging || hasActiveUserSubscription" @click="managementConfirmation = 'demo'">
            {{ t('adminUsers.overview.legacyPersonalDemo') }}
          </button>
          <button v-if="canManageBilling" class="secondary-button" type="button" :disabled="isManaging || hasActiveUserSubscription" @click="managementConfirmation = 'grant-plan'">
            {{ t('adminUsers.overview.grantPersonalPlan') }}
          </button>
          <button v-if="canManageBilling && hasActiveUserDemo" class="danger-button" type="button" :disabled="isManaging" @click="managementConfirmation = 'revoke-demo'">
            {{ t('adminUsers.overview.revokePersonalDemo') }}
          </button>
          <button v-if="canManageBilling && activeUserSubscriptionsCount === 1" class="secondary-button" type="button" :disabled="isManaging" @click="managementConfirmation = 'extend'">
            {{ t('adminUsers.overview.extendPersonal') }}
          </button>
          <button v-if="canManageBilling" class="danger-button" type="button" :disabled="isManaging || !hasActiveUserSubscription" @click="managementConfirmation = 'default-plan'">
            {{ t('adminUsers.overview.finishPersonal') }}
          </button>
        </div>
        <div class="modal-actions">
          <button class="secondary-button" type="button" :disabled="isManaging" @click="isManagementModalOpen = false">{{ t('adminUsers.overview.close') }}</button>
        </div>
      </div>
    </div>
  </section>
</template>

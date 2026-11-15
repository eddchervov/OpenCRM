<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ApiError } from '@/api/ApiError'
import { usesFreePro, subscriptionFallback } from '@/utils/subscriptionPolicy'
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

type TabName = 'overview' | 'vehicles' | 'organizations' | 'payments' | 'support' | 'features'

const route = useRoute()
const currentUserStore = useAdminCurrentUserStore()
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
  { value: 'overview', label: 'Обзор' },
  { value: 'vehicles', label: 'Техника и ТО' },
  { value: 'organizations', label: 'Организации и подписки' },
  { value: 'payments', label: 'Платежи' },
  { value: 'support', label: 'Обращения' },
  { value: 'features', label: 'Возможности' }
]
const visibleTabs = computed(() => tabs
  .filter(tab => commercialEnabled.value || (tab.value !== 'payments' && tab.value !== 'features'))
  .map(tab => tab.value === 'organizations' && !commercialEnabled.value ? { ...tab, label: 'Организации' } : tab))

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
    errorMessage.value = error instanceof ApiError
      ? error.message
      : 'Не удалось загрузить карточку пользователя.'
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
    impersonationError.value = error instanceof ApiError
      ? error.message
      : 'Не удалось войти от имени пользователя.'
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
    managementError.value = error instanceof ApiError ? error.message : 'Не удалось загрузить доступные тарифы.'
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
    organizationContentsError.value = error instanceof ApiError ? error.message : 'Не удалось загрузить содержимое организации.'
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
    managementError.value = error instanceof ApiError ? error.message : 'Не удалось выполнить операцию.'
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
    organizationError.value = error instanceof ApiError ? error.message : 'Не удалось изменить подписку организации.'
  } finally {
    isManagingOrganization.value = false
  }
}

function formatDate(value: string | null): string {
  if (!value) {
    return '—'
  }

  return new Intl.DateTimeFormat('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric'
  }).format(new Date(value))
}

function formatDateTime(value: string | null): string {
  if (!value || new Date(value).getFullYear() <= 1) {
    return '—'
  }

  return new Intl.DateTimeFormat('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  }).format(new Date(value))
}

function formatMoney(value: number | null): string {
  if (value === null) {
    return '—'
  }

  return new Intl.NumberFormat('ru-RU', {
    style: 'currency',
    currency: 'RUB',
    maximumFractionDigits: 0
  }).format(value)
}

function formatFeatureValue(grant: AdminUserFeatureGrant): string {
  if (grant.valueType.toLowerCase().includes('bool')) {
    return grant.boolValue ? 'Разрешено' : 'Запрещено'
  }

  return grant.intValue?.toString() ?? 'Без значения'
}
</script>

<template>
  <section class="user-overview-page">
    <div class="user-overview-heading">
      <div>
        <RouterLink class="back-link" :to="{ name: 'users' }">← Пользователи</RouterLink>
        <h1>Карточка 360</h1>
      </div>
      <div class="user-overview-actions">
        <button
          v-if="commercialEnabled && (canManage || canManageBilling) && profile && !profile.isDeleted"
          class="secondary-button"
          type="button"
          @click="openManagementModal"
        >
          Управление
        </button>
        <button
          v-if="canImpersonate && profile && !profile.isDeleted && profile.id !== currentUserStore.currentUser?.id"
          class="primary-button"
          type="button"
          @click="openImpersonationModal"
        >
          Войти как пользователь
        </button>
        <button class="secondary-button" type="button" :disabled="isLoading" @click="loadUser">
          Обновить
        </button>
      </div>
    </div>

    <div v-if="isLoading && !overview" class="panel-state">
      Загружаем карточку пользователя…
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
          <strong>{{ profile.email || 'Email не указан' }}</strong>
          <span>{{ profile.phone || profile.id }}</span>
        </div>
        <div class="user-hero__badges">
          <span class="status-badge" :class="profile.isDeleted ? 'status-badge--danger' : 'status-badge--success'">
            {{ profile.isDeleted ? 'Удалён' : 'Активен' }}
          </span>
          <span v-for="role in profile.roles" :key="role" class="soft-badge">{{ role }}</span>
        </div>
      </div>

      <div class="kpi-grid">
        <div><span>Техника</span><strong>{{ overview.kpis.vehiclesTotal }}</strong></div>
        <div><span>Обслуживания</span><strong>{{ overview.kpis.maintenancesTotal }}</strong></div>
        <div><span>Организации</span><strong>{{ overview.kpis.organizationsTotal }}</strong></div>
        <div v-if="commercialEnabled"><span>Сумма платежей</span><strong>{{ formatMoney(overview.kpis.paymentsAmount) }}</strong></div>
        <div v-if="commercialEnabled"><span>Активные подписки</span><strong>{{ overview.kpis.activeUserSubscriptions + overview.kpis.activeOrganizationSubscriptions }}</strong></div>
        <div><span>Открытые обращения</span><strong>{{ overview.kpis.supportOpen }}</strong></div>
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
          <div class="detail-panel__title">Основная информация</div>
          <dl class="detail-list">
            <div><dt>Email</dt><dd>{{ profile.email || '—' }}</dd></div>
            <div><dt>Email подтверждён</dt><dd>{{ profile.emailConfirmed ? 'Да' : 'Нет' }}</dd></div>
            <div><dt>Телефон</dt><dd>{{ profile.phone || '—' }}</dd></div>
            <div><dt>Телефон подтверждён</dt><dd>{{ profile.phoneNumberConfirmed ? 'Да' : 'Нет' }}</dd></div>
            <div><dt>Регистрация</dt><dd>{{ formatDateTime(profile.datetimeCreation) }}</dd></div>
            <div><dt>Пароль</dt><dd>{{ profile.hasPassword ? 'Установлен' : 'Не установлен' }}</dd></div>
          </dl>
        </section>

        <section class="detail-panel">
          <div class="detail-panel__title">Безопасность и согласия</div>
          <dl class="detail-list">
            <div><dt>Блокировка разрешена</dt><dd>{{ profile.lockoutEnabled ? 'Да' : 'Нет' }}</dd></div>
            <div><dt>Заблокирован до</dt><dd>{{ formatDateTime(profile.lockoutEnd) }}</dd></div>
            <div><dt>Неудачных входов</dt><dd>{{ profile.accessFailedCount }}</dd></div>
            <div><dt>Обработка данных</dt><dd>{{ profile.personalDataProcessingConsentAccepted ? 'Принято' : 'Не принято' }}</dd></div>
            <div><dt>Дата согласия</dt><dd>{{ formatDateTime(profile.personalDataProcessingConsentAcceptedAt) }}</dd></div>
            <div v-if="commercialEnabled"><dt>Тестовые тарифы</dt><dd>{{ profile.canUseTestSubscriptions ? 'Разрешены' : 'Не разрешены' }}</dd></div>
          </dl>
        </section>

        <section class="detail-panel detail-panel--wide">
          <div class="detail-panel__title">История</div>
          <div v-if="overview.timeline.length" class="timeline">
            <div v-for="item in overview.timeline" :key="`${item.type}-${item.key}-${item.date}`">
              <span class="timeline__dot"></span>
              <strong>{{ item.label }}</strong>
              <span>{{ item.title }}</span>
              <time>{{ formatDateTime(item.date) }}</time>
            </div>
          </div>
          <div v-else class="inline-empty">Событий нет.</div>
        </section>
      </div>

      <div v-if="activeTab === 'vehicles'" class="detail-stack">
        <section class="detail-panel">
          <div class="detail-panel__title">Техника</div>
          <div v-if="overview.vehicles.length" class="record-grid">
            <article v-for="vehicle in overview.vehicles" :key="vehicle.id" class="record-card">
              <div class="record-card__top">
                <strong>{{ vehicle.title }}</strong>
                <span class="soft-badge">{{ vehicle.isArchived ? 'Архив' : 'Активна' }}</span>
              </div>
              <span>{{ [vehicle.brand, vehicle.model, vehicle.yearIssue].filter(Boolean).join(' · ') || vehicle.kindName }}</span>
              <dl>
                <div><dt>Номер / VIN</dt><dd>{{ vehicle.registrationPlate || vehicle.vin || '—' }}</dd></div>
                <div><dt>Организация</dt><dd>{{ vehicle.organizationName || 'Личная' }}</dd></div>
                <div><dt>Обслуживания</dt><dd>{{ vehicle.maintenancesCount }}</dd></div>
                <div><dt>Открытые дефекты</dt><dd>{{ vehicle.openDefectsCount }}</dd></div>
              </dl>
            </article>
          </div>
          <div v-else class="inline-empty">Техники нет.</div>
        </section>

        <section class="detail-panel">
          <div class="detail-panel__title">Последние обслуживания</div>
          <div v-if="overview.maintenances.length" class="table-wrap">
            <table class="data-table">
              <thead><tr><th>Дата</th><th>Техника</th><th>Тип</th><th>Статус</th><th>Стоимость</th></tr></thead>
              <tbody>
                <tr v-for="maintenance in overview.maintenances" :key="maintenance.id">
                  <td>{{ formatDate(maintenance.date) }}</td>
                  <td>{{ maintenance.vehicleTitle }}</td>
                  <td>{{ maintenance.typeName }}</td>
                  <td>{{ maintenance.statusName }}</td>
                  <td>{{ formatMoney(maintenance.paidAmount) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div v-else class="inline-empty">Обслуживаний нет.</div>
        </section>
      </div>

      <div v-if="activeTab === 'organizations'" class="detail-stack">
        <section class="detail-panel">
          <div class="detail-panel__title">{{ commercialEnabled ? 'Организации и тарифы' : 'Организации' }}</div>
          <p v-if="commercialEnabled" class="muted">Free и PRO относятся к конкретной организации. Личная техника доступна каждому пользователю. Подарочный PRO подключается промокодом; автоматического демо нет.</p>
          <div v-if="organizationMessage" class="form-message form-message--success">{{ organizationMessage }}</div>
          <div v-if="overview.organizations.length" class="organization-cards">
            <article v-for="organization in overview.organizations" :key="organization.organizationId" class="record-card">
              <div class="record-card__top">
                <strong>{{ organization.organizationName || 'Без названия' }}</strong>
                <span v-if="commercialEnabled" class="status-badge" :class="organization.hasActiveSubscription ? 'status-badge--success' : ''">
                  {{ organization.currentPlanIsDemo ? 'Демо активно' : organization.hasActiveSubscription ? usesFreePro(organization.primaryScenarioCode) ? 'PRO активен' : 'Тариф активен' : organization.isActive && !organization.isArchived && !organization.isDeleted ? usesFreePro(organization.primaryScenarioCode) ? 'Free · бессрочно' : 'Базовый тариф' : 'Неактивна' }}
                </span>
                <span v-else class="status-badge" :class="organization.isActive && !organization.isArchived && !organization.isDeleted ? 'status-badge--success' : ''">{{ organization.isDeleted ? 'Удалена' : organization.isArchived ? 'В архиве' : organization.isActive ? 'Активна' : 'Неактивна' }}</span>
              </div>
              <span>{{ organization.scenarioName }} · {{ organization.roleName }}</span>
              <dl>
                <div><dt>Участники</dt><dd>{{ organization.membersCount }}</dd></div>
                <div><dt>Техника</dt><dd>{{ organization.vehiclesCount }}</dd></div>
                <div><dt>Клиенты</dt><dd>{{ organization.clientsCount }}</dd></div>
                <div><dt>Вступил</dt><dd>{{ formatDate(organization.joinedAt) }}</dd></div>
                <div v-if="commercialEnabled"><dt>Текущий тариф</dt><dd>{{ organization.currentPlanName }}</dd></div>
                <div v-if="commercialEnabled && organization.currentPlanEndDate"><dt>Действует до</dt><dd>{{ formatDate(organization.currentPlanEndDate) }}</dd></div>
              </dl>
              <div class="record-card__actions">
                <button class="secondary-button" type="button" :disabled="isLoadingOrganizationContents" @click="toggleOrganizationContents(organization.organizationId)">
                  {{ expandedOrganizationId === organization.organizationId ? 'Скрыть состав' : 'Показать состав' }}
                </button>
                <template v-if="canManageBilling">
                  <button v-if="!usesFreePro(organization.primaryScenarioCode) && !organization.hasActiveSubscription && organization.isActive && !organization.isArchived && !organization.isDeleted" class="secondary-button" type="button" :disabled="isManagingOrganization" @click="openOrganizationConfirmation(organization, 'grant')">Выдать демо</button>
                  <button v-if="!organization.hasActiveSubscription && organization.isActive && !organization.isArchived && !organization.isDeleted" class="secondary-button" type="button" :disabled="isManagingOrganization" @click="openOrganizationConfirmation(organization, 'grant-plan')">{{ usesFreePro(organization.primaryScenarioCode) ? 'Выдать PRO без оплаты' : 'Выдать тариф без оплаты' }}</button>
                  <button v-if="activeOrganizationSubscriptionsCount(organization.organizationId) === 1" class="secondary-button" type="button" :disabled="isManagingOrganization" @click="openOrganizationConfirmation(organization, 'extend')">Продлить</button>
                  <button v-if="organization.currentPlanIsDemo" class="danger-button" type="button" :disabled="isManagingOrganization" @click="openOrganizationConfirmation(organization, 'revoke')">Отозвать демо</button>
                  <button v-if="organization.hasActiveSubscription" class="danger-button" type="button" :disabled="isManagingOrganization" @click="openOrganizationConfirmation(organization, 'stop')">Остановить все действующие</button>
                </template>
              </div>
              <div v-if="commercialEnabled && overview.subscriptions.organizations.some(subscription => subscription.organizationId === organization.organizationId)" class="organization-subscriptions">
                <strong>Подписки организации</strong>
                <div v-for="subscription in overview.subscriptions.organizations.filter(item => item.organizationId === organization.organizationId)" :key="subscription.id" class="organization-subscriptions__row">
                  <span>{{ subscription.planName }} · {{ formatDate(subscription.startDate) }} — {{ formatDate(subscription.endDate) }}</span>
                  <span class="status-badge" :class="subscription.isActive ? 'status-badge--success' : ''">{{ subscription.isActive ? 'Активна' : 'Истекла' }}</span>
                </div>
              </div>
              <div v-if="expandedOrganizationId === organization.organizationId" class="organization-contents">
                <div v-if="isLoadingOrganizationContents" class="inline-empty">Загрузка состава…</div>
                <div v-else-if="organizationContentsError" class="form-message form-message--error">{{ organizationContentsError }}</div>
                <div v-else-if="organizationContents[organization.organizationId]" class="organization-contents__grid">
                  <div>
                    <strong>Участники ({{ organizationContents[organization.organizationId].members.length }})</strong>
                    <ul><li v-for="member in organizationContents[organization.organizationId].members" :key="member.userId">{{ member.name }} · {{ member.roleName }}</li></ul>
                    <span v-if="!organizationContents[organization.organizationId].members.length">Нет участников.</span>
                  </div>
                  <div>
                    <strong>Техника ({{ organizationContents[organization.organizationId].vehicles.length }})</strong>
                    <ul><li v-for="vehicle in organizationContents[organization.organizationId].vehicles" :key="vehicle.id">{{ vehicle.name }}<template v-if="vehicle.clientName"> · {{ vehicle.clientName }}</template></li></ul>
                    <span v-if="!organizationContents[organization.organizationId].vehicles.length">Нет техники.</span>
                  </div>
                  <div>
                    <strong>Клиенты ({{ organizationContents[organization.organizationId].clients.length }})</strong>
                    <ul><li v-for="client in organizationContents[organization.organizationId].clients" :key="client.id">{{ client.name }} · техники: {{ client.vehiclesCount }}</li></ul>
                    <span v-if="!organizationContents[organization.organizationId].clients.length">Нет клиентов.</span>
                  </div>
                  <div>
                    <strong>Последние обслуживания ({{ organizationContents[organization.organizationId].maintenancesCount }})</strong>
                    <ul><li v-for="maintenance in organizationContents[organization.organizationId].recentMaintenances" :key="maintenance.id">{{ formatDate(maintenance.date) }} · {{ maintenance.vehicleName }} · {{ maintenance.statusName }}</li></ul>
                    <span v-if="organizationContents[organization.organizationId].maintenancesCount > organizationContents[organization.organizationId].recentMaintenances.length">Показаны 20 последних.</span>
                    <span v-if="!organizationContents[organization.organizationId].recentMaintenances.length">Обслуживаний нет.</span>
                  </div>
                </div>
              </div>
            </article>
          </div>
          <div v-else class="inline-empty">Организаций нет.</div>
        </section>

        <section class="detail-panel">
          <div class="detail-panel__title">Приглашения</div>
          <div v-if="overview.invitations.length" class="table-wrap">
            <table class="data-table">
              <thead><tr><th>Организация</th><th>Роль</th><th>Статус</th><th>Пригласил</th><th>Создано</th></tr></thead>
              <tbody>
                <tr v-for="invitation in overview.invitations" :key="invitation.id">
                  <td>{{ invitation.organizationName }}</td>
                  <td>{{ invitation.roleName }}</td>
                  <td>{{ invitation.statusCode }}</td>
                  <td>{{ invitation.invitedBy }}</td>
                  <td>{{ formatDateTime(invitation.createdAt) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <div v-else class="inline-empty">Приглашений нет.</div>
        </section>
      </div>

      <div v-if="commercialEnabled && activeTab === 'organizations'" class="detail-stack">
        <section class="detail-panel">
          <div class="detail-panel__title">Личные подписки · прежняя модель</div>
          <div v-if="overview.subscriptions.users.length" class="record-grid">
            <article v-for="subscription in overview.subscriptions.users" :key="subscription.id" class="record-card">
              <div class="record-card__top">
                <strong>{{ subscription.planName }}</strong>
                <span class="status-badge" :class="subscription.isActive ? 'status-badge--success' : ''">
                  {{ subscription.isActive ? 'Активна' : 'Истекла' }}
                </span>
              </div>
              <dl>
                <div><dt>Начало</dt><dd>{{ formatDate(subscription.startDate) }}</dd></div>
                <div><dt>Окончание</dt><dd>{{ formatDate(subscription.endDate) }}</dd></div>
                <div><dt>Осталось дней</dt><dd>{{ Math.max(0, subscription.daysLeft) }}</dd></div>
                <div><dt>Автопродление</dt><dd>{{ subscription.autoRenewal ? 'Да' : 'Нет' }}</dd></div>
              </dl>
              <div v-if="canManageBilling && subscription.id === activeUserSubscription?.id" class="record-card__actions">
                <button v-if="activeUserSubscriptionsCount === 1" class="secondary-button" type="button" @click="openPersonalSubscriptionAction('extend')">Продлить</button>
                <button class="danger-button" type="button" @click="openPersonalSubscriptionAction('default-plan')">Остановить все действующие</button>
              </div>
            </article>
          </div>
          <div v-else class="inline-empty">Подписок нет.</div>
        </section>

        <section class="detail-panel">
          <div class="detail-panel__title">История пользовательских подписок</div>
          <div v-if="overview.subscriptions.history.length" class="table-wrap">
            <table class="data-table">
              <thead><tr><th>Тариф</th><th>Статус</th><th>Начало</th><th>Окончание</th><th>Комментарий</th></tr></thead>
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
          <div v-else class="inline-empty">Истории нет.</div>
        </section>
      </div>

      <section v-if="commercialEnabled && activeTab === 'payments'" class="detail-panel">
        <div class="detail-panel__title">Платежи</div>
        <div v-if="overview.payments.length" class="table-wrap">
          <table class="data-table">
            <thead><tr><th>Дата</th><th>Тариф</th><th>Сумма</th><th>Способ</th><th>Статус</th><th>Транзакция</th></tr></thead>
            <tbody>
              <tr v-for="payment in overview.payments" :key="payment.id">
                <td>{{ formatDateTime(payment.paymentDate) }}</td>
                <td>{{ payment.planName }}</td>
                <td>{{ formatMoney(payment.amount) }}</td>
                <td>{{ payment.paymentMethodName }}</td>
                <td><span class="status-badge" :class="`status-badge--${payment.paymentStatusGroup}`">{{ payment.paymentStatusName }}</span></td>
                <td>{{ payment.transactionId || payment.externalPaymentId || '—' }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div v-else class="inline-empty">Платежей нет.</div>
      </section>

      <section v-if="activeTab === 'support'" class="detail-panel">
        <div class="detail-panel__title">Обращения</div>
        <div v-if="overview.supportMessages.length" class="record-grid">
          <article v-for="message in overview.supportMessages" :key="message.id" class="record-card">
            <div class="record-card__top">
              <strong>{{ message.topic || 'Без темы' }}</strong>
              <span class="soft-badge">{{ message.statusName }}</span>
            </div>
            <span>{{ message.typeName }} · {{ formatDateTime(message.createdAt) }}</span>
            <p>{{ message.text }}</p>
            <p v-if="message.adminComment"><strong>Комментарий:</strong> {{ message.adminComment }}</p>
          </article>
        </div>
        <div v-else class="inline-empty">Обращений нет.</div>
      </section>

      <section v-if="commercialEnabled && activeTab === 'features'" class="detail-panel">
        <div class="detail-panel__title">Индивидуальные возможности</div>
        <div v-if="overview.featureGrants.length" class="record-grid">
          <article v-for="grant in overview.featureGrants" :key="grant.id" class="record-card">
            <div class="record-card__top">
              <strong>{{ grant.featureName }}</strong>
              <span class="status-badge" :class="grant.isActive ? 'status-badge--success' : ''">
                {{ grant.isActive ? 'Активна' : 'Неактивна' }}
              </span>
            </div>
            <span>{{ grant.featureCode }}</span>
            <dl>
              <div><dt>Значение</dt><dd>{{ formatFeatureValue(grant) }}</dd></div>
              <div><dt>Источник</dt><dd>{{ grant.source || 'Ручная выдача' }}</dd></div>
              <div><dt>Начало</dt><dd>{{ formatDate(grant.startDate) }}</dd></div>
              <div><dt>Окончание</dt><dd>{{ formatDate(grant.endDate) }}</dd></div>
            </dl>
            <p v-if="grant.comment">{{ grant.comment }}</p>
          </article>
        </div>
        <div v-else class="inline-empty">Индивидуальных возможностей нет.</div>
      </section>
    </template>

    <div v-if="isImpersonationModalOpen && profile" class="modal-backdrop" @click.self="isImpersonationModalOpen = false">
      <form class="modal-card modal-card--compact" @submit.prevent="impersonate">
        <div class="modal-heading">
          <strong>Войти как пользователь</strong>
          <button type="button" :disabled="isImpersonating" @click="isImpersonationModalOpen = false">×</button>
        </div>
        <p>{{ profile.email || profile.phone || profile.id }}</p>
        <div v-if="impersonationError" class="form-message form-message--error">{{ impersonationError }}</div>
        <div class="modal-actions">
          <button class="primary-button" type="submit" :disabled="isImpersonating">
            {{ isImpersonating ? 'Входим…' : 'Продолжить' }}
          </button>
          <button class="secondary-button" type="button" :disabled="isImpersonating" @click="isImpersonationModalOpen = false">
            Отмена
          </button>
        </div>
      </form>
    </div>

    <div v-if="commercialEnabled && organizationConfirmation" class="modal-backdrop" @click.self="!isManagingOrganization && (organizationConfirmation = null)">
      <div class="modal-card modal-card--compact">
        <div class="modal-heading">
          <strong>{{ organizationConfirmation.action === 'grant-plan' ? 'Выдать тариф организации без оплаты' : organizationConfirmation.action === 'grant' ? 'Выдать демо организации' : organizationConfirmation.action === 'extend' ? 'Продлить тариф организации' : organizationConfirmation.action === 'revoke' ? 'Отозвать демо организации' : 'Остановить тариф организации' }}</strong>
          <button type="button" :disabled="isManagingOrganization" @click="organizationConfirmation = null">×</button>
        </div>
        <p>{{ organizationConfirmation.organization.organizationName || 'Без названия' }}</p>
        <p v-if="organizationConfirmation.action === 'stop'">Все действующие подписки организации будут остановлены сразу. Неиспользованный оплаченный срок не восстанавливается автоматически. {{ subscriptionFallback(organizationConfirmation.organization.primaryScenarioCode) }}</p>
        <p v-if="organizationConfirmation.action === 'revoke'">Демо организации будет отозвано. {{ subscriptionFallback(organizationConfirmation.organization.primaryScenarioCode) }}</p>
        <template v-if="organizationConfirmation.action === 'grant-plan'">
          <label><span>Тариф</span><SearchSelect v-model="organizationPlanId" :options="[{ value: 0, label: 'Выберите тариф' }, ...businessPlans.map(plan => ({ value: plan.id, label: plan.name }))]" /></label>
          <label><span>Срок в днях</span><input v-model.number="organizationPlanDays" type="number" min="1" max="366" required></label>
          <p>Тариф начнётся сейчас и закончится {{ organizationPlanEndDate ? formatDateTime(organizationPlanEndDate) : 'после выбора тарифа и срока' }}. Платёж не создаётся.</p>
        </template>
        <template v-if="organizationConfirmation.action === 'extend'">
          <label><span>Продлить на дней</span><input v-model.number="organizationPlanDays" type="number" min="1" max="366" required></label>
          <p>Новый срок до {{ organizationExtensionEndDate ? formatDateTime(organizationExtensionEndDate) : 'укажите срок' }}. Платёж не создаётся.</p>
        </template>
        <label><span>Причина</span><textarea v-model="organizationActionReason" maxlength="500" rows="3" required></textarea></label>
        <div v-if="organizationError" class="form-message form-message--error">{{ organizationError }}</div>
        <div class="modal-actions">
          <button :class="organizationConfirmation.action === 'stop' || organizationConfirmation.action === 'revoke' ? 'danger-button' : 'primary-button'" type="button" :disabled="isManagingOrganization || !organizationActionReason.trim() || (organizationConfirmation.action === 'grant-plan' && !organizationPlanEndDate) || (organizationConfirmation.action === 'extend' && !organizationExtensionEndDate)" @click="confirmOrganizationAction">
            {{ isManagingOrganization ? 'Сохраняем…' : 'Подтвердить' }}
          </button>
          <button class="secondary-button" type="button" :disabled="isManagingOrganization" @click="organizationConfirmation = null">Отмена</button>
        </div>
      </div>
    </div>

    <div v-if="commercialEnabled && isManagementModalOpen && profile" class="modal-backdrop" @click.self="isManagementModalOpen = false">
      <div class="modal-card modal-card--compact">
        <div class="modal-heading">
          <strong>Управление пользователем</strong>
          <button type="button" :disabled="isManaging" @click="isManagementModalOpen = false">×</button>
        </div>
        <div v-if="managementMessage" class="form-message form-message--success">{{ managementMessage }}</div>
        <div v-if="managementError" class="form-message form-message--error">{{ managementError }}</div>
        <div v-if="managementConfirmation" class="user-management-confirmation">
          <strong>{{ managementConfirmation === 'grant-plan' ? 'Выдать тариф без оплаты?' : managementConfirmation === 'demo' ? 'Выдать пользователю демо-тариф?' : managementConfirmation === 'revoke-demo' ? 'Отозвать демо-тариф?' : managementConfirmation === 'extend' ? 'Продлить личную подписку?' : 'Остановить личную подписку?' }}</strong>
          <p v-if="managementConfirmation === 'demo'">Срок демо определяется настройками тарифа.</p>
          <p v-if="managementConfirmation === 'default-plan'">Все действующие личные подписки будут остановлены. Неиспользованный оплаченный срок не восстанавливается автоматически.</p>
          <template v-if="managementConfirmation === 'grant-plan'">
            <label><span>Тариф</span><SearchSelect v-model="managementPlanId" :options="[{ value: 0, label: 'Выберите тариф' }, ...personalPlans.map(plan => ({ value: plan.id, label: plan.name }))]" /></label>
            <label><span>Срок в днях</span><input v-model.number="managementPlanDays" type="number" min="1" max="366" required></label>
            <p>Тариф начнётся сейчас и закончится {{ managementPlanEndDate ? formatDateTime(managementPlanEndDate) : 'после выбора тарифа и срока' }}. Платёж не создаётся.</p>
          </template>
          <template v-if="managementConfirmation === 'extend'">
            <label><span>Продлить на дней</span><input v-model.number="managementPlanDays" type="number" min="1" max="366" required></label>
            <p>Новый срок до {{ managementExtensionEndDate ? formatDateTime(managementExtensionEndDate) : 'укажите срок' }}. Платёж не создаётся.</p>
          </template>
          <label><span>Причина</span><textarea v-model="managementReason" maxlength="500" rows="3" required></textarea></label>
          <div class="modal-actions">
            <button class="primary-button" type="button" :disabled="isManaging || !managementReason.trim() || (managementConfirmation === 'grant-plan' && !managementPlanEndDate) || (managementConfirmation === 'extend' && !managementExtensionEndDate)" @click="confirmManagementAction">Подтвердить</button>
            <button class="secondary-button" type="button" :disabled="isManaging" @click="managementConfirmation = null">Отмена</button>
          </div>
        </div>
        <div v-else class="user-management-actions">
          <button v-if="canManage" class="secondary-button" type="button" :disabled="isManaging" @click="toggleTestSubscriptions">
            {{ profile.canUseTestSubscriptions ? 'Отключить тестовые тарифы' : 'Разрешить тестовые тарифы' }}
          </button>
          <button v-if="canManageBilling" class="secondary-button" type="button" :disabled="isManaging || hasActiveUserSubscription" @click="managementConfirmation = 'demo'">
            Выдать личное демо · прежняя модель
          </button>
          <button v-if="canManageBilling" class="secondary-button" type="button" :disabled="isManaging || hasActiveUserSubscription" @click="managementConfirmation = 'grant-plan'">
            Выдать личный тариф без оплаты
          </button>
          <button v-if="canManageBilling && hasActiveUserDemo" class="danger-button" type="button" :disabled="isManaging" @click="managementConfirmation = 'revoke-demo'">
            Отозвать демо-тариф
          </button>
          <button v-if="canManageBilling && activeUserSubscriptionsCount === 1" class="secondary-button" type="button" :disabled="isManaging" @click="managementConfirmation = 'extend'">
            Продлить личную подписку
          </button>
          <button v-if="canManageBilling" class="danger-button" type="button" :disabled="isManaging || !hasActiveUserSubscription" @click="managementConfirmation = 'default-plan'">
            Завершить личную подписку
          </button>
        </div>
        <div class="modal-actions">
          <button class="secondary-button" type="button" :disabled="isManaging" @click="isManagementModalOpen = false">Закрыть</button>
        </div>
      </div>
    </div>
  </section>
</template>

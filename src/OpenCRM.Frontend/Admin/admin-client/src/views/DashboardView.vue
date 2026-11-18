<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { getAdminDashboard } from '@/api/adminDashboardApi'
import type { AdminDashboard } from '@/models/AdminDashboard'
import DashboardActivityChart from '@/components/DashboardActivityChart.vue'
import DashboardPaymentsChart from '@/components/DashboardPaymentsChart.vue'
import { useAdminCurrentUserStore } from '@/stores/adminCurrentUserStore'
import { formattingCulture, useLocalization } from '@opencrm/ui/i18n'
import '../adminUsersMessages'

const currentUserStore = useAdminCurrentUserStore()
const { t } = useLocalization()
const dashboard = ref<AdminDashboard | null>(null)
const periodDays = ref<1 | 7 | 30>(1)
const isLoading = ref(false)
const error = ref('')
const refreshIntervals = [5, 10, 30, 60] as const
const refreshStorageKey = 'opencrm.admin.dashboard.refreshSeconds'
const refreshSeconds = ref(readRefreshInterval())
const isRefreshing = ref(false)
let refreshTimer: ReturnType<typeof setInterval> | undefined
let activeRequest: AbortController | null = null
const canViewBilling = computed(() =>
  currentUserStore.currentUser?.commercialEnabled === true &&
  (currentUserStore.isSystem || currentUserStore.currentUser?.permissions.includes('Admin.Billing.View') === true))
const chartPeriodLabel = computed(() => {
  if (periodDays.value === 1) {
    return t('adminUsers.dashboard.todayByHour')
  }

  return periodDays.value === 7 ? t('adminUsers.dashboard.forSevenDays') : t('adminUsers.dashboard.forMonth')
})

const statisticGroups = computed(() => dashboard.value ? [
  {
    title: t('adminUsers.dashboard.users'),
    items: [
      [t('adminUsers.dashboard.total'), dashboard.value.kpis.usersTotal],
      [t('adminUsers.dashboard.newToday'), dashboard.value.kpis.newUsersToday],
      [t('adminUsers.dashboard.newSevenDays'), dashboard.value.kpis.newUsers7Days]
    ]
  },
  {
    title: t('adminUsers.dashboard.organizations'),
    items: [
      [t('adminUsers.dashboard.total'), dashboard.value.kpis.organizationsTotal],
      [t('adminUsers.dashboard.active'), dashboard.value.kpis.activeOrganizations],
      [t('adminUsers.dashboard.newSevenDays'), dashboard.value.kpis.newOrganizations7Days],
      [t('adminUsers.dashboard.members'), dashboard.value.kpis.organizationMembers],
      ...(canViewBilling.value
        ? [[t('adminUsers.dashboard.activeSubscriptions'), dashboard.value.kpis.activeOrganizationSubscriptions]]
        : [])
    ]
  },
  {
    title: t('adminUsers.dashboard.equipmentAndMaintenance'),
    items: [
      [t('adminUsers.dashboard.vehicles'), dashboard.value.kpis.vehiclesTotal],
      [t('adminUsers.dashboard.maintenances'), dashboard.value.kpis.maintenancesTotal],
      [t('adminUsers.dashboard.planned'), dashboard.value.kpis.plannedMaintenances],
      [t('adminUsers.dashboard.documents'), dashboard.value.kpis.documentsTotal],
      [t('adminUsers.dashboard.openDefects'), dashboard.value.kpis.openDefects]
    ]
  },
  ...(canViewBilling.value
    ? [{
        title: t('adminUsers.dashboard.finance'),
        items: [
          [t('adminUsers.dashboard.amountThirtyDays'), formatMoney(dashboard.value.kpis.payments30DaysAmount)],
          [t('adminUsers.dashboard.paymentsThirtyDays'), dashboard.value.kpis.payments30DaysCount],
          [t('adminUsers.dashboard.problemPayments'), dashboard.value.attention.failedPayments]
        ]
      }]
    : []),
  {
    title: t('adminUsers.dashboard.communications'),
    items: [
      [t('adminUsers.dashboard.emailToday'), dashboard.value.kpis.emailsToday],
      [t('adminUsers.dashboard.emailSevenDays'), dashboard.value.kpis.emails7Days],
      [t('adminUsers.dashboard.sentSevenDays'), dashboard.value.kpis.sentEmails7Days],
      [t('adminUsers.dashboard.errorsSevenDays'), dashboard.value.kpis.failedEmails7Days],
      [t('adminUsers.dashboard.pendingSend'), dashboard.value.kpis.pendingEmails]
    ]
  },
  {
    title: t('adminUsers.dashboard.attention'),
    items: [
      [t('adminUsers.dashboard.newRequests'), dashboard.value.attention.newSupportMessages],
      [t('adminUsers.dashboard.suggestions'), dashboard.value.attention.pendingCatalogSuggestions],
      [t('adminUsers.dashboard.pendingInvitations'), dashboard.value.attention.pendingInvitations],
      [t('adminUsers.dashboard.overdueTasks'), dashboard.value.attention.overdueTasks],
      [t('adminUsers.dashboard.lowStock'), dashboard.value.attention.lowStockItems]
    ]
  }
] : [])

function readRefreshInterval(): number {
  try {
    const stored = Number(localStorage.getItem(refreshStorageKey))
    return refreshIntervals.some(value => value === stored) ? stored : 10
  } catch {
    return 10
  }
}

function restartRefreshTimer(): void {
  clearInterval(refreshTimer)
  refreshTimer = setInterval(() => {
    if (!document.hidden && !activeRequest) void load(true)
  }, refreshSeconds.value * 1000)
}

function handleVisibilityChange(): void {
  if (!document.hidden && !activeRequest) void load(true)
}

onMounted(() => {
  void load()
  restartRefreshTimer()
  document.addEventListener('visibilitychange', handleVisibilityChange)
})

onBeforeUnmount(() => {
  clearInterval(refreshTimer)
  document.removeEventListener('visibilitychange', handleVisibilityChange)
  activeRequest?.abort()
  activeRequest = null
})

watch(refreshSeconds, value => {
  if (!refreshIntervals.some(interval => interval === value)) {
    refreshSeconds.value = 10
    return
  }

  try {
    localStorage.setItem(refreshStorageKey, String(value))
  } catch {
    // При запрете хранилища выбор действует до закрытия страницы.
  }

  restartRefreshTimer()
})

async function load(background = false): Promise<void> {
  if (activeRequest) return
  const controller = new AbortController()
  activeRequest = controller
  isLoading.value = !background || !dashboard.value
  isRefreshing.value = true

  try {
    const result = await getAdminDashboard(periodDays.value, controller.signal)
    if (activeRequest !== controller) return
    dashboard.value = result
    error.value = ''
  } catch {
    if (activeRequest === controller && !controller.signal.aborted) {
      error.value = dashboard.value
        ? t('adminUsers.dashboard.refreshError')
        : t('adminUsers.dashboard.loadError')
    }
  } finally {
    if (activeRequest === controller) {
      activeRequest = null
      isLoading.value = false
      isRefreshing.value = false
    }
  }
}

function setPeriod(days: 1 | 7 | 30): void {
  if (periodDays.value === days) return
  activeRequest?.abort()
  activeRequest = null
  periodDays.value = days
  void load()
}

function formatMoney(value: number): string {
  return new Intl.NumberFormat(formattingCulture.value, { style: 'currency', currency: 'RUB', currencyDisplay: 'code' }).format(value)
}

</script>

<template>
  <section class="dashboard-page">
    <div class="dashboard-toolbar">
      <h1>{{ t('adminUsers.dashboard.title') }}</h1>
      <div class="dashboard-refresh">
        <span class="dashboard-update-note" aria-live="polite">
          {{ isRefreshing ? t('adminUsers.dashboard.refreshing') : dashboard ? t('adminUsers.dashboard.updatedAt', { time: new Date(dashboard.generatedAt).toLocaleTimeString(formattingCulture) }) : '' }}
        </span>
        <label for="dashboard-refresh-interval">{{ t('adminUsers.dashboard.refreshInterval') }}</label>
        <select id="dashboard-refresh-interval" v-model.number="refreshSeconds">
          <option v-for="seconds in refreshIntervals" :key="seconds" :value="seconds">
            {{ seconds === 60 ? t('adminUsers.dashboard.refreshMinute') : t('adminUsers.dashboard.refreshSeconds', { seconds }) }}
          </option>
        </select>
        <button type="button" :disabled="isRefreshing" @click="load(true)">{{ t('adminUsers.dashboard.refresh') }}</button>
      </div>
    </div>

    <div class="dashboard-content" :aria-busy="isLoading">
      <div v-if="isLoading" class="dashboard-loader dashboard-loader--overlay" role="status" :aria-label="t('adminUsers.dashboard.loading')">
        <i></i>
      </div>

      <div class="dashboard-content__body">
        <div class="dashboard-section dashboard-news">
          <div class="dashboard-section__heading">
            <strong>{{ t('adminUsers.dashboard.newSection') }}</strong>
            <div class="period-switcher">
              <button :class="{ active: periodDays === 1 }" :disabled="isLoading" @click="setPeriod(1)">{{ t('adminUsers.dashboard.today') }}</button>
              <button :class="{ active: periodDays === 7 }" :disabled="isLoading" @click="setPeriod(7)">{{ t('adminUsers.dashboard.sevenDays') }}</button>
              <button :class="{ active: periodDays === 30 }" :disabled="isLoading" @click="setPeriod(30)">{{ t('adminUsers.dashboard.month') }}</button>
            </div>
          </div>
          <div v-if="error" class="form-message form-message--error">{{ error }}</div>
          <div v-if="dashboard" class="period-metrics">
            <RouterLink to="/users"><span>{{ t('adminUsers.dashboard.newUsers') }}</span><strong>{{ dashboard.period.newUsers }}</strong></RouterLink>
            <div><span>{{ t('adminUsers.dashboard.newOrganizations') }}</span><strong>{{ dashboard.period.newOrganizations }}</strong></div>
            <RouterLink v-if="canViewBilling" to="/finance/billing"><span>{{ t('adminUsers.dashboard.payments') }}</span><strong>{{ dashboard.period.paymentsCount }}</strong><small>{{ formatMoney(dashboard.period.paymentsAmount) }}</small></RouterLink>
            <RouterLink to="/communications/emails"><span>{{ t('adminUsers.dashboard.email') }}</span><strong>{{ dashboard.period.emails }}</strong></RouterLink>
            <RouterLink to="/content/support"><span>{{ t('adminUsers.dashboard.support') }}</span><strong>{{ dashboard.period.supportMessages }}</strong></RouterLink>
            <RouterLink to="/content/catalog-suggestions"><span>{{ t('adminUsers.dashboard.catalogSuggestions') }}</span><strong>{{ dashboard.period.catalogSuggestions }}</strong></RouterLink>
          </div>
        </div>

        <div v-if="dashboard" class="dashboard-charts" :class="{ 'dashboard-charts--single': !canViewBilling }">
          <DashboardActivityChart
            :period-label="chartPeriodLabel"
            :registrations="dashboard.charts.registrations"
            :emails="dashboard.charts.emails"
            :maintenances="dashboard.charts.maintenances"
          />
          <DashboardPaymentsChart v-if="canViewBilling" :period-label="chartPeriodLabel" :period-days="dashboard.period.days" :points="dashboard.charts.payments" />
        </div>


        <div v-if="dashboard" class="dashboard-statistics">
          <section v-for="group in statisticGroups" :key="group.title" class="dashboard-section statistic-group">
            <strong>{{ group.title }}</strong>
            <dl>
              <template v-for="item in group.items" :key="item[0]">
                <dt>{{ item[0] }}</dt>
                <dd>{{ item[1] }}</dd>
              </template>
            </dl>
          </section>
        </div>
      </div>
    </div>
  </section>
</template>

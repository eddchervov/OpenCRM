<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { getAdminDashboard } from '@/api/adminDashboardApi'
import type { AdminDashboard } from '@/models/AdminDashboard'
import DashboardActivityChart from '@/components/DashboardActivityChart.vue'
import DashboardPaymentsChart from '@/components/DashboardPaymentsChart.vue'
import { useAdminCurrentUserStore } from '@/stores/adminCurrentUserStore'

const currentUserStore = useAdminCurrentUserStore()
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
    return 'Сегодня по часам'
  }

  return periodDays.value === 7 ? 'За 7 дней' : 'За месяц'
})

const statisticGroups = computed(() => dashboard.value ? [
  {
    title: 'Пользователи',
    items: [
      ['Всего', dashboard.value.kpis.usersTotal],
      ['Новых сегодня', dashboard.value.kpis.newUsersToday],
      ['Новых за 7 дней', dashboard.value.kpis.newUsers7Days]
    ]
  },
  {
    title: 'Организации',
    items: [
      ['Всего', dashboard.value.kpis.organizationsTotal],
      ['Активных', dashboard.value.kpis.activeOrganizations],
      ['Новых за 7 дней', dashboard.value.kpis.newOrganizations7Days],
      ['Участников', dashboard.value.kpis.organizationMembers],
      ...(canViewBilling.value
        ? [['Активных подписок', dashboard.value.kpis.activeOrganizationSubscriptions]]
        : [])
    ]
  },
  {
    title: 'Техника и обслуживание',
    items: [
      ['Техники', dashboard.value.kpis.vehiclesTotal],
      ['Обслуживаний', dashboard.value.kpis.maintenancesTotal],
      ['Запланировано', dashboard.value.kpis.plannedMaintenances],
      ['Документов', dashboard.value.kpis.documentsTotal],
      ['Открытых дефектов', dashboard.value.kpis.openDefects]
    ]
  },
  ...(canViewBilling.value
    ? [{
        title: 'Финансы',
        items: [
          ['Сумма за 30 дней', formatMoney(dashboard.value.kpis.payments30DaysAmount)],
          ['Платежей за 30 дней', dashboard.value.kpis.payments30DaysCount],
          ['Проблемных платежей', dashboard.value.attention.failedPayments]
        ]
      }]
    : []),
  {
    title: 'Коммуникации',
    items: [
      ['Email сегодня', dashboard.value.kpis.emailsToday],
      ['Email за 7 дней', dashboard.value.kpis.emails7Days],
      ['Отправлено за 7 дней', dashboard.value.kpis.sentEmails7Days],
      ['Ошибок за 7 дней', dashboard.value.kpis.failedEmails7Days],
      ['Ожидают отправки', dashboard.value.kpis.pendingEmails]
    ]
  },
  {
    title: 'Требует внимания',
    items: [
      ['Новые обращения', dashboard.value.attention.newSupportMessages],
      ['Предложения в каталог', dashboard.value.attention.pendingCatalogSuggestions],
      ['Ожидающие приглашения', dashboard.value.attention.pendingInvitations],
      ['Просроченные задачи', dashboard.value.attention.overdueTasks],
      ['Низкие остатки', dashboard.value.attention.lowStockItems]
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
        ? 'Не удалось обновить данные. Показаны ранее полученные значения.'
        : 'Не удалось загрузить дашборд.'
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
  return new Intl.NumberFormat('ru-RU', { style: 'currency', currency: 'RUB' }).format(value)
}

</script>

<template>
  <section class="dashboard-page">
    <div class="dashboard-toolbar">
      <h1>Дашборд</h1>
      <div class="dashboard-refresh">
        <span class="dashboard-update-note" aria-live="polite">
          {{ isRefreshing ? 'Обновляем…' : dashboard ? 'Обновлено: ' + new Date(dashboard.generatedAt).toLocaleTimeString('ru-RU') : '' }}
        </span>
        <label for="dashboard-refresh-interval">Автообновление</label>
        <select id="dashboard-refresh-interval" v-model.number="refreshSeconds">
          <option v-for="seconds in refreshIntervals" :key="seconds" :value="seconds">
            {{ seconds === 60 ? '1 минута' : seconds + ' секунд' }}
          </option>
        </select>
        <button type="button" :disabled="isRefreshing" @click="load(true)">Обновить</button>
      </div>
    </div>

    <div class="dashboard-content" :aria-busy="isLoading">
      <div v-if="isLoading" class="dashboard-loader dashboard-loader--overlay" role="status" aria-label="Загрузка данных">
        <i></i>
      </div>

      <div class="dashboard-content__body">
        <div class="dashboard-section dashboard-news">
          <div class="dashboard-section__heading">
            <strong>Что нового</strong>
            <div class="period-switcher">
              <button :class="{ active: periodDays === 1 }" :disabled="isLoading" @click="setPeriod(1)">Сегодня</button>
              <button :class="{ active: periodDays === 7 }" :disabled="isLoading" @click="setPeriod(7)">7 дней</button>
              <button :class="{ active: periodDays === 30 }" :disabled="isLoading" @click="setPeriod(30)">Месяц</button>
            </div>
          </div>
          <div v-if="error" class="form-message form-message--error">{{ error }}</div>
          <div v-if="dashboard" class="period-metrics">
            <RouterLink to="/users"><span>Новые пользователи</span><strong>{{ dashboard.period.newUsers }}</strong></RouterLink>
            <div><span>Новые организации</span><strong>{{ dashboard.period.newOrganizations }}</strong></div>
            <RouterLink v-if="canViewBilling" to="/finance/billing"><span>Платежи</span><strong>{{ dashboard.period.paymentsCount }}</strong><small>{{ formatMoney(dashboard.period.paymentsAmount) }}</small></RouterLink>
            <RouterLink to="/communications/emails"><span>Email</span><strong>{{ dashboard.period.emails }}</strong></RouterLink>
            <RouterLink to="/content/support"><span>Обращения</span><strong>{{ dashboard.period.supportMessages }}</strong></RouterLink>
            <RouterLink to="/content/catalog-suggestions"><span>Предложения в каталог</span><strong>{{ dashboard.period.catalogSuggestions }}</strong></RouterLink>
          </div>
        </div>

        <div v-if="dashboard" class="dashboard-charts" :class="{ 'dashboard-charts--single': !canViewBilling }">
          <DashboardActivityChart
            :period-label="chartPeriodLabel"
            :registrations="dashboard.charts.registrations"
            :emails="dashboard.charts.emails"
            :maintenances="dashboard.charts.maintenances"
          />
          <DashboardPaymentsChart v-if="canViewBilling" :period-label="chartPeriodLabel" :points="dashboard.charts.payments" />
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

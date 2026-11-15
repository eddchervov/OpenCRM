<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import Chart from 'chart.js/auto'
import { workspaceChartColors, workspaceTheme } from '@opencrm/ui'
import type { Chart as ChartInstance } from 'chart.js'
import { UiButton, UiCard, UiDatePicker, UiLoader, formatDateOnly, parseDateOnly } from '@opencrm/ui'
import { loadWorkspaceFinance, type WorkspaceFinanceSummary } from '../api/finance'
import { loadExport1CCapabilities, type Export1CCapabilities } from '../api/export1C'
import type { WorkspaceContext } from '../types'
import WorkspacePageHelpButton from './WorkspacePageHelpButton.vue'
import Export1CSection from './Export1CSection.vue'

const props = defineProps<{
  context: WorkspaceContext
  initialDateFrom?: string
  initialDateTo?: string
  initialExportOpen?: boolean
}>()

const today = new Date(`${formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')}T12:00:00`)
const thirtyDaysAgo = new Date(today)
thirtyDaysAgo.setDate(today.getDate() - 29)

const isLoading = ref(false)
const errorMessage = ref('')
const finance = ref<WorkspaceFinanceSummary | null>(null)
const exportCapabilities = ref<Export1CCapabilities | null>(null)
const exportDialog = ref<HTMLDialogElement | null>(null)
const exportDateFrom = ref(filtersDate(props.initialDateFrom, toDateInput(thirtyDaysAgo)))
const exportDateTo = ref(filtersDate(props.initialDateTo, toDateInput(today)))
const revenueChartCanvas = ref<HTMLCanvasElement | null>(null)
let revenueChart: ChartInstance | null = null
let financeRequest = 0
let exportCapabilitiesRequest = 0
let exportDialogOpener: HTMLElement | null = null
const filters = reactive({
  dateFrom: isDate(props.initialDateFrom) ? props.initialDateFrom! : toDateInput(thirtyDaysAgo),
  dateTo: isDate(props.initialDateTo) ? props.initialDateTo! : toDateInput(today)
})
const isFleetScenario = computed(() =>
  (props.context.organization.primaryScenarioCode ?? props.context.shell.scenario?.code ?? '').toLowerCase() === 'fleet'
)
const kpis = computed(() => finance.value?.kpis)
const paymentBreakdown = computed(() => finance.value?.paymentBreakdown ?? [])
const costBreakdown = computed(() => finance.value?.costBreakdown ?? [])
const title = computed(() => isFleetScenario.value ? 'Расходы и стоимость владения' : 'Финансы')
const revenueLabel = computed(() => isFleetScenario.value ? 'Оплачено за период' : 'Выручка')
const netLabel = computed(() => isFleetScenario.value ? 'Начислено расходов' : 'Начислено')
const profitLabel = computed(() => isFleetScenario.value ? 'Остаток после затрат' : 'Прибыль')
const exportHref = computed(() => `#finance/export?dateFrom=${filters.dateFrom}&dateTo=${filters.dateTo}`)

function filtersDate(value: string | undefined, fallback: string) {
  return isDate(value) ? value : fallback
}

async function loadFinance() {
  const request = ++financeRequest
  const organizationId = props.context.organization.id
  errorMessage.value = ''
  if (!isDate(filters.dateFrom) || !isDate(filters.dateTo)) {
    errorMessage.value = 'Укажите начало и окончание периода.'
    isLoading.value = false
    return
  }

  if (filters.dateFrom > filters.dateTo) {
    errorMessage.value = 'Дата начала не может быть позже даты окончания.'
    isLoading.value = false
    return
  }

  isLoading.value = true

  try {
    const result = await loadWorkspaceFinance(organizationId, filters.dateFrom, filters.dateTo)
    if (request !== financeRequest || props.context.organization.id !== organizationId) return
    finance.value = result
  } catch (error) {
    if (request === financeRequest && props.context.organization.id === organizationId) {
      errorMessage.value = error instanceof Error ? error.message : 'Не удалось загрузить финансы.'
    }
  } finally {
    if (request === financeRequest) isLoading.value = false
  }
}

async function loadExportCapabilities() {
  const request = ++exportCapabilitiesRequest
  const organizationId = props.context.organization.id
  try {
    const result = await loadExport1CCapabilities(organizationId)
    if (request !== exportCapabilitiesRequest || props.context.organization.id !== organizationId) return
    exportCapabilities.value = result
  } catch {
    if (request === exportCapabilitiesRequest && props.context.organization.id === organizationId) {
      exportCapabilities.value = null
    }
  }
}

function openExport(event: MouseEvent) {
  exportDialogOpener = event.currentTarget instanceof HTMLElement ? event.currentTarget : null
  exportDateFrom.value = filters.dateFrom
  exportDateTo.value = filters.dateTo
  window.location.hash = exportHref.value
}

function syncExportDates(dateFrom: string, dateTo: string) {
  exportDateFrom.value = dateFrom
  exportDateTo.value = dateTo
}

function closeExport() {
  if (props.initialExportOpen) {
    window.location.hash = `#finance?dateFrom=${exportDateFrom.value}&dateTo=${exportDateTo.value}`
  }
}

function handleExportDialogClose() {
  if (!props.initialExportOpen) return
  closeExport()
}

watch(() => props.initialExportOpen, async isOpen => {
  await nextTick()
  if (isOpen) {
    if (!exportDialog.value?.open) exportDialog.value?.showModal()
    return
  }

  if (exportDialog.value?.open) exportDialog.value.close()
  await nextTick()
  exportDialogOpener?.focus()
  exportDialogOpener = null
}, { immediate: true })

watch(() => [props.initialDateFrom, props.initialDateTo], ([dateFrom, dateTo]) => {
  filters.dateFrom = filtersDate(dateFrom, filters.dateFrom)
  filters.dateTo = filtersDate(dateTo, filters.dateTo)
  void loadFinance()
})

watch(() => props.context.organization.id, () => {
  financeRequest++
  exportCapabilitiesRequest++
  finance.value = null
  exportCapabilities.value = null
  void loadFinance()
  void loadExportCapabilities()
})

function setPreset(days: number) {
  const to = new Date(`${formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')}T12:00:00`)
  const from = new Date(to)
  from.setDate(to.getDate() - days + 1)
  filters.dateFrom = toDateInput(from)
  filters.dateTo = toDateInput(to)
  void loadFinance()
}

function isPreset(days: number) {
  const to = new Date(`${formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')}T12:00:00`)
  const from = new Date(to)
  from.setDate(to.getDate() - days + 1)

  return filters.dateFrom === toDateInput(from) && filters.dateTo === toDateInput(to)
}

function getBreakdownWidth(value: number) {
  const maxValue = Math.max(1, ...paymentBreakdown.value.concat(costBreakdown.value).map(item => item.value))
  return `${Math.max(4, Math.round(value * 100 / maxValue))}%`
}

function formatMoney(value: number | undefined) {
  return new Intl.NumberFormat('ru-RU', {
    style: 'currency',
    currency: 'RUB',
    maximumFractionDigits: 2
  }).format(value ?? 0)
}

function formatNumber(value: number | undefined) {
  return new Intl.NumberFormat('ru-RU').format(value ?? 0)
}

function formatDate(value: string) {
  if (!value) {
    return ''
  }

  return new Intl.DateTimeFormat('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(parseDateOnly(value.slice(0, 10)) ?? new Date(value))
}

function toDateInput(value: Date) {
  const year = value.getFullYear()
  const month = String(value.getMonth() + 1).padStart(2, '0')
  const day = String(value.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function isDate(value?: string): value is string {
  return Boolean(value && /^\d{4}-\d{2}-\d{2}$/.test(value))
}

function destroyRevenueChart() {
  revenueChart?.destroy()
  revenueChart = null
}

function renderRevenueChart() {
  const canvas = revenueChartCanvas.value
  const data = finance.value
  if (!canvas || !data) {
    destroyRevenueChart()
    return
  }

  destroyRevenueChart()
  const labels = data.revenueSeries.map(point => point.label)
  const accruedByLabel = new Map(data.accruedSeries.map(point => [point.label, point.value]))

  revenueChart = new Chart(canvas, {
    type: 'bar',
    data: {
      labels,
      datasets: [
        {
          type: 'bar',
          label: 'Начислено',
          data: labels.map(label => accruedByLabel.get(label) ?? 0),
          backgroundColor: 'rgba(245, 158, 11, 0.72)',
          borderColor: '#d97706',
          borderRadius: 6,
          borderSkipped: false,
          maxBarThickness: 18
        },
        {
          type: 'line',
          label: 'Оплачено',
          data: data.revenueSeries.map(point => point.value),
          borderColor: '#0f766e',
          backgroundColor: 'rgba(15, 118, 110, 0.14)',
          borderWidth: 3,
          pointBackgroundColor: '#0f766e',
          pointBorderColor: '#ffffff',
          pointBorderWidth: 2,
          pointRadius: 3,
          pointHoverRadius: 5,
          tension: 0.32,
          fill: true
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      animation: false,
      interaction: {
        mode: 'index',
        intersect: false
      },
      plugins: {
        legend: {
          position: 'bottom',
          labels: {
            boxWidth: 10,
            boxHeight: 10,
            color: workspaceChartColors.value.text,
            font: {
              size: 12,
              weight: 700
            }
          }
        },
        tooltip: {
          callbacks: {
            label: item => `${item.dataset.label}: ${formatMoney(Number(item.raw ?? 0))}`
          }
        }
      },
      scales: {
        x: {
          grid: {
            display: false
          },
          ticks: {
            color: workspaceChartColors.value.text,
            maxRotation: 0,
            autoSkip: true,
            font: {
              size: 11,
              weight: 700
            }
          }
        },
        y: {
          beginAtZero: true,
          grid: {
            color: workspaceChartColors.value.grid
          },
          ticks: {
            color: workspaceChartColors.value.text,
            callback: value => formatMoney(Number(value)),
            font: {
              size: 11,
              weight: 700
            }
          }
        }
      }
    }
  })
}

watch([finance, workspaceTheme], async () => {
  await nextTick()
  renderRevenueChart()
})

onMounted(loadFinance)
onMounted(loadExportCapabilities)
onBeforeUnmount(destroyRevenueChart)
</script>

<template>
  <UiCard page :title="title" class="workspace-finance">
    <template #actions>
      <a v-if="exportCapabilities?.canExport" class="finance-export-open" :href="exportHref" @click.prevent="openExport">Выгрузка</a>
      <WorkspacePageHelpButton section-code="statistics" :title="title" />
    </template>

    <section class="finance-toolbar">
      <div class="finance-presets">
        <UiButton variant="secondary" :class="{ active: isPreset(7) }" :aria-pressed="isPreset(7)" @click="setPreset(7)">7 дней</UiButton>
        <UiButton variant="secondary" :class="{ active: isPreset(30) }" :aria-pressed="isPreset(30)" @click="setPreset(30)">30 дней</UiButton>
        <UiButton variant="secondary" :class="{ active: isPreset(90) }" :aria-pressed="isPreset(90)" @click="setPreset(90)">90 дней</UiButton>
      </div>
      <form class="finance-date-form" @submit.prevent="loadFinance">
        <UiDatePicker v-model="filters.dateFrom" label="С" required />
        <UiDatePicker v-model="filters.dateTo" label="По" required />
        <UiButton type="submit">Показать</UiButton>
      </form>
    </section>

    <section v-if="isLoading" class="finance-state">
      <UiLoader text="Собираем финансовую сводку..." />
    </section>

    <section v-else-if="errorMessage" class="finance-state finance-state--error">
      {{ errorMessage }}
    </section>

    <section v-else-if="finance" class="finance-section">
      <div class="finance-kpi-grid">
        <article class="finance-kpi finance-kpi--strong">
          <span>{{ revenueLabel }}</span>
          <strong>{{ formatMoney(kpis?.paidAmount) }}</strong>
          <small>Заказов: {{ formatNumber(kpis?.ordersCount) }}</small>
        </article>
        <article class="finance-kpi">
          <span>{{ netLabel }}</span>
          <strong>{{ formatMoney(kpis?.netAmount) }}</strong>
          <small>после скидок</small>
        </article>
        <article class="finance-kpi finance-kpi--debt">
          <span>Долг</span>
          <strong>{{ formatMoney(kpis?.debtAmount) }}</strong>
          <small>к доплате</small>
        </article>
        <article class="finance-kpi">
          <span>Себестоимость</span>
          <strong>{{ formatMoney(kpis?.costAmount) }}</strong>
          <small>материалы и труд</small>
        </article>
        <article class="finance-kpi">
          <span>{{ profitLabel }}</span>
          <strong>{{ formatMoney(kpis?.profitAmount) }}</strong>
          <small>управленческий расчет</small>
        </article>
        <article class="finance-kpi">
          <span>Средний чек</span>
          <strong>{{ formatMoney(kpis?.averageCheckAmount) }}</strong>
          <small>по заказам периода</small>
        </article>
      </div>

      <div class="finance-grid">
        <article class="finance-panel finance-panel--wide">
          <div class="finance-panel__head">
            <h3 class="ui-section-header">Динамика денег</h3>
            <p>начислено и оплачено по дням</p>
          </div>
          <div class="finance-chart">
            <canvas ref="revenueChartCanvas" aria-label="Revenue chart" role="img"></canvas>
          </div>
        </article>

        <article class="finance-panel">
          <div class="finance-panel__head">
            <h3 class="ui-section-header">Оплата</h3>
            <p>что уже закрыто деньгами</p>
          </div>
          <div class="finance-breakdown">
            <div v-for="item in paymentBreakdown" :key="item.code" class="finance-breakdown__item">
              <div>
                <strong>{{ item.label }}</strong>
                <span>{{ formatMoney(item.value) }}</span>
              </div>
              <div class="finance-breakdown__track">
                <span :style="{ width: getBreakdownWidth(item.value) }"></span>
              </div>
            </div>
          </div>
        </article>

        <article class="finance-panel">
          <div class="finance-panel__head">
            <h3 class="ui-section-header">Затраты</h3>
            <p>что съедает маржу</p>
          </div>
          <div v-if="costBreakdown.length" class="finance-breakdown">
            <div v-for="item in costBreakdown" :key="item.code" class="finance-breakdown__item">
              <div>
                <strong>{{ item.label }}</strong>
                <span>{{ formatMoney(item.value) }}</span>
              </div>
              <div class="finance-breakdown__track finance-breakdown__track--cost">
                <span :style="{ width: getBreakdownWidth(item.value) }"></span>
              </div>
            </div>
          </div>
          <div v-else class="finance-empty">Затраты за период не указаны.</div>
        </article>
      </div>

      <div class="finance-grid finance-grid--lists">
        <article class="finance-panel">
          <div class="finance-panel__head">
            <h3 class="ui-section-header">Популярные услуги</h3>
            <p>по сумме строк заказа</p>
          </div>
          <div v-if="finance.topServices.length" class="finance-list">
            <div v-for="item in finance.topServices" :key="item.name" class="finance-list__row">
              <div>
                <strong>{{ item.name }}</strong>
                <small>Строк: {{ formatNumber(item.count) }}</small>
              </div>
              <span>{{ formatMoney(item.amount) }}</span>
            </div>
          </div>
          <div v-else class="finance-empty">Пока нет услуг с суммой.</div>
        </article>

        <article class="finance-panel">
          <div class="finance-panel__head">
            <h3 class="ui-section-header">Долги клиентов</h3>
            <p>кому стоит напомнить об оплате</p>
          </div>
          <div v-if="finance.topDebtors.length" class="finance-list">
            <div v-for="item in finance.topDebtors" :key="item.clientId ?? item.clientName" class="finance-list__row">
              <div>
                <strong>{{ item.clientName }}</strong>
                <small>Заказов: {{ formatNumber(item.ordersCount) }}</small>
              </div>
              <span>{{ formatMoney(item.debtAmount) }}</span>
            </div>
          </div>
          <div v-else class="finance-empty">Долгов за период нет.</div>
        </article>
      </div>

      <article class="finance-panel">
        <div class="finance-panel__head">
          <h3 class="ui-section-header">Заказы периода</h3>
          <p>{{ formatDate(finance.dateFrom) }} - {{ formatDate(finance.dateTo) }}</p>
        </div>
        <div v-if="finance.orders.length" class="finance-table">
          <div class="finance-table__head">
            <span>Дата и заказ</span>
            <span>Начислено</span>
            <span>Оплачено</span>
            <span>Долг</span>
            <span>Прибыль</span>
            <span>Статус</span>
          </div>
          <a v-for="order in finance.orders" :key="order.id" class="finance-table__row" :href="order.detailUrl">
            <div>
              <strong>{{ formatDate(order.date) }}</strong>
              <span>{{ order.vehicleDisplayName }}</span>
              <small>{{ order.vehicleSubtitle ? `${order.vehicleSubtitle} · ` : '' }}Покупатель: {{ order.clientName ?? 'не указан' }}</small>
            </div>
            <span data-label="Начислено">{{ formatMoney(order.netAmount) }}</span>
            <span data-label="Оплачено">{{ formatMoney(order.paidAmount) }}</span>
            <span data-label="Долг" :class="{ 'finance-money-danger': order.debtAmount > 0 }">{{ formatMoney(order.debtAmount) }}</span>
            <span data-label="Прибыль" :class="{ 'finance-money-danger': order.profitAmount < 0 }">{{ formatMoney(order.profitAmount) }}</span>
            <span>
              <em v-if="order.isCancelled" class="finance-status">Отменено</em>
              <em :class="`finance-status finance-status--${order.paymentStatus}`">{{ order.paymentStatusName }}</em>
            </span>
          </a>
        </div>
        <div v-else class="finance-empty">За выбранный период заказов нет.</div>
      </article>
    </section>
  </UiCard>

  <dialog ref="exportDialog" class="finance-export-dialog" aria-labelledby="finance-export-dialog-title" @close="handleExportDialogClose" @click.self="closeExport">
    <header class="finance-export-dialog__header">
      <h2 id="finance-export-dialog-title">Выгрузка данных</h2>
      <button type="button" aria-label="Закрыть окно" @click="closeExport">×</button>
    </header>
    <div class="finance-export-dialog__body">
      <Export1CSection
        v-if="initialExportOpen"
        :context="context"
        :accounting-export="context.shell.scenario?.modules?.accountingExport"
        :initial-date-from="exportDateFrom"
        :initial-date-to="exportDateTo"
        @date-range-change="syncExportDates"
      />
    </div>
  </dialog>
</template>

<style scoped>
.finance-export-dialog {
  width: min(920px, calc(100vw - 32px));
  max-width: none;
  max-height: min(88dvh, 900px);
  padding: 0;
  overflow: hidden;
  border: 1px solid var(--crm-line, #d8dee8);
  border-radius: 14px;
  background: var(--crm-surface, #fff);
  color: var(--crm-text, #172536);
  box-shadow: 0 24px 80px rgb(15 23 42 / .28);
}
.finance-export-dialog::backdrop { background: rgb(17 24 39 / .48); backdrop-filter: blur(2px); }
.finance-export-dialog__header { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 14px 20px; border-bottom: 1px solid var(--crm-line, #d8dee8); }
.finance-export-dialog__header h2 { margin: 0; font-size: 18px; }
.finance-export-dialog__header button { display: grid; width: 36px; height: 36px; place-items: center; border: 1px solid var(--crm-line, #d8dee8); border-radius: 8px; background: var(--crm-surface, #fff); color: inherit; font-size: 24px; cursor: pointer; }
.finance-export-dialog__body { max-height: calc(min(88dvh, 900px) - 66px); padding: 20px; overflow: auto; overscroll-behavior: contain; }
@media (max-width: 600px) {
  .finance-export-dialog { width: calc(100vw - 16px); max-height: 94dvh; border-radius: 12px; }
  .finance-export-dialog__header { padding: 12px 14px; }
  .finance-export-dialog__body { max-height: calc(94dvh - 60px); padding: 14px; }
}
</style>

<script setup lang="ts">
import { formattingCulture, translate as t } from '@opencrm/ui/i18n'
import '../financeMessages'
import { useWorkspaceCurrency } from '../workspaceCurrency'

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

const workspaceCurrency = useWorkspaceCurrency()

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
const breakdownKeys: Record<string, string> = { materials: 'materials', labor: 'labor', discount: 'discounts', paid: 'paid', debt: 'debt' }
const paymentStatusKeys: Record<string, string> = {
  unpaid: 'statusUnpaid', partial: 'statusPartial', paid: 'paid', overpaid: 'statusOverpaid', no_payment_required: 'statusNoPayment'
}
const breakdownLabel = (code: string, fallback: string) => breakdownKeys[code] ? t(`finance.${breakdownKeys[code]}`) : fallback
const paymentStatusLabel = (code: string) => t(`finance.${paymentStatusKeys[code] ?? 'unknownStatus'}`)
const paymentBreakdown = computed(() => finance.value?.paymentBreakdown ?? [])
const costBreakdown = computed(() => finance.value?.costBreakdown ?? [])
const title = computed(() => isFleetScenario.value ? t('finance.fleetTitle') : t('finance.title'))
const revenueLabel = computed(() => isFleetScenario.value ? t('finance.paidPeriod') : t('finance.revenue'))
const netLabel = computed(() => isFleetScenario.value ? t('finance.expensesAccrued') : t('finance.accrued'))
const profitLabel = computed(() => isFleetScenario.value ? t('finance.remainingAfterCosts') : t('finance.profit'))
const exportHref = computed(() => `#finance/export?dateFrom=${filters.dateFrom}&dateTo=${filters.dateTo}`)

function filtersDate(value: string | undefined, fallback: string) {
  return isDate(value) ? value : fallback
}

async function loadFinance() {
  const request = ++financeRequest
  const organizationId = props.context.organization.id
  errorMessage.value = ''
  if (!isDate(filters.dateFrom) || !isDate(filters.dateTo)) {
    errorMessage.value = t('finance.periodRequired')
    isLoading.value = false
    return
  }

  if (filters.dateFrom > filters.dateTo) {
    errorMessage.value = t('finance.periodOrder')
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
      errorMessage.value = error instanceof Error ? error.message : t('finance.loadError')
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

function formatMoney(value: number | undefined, currencyCode = finance.value?.currencyCode ?? workspaceCurrency.value) {
  return new Intl.NumberFormat(formattingCulture.value, {
    style: 'currency',
    currency: currencyCode,
    maximumFractionDigits: 2
  }).format(value ?? 0)
}

function formatNumber(value: number | undefined) {
  return new Intl.NumberFormat(formattingCulture.value).format(value ?? 0)
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
          label: t('finance.accrued'),
          data: labels.map(label => accruedByLabel.get(label) ?? 0),
          backgroundColor: 'rgba(245, 158, 11, 0.72)',
          borderColor: '#d97706',
          borderRadius: 6,
          borderSkipped: false,
          maxBarThickness: 18
        },
        {
          type: 'line',
          label: t('finance.paid'),
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
      <a v-if="exportCapabilities?.canExport" class="finance-export-open" :href="exportHref" @click.prevent="openExport">{{ t('finance.export') }}</a>
      <WorkspacePageHelpButton section-code="statistics" :title="title" />
    </template>

    <section class="finance-toolbar">
      <div class="finance-presets">
        <UiButton variant="secondary" :class="{ active: isPreset(7) }" :aria-pressed="isPreset(7)" @click="setPreset(7)">{{ t('finance.week') }}</UiButton>
        <UiButton variant="secondary" :class="{ active: isPreset(30) }" :aria-pressed="isPreset(30)" @click="setPreset(30)">{{ t('finance.month') }}</UiButton>
        <UiButton variant="secondary" :class="{ active: isPreset(90) }" :aria-pressed="isPreset(90)" @click="setPreset(90)">{{ t('finance.quarter') }}</UiButton>
      </div>
      <form class="finance-date-form" @submit.prevent="loadFinance">
        <UiDatePicker v-model="filters.dateFrom" :label="t('finance.from')" required />
        <UiDatePicker v-model="filters.dateTo" :label="t('finance.to')" required />
        <UiButton type="submit">{{ t('finance.show') }}</UiButton>
      </form>
    </section>

    <section v-if="isLoading" class="finance-state">
      <UiLoader :text="t('finance.loading')" />
    </section>

    <section v-else-if="errorMessage" class="finance-state finance-state--error">
      {{ errorMessage }}
    </section>

    <section v-else-if="finance" class="finance-section">
      <p class="finance-currency-note">{{ t('finance.primaryCurrency', { currency: finance.currencyCode ?? workspaceCurrency }) }}</p>
      <section v-if="finance.otherCurrencyTotals?.length" class="finance-panel">
        <h3 class="ui-section-header">{{ t('finance.otherCurrencies') }}</h3>
        <p>{{ t('finance.separateTotals') }}</p>
        <div class="finance-currency-totals">
          <table>
            <thead><tr><th>{{ t('finance.currency') }}</th><th>{{ t('finance.accrued') }}</th><th>{{ t('finance.paid') }}</th><th>{{ t('finance.debt') }}</th><th>{{ t('finance.cost') }}</th><th>{{ t('finance.profit') }}</th></tr></thead>
            <tbody><tr v-for="total in finance.otherCurrencyTotals" :key="total.currencyCode">
              <th scope="row">{{ total.currencyCode }}</th>
              <td>{{ formatMoney(total.netAmount, total.currencyCode) }}</td><td>{{ formatMoney(total.paidAmount, total.currencyCode) }}</td>
              <td>{{ formatMoney(total.debtAmount, total.currencyCode) }}</td><td>{{ formatMoney(total.costAmount, total.currencyCode) }}</td><td>{{ formatMoney(total.profitAmount, total.currencyCode) }}</td>
            </tr></tbody>
          </table>
        </div>
      </section>
      <div class="finance-kpi-grid">
        <article class="finance-kpi finance-kpi--strong">
          <span>{{ revenueLabel }}</span>
          <strong>{{ formatMoney(kpis?.paidAmount) }}</strong>
          <small>{{ t('finance.orderCount', { count: formatNumber(kpis?.ordersCount) }) }}</small>
        </article>
        <article class="finance-kpi">
          <span>{{ netLabel }}</span>
          <strong>{{ formatMoney(kpis?.netAmount) }}</strong>
          <small>{{ t('finance.afterDiscounts') }}</small>
        </article>
        <article class="finance-kpi finance-kpi--debt">
          <span>{{ t('finance.debt') }}</span>
          <strong>{{ formatMoney(kpis?.debtAmount) }}</strong>
          <small>{{ t('finance.balanceDue') }}</small>
        </article>
        <article class="finance-kpi">
          <span>{{ t('finance.cost') }}</span>
          <strong>{{ formatMoney(kpis?.costAmount) }}</strong>
          <small>{{ t('finance.materialsAndLabor') }}</small>
        </article>
        <article class="finance-kpi">
          <span>{{ profitLabel }}</span>
          <strong>{{ formatMoney(kpis?.profitAmount) }}</strong>
          <small>{{ t('finance.managementCalculation') }}</small>
        </article>
        <article class="finance-kpi">
          <span>{{ t('finance.averageCheck') }}</span>
          <strong>{{ formatMoney(kpis?.averageCheckAmount) }}</strong>
          <small>{{ t('finance.periodOrders') }}</small>
        </article>
      </div>

      <div class="finance-grid">
        <article class="finance-panel finance-panel--wide">
          <div class="finance-panel__head">
            <h3 class="ui-section-header">{{ t('finance.moneyTrend') }}</h3>
            <p>{{ t('finance.accruedPaidDays') }}</p>
          </div>
          <div class="finance-chart">
            <canvas ref="revenueChartCanvas" aria-label="Revenue chart" role="img"></canvas>
          </div>
        </article>

        <article class="finance-panel">
          <div class="finance-panel__head">
            <h3 class="ui-section-header">{{ t('finance.payment') }}</h3>
            <p>{{ t('finance.coveredByPayments') }}</p>
          </div>
          <div class="finance-breakdown">
            <div v-for="item in paymentBreakdown" :key="item.code" class="finance-breakdown__item">
              <div>
                <strong>{{ breakdownLabel(item.code, item.label) }}</strong>
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
            <h3 class="ui-section-header">{{ t('finance.costs') }}</h3>
            <p>{{ t('finance.marginCosts') }}</p>
          </div>
          <div v-if="costBreakdown.length" class="finance-breakdown">
            <div v-for="item in costBreakdown" :key="item.code" class="finance-breakdown__item">
              <div>
                <strong>{{ breakdownLabel(item.code, item.label) }}</strong>
                <span>{{ formatMoney(item.value) }}</span>
              </div>
              <div class="finance-breakdown__track finance-breakdown__track--cost">
                <span :style="{ width: getBreakdownWidth(item.value) }"></span>
              </div>
            </div>
          </div>
          <div v-else class="finance-empty">{{ t('finance.noCosts') }}</div>
        </article>
      </div>

      <div class="finance-grid finance-grid--lists">
        <article class="finance-panel">
          <div class="finance-panel__head">
            <h3 class="ui-section-header">{{ t('finance.topServices') }}</h3>
            <p>{{ t('finance.byOrderLines') }}</p>
          </div>
          <div v-if="finance.topServices.length" class="finance-list">
            <div v-for="item in finance.topServices" :key="item.name" class="finance-list__row">
              <div>
                <strong>{{ item.name }}</strong>
                <small>{{ t('finance.lineCount', { count: formatNumber(item.count) }) }}</small>
              </div>
              <span>{{ formatMoney(item.amount) }}</span>
            </div>
          </div>
          <div v-else class="finance-empty">{{ t('finance.noServices') }}</div>
        </article>

        <article class="finance-panel">
          <div class="finance-panel__head">
            <h3 class="ui-section-header">{{ t('finance.clientDebts') }}</h3>
            <p>{{ t('finance.paymentReminder') }}</p>
          </div>
          <div v-if="finance.topDebtors.length" class="finance-list">
            <div v-for="item in finance.topDebtors" :key="item.clientId ?? item.clientName" class="finance-list__row">
              <div>
                <strong>{{ item.clientName }}</strong>
                <small>{{ t('finance.orderCount', { count: formatNumber(item.ordersCount) }) }}</small>
              </div>
              <span>{{ formatMoney(item.debtAmount) }}</span>
            </div>
          </div>
          <div v-else class="finance-empty">{{ t('finance.noDebts') }}</div>
        </article>
      </div>

      <article class="finance-panel">
        <div class="finance-panel__head">
          <h3 class="ui-section-header">{{ t('finance.orders') }}</h3>
          <p>{{ formatDate(finance.dateFrom) }} - {{ formatDate(finance.dateTo) }}</p>
        </div>
        <div v-if="finance.orders.length" class="finance-table">
          <div class="finance-table__head">
            <span>{{ t('finance.dateOrder') }}</span>
            <span>{{ t('finance.accrued') }}</span>
            <span>{{ t('finance.paid') }}</span>
            <span>{{ t('finance.debt') }}</span>
            <span>{{ t('finance.profit') }}</span>
            <span>{{ t('finance.status') }}</span>
          </div>
          <a v-for="order in finance.orders" :key="order.id" class="finance-table__row" :href="order.detailUrl">
            <div>
              <strong>{{ formatDate(order.date) }}</strong>
              <span>{{ order.vehicleDisplayName }}</span>
              <small>{{ order.vehicleSubtitle ? `${order.vehicleSubtitle} · ` : '' }}{{ t('finance.buyer', { name: order.clientName ?? t('finance.noBuyer') }) }}</small>
            </div>
            <span :data-label="t('finance.accrued')">{{ formatMoney(order.netAmount, order.currencyCode) }}</span>
            <span :data-label="t('finance.paid')">{{ formatMoney(order.paidAmount, order.currencyCode) }}</span>
            <span :data-label="t('finance.debt')" :class="{ 'finance-money-danger': order.debtAmount > 0 }">{{ formatMoney(order.debtAmount, order.currencyCode) }}</span>
            <span :data-label="t('finance.profit')" :class="{ 'finance-money-danger': order.profitAmount < 0 }">{{ formatMoney(order.profitAmount, order.currencyCode) }}</span>
            <span>
              <em v-if="order.isCancelled" class="finance-status">{{ t('finance.cancelled') }}</em>
              <em :class="`finance-status finance-status--${order.paymentStatus}`">{{ paymentStatusLabel(order.paymentStatus) }}</em>
            </span>
          </a>
        </div>
        <div v-else class="finance-empty">{{ t('finance.noOrders') }}</div>
      </article>
    </section>
  </UiCard>

  <dialog ref="exportDialog" class="finance-export-dialog" aria-labelledby="finance-export-dialog-title" @close="handleExportDialogClose" @click.self="closeExport">
    <header class="finance-export-dialog__header">
      <h2 id="finance-export-dialog-title">{{ t('finance.exportTitle') }}</h2>
      <button type="button" :aria-label="t('finance.close')" @click="closeExport">×</button>
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
.finance-currency-note { color: var(--crm-text-muted); font-size: 13px; }
.finance-currency-totals { overflow-x: auto; }
.finance-currency-totals table { width: 100%; border-collapse: collapse; font-size: 13px; }
.finance-currency-totals th, .finance-currency-totals td { padding: 10px; border-bottom: 1px solid var(--crm-line); text-align: start; white-space: nowrap; }
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

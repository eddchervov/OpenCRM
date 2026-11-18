<script setup lang="ts">
import { formattingCulture, useLocalization } from '@opencrm/ui/i18n'
import '../sectionPagesMessages'
import { useWorkspaceCurrency } from '../workspaceCurrency'

import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import Chart from 'chart.js/auto'
import { workspaceChartColors, workspaceTheme } from '@opencrm/ui'
import type { Chart as ChartInstance, ChartOptions } from 'chart.js'
import type { WorkspaceStatisticsData } from '../api/workspaceAdmin'

const workspaceCurrency = useWorkspaceCurrency()

const { t, te, locale } = useLocalization()

const props = defineProps<{
  statistics: Pick<WorkspaceStatisticsData, 'analytics'>
  previewOnly?: boolean
}>()

const analytics = computed(() => props.statistics.analytics)
const kpis = computed(() => analytics.value.kpis)
const maintenanceChartCanvas = ref<HTMLCanvasElement | null>(null)
const revenueChartCanvas = ref<HTMLCanvasElement | null>(null)
let maintenanceChart: ChartInstance | null = null
let revenueChart: ChartInstance | null = null
const statusTotal = computed(() =>
  analytics.value.statusBreakdown.reduce((sum, item) => sum + item.value, 0)
)
const materialRiskTotal = computed(() =>
  analytics.value.materialRiskBreakdown.reduce((sum, item) => sum + item.value, 0)
)
const reportCards = computed(() => [
  {
    title: t('sectionPages.work'),
    value: formatNumber(kpis.value.completedWorks14Days),
    meta: t('sectionPages.workMeta', { planned: formatNumber(kpis.value.plannedWorks), inProgress: formatNumber(kpis.value.inProgressWorks) })
  },
  {
    title: t('sectionPages.charged'),
    value: formatMoney(kpis.value.revenue14Days),
    meta: t('sectionPages.chargedHint')
  },
  {
    title: t('sectionPages.clientsVehicles'),
    value: `${formatNumber(kpis.value.clients)} / ${formatNumber(kpis.value.activeVehicles)}`,
    meta: t('sectionPages.clientsVehiclesHint')
  },
  {
    title: t('sectionPages.control'),
    value: formatNumber(kpis.value.openDefects + kpis.value.materialsLowStock + kpis.value.materialsOutOfStock),
    meta: t('sectionPages.controlHint')
  }
])

function formatNumber(value: number) {
  if (props.previewOnly) return '—'

  return new Intl.NumberFormat(formattingCulture.value).format(value)
}

function formatMoney(value: number) {
  if (props.previewOnly) return '—'

  return new Intl.NumberFormat(formattingCulture.value, {
    style: 'currency',
    currency: workspaceCurrency.value,
    currencyDisplay: 'code',
    maximumFractionDigits: 2
  }).format(value)
}

function seriesDate(value: string) {
  return new Intl.DateTimeFormat(formattingCulture.value, { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(`${value.slice(0, 10)}T00:00:00Z`))
}

function breakdownName(item: { code: string; label: string }) {
  const key = `sectionPages.breakdown_${item.code}`

  return te(key) ? t(key) : item.label
}

function destroyCharts() {
  maintenanceChart?.destroy()
  revenueChart?.destroy()
  maintenanceChart = null
  revenueChart = null
}

function buildChartOptions(formatValue: (value: number) => string): ChartOptions<'bar' | 'line'> {
  return {
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
          label: item => `${item.dataset.label}: ${formatValue(Number(item.raw ?? 0))}`
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
          callback: value => formatValue(Number(value)),
          font: {
            size: 11,
            weight: 700
          }
        }
      }
    }
  }
}

function renderCharts() {
  if (props.previewOnly) {
    destroyCharts()
    return
  }

  const maintenanceCanvas = maintenanceChartCanvas.value
  const revenueCanvas = revenueChartCanvas.value
  if (!maintenanceCanvas || !revenueCanvas) {
    destroyCharts()
    return
  }

  destroyCharts()
  const createdSeries = analytics.value.maintenanceCreatedSeries
  const completedSeries = analytics.value.maintenanceCompletedSeries
  const completedByDate = new Map(completedSeries.map(point => [point.date, point.value]))
  const workLabels = createdSeries.map(point => seriesDate(point.date))

  maintenanceChart = new Chart(maintenanceCanvas, {
    type: 'bar',
    data: {
      labels: workLabels,
      datasets: [
        {
          label: t('sectionPages.created'),
          data: createdSeries.map(point => point.value),
          backgroundColor: 'rgba(14, 165, 233, 0.72)',
          borderColor: '#0284c7',
          borderRadius: 6,
          borderSkipped: false,
          maxBarThickness: 18
        },
        {
          label: t('sectionPages.completed'),
          data: createdSeries.map(point => completedByDate.get(point.date) ?? 0),
          backgroundColor: 'rgba(15, 118, 110, 0.72)',
          borderColor: '#0f766e',
          borderRadius: 6,
          borderSkipped: false,
          maxBarThickness: 18
        }
      ]
    },
    options: buildChartOptions(formatNumber)
  })

  revenueChart = new Chart(revenueCanvas, {
    type: 'line',
    data: {
      labels: analytics.value.revenueSeries.map(point => seriesDate(point.date)),
      datasets: [
        {
          label: t('sectionPages.charges'),
          data: analytics.value.revenueSeries.map(point => point.value),
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
    options: buildChartOptions(formatMoney)
  })
}

watch(() => [props.statistics, workspaceTheme.value, locale.value, formattingCulture.value, workspaceCurrency.value], async () => {
  await nextTick()
  renderCharts()
}, { deep: true })

onMounted(async () => {
  await nextTick()
  renderCharts()
})
onBeforeUnmount(destroyCharts)
</script>

<template>
  <div class="workspace-statistics">
    <div class="workspace-statistics-kpis">
      <article v-for="card in reportCards" :key="card.title" class="workspace-statistics-kpi">
        <span>{{ card.title }}</span>
        <strong>{{ card.value }}</strong>
        <small>{{ card.meta }}</small>
      </article>
    </div>

    <div class="workspace-statistics-grid">
      <article class="workspace-statistics-panel workspace-statistics-panel--wide">
        <div class="workspace-statistics-panel-head">
          <h3 class="ui-section-header">{{ t('sectionPages.workTrend') }}</h3>
          <p>{{ t('sectionPages.workTrendHint') }}</p>
        </div>
        <div v-if="previewOnly" class="workspace-statistics-chart workspace-statistics-chart--dual workspace-statistics-chart--preview" aria-hidden="true">
          <span v-for="index in 14" :key="index" :style="{ height: `${24 + (index % 5) * 11}%` }"></span>
        </div>
        <div v-else class="workspace-statistics-chart workspace-statistics-chart--dual" :aria-label="t('sectionPages.workTrend')">
          <canvas ref="maintenanceChartCanvas" :aria-label="t('sectionPages.maintenanceChart')" role="img"></canvas>
        </div>
      </article>

      <article class="workspace-statistics-panel">
        <div class="workspace-statistics-panel-head">
          <h3 class="ui-section-header">{{ t('sectionPages.charged') }}</h3>
          <p>{{ t('sectionPages.chargedPeriod', { amount: formatMoney(kpis.revenue14Days) }) }}</p>
        </div>
        <div v-if="previewOnly" class="workspace-statistics-chart workspace-statistics-chart--money workspace-statistics-chart--preview" aria-hidden="true">
          <span v-for="index in 14" :key="index" :style="{ height: `${20 + (index % 6) * 10}%` }"></span>
        </div>
        <div v-else class="workspace-statistics-chart workspace-statistics-chart--money" :aria-label="t('sectionPages.charged')">
          <canvas ref="revenueChartCanvas" :aria-label="t('sectionPages.revenueChart')" role="img"></canvas>
        </div>
      </article>

      <article class="workspace-statistics-panel">
        <div class="workspace-statistics-panel-head">
          <h3 class="ui-section-header">{{ t('sectionPages.statuses') }}</h3>
          <p>{{ t('sectionPages.workCount', { count: formatNumber(statusTotal) }) }}</p>
        </div>
        <div class="workspace-statistics-breakdown">
          <template v-if="previewOnly">
            <div v-for="index in 4" :key="index" class="workspace-statistics-breakdown-row workspace-statistics-preview-row" aria-hidden="true">
              <span></span><strong>—</strong><i><b></b></i>
            </div>
          </template>
          <template v-else>
            <div v-for="item in analytics.statusBreakdown" :key="item.code" class="workspace-statistics-breakdown-row">
              <span>{{ breakdownName(item) }}</span>
              <strong>{{ formatNumber(item.value) }}</strong>
              <i><b :style="{ width: item.percent + '%' }"></b></i>
            </div>
          </template>
        </div>
      </article>

      <article class="workspace-statistics-panel">
        <div class="workspace-statistics-panel-head">
          <h3 class="ui-section-header">{{ t('sectionPages.stockRisks') }}</h3>
          <p>{{ t('sectionPages.stockRiskCount', { count: formatNumber(materialRiskTotal) }) }}</p>
        </div>
        <div class="workspace-statistics-breakdown">
          <template v-if="previewOnly">
            <div v-for="index in 3" :key="index" class="workspace-statistics-breakdown-row workspace-statistics-preview-row" aria-hidden="true">
              <span></span><strong>—</strong><i><b></b></i>
            </div>
          </template>
          <template v-else>
            <div v-for="item in analytics.materialRiskBreakdown" :key="item.code" class="workspace-statistics-breakdown-row">
              <span>{{ breakdownName(item) }}</span>
              <strong>{{ formatNumber(item.value) }}</strong>
              <i><b :style="{ width: item.percent + '%' }"></b></i>
            </div>
          </template>
        </div>
      </article>
    </div>
  </div>
</template>

<style scoped>
.workspace-statistics-chart--preview { display: flex; align-items: end; justify-content: space-around; gap: 8px; padding: 18px 8px; }
.workspace-statistics-chart--preview span { flex: 1; max-width: 20px; border-radius: 4px 4px 0 0; background: var(--crm-border, #dce2e9); opacity: .72; }
.workspace-statistics-preview-row span { width: 55%; height: 10px; border-radius: 5px; background: var(--crm-border, #dce2e9); opacity: .65; }
.workspace-statistics-preview-row i b { width: 55%; background: var(--crm-border, #dce2e9); }
</style>

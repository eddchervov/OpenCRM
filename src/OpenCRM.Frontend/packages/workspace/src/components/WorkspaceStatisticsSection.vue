<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import Chart from 'chart.js/auto'
import { workspaceChartColors, workspaceTheme } from '@opencrm/ui'
import type { Chart as ChartInstance, ChartOptions } from 'chart.js'
import type { WorkspaceStatisticsData } from '../api/workspaceAdmin'

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
    title: 'Работы',
    value: formatNumber(kpis.value.completedWorks14Days),
    meta: `${formatNumber(kpis.value.plannedWorks)} запланировано, ${formatNumber(kpis.value.inProgressWorks)} в работе`
  },
  {
    title: 'Начислено по закрытым работам',
    value: formatMoney(kpis.value.revenue14Days),
    meta: 'по закрытым работам за 14 дней'
  },
  {
    title: 'Клиенты и техника',
    value: `${formatNumber(kpis.value.clients)} / ${formatNumber(kpis.value.activeVehicles)}`,
    meta: 'клиентов / активной техники'
  },
  {
    title: 'Контроль',
    value: formatNumber(kpis.value.openDefects + kpis.value.materialsLowStock + kpis.value.materialsOutOfStock),
    meta: 'открытые дефекты и складские риски'
  }
])

function formatNumber(value: number) {
  if (props.previewOnly) return '—'

  return new Intl.NumberFormat('ru-RU').format(value)
}

function formatMoney(value: number) {
  if (props.previewOnly) return '—'

  return new Intl.NumberFormat('ru-RU', {
    style: 'currency',
    currency: 'RUB',
    maximumFractionDigits: 2
  }).format(value)
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
  const completedByLabel = new Map(completedSeries.map(point => [point.label, point.value]))
  const workLabels = createdSeries.map(point => point.label)

  maintenanceChart = new Chart(maintenanceCanvas, {
    type: 'bar',
    data: {
      labels: workLabels,
      datasets: [
        {
          label: 'Создано',
          data: createdSeries.map(point => point.value),
          backgroundColor: 'rgba(14, 165, 233, 0.72)',
          borderColor: '#0284c7',
          borderRadius: 6,
          borderSkipped: false,
          maxBarThickness: 18
        },
        {
          label: 'Закрыто',
          data: workLabels.map(label => completedByLabel.get(label) ?? 0),
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
      labels: analytics.value.revenueSeries.map(point => point.label),
      datasets: [
        {
          label: 'Начислено',
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

watch(() => [props.statistics, workspaceTheme.value], async () => {
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
          <h3 class="ui-section-header">Динамика работ</h3>
          <p>созданные и закрытые работы за последние 14 дней</p>
        </div>
        <div v-if="previewOnly" class="workspace-statistics-chart workspace-statistics-chart--dual workspace-statistics-chart--preview" aria-hidden="true">
          <span v-for="index in 14" :key="index" :style="{ height: `${24 + (index % 5) * 11}%` }"></span>
        </div>
        <div v-else class="workspace-statistics-chart workspace-statistics-chart--dual" aria-label="Динамика работ">
          <canvas ref="maintenanceChartCanvas" aria-label="Workspace maintenance chart" role="img"></canvas>
        </div>
      </article>

      <article class="workspace-statistics-panel">
        <div class="workspace-statistics-panel-head">
          <h3 class="ui-section-header">Начислено по закрытым работам</h3>
          <p>{{ formatMoney(kpis.revenue14Days) }} за 14 дней</p>
        </div>
        <div v-if="previewOnly" class="workspace-statistics-chart workspace-statistics-chart--money workspace-statistics-chart--preview" aria-hidden="true">
          <span v-for="index in 14" :key="index" :style="{ height: `${20 + (index % 6) * 10}%` }"></span>
        </div>
        <div v-else class="workspace-statistics-chart workspace-statistics-chart--money" aria-label="Начислено по закрытым работам">
          <canvas ref="revenueChartCanvas" aria-label="Workspace revenue chart" role="img"></canvas>
        </div>
      </article>

      <article class="workspace-statistics-panel">
        <div class="workspace-statistics-panel-head">
          <h3 class="ui-section-header">Статусы</h3>
          <p>{{ formatNumber(statusTotal) }} работ в срезе</p>
        </div>
        <div class="workspace-statistics-breakdown">
          <template v-if="previewOnly">
            <div v-for="index in 4" :key="index" class="workspace-statistics-breakdown-row workspace-statistics-preview-row" aria-hidden="true">
              <span></span><strong>—</strong><i><b></b></i>
            </div>
          </template>
          <template v-else>
            <div v-for="item in analytics.statusBreakdown" :key="item.code" class="workspace-statistics-breakdown-row">
              <span>{{ item.label }}</span>
              <strong>{{ formatNumber(item.value) }}</strong>
              <i><b :style="{ width: item.percent + '%' }"></b></i>
            </div>
          </template>
        </div>
      </article>

      <article class="workspace-statistics-panel">
        <div class="workspace-statistics-panel-head">
          <h3 class="ui-section-header">Складские риски</h3>
          <p>{{ formatNumber(materialRiskTotal) }} позиций требуют внимания</p>
        </div>
        <div class="workspace-statistics-breakdown">
          <template v-if="previewOnly">
            <div v-for="index in 3" :key="index" class="workspace-statistics-breakdown-row workspace-statistics-preview-row" aria-hidden="true">
              <span></span><strong>—</strong><i><b></b></i>
            </div>
          </template>
          <template v-else>
            <div v-for="item in analytics.materialRiskBreakdown" :key="item.code" class="workspace-statistics-breakdown-row">
              <span>{{ item.label }}</span>
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

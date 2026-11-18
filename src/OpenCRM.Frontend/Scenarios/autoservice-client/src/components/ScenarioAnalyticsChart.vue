<script setup lang="ts">
import { formattingCulture } from '@opencrm/ui/i18n'
import { useWorkspaceCurrency } from '@opencrm/workspace'

import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import Chart from 'chart.js/auto'
import { workspaceChartColors, workspaceTheme } from '@opencrm/ui'

const workspaceCurrency = useWorkspaceCurrency()

type ChartPoint = {
  label: string
  value: number
}

type ChartDataset = {
  label: string
  points: ChartPoint[]
  color: string
  fill?: boolean
}

const props = defineProps<{
  kind: 'bar' | 'line' | 'doughnut'
  datasets: ChartDataset[]
  money?: boolean
}>()

const canvasRef = ref<HTMLCanvasElement | null>(null)
let chart: any = null

const labels = computed(() => props.datasets[0]?.points.map(point => point.label) ?? [])

function formatValue(value: number) {
  if (!props.money) {
    return new Intl.NumberFormat(formattingCulture.value).format(value)
  }

  return new Intl.NumberFormat(formattingCulture.value, {
    style: 'currency',
    currency: workspaceCurrency.value,
    maximumFractionDigits: 0
  }).format(value)
}

function hexToRgba(hex: string, alpha: number) {
  const normalized = hex.replace('#', '')
  const value = parseInt(normalized.length === 3
    ? normalized.split('').map(char => char + char).join('')
    : normalized, 16)
  const red = (value >> 16) & 255
  const green = (value >> 8) & 255
  const blue = value & 255
  return `rgba(${red}, ${green}, ${blue}, ${alpha})`
}

function destroyChart() {
  chart?.destroy()
  chart = null
}

async function renderChart() {
  await nextTick()
  const canvas = canvasRef.value
  if (!canvas) {
    return
  }

  destroyChart()

  if (props.kind === 'doughnut') {
    const points = props.datasets.map(dataset => ({
      label: dataset.label,
      value: dataset.points[0]?.value ?? 0,
      color: dataset.color
    }))
    chart = new Chart(canvas, {
      type: 'doughnut',
      data: {
        labels: points.map(point => point.label),
        datasets: [{
          data: points.map(point => point.value),
          backgroundColor: points.map(point => point.color),
          borderColor: workspaceChartColors.value.surface,
          borderWidth: 3,
          hoverOffset: 6
        }]
      },
      options: {
        cutout: '68%',
        maintainAspectRatio: false,
        plugins: {
          legend: { position: 'bottom', labels: { color: workspaceChartColors.value.text, boxWidth: 10, boxHeight: 10, usePointStyle: true } },
          tooltip: { callbacks: { label: context => `${context.label}: ${formatValue(Number(context.raw ?? 0))}` } }
        }
      }
    })
    return
  }

  chart = new Chart(canvas, {
    type: props.kind,
    data: {
      labels: labels.value,
      datasets: props.datasets.map(dataset => ({
        label: dataset.label,
        data: dataset.points.map(point => point.value),
        borderColor: dataset.color,
        backgroundColor: props.kind === 'line'
          ? hexToRgba(dataset.color, dataset.fill ? 0.16 : 0.04)
          : hexToRgba(dataset.color, 0.74),
        borderWidth: 2,
        borderRadius: props.kind === 'bar' ? 7 : 0,
        tension: 0.35,
        fill: dataset.fill ?? false,
        pointRadius: props.kind === 'line' ? 3 : 0,
        pointHoverRadius: props.kind === 'line' ? 5 : 0
      }))
    },
    options: {
      maintainAspectRatio: false,
      interaction: { intersect: false, mode: 'index' },
      scales: {
        x: { grid: { display: false }, ticks: { color: workspaceChartColors.value.text, maxRotation: 0, autoSkip: true } },
        y: {
          beginAtZero: true,
          grid: { color: workspaceChartColors.value.grid },
          ticks: { color: workspaceChartColors.value.text, callback: value => formatValue(Number(value)) }
        }
      },
      plugins: {
        legend: { display: props.datasets.length > 1, position: 'bottom', labels: { color: workspaceChartColors.value.text, boxWidth: 10, boxHeight: 10, usePointStyle: true } },
        tooltip: { callbacks: { label: context => `${context.dataset.label}: ${formatValue(Number(context.raw ?? 0))}` } }
      }
    }
  })
}

watch(() => [props.kind, props.money, props.datasets, workspaceTheme.value], renderChart, { deep: true })
onMounted(renderChart)
onBeforeUnmount(destroyChart)
</script>

<template>
  <div class="scenario-analytics-chart">
    <canvas ref="canvasRef"></canvas>
  </div>
</template>

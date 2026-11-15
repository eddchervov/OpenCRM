<script setup lang="ts">
import { computed, ref } from 'vue'

type Point = { label: string; value: number }
const props = defineProps<{
  periodLabel: string
  registrations: Point[]
  emails: Point[]
  maintenances: Point[]
}>()

const width = 820
const height = 320
const left = 58
const right = 18
const top = 24
const bottom = 44
const hoverIndex = ref<number | null>(null)

const series = computed(() => [
  { key: 'registrations' as const, name: 'Регистрации', color: '#3478f6', points: props.registrations },
  { key: 'emails' as const, name: 'Email', color: '#e67e35', points: props.emails },
  { key: 'maintenances' as const, name: 'Обслуживания', color: '#7c55c7', points: props.maintenances }
])
const visibleSeries = computed(() => series.value)
const labels = computed(() => props.registrations.map(point => point.label))
const maximum = computed(() => Math.max(1, ...visibleSeries.value.flatMap(item => item.points.map(point => point.value))))
const gridValues = computed(() => Array.from({ length: 5 }, (_, index) => Math.round(maximum.value * (4 - index) / 4)))
const labelStep = computed(() => Math.max(1, Math.ceil(labels.value.length / 8)))
const hoveredSeries = computed(() => hoverIndex.value === null
  ? []
  : series.value.map(item => ({ ...item, value: item.points[hoverIndex.value!]?.value ?? 0 })))

function x(index: number): number {
  return labels.value.length <= 1
    ? (left + width - right) / 2
    : left + index * ((width - left - right) / (labels.value.length - 1))
}

function y(value: number): number {
  return top + (height - top - bottom) * (1 - value / maximum.value)
}

function coordinates(points: Point[]): string {
  return points.map((point, index) => `${x(index)},${y(point.value)}`).join(' ')
}

function selectPoint(event: MouseEvent): void {
  if (labels.value.length === 0) {
    return
  }

  const bounds = (event.currentTarget as SVGElement).getBoundingClientRect()
  const chartLeft = bounds.left + bounds.width * left / width
  const chartWidth = bounds.width * (width - left - right) / width
  const ratio = Math.max(0, Math.min(1, (event.clientX - chartLeft) / chartWidth))
  hoverIndex.value = Math.round(ratio * (labels.value.length - 1))
}
</script>

<template>
  <section class="analytics-chart analytics-chart--activity">
    <div class="analytics-chart__header">
      <div><strong>Регистрации и работа</strong><small>{{ periodLabel }}</small></div>
      <div class="chart-legend">
        <span v-for="item in series" :key="item.key">
          <i :style="{ backgroundColor: item.color }"></i>{{ item.name }}
        </span>
      </div>
    </div>

    <div class="chart-hover-values" :class="{ hidden: hoverIndex === null }">
      <strong>{{ hoverIndex === null ? 'Дата' : labels[hoverIndex] }}</strong>
      <span v-for="item in hoveredSeries" :key="item.key"><i :style="{ backgroundColor: item.color }"></i>{{ item.name }}: <b>{{ item.value }}</b></span>
    </div>

    <svg :viewBox="`0 0 ${width} ${height}`" role="img" aria-label="График регистраций и рабочих процессов" @mousemove="selectPoint" @mouseleave="hoverIndex = null">
      <g v-for="(value, index) in gridValues" :key="index">
        <line :x1="left" :x2="width - right" :y1="y(value)" :y2="y(value)" class="analytics-grid-line" />
        <text :x="left - 10" :y="y(value) + 4" text-anchor="end">{{ value }}</text>
      </g>
      <g v-for="item in visibleSeries" :key="item.key">
        <polyline :points="coordinates(item.points)" :stroke="item.color" class="analytics-line" />
        <circle
          v-for="(point, index) in item.points"
          :key="`${item.key}-${index}`"
          :cx="x(index)"
          :cy="y(point.value)"
          :stroke="item.color"
          r="3"
          class="analytics-point analytics-point--static"
        />
      </g>
      <template v-if="hoverIndex !== null">
        <line :x1="x(hoverIndex)" :x2="x(hoverIndex)" :y1="top" :y2="height - bottom" class="analytics-crosshair" />
        <circle v-for="item in visibleSeries" :key="item.key" :cx="x(hoverIndex)" :cy="y(item.points[hoverIndex]?.value ?? 0)" r="5" :stroke="item.color" class="analytics-point" />
      </template>
      <text v-for="(label, index) in labels" v-show="index % labelStep === 0 || index === labels.length - 1" :key="label" :x="x(index)" :y="height - 10" text-anchor="middle">{{ label }}</text>
      <rect :x="left" :y="top" :width="width - left - right" :height="height - top - bottom" class="analytics-hit-area" />
    </svg>
  </section>
</template>

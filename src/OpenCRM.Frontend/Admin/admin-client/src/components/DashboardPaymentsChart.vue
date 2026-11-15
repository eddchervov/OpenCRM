<script setup lang="ts">
import { computed, ref } from 'vue'

const props = defineProps<{
  periodLabel: string
  points: Array<{ label: string; value: number }>
}>()

const width = 420
const height = 320
const left = 66
const right = 14
const top = 24
const bottom = 44
const hoveredIndex = ref<number | null>(null)
const maximum = computed(() => Math.max(1, ...props.points.map(point => point.value)))
const total = computed(() => props.points.reduce((sum, point) => sum + point.value, 0))
const gridValues = computed(() => Array.from({ length: 5 }, (_, index) => maximum.value * (4 - index) / 4))
const slotWidth = computed(() => (width - left - right) / Math.max(props.points.length, 1))
const barWidth = computed(() => Math.max(4, Math.min(34, slotWidth.value * 0.62)))
const labelStep = computed(() => Math.max(1, Math.ceil(props.points.length / 6)))

function y(value: number): number {
  return top + (height - top - bottom) * (1 - value / maximum.value)
}

function x(index: number): number {
  return left + slotWidth.value * index + (slotWidth.value - barWidth.value) / 2
}

function money(value: number, compact = false): string {
  return new Intl.NumberFormat('ru-RU', {
    style: 'currency',
    currency: 'RUB',
    notation: compact ? 'compact' : 'standard',
    maximumFractionDigits: 0
  }).format(value)
}
</script>

<template>
  <section class="analytics-chart analytics-chart--payments">
    <div class="analytics-chart__header">
      <div><strong>Платежи</strong><small>{{ periodLabel }}</small></div>
      <b>{{ money(total) }}</b>
    </div>
    <div class="chart-hover-values" :class="{ hidden: hoveredIndex === null }"><strong>{{ hoveredIndex === null ? 'Дата' : points[hoveredIndex]?.label }}</strong><span>{{ money(hoveredIndex === null ? 0 : points[hoveredIndex]?.value ?? 0) }}</span></div>
    <svg :viewBox="`0 0 ${width} ${height}`" role="img" aria-label="График платежей" @mouseleave="hoveredIndex = null">
      <g v-for="(value, index) in gridValues" :key="index">
        <line :x1="left" :x2="width - right" :y1="y(value)" :y2="y(value)" class="analytics-grid-line" />
        <text :x="left - 8" :y="y(value) + 4" text-anchor="end">{{ money(value, true) }}</text>
      </g>
      <g v-for="(point, index) in points" :key="`${point.label}-${index}`" @mouseenter="hoveredIndex = index">
        <rect :x="x(index)" :y="y(point.value)" :width="barWidth" :height="height - bottom - y(point.value)" rx="5" class="payment-bar" />
        <rect :x="left + slotWidth * index" :y="top" :width="slotWidth" :height="height - top - bottom" class="analytics-hit-area" />
        <text v-if="index % labelStep === 0 || index === points.length - 1" :x="left + slotWidth * index + slotWidth / 2" :y="height - 10" text-anchor="middle">{{ point.label }}</text>
      </g>
    </svg>
  </section>
</template>

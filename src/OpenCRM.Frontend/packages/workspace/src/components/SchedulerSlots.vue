<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { UiButton, UiSelect } from '@opencrm/ui'
import { addSchedulerMinutes, getSchedulerFreeStarts, getSchedulerDayInterval, normalizeSchedulerLocation, type SchedulerCalendarItem } from '../utils/schedulerDay'
import { getVehicleBrandLogo } from '../utils/vehicleBrandLogo'

const props = withDefaults(defineProps<{
  items: SchedulerCalendarItem[]
  date: string
  posts: string[]
  closedPosts?: string[]
  intervals: { startTime: string; endTime: string }[]
  canCreate: boolean
  postsLoaded?: boolean
}>(), { postsLoaded: true, closedPosts: () => [] })
const emit = defineEmits<{
  create: [slot: { date: string; start: string; end: string; endDate: string; location: string }]
}>()
const scrollContainer = ref<HTMLDivElement | null>(null)
const duration = defineModel<number>('duration', { default: 60 })
const durationSelection = computed({ get: () => String(duration.value), set: (value: string) => { duration.value = Number(value) } })
const durationOptions = [
  { value: '30', label: '30 минут' },
  { value: '60', label: '1 час' },
  { value: '90', label: '1 час 30 минут' },
  { value: '120', label: '2 часа' },
  { value: '180', label: '3 часа' },
  { value: '240', label: '4 часа' }
]
const postFilter = defineModel<string>('postFilter', { default: '' })
const postOptions = computed(() => [{ value: '', label: 'Все посты' }, ...props.posts.map(post => ({ value: post, label: post }))])
const periods = [
  { title: 'Утро', start: 0, end: 720 },
  { title: 'День', start: 720, end: 960 },
  { title: 'Вечер', start: 960, end: 1440 }
]
const closedPostKeys = computed(() => new Set(props.closedPosts.map(normalizeSchedulerLocation)))
const columns = computed(() => props.posts.filter(post => !postFilter.value || post === postFilter.value).map(location => ({
  location,
  isClosed: closedPostKeys.value.has(normalizeSchedulerLocation(location)),
  starts: closedPostKeys.value.has(normalizeSchedulerLocation(location)) ? [] : getSchedulerFreeStarts(props.items, props.date, location, props.intervals, duration.value),
  busy: props.items.flatMap(item => {
    if (item.status !== 1 && item.status !== 2 || normalizeSchedulerLocation(item.location) !== normalizeSchedulerLocation(location)) return []
    const interval = getSchedulerDayInterval(item, props.date)
    return interval ? [{ item, ...interval }] : []
  }).sort((left, right) => left.start - right.start)
})))
watch([() => props.posts, () => props.postsLoaded], ([posts, postsLoaded]) => {
  if (postsLoaded && !posts.includes(postFilter.value)) {
    postFilter.value = ''
  }
}, { immediate: true })
function scrollPosts(direction: number) {
  const container = scrollContainer.value
  if (!container) return
  container.scrollBy({ left: direction * Math.max(220, container.clientWidth * 0.8), behavior: 'smooth' })
}
function time(minute: number) {
  if (minute === 1440) return '24:00'
  return `${String(Math.floor(minute / 60)).padStart(2, '0')}:${String(minute % 60).padStart(2, '0')}`
}
function create(location: string, minute: number) {
  if (!props.canCreate || !columns.value.some(column => column.location === location && column.starts.includes(minute))) return
  const end = addSchedulerMinutes(props.date, minute + duration.value)
  emit('create', { date: props.date, start: time(minute), end: end.time, endDate: end.date, location })
}
</script>

<template>
  <section class="scheduler-free" aria-label="Свободное время по постам">
    <div class="scheduler-free__tools">
      <UiSelect v-model="durationSelection" label="Длительность" aria-label="Длительность работы" :options="durationOptions" :searchable="false" required />
      <UiSelect v-model="postFilter" label="Пост" aria-label="Показать пост" :options="postOptions" :searchable="false" />
      <div v-if="intervals.length && columns.length > 1" class="scheduler-free__navigation" aria-label="Прокрутка постов">
        <UiButton variant="secondary" aria-label="Показать посты слева" @click="scrollPosts(-1)">←</UiButton>
        <UiButton variant="secondary" aria-label="Показать посты справа" @click="scrollPosts(1)">→</UiButton>
      </div>
    </div>
    <p v-if="!intervals.length" class="scheduler-free__empty">В этот день нет рабочих интервалов.</p>
    <div v-else ref="scrollContainer" class="scheduler-free__scroll" tabindex="0" aria-label="Расписание по постам с прокруткой">
      <div class="scheduler-free__grid" :style="{ '--post-count': columns.length }">
        <article v-for="column in columns" :key="column.location" class="scheduler-free__post">
          <header class="ui-section-header"><h3>{{ column.location }}</h3><span>{{ column.isClosed ? 'Пост закрыт' : column.starts.length ? `Ближайшее: ${time(column.starts[0]!)}` : 'Свободного времени нет' }}</span></header>
          <section v-for="period in periods" :key="period.title" class="scheduler-free__period">
            <h4 class="ui-section-header">{{ period.title }}</h4>
            <div class="scheduler-free__times">
              <a v-for="entry in column.busy.filter(entry => entry.start >= period.start && entry.start < period.end)" :key="entry.item.id"
                :href="entry.item.detailUrl || `#maintenance/${entry.item.id}`" class="scheduler-free__busy" :title="`${entry.item.vehicleDisplayName} · ${time(entry.start)}–${time(entry.end)}`">
                <img v-if="getVehicleBrandLogo(entry.item.brandLogoKey)" :src="getVehicleBrandLogo(entry.item.brandLogoKey)!.src" alt="" />
                <span>{{ time(entry.start) }}–{{ time(entry.end) }} · {{ entry.item.vehicleDisplayName }}</span>
              </a>
              <button v-for="minute in column.starts.filter(minute => minute >= period.start && minute < period.end)" :key="minute" type="button"
                :aria-label="`Записать на ${column.location}, ${time(minute)}–${time(minute + duration)}`"
                :disabled="!canCreate" @click="create(column.location, minute)">
                {{ time(minute) }}–{{ time(minute + duration) }}
              </button>
              <span v-if="!column.starts.some(minute => minute >= period.start && minute < period.end)" class="scheduler-free__empty">Нет свободных интервалов</span>
            </div>
          </section>
        </article>
      </div>
    </div>
    <footer class="scheduler-free__footer">
      <span class="scheduler-free__empty">{{ canCreate ? 'Нажмите на свободный интервал, чтобы создать запись' : 'Расписание доступно для просмотра' }}</span>
      <div class="scheduler-free__legend"><span>○ Свободно</span><span>● Занято</span></div>
    </footer>
  </section>
</template>

<style scoped>
.scheduler-free { min-width: 0; margin-top: 18px; color: var(--crm-text, #202833); }
.scheduler-free__tools { display: flex; flex-wrap: wrap; align-items: end; gap: 12px; margin-bottom: 16px; }
.scheduler-free__tools :deep(.ui-select-field) { flex: 0 1 190px; min-width: 0; margin: 0; }
.scheduler-free__tools p { margin: 0 0 8px auto; color: var(--crm-text-muted, #667085); font-size: 12px; }
.scheduler-free__navigation { display: flex; gap: 8px; margin-left: auto; }
.scheduler-free__scroll { max-height: min(65vh, 640px); overflow: auto; scrollbar-gutter: stable; border: 1px solid var(--crm-line, #dce2e9); border-radius: 9px; }
.scheduler-free__grid { display: grid; grid-template-columns: repeat(var(--post-count), minmax(220px, 1fr)); grid-template-rows: auto repeat(3, auto); }
.scheduler-free__post { display: grid; grid-template-rows: subgrid; grid-row: span 4; min-width: 0; border-right: 1px solid var(--crm-line, #dce2e9); }
.scheduler-free__post:last-child { border-right: 0; }
.scheduler-free__post header { position: sticky; top: 0; z-index: 1; padding: 14px; background: var(--crm-surface-soft, #f4f6f8); }
.scheduler-free h3, .scheduler-free h4 { margin: 0; font-size: 13px; }
.scheduler-free__post header span { display: block; margin-top: 5px; font-size: 11px; color: var(--crm-text-muted, #667085); }
.scheduler-free__period { padding: 12px; border-top: 1px solid var(--crm-line, #dce2e9); }
.scheduler-free h4 { margin-bottom: 9px; font-size: 11px; color: var(--crm-text-muted, #667085); }
.scheduler-free__times { display: flex; flex-wrap: wrap; gap: 7px; align-content: start; }
.scheduler-free__times button { flex: 1 0 95px; min-height: 33px; padding: 6px; border: 1px solid #6c98c5; border-radius: 7px; background: color-mix(in srgb, #6c98c5 7%, var(--crm-surface, #fff)); color: var(--crm-text, #202833); font: inherit; font-size: 12px; cursor: pointer; font-variant-numeric: tabular-nums; }
.scheduler-free__times button:hover:not(:disabled) { background: color-mix(in srgb, #6c98c5 20%, var(--crm-surface, #fff)); }
.scheduler-free__times button:disabled { cursor: default; opacity: .6; }
.scheduler-free button:focus-visible, .scheduler-free a:focus-visible { outline: 2px solid var(--crm-accent, #cc5515); outline-offset: 2px; }
.scheduler-free__busy { display: flex; gap: 6px; align-items: center; flex: 1 0 100%; min-width: 0; padding: 6px 8px; border-radius: 5px; border-left: 2px solid #e87532; color: var(--crm-text-muted, #667085); background: var(--crm-surface-soft, #f4f6f8); font-size: 11px; text-decoration: none; }
.scheduler-free__busy img { width: 19px; height: 19px; padding: 2px; border-radius: 50%; background: #fff; object-fit: contain; }
.scheduler-free__busy span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.scheduler-free__empty { color: var(--crm-text-muted, #667085); font-size: 12px; }
.scheduler-free__footer { display: flex; flex-wrap: wrap; align-items: center; gap: 14px; padding: 14px 0 0; font-size: 13px; }
.scheduler-free__legend { display: flex; gap: 12px; margin-left: auto; font-size: 11px; color: var(--crm-text-muted, #667085); }
.scheduler-free__legend span:first-child { color: #6c98c5; }
@media (max-width: 600px) {
  .scheduler-free__tools p { flex-basis: 100%; margin: 0; }
  .scheduler-free__footer { align-items: start; }
  .scheduler-free__legend { order: 3; margin-left: 0; width: 100%; }
}
</style>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton } from '@opencrm/ui'
import { i18n, translate as t } from '@opencrm/ui/i18n'
import '../schedulerMessages'
import '../schedulerControlMessages'
import '../schedulerCalendarMessages'
import SchedulerRecordPreview from './SchedulerRecordPreview.vue'
import { shouldOpenRecord } from '../utils/workdayCard'
import { getVehicleBrandLogo } from '../utils/vehicleBrandLogo'
import { loadVehicleRegistrationPlateFormats, matchesRegistrationPlateSearch, type VehicleRegistrationPlateFormats } from '../vehicleRegistrationPlate'
import {
  addSchedulerMinutes,
  getSchedulerDayLayout,
  getSchedulerDayInterval,
  getSchedulerDayWindow,
  isSchedulerSlotAvailable,
  normalizeSchedulerLocation,
  type SchedulerCalendarItem
} from '../utils/schedulerDay'

const props = withDefaults(defineProps<{
  items: SchedulerCalendarItem[]
  date: string
  posts: string[]
  closedPosts?: string[]
  start: string
  end: string
  canCreate: boolean
  canMove?: boolean
  postsLoaded?: boolean
}>(), { postsLoaded: true, closedPosts: () => [] })

const emit = defineEmits<{
  create: [payload: { date: string, start: string, end: string, endDate: string, location: string }]
  reschedule: [item: SchedulerCalendarItem]
}>()

const closedPostKeys = computed(() => new Set(props.closedPosts.map(normalizeLocation)))
const normalizedItems = computed(() => props.items.map(item => isPlaceholderPost(item.location) ? { ...item, location: 'Без поста' } : item))
const pixelsPerMinute = 1.2
const search = defineModel<string>('search', { default: '' })
const selectedPost = defineModel<string>('postFilter', { default: '' })
const previewItem = ref<SchedulerCalendarItem | null>(null)
watch(() => props.items, () => { previewItem.value = null })
watch([() => props.posts, () => props.postsLoaded], ([posts, postsLoaded]) => {
  if (postsLoaded && !posts.includes(selectedPost.value)) {
    selectedPost.value = ''
  }
}, { immediate: true })
const now = ref(new Date())
const plateFormats = ref<VehicleRegistrationPlateFormats | null>(null)
onMounted(async () => { plateFormats.value = await loadVehicleRegistrationPlateFormats().catch(() => null) })
let clock: ReturnType<typeof setInterval> | undefined
function isPlaceholderPost(post?: string | null) {
  return post === t('scheduler.noPost') || post === 'Без поста'
}
function normalizeLocation(post?: string | null) {
  return normalizeSchedulerLocation(isPlaceholderPost(post) ? 'Без поста' : post)
}
const placeholderPost = computed(() => props.posts.find(isPlaceholderPost) || t('scheduler.noPost'))
const window = computed(() => getSchedulerDayWindow(props.items, props.date, props.start, props.end))
const gridStart = computed(() => Math.floor(window.value.start / 30) * 30)
const gridEnd = computed(() => Math.ceil(window.value.end / 30) * 30)
const totalHeight = computed(() => Math.max(60, (gridEnd.value - gridStart.value) * pixelsPerMinute))
const postOptions = computed(() => props.posts.length ? props.posts : [placeholderPost.value])
const visiblePosts = computed(() => selectedPost.value ? [selectedPost.value] : postOptions.value)
const normalizedQuery = computed(() => search.value.trim().toLocaleLowerCase(i18n.global.locale.value))
const visibleItems = computed(() => props.items.filter(item => getSchedulerDayInterval(item, props.date) !== null && matchesSearch(item)))
const ticks = computed(() => {
  const values: number[] = []
  for (let minute = gridStart.value; minute <= gridEnd.value; minute += 30) values.push(minute)
  return values
})
const today = computed(() => {
  const date = now.value
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
})
const currentTime = computed(() => now.value.getHours() * 60 + now.value.getMinutes())
const showNowLine = computed(() => props.date.slice(0, 10) === today.value
  && currentTime.value >= gridStart.value && currentTime.value <= gridEnd.value)
const nowTop = computed(() => (currentTime.value - gridStart.value) * pixelsPerMinute)

function matchesSearch(item: SchedulerCalendarItem) {
  if (!normalizedQuery.value) return true
  const searchable = [
    item.vehicleDisplayName,
    item.vehicleSubtitle,
    item.registrationPlate,
    item.statusName,
    item.location,
    ...(item.serviceNames ?? []),
    ...(item.employeeNames ?? [])
  ].join(' ').toLocaleLowerCase(i18n.global.locale.value)
  return searchable.includes(normalizedQuery.value)
    || matchesRegistrationPlateSearch(item.registrationPlate, normalizedQuery.value, plateFormats.value)
}

function itemsForPost(post: string) {
  const normalizedPost = normalizeLocation(post)
  return visibleItems.value.filter(item => normalizeLocation(item.location) === normalizedPost)
}

function layoutForPost(post: string) {
  return getSchedulerDayLayout(itemsForPost(post), props.date, gridStart.value, gridEnd.value, pixelsPerMinute)
}

function slotMinutes(slotIndex: number) {
  return gridStart.value + slotIndex * 30
}

function slotStyle(minute: number) {
  return { top: `${(minute - gridStart.value) * pixelsPerMinute}px`, height: `${30 * pixelsPerMinute}px` }
}

function itemStyle(layout: ReturnType<typeof getSchedulerDayLayout>[number]) {
  const width = 100 / layout.laneCount
  return {
    top: `${layout.top}px`,
    height: `${layout.height}px`,
    left: `calc(${width * layout.lane}% + 3px)`,
    width: `calc(${width}% - 6px)`
  }
}

function cardClass(item: SchedulerCalendarItem) {
  if (item.handedOverAt) return 'scheduler-day__booking--handed-over'
  if (item.readyForHandoverAt) return 'scheduler-day__booking--ready'
  if (item.status === 2) return 'scheduler-day__booking--in-progress'
  if (item.status === 3) return 'scheduler-day__booking--completed'
  if (item.status === 4) return 'scheduler-day__booking--cancelled'
  return 'scheduler-day__booking--planned'
}

function statusText(item: SchedulerCalendarItem) {
  if (item.handedOverAt) return t('scheduler.handedOver')
  if (item.readyForHandoverAt) return t('scheduler.ready')

  const statusKeys: Record<number, string> = {
    1: 'scheduler.scheduled',
    2: 'scheduler.inProgress',
    3: 'scheduler.completed',
    4: 'scheduler.cancelled'
  }

  return statusKeys[item.status] ? t(statusKeys[item.status]!) : item.statusName || t('scheduler.statusUnknown')
}

function timeText(value?: string | null) {
  return value ? value.slice(0, 5) : ''
}

function itemTime(item: SchedulerCalendarItem) {
  const selectedDate = props.date.slice(0, 10)
  const startDate = item.date.slice(0, 10)
  const endDate = (item.plannedEndDate || startDate).slice(0, 10)
  const end = timeText(item.plannedEndTime)

  if (startDate < selectedDate) return endDate > selectedDate
    ? t('schedulerCalendar.continuing')
    : t('schedulerCalendar.until', { time: end || t('schedulerCalendar.endOfDay') })

  const start = timeText(item.plannedStartTime)
  if (!end) return start

  return `${start}–${endDate > selectedDate || end <= start ? '24:00' : end}`
}

function formatTick(minute: number) {
  if (minute >= 1440) return '24:00'
  return `${String(Math.floor(minute / 60)).padStart(2, '0')}:${String(minute % 60).padStart(2, '0')}`
}

function statusTone(item: SchedulerCalendarItem) {
  return item.status === 4 ? 'muted' : item.handedOverAt || item.readyForHandoverAt || item.status === 3 ? 'green' : item.status === 2 ? 'orange' : 'blue'
}

function accessibleBookingText(item: SchedulerCalendarItem) {
  return [itemTime(item), item.vehicleDisplayName, item.registrationPlate, statusText(item),
    ...(item.serviceNames ?? []), ...(item.employeeNames ?? [])].filter(Boolean).join(', ')
}

function canCreateAt(location: string, minute: number) {
  return props.canCreate && !closedPostKeys.value.has(normalizeLocation(location)) && minute < 1440
    && isSchedulerSlotAvailable(normalizedItems.value, props.date, isPlaceholderPost(location) ? 'Без поста' : location, minute)
}

function createAt(location: string, minute: number) {
  if (!canCreateAt(location, minute)) return

  const start = addSchedulerMinutes(props.date, minute)
  const end = addSchedulerMinutes(props.date, minute + 60)
  emit('create', { date: props.date.slice(0, 10), start: start.time, end: end.time, endDate: end.date, location: isPlaceholderPost(location) ? t('scheduler.noPost') : location })
}

function reschedule(item: SchedulerCalendarItem, event: MouseEvent) {
  event.preventDefault()
  event.stopPropagation()
  emit('reschedule', item)
}

function openPreview(item: SchedulerCalendarItem, event: MouseEvent) {
  if (!shouldOpenRecord(true, event)) return
  event.preventDefault()
  previewItem.value = item
}

function brandLogo(item: SchedulerCalendarItem) {
  return getVehicleBrandLogo(item.brandLogoKey)
}

onMounted(() => {
  clock = setInterval(() => { now.value = new Date() }, 60_000)
})

onBeforeUnmount(() => {
  if (clock) clearInterval(clock)
})
</script>

<template>
  <section class="scheduler-day" :aria-label="t('schedulerCalendar.dayAria')">
    <div class="scheduler-day__toolbar">
      <label class="scheduler-day__search">
        <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="10.8" cy="10.8" r="6.8" /><path d="m16 16 5 5" /></svg>
        <span class="visually-hidden">{{ t('schedulerCalendar.searchLabel') }}</span>
        <input v-model="search" type="search" :placeholder="t('schedulerCalendar.searchPlaceholder')">
      </label>
      <label class="scheduler-day__filter"><span class="visually-hidden">{{ t('schedulerControl.filterLocation') }}</span>
        <select v-model="selectedPost" :aria-label="t('schedulerCalendar.filterPostLabel')">
          <option value="">{{ t('schedulerControl.allLocations') }}</option>
          <option v-for="post in postOptions" :key="post" :value="post">{{ post }}</option>
        </select>
      </label>
    </div>

    <div class="scheduler-day__scroll">
      <div class="scheduler-day__grid" :style="{ '--day-height': `${totalHeight}px`, '--day-post-count': visiblePosts.length }">
        <div class="scheduler-day__corner">{{ t('schedulerCalendar.timeAxis') }}</div>
        <div v-for="post in visiblePosts" :key="post" class="scheduler-day__post-heading">{{ post }}<small v-if="closedPostKeys.has(normalizeLocation(post))"> · {{ t('schedulerCalendar.closed') }}</small></div>
        <div class="scheduler-day__axis" :style="{ height: `${totalHeight}px` }">
          <span v-for="tick in ticks" :key="tick" class="scheduler-day__tick" :class="{ 'scheduler-day__tick--hour': tick % 60 === 0 }" :style="{ top: `${(tick - gridStart) * pixelsPerMinute}px` }">{{ tick % 60 === 0 ? formatTick(tick) : '' }}</span>
        </div>
        <section v-for="post in visiblePosts" :key="post" class="scheduler-day__column" :aria-label="t('schedulerCalendar.postColumnAria', { post })" :style="{ height: `${totalHeight}px` }">
          <template v-for="(tick, index) in ticks.slice(0, -1)" :key="tick">
            <button
              v-if="canCreateAt(post, slotMinutes(index))"
              type="button"
              class="scheduler-day__slot"
              :style="slotStyle(slotMinutes(index))"
              :aria-label="t('schedulerCalendar.createAt', { post, time: formatTick(slotMinutes(index)) })"
              @click="createAt(post, slotMinutes(index))"
            ><span>{{ t('schedulerCalendar.create') }}</span></button>
          </template>
          <div
            v-for="entry in layoutForPost(post)"
            :key="entry.item.id"
            class="scheduler-day__booking"
            :class="[cardClass(entry.item), { 'scheduler-day__booking--compact': entry.height < 95, 'scheduler-day__booking--tiny': entry.height < 48 }]"
            :style="itemStyle(entry)"
          >
            <a
              class="scheduler-day__booking-link"
              :href="entry.item.detailUrl || `#maintenance/${entry.item.id}`"
              :aria-label="accessibleBookingText(entry.item)"
              @click="openPreview(entry.item, $event)"
            ></a>
            <div class="scheduler-day__booking-content" aria-hidden="true">
              <div class="scheduler-day__booking-top">
                <strong>{{ itemTime(entry.item) || t('schedulerCalendar.unknownTime') }}</strong>
                <span class="scheduler-day__status" :class="`scheduler-day__status--${statusTone(entry.item)}`">{{ statusText(entry.item) }}</span>
              </div>
              <div class="scheduler-day__vehicle">
                <img v-if="brandLogo(entry.item)" :src="brandLogo(entry.item)!.src" :alt="t('schedulerControl.logoAlt', { name: brandLogo(entry.item)!.name })" class="scheduler-day__brand">
                <span class="scheduler-day__vehicle-name" :title="entry.item.vehicleDisplayName">{{ entry.item.vehicleDisplayName }}</span>
              </div>
            </div>
            <UiButton
              v-if="canMove && entry.item.canReschedule && (entry.item.status === 1 || entry.item.status === 2)"
              variant="secondary"
              class="scheduler-day__move"
              @click="reschedule(entry.item, $event)"
            >{{ t('schedulerCalendar.reschedule') }}</UiButton>
          </div>
          <div v-if="showNowLine" class="scheduler-day__now" :style="{ top: `${nowTop}px` }" aria-hidden="true"><span></span></div>
          <p v-if="normalizedQuery && !itemsForPost(post).length" class="scheduler-day__empty">{{ t('schedulerCalendar.noMatch') }}</p>
          <p v-else-if="!itemsForPost(post).length" class="scheduler-day__empty">{{ t('schedulerCalendar.noRecords') }}</p>
        </section>
      </div>
    </div>
    <p v-if="!visibleItems.length && normalizedQuery" class="scheduler-day__empty-search">{{ t('schedulerCalendar.noRecordsForQuery', { query: search.trim() }) }}</p>
    <ul class="scheduler-day__legend" :aria-label="t('schedulerCalendar.legend')">
      <li><span class="scheduler-day__legend-dot scheduler-day__legend-dot--blue"></span>{{ t('scheduler.scheduled') }}</li>
      <li><span class="scheduler-day__legend-dot scheduler-day__legend-dot--orange"></span>{{ t('scheduler.inProgress') }}</li>
      <li><span class="scheduler-day__legend-dot scheduler-day__legend-dot--green"></span>{{ t('schedulerCalendar.greenStatus') }}</li>
      <li><span class="scheduler-day__legend-dot scheduler-day__legend-dot--muted"></span>{{ t('scheduler.cancelled') }}</li>
    </ul>
    <SchedulerRecordPreview :item="previewItem" @close="previewItem = null" />
  </section>
</template>

<style scoped>
.scheduler-day { min-width: 0; color: var(--crm-text, #202733); }
.scheduler-day__toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 0 0 14px; }
.scheduler-day__search { display: flex; align-items: center; gap: 8px; width: min(380px, 100%); min-height: 36px; padding: 0 10px; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 7px; background: var(--crm-surface, #fff); color: var(--crm-text-muted, #667085); }
.scheduler-day__search svg { width: 17px; height: 17px; flex: 0 0 auto; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; }
.scheduler-day__search input { width: 100%; min-width: 0; padding: 7px 0; border: 0; outline: 0; background: transparent; color: var(--crm-text, #202733); font: inherit; font-size: 12px; }
.scheduler-day__search input::placeholder { color: var(--crm-text-muted, #98a2b3); }
.scheduler-day__filter select { min-height: 36px; max-width: 220px; padding: 0 30px 0 10px; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 7px; background: var(--crm-surface, #fff); color: var(--crm-text, #202733); font: inherit; font-size: 12px; }
.scheduler-day__scroll { overflow-x: auto; overscroll-behavior-inline: contain; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 9px; background: var(--crm-surface, #fff); }
.scheduler-day__grid { display: grid; grid-template-columns: 54px repeat(var(--day-post-count), minmax(210px, 1fr)); min-width: max(100%, calc(54px + var(--day-post-count) * 210px)); }
.scheduler-day__corner, .scheduler-day__post-heading { position: sticky; top: 0; z-index: 5; display: flex; align-items: center; min-height: 44px; padding: 0 10px; border-bottom: 1px solid var(--crm-line, #e3e6eb); background: var(--crm-surface-soft, #f7f8fa); color: var(--crm-text-muted, #667085); font-size: 11px; font-weight: 650; }
.scheduler-day__corner { left: 0; z-index: 7; border-right: 1px solid var(--crm-line, #e3e6eb); }
.scheduler-day__post-heading { border-right: 1px solid var(--crm-line, #e3e6eb); color: var(--crm-text, #344054); }
.scheduler-day__axis { position: sticky; left: 0; z-index: 3; grid-column: 1; grid-row: 2; border-right: 1px solid var(--crm-line, #e3e6eb); background: var(--crm-surface, #fff); }
.scheduler-day__tick { position: absolute; right: 7px; z-index: 2; transform: translateY(-50%); color: var(--crm-text-muted, #98a2b3); font-size: 10px; font-variant-numeric: tabular-nums; }
.scheduler-day__tick--hour { color: var(--crm-text-muted, #667085); font-weight: 600; }
.scheduler-day__column { position: relative; grid-row: 2; overflow: hidden; border-right: 1px solid var(--crm-line, #e3e6eb); background-image: repeating-linear-gradient(to bottom, transparent 0, transparent calc(36px - 1px), var(--crm-line, #e3e6eb) calc(36px - 1px), var(--crm-line, #e3e6eb) 36px), repeating-linear-gradient(to bottom, transparent 0, transparent calc(72px - 1px), color-mix(in srgb, var(--crm-line, #e3e6eb) 65%, transparent) calc(72px - 1px), color-mix(in srgb, var(--crm-line, #e3e6eb) 65%, transparent) 72px); background-size: 100% 36px, 100% 72px; }
.scheduler-day__slot { position: absolute; z-index: 0; left: 0; width: 100%; padding: 0 5px; border: 0; background: transparent; color: var(--opencrm-accent, #e67337); text-align: left; cursor: pointer; }
.scheduler-day__slot span { display: inline-flex; align-items: center; height: 24px; padding: 0 7px; border: 1px dashed transparent; border-radius: 5px; font-size: 10px; opacity: 0; transition: opacity .12s, background-color .12s; }
.scheduler-day__slot:hover span, .scheduler-day__slot:focus-visible span { border-color: color-mix(in srgb, var(--opencrm-accent, #e67337) 45%, transparent); background: var(--crm-surface, #fff); opacity: 1; }
.scheduler-day__booking { position: absolute; z-index: 1; display: flex; flex-direction: column; gap: 3px; overflow: hidden; padding: 6px 7px; border: 1px solid var(--booking-line); border-left: 3px solid var(--booking-accent); border-radius: 6px; background: var(--booking-bg); color: var(--crm-text, #202733); box-shadow: 0 1px 2px var(--crm-shadow, #1018280d); }
.scheduler-day__booking:hover { z-index: 2; filter: saturate(1.08); box-shadow: 0 2px 8px var(--crm-shadow, #1018281a); }
.scheduler-day__booking-link { position: absolute; z-index: 1; inset: 0; border-radius: inherit; color: inherit; text-decoration: none; }
.scheduler-day__booking-link:focus-visible { outline: 2px solid var(--opencrm-accent, #e67337); outline-offset: -2px; }
.scheduler-day__booking-content { position: relative; z-index: 0; display: flex; flex-direction: column; gap: 3px; min-height: 0; pointer-events: none; }
.scheduler-day__booking--planned { --booking-bg: color-mix(in srgb, #6888c8 11%, var(--crm-surface, #fff)); --booking-line: color-mix(in srgb, #6d91d4 38%, var(--crm-line, #e3e6eb)); --booking-accent: #6888c8; }
.scheduler-day__booking--in-progress { --booking-bg: color-mix(in srgb, #e67337 11%, var(--crm-surface, #fff)); --booking-line: color-mix(in srgb, #e88c5c 42%, var(--crm-line, #e3e6eb)); --booking-accent: #e67337; }
.scheduler-day__booking--ready, .scheduler-day__booking--handed-over, .scheduler-day__booking--completed { --booking-bg: color-mix(in srgb, #389b82 11%, var(--crm-surface, #fff)); --booking-line: color-mix(in srgb, #53a68d 38%, var(--crm-line, #e3e6eb)); --booking-accent: #389b82; }
.scheduler-day__booking--cancelled { --booking-bg: color-mix(in srgb, #98a2b3 11%, var(--crm-surface, #fff)); --booking-line: var(--crm-line, #e3e6eb); --booking-accent: #98a2b3; opacity: .72; }
.scheduler-day__booking-top { display: flex; align-items: flex-start; justify-content: space-between; gap: 5px; min-width: 0; }
.scheduler-day__booking-top strong { overflow: hidden; color: var(--crm-text, #202733); font-size: 12px; font-variant-numeric: tabular-nums; text-overflow: ellipsis; white-space: nowrap; }
.scheduler-day__status { flex: 0 0 auto; max-width: 58%; overflow: hidden; padding: 2px 5px; border-radius: 999px; font-size: 10px; line-height: 1.2; text-overflow: ellipsis; white-space: nowrap; }
.scheduler-day__status--blue { background: color-mix(in srgb, #6888c8 12%, var(--crm-surface, #fff)); color: var(--crm-text, #355a9a); }
.scheduler-day__status--orange { background: color-mix(in srgb, #e67337 12%, var(--crm-surface, #fff)); color: var(--crm-text, #a94d20); }
.scheduler-day__status--green { background: color-mix(in srgb, #389b82 12%, var(--crm-surface, #fff)); color: var(--crm-text, #287863); }
.scheduler-day__status--muted { background: var(--crm-surface-soft, #e8e9ec); color: var(--crm-text-muted, #626b78); }
.scheduler-day__vehicle { display: flex; align-items: center; min-width: 0; gap: 5px; }
.scheduler-day__brand { flex: 0 0 24px; width: 24px; height: 24px; padding: 3px; border: 1px solid var(--crm-line, #e1e3e7); border-radius: 50%; background: #fff; object-fit: contain; }
.scheduler-day__vehicle-name { overflow: hidden; color: var(--crm-text, #202733); font-size: 13px; font-weight: 700; line-height: 1.25; text-overflow: ellipsis; }
.scheduler-day__move { position: relative; z-index: 2; align-self: flex-end; min-height: 32px; margin-top: auto; padding: 6px 10px; font-size: 12px; white-space: nowrap; }
.scheduler-day__now { position: absolute; z-index: 3; left: 0; width: 100%; height: 1px; background: #d84c4c; pointer-events: none; }
.scheduler-day__now span { position: absolute; top: -3px; left: -3px; width: 7px; height: 7px; border-radius: 50%; background: #d84c4c; }
.scheduler-day__empty { position: absolute; top: 10px; left: 50%; transform: translateX(-50%); margin: 0; color: var(--crm-text-muted, #98a2b3); font-size: 11px; pointer-events: none; }
.scheduler-day__empty-search { margin: 10px 0 0; color: var(--crm-text-muted, #667085); font-size: 12px; }
.scheduler-day__legend { display: flex; flex-wrap: wrap; gap: 9px 18px; margin: 12px 0 0; padding: 0; color: var(--crm-text-muted, #667085); font-size: 11px; list-style: none; }
.scheduler-day__legend li { display: inline-flex; align-items: center; gap: 6px; }
.scheduler-day__legend-dot { width: 8px; height: 8px; border-radius: 50%; }
.scheduler-day__legend-dot--blue { background: #6888c8; }
.scheduler-day__legend-dot--orange { background: #e67337; }
.scheduler-day__legend-dot--green { background: #389b82; }
.scheduler-day__legend-dot--muted { background: #98a2b3; }
.visually-hidden { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; border: 0; }
.scheduler-day button:focus-visible, .scheduler-day a:focus-visible, .scheduler-day select:focus-visible { outline: 2px solid var(--opencrm-accent, #e67337); outline-offset: 2px; }
@media (max-width: 620px) {
  .scheduler-day__toolbar { align-items: stretch; flex-direction: column; }
  .scheduler-day__search, .scheduler-day__filter select { width: 100%; max-width: none; }
  .scheduler-day__booking { padding: 5px; }
  .scheduler-day__booking-top { flex-direction: column; gap: 2px; }
}
.scheduler-day__booking--compact .scheduler-day__move { display: none; }
.scheduler-day__booking--tiny { padding: 0 4px; justify-content: center; }
.scheduler-day__booking--tiny .scheduler-day__booking-top,
.scheduler-day__booking--tiny .scheduler-day__move,
.scheduler-day__booking--tiny .scheduler-day__brand { display: none; }
.scheduler-day__booking--tiny .scheduler-day__vehicle-name { font-size: 11px; line-height: 1; white-space: nowrap; }
.scheduler-day__search:focus-within {
  border-color: var(--opencrm-accent, #e67337);
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--opencrm-accent, #e67337) 18%, transparent);
}
.scheduler-day__search input:focus, .scheduler-day__search input:focus-visible { outline: none; box-shadow: none; }
.scheduler-day__tick:first-child { transform: none; }
.scheduler-day__tick:last-child { transform: translateY(-100%); }
</style>

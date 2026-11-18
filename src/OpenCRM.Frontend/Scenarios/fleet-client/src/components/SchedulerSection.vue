<script setup lang="ts">
import { loadWorkspaceSetup, type WorkspaceSetup } from '@opencrm/workspace'
import { SchedulerClosedDay, SchedulerSlots, SchedulerDayCalendar, SchedulerList, SchedulerToolbar, WorkspaceAccessNotice } from '@opencrm/workspace'
import { computed, onMounted, ref, watch } from 'vue'
import { UiButton, UiCard, UiLoader, formatDateOnly } from '@opencrm/ui'
import { useLocalization } from '@opencrm/ui'
import '@opencrm/workspace/schedulerMessages'
import { WorkspacePageHelpButton } from '@opencrm/workspace'
import {
  loadScenarioCatalog,
  loadMaintenanceCalendar,
  type FleetCatalogItem,
  type FleetMaintenanceCalendarItem,
  type FleetWorkspaceContext
} from '@/api/fleetBff'
import MaintenanceCalendar from './MaintenanceCalendar.vue'
import { hasWorkingSchedule, workingIntervals } from '@opencrm/workspace'

const props = defineProps<{
  context: FleetWorkspaceContext
}>()

const emit = defineEmits<{
  changed: []
}>()
const { t, locale } = useLocalization()

const selectedDate = ref(formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC'))
const listDays = ref(7)
const schedule = ref<WorkspaceSetup | null>(null)
const workdayStart = ref('08:00')
const workdayEnd = ref('22:00')
const items = ref<FleetMaintenanceCalendarItem[]>([])
const nextDayItems = ref<FleetMaintenanceCalendarItem[]>([])
const workPosts = ref<FleetCatalogItem[]>([])
const viewMode = ref<'day' | 'slots' | 'list' | 'calendar'>('day')
const isLoading = ref(false)
const errorMessage = ref('')
const itemsLimitWarning = ref('')
const scheduleError = ref('')

const canWrite = computed(() => props.context.workspaceAccess?.isReadOnly !== true)
const readOnlyMessage = computed(() => props.context.workspaceAccess?.readOnlyMessage ?? '')
const billingHref = computed(() => '#billing')
const schedulerSettingsKey = computed(() => `opencrm-fleet-scheduler:${props.context.organization.id}`)
const viewModeStorageKey = computed(() => `opencrm-fleet-scheduler:${props.context.organization.id}:view-mode`)
const sortedItems = computed(() =>
  [...items.value].sort((left, right) =>
    `${left.date.slice(0, 10)}${left.plannedStartTime ?? '99:99'}${left.vehicleDisplayName}`.localeCompare(
      `${right.date.slice(0, 10)}${right.plannedStartTime ?? '99:99'}${right.vehicleDisplayName}`
    )
  )
)
const noTimeItems = computed(() => sortedItems.value.filter(item => !item.plannedStartTime))
const postColumns = computed(() => {
  const configuredPosts = workPosts.value
    .filter(post => post.isActive)
    .map(post => normalizeLocation(post.name))
  const usedLocations = sortedItems.value.filter(item => item.plannedStartTime).map(item => normalizeLocation(item.location))
  const unique = Array.from(new Set([...configuredPosts, ...usedLocations]))
  return unique.length ? unique : [t('scheduler.noZone')]
})
const selectedWorkingDay = computed(() => {
  const date = new Date(selectedDate.value + 'T12:00:00')
  return schedule.value?.weeklySchedule?.find(day => day.dayOfWeek === date.getDay())
})
watch(selectedWorkingDay, day => {
  if (!day?.isWorking || !day.startTime || !day.endTime) return
  workdayStart.value = day.startTime.slice(0, 5)
  workdayEnd.value = day.endTime.slice(0, 5)
})
const workingHoursText = computed(() => {
  const day = selectedWorkingDay.value
  if (!day || !schedule.value?.weeklySchedule?.some(value => value.isWorking)) return ''
  if (!day.isWorking) return ''
  return t('scheduler.workingHours', { hours: workingIntervals(day).map(interval => interval.startTime + '–' + interval.endTime).join(', ') })
})
const isScheduleMissing = computed(() => schedule.value !== null && !hasWorkingSchedule(schedule.value.weeklySchedule ?? []))
const isClosedDay = computed(() => !isScheduleMissing.value && selectedWorkingDay.value?.isWorking === false)
const slotIntervals = computed(() => {
  if (selectedWorkingDay.value) {
    return workingIntervals(selectedWorkingDay.value)
  }
  return [{ startTime: workdayStart.value, endTime: workdayEnd.value }]
})

function normalizeLocation(value?: string | null) {
  const text = value?.trim()
  return text || t('scheduler.noZone')
}

function formatTime(value?: string | null) {
  return value ? value.slice(0, 5) : ''
}

function parseTimeToMinutes(value?: string | null) {
  const time = formatTime(value)
  const match = time.match(/^(\d{2}):(\d{2})$/)
  if (!match) {
    return null
  }

  return Number(match[1]) * 60 + Number(match[2])
}

function loadSchedulerSettings() {
  try {
    const raw = window.localStorage?.getItem(schedulerSettingsKey.value)
    if (!raw) {
      return
    }

    const settings = JSON.parse(raw) as { workdayStart?: string, workdayEnd?: string }
    const start = formatTime(settings.workdayStart)
    const end = formatTime(settings.workdayEnd)

    if (parseTimeToMinutes(start) !== null) {
      workdayStart.value = start
    }

    if (parseTimeToMinutes(end) !== null) {
      workdayEnd.value = end
    }
  } catch {
    workdayStart.value = '08:00'
    workdayEnd.value = '22:00'
  }
}

function loadViewMode() {
  try {
    const value = window.localStorage?.getItem(viewModeStorageKey.value)
    if (value === 'day' || value === 'slots' || value === 'list' || value === 'calendar') {
      viewMode.value = value
    }
  } catch {
    viewMode.value = 'day'
  }
}

function saveSchedulerSettings() {
  try {
    window.localStorage?.setItem(schedulerSettingsKey.value, JSON.stringify({
      workdayStart: workdayStart.value,
      workdayEnd: workdayEnd.value
    }))
  } catch {
    // Local display settings are optional.
  }
}

function openCalendarDay(date: string) {
  selectedDate.value = date
  setViewMode('day')
}

function setViewMode(mode: 'day' | 'slots' | 'list' | 'calendar') {
  viewMode.value = mode

  try {
    window.localStorage?.setItem(viewModeStorageKey.value, mode)
  } catch {
    // View preference is optional.
  }
}

function getSlotEndTime(time: string) {
  const [hours, minutes] = time.split(':').map(Number)
  const date = new Date()
  date.setHours(hours + 1, minutes, 0, 0)
  return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
}

function getMaintenanceStatusName(status: number) {
  const names: Record<number, string> = {
    1: t('scheduler.scheduled'),
    2: t('scheduler.inProgress'),
    3: t('scheduler.completed'),
    4: t('scheduler.cancelled')
  }

  return names[status] ?? t('scheduler.statusUnknown')
}

function applyMaintenanceStatus(item: FleetMaintenanceCalendarItem, status: number) {
  item.status = status
  item.statusName = getMaintenanceStatusName(status)
  void loadItems()
  emit('changed')
}

function buildBookingHref(time?: string, location?: string) {
  const params = new URLSearchParams()
  params.set('date', selectedDate.value)

  if (time) {
    params.set('start', time)
    params.set('end', getSlotEndTime(time))
  }

  if (location && location !== t('scheduler.noZone')) {
    params.set('location', location)
  }

  return `#maintenance/new?${params.toString()}`
}

function createFromCalendar(slot: { date: string; start?: string; end?: string; endDate?: string; location?: string }) {
  if (!canWrite.value) return
  const params = new URLSearchParams({ date: slot.date })
  if (slot.start) params.set('start', slot.start)
  if (slot.end) params.set('end', slot.end)
  if (slot.endDate) params.set('endDate', slot.endDate)
  if (slot.location && slot.location !== t('scheduler.noZone')) params.set('location', slot.location)
  window.location.hash = '#maintenance/new?' + params.toString()
}

let itemsRequest = 0

async function loadItems() {
  const request = ++itemsRequest
  const date = selectedDate.value
  const nextDate = new Date(date + 'T00:00:00Z')
  nextDate.setUTCDate(nextDate.getUTCDate() + (viewMode.value === 'list' ? listDays.value - 1 : 1))
  const dateTo = nextDate.toISOString().slice(0, 10)
  isLoading.value = true
  errorMessage.value = ''

  try {
    const loaded = await loadMaintenanceCalendar(props.context.links.api.calendar, date, dateTo, { includeAllStatuses: true })
    if (request !== itemsRequest) return
    itemsLimitWarning.value = loaded.length >= 500 ? t('scheduler.itemLimit', { count: 500 }) : ''
    const localizedItems = loaded.map(localizeStatus)
    items.value = viewMode.value === 'list' ? localizedItems : localizedItems.filter(item => item.date.slice(0, 10) <= date)
    nextDayItems.value = localizedItems.filter(item => item.date.slice(0, 10) > date)
  } catch (error) {
    if (request !== itemsRequest) return
    errorMessage.value = error instanceof Error ? error.message : t('scheduler.loadError')
  } finally {
    if (request === itemsRequest) isLoading.value = false
  }
}

function localizeStatus(item: FleetMaintenanceCalendarItem) {
  return {
    ...item,
    statusName: item.handedOverAt ? t('scheduler.handedOver') : item.readyForHandoverAt ? t('scheduler.ready') : getMaintenanceStatusName(item.status)
  }
}

async function loadWorkPosts() {
  try {
    const catalog = await loadScenarioCatalog(props.context.organization.id)
    workPosts.value = catalog.workPosts ?? []
  } catch {
    workPosts.value = []
  }
}

async function loadOrganizationSchedule() {
  try {
    schedule.value = await loadWorkspaceSetup(props.context.organization.id)
  } catch {
    schedule.value = null
    scheduleError.value = t('scheduler.scheduleLoadError')
  }
}

onMounted(() => {
  void loadOrganizationSchedule()
  loadSchedulerSettings()
  loadViewMode()
  void loadWorkPosts()
  void loadItems()
})
watch([selectedDate, listDays, viewMode], loadItems)
watch([workdayStart, workdayEnd], saveSchedulerSettings)
watch(locale, () => {
  items.value = items.value.map(localizeStatus)
  nextDayItems.value = nextDayItems.value.map(localizeStatus)
})
</script>

<template>
  <UiCard page :title="t('scheduler.maintenancePlanTitle')">
    <template #actions>
      <UiButton class="scheduler-create" :disabled="context.workspaceAccess?.isReadOnly === true && !context.organization.canManageBilling" :href="canWrite ? buildBookingHref() : billingHref" :variant="canWrite ? 'primary' : 'secondary'">
        <span aria-hidden="true">＋</span> {{ t('scheduler.newAppointment') }}
      </UiButton>
      <WorkspacePageHelpButton section-code="scheduler" :title="t('scheduler.maintenancePlanTitle')" />
    </template>
    <SchedulerToolbar :time-zone-id="context.organization.timeZoneId"
      v-model:date="selectedDate"
      v-model:start="workdayStart"
      v-model:end="workdayEnd"
      v-model:range-days="listDays"
      :view-mode="viewMode"
      @update:view-mode="setViewMode"
    />
    <p v-if="workingHoursText" class="scheduler-hours">{{ workingHoursText }}</p>
    <p v-if="itemsLimitWarning && viewMode !== 'calendar'" class="scheduler-hours" role="status">{{ itemsLimitWarning }}</p>
    <p v-if="scheduleError" class="scheduler-hours" role="status">{{ scheduleError }}</p>
    <WorkspaceAccessNotice :can-manage-billing="context.organization.canManageBilling === true" v-if="readOnlyMessage" :message="readOnlyMessage" :href="billingHref" />

    <MaintenanceCalendar :time-zone-id="context.organization.timeZoneId"
      @day="openCalendarDay"
      @create="createFromCalendar"
      v-if="viewMode === 'calendar'"
      :calendar-url="context.links.api.calendar"
      :organization-id="context.organization.id"
      :storage-key="`opencrm-fleet-calendar:${context.organization.id}:scheduler`"
      :include-all-statuses="true"
      :can-create="canWrite"
      :initial-date="selectedDate"
      :weekly-schedule="schedule?.weeklySchedule"
      :empty-text="t('scheduler.noWorkPeriod')"
    />

    <div v-else-if="isLoading" class="scheduler-loading-card">
      <UiLoader :text="t('scheduler.loadingPlan')" />
      <div class="scheduler-loading-board" aria-hidden="true">
        <span v-for="index in 12" :key="index"></span>
      </div>
    </div>
    <div v-else-if="errorMessage" class="fleet-state fleet-state-error" role="alert">
      {{ errorMessage }} <UiButton variant="secondary" @click="loadItems">{{ t('scheduler.retry') }}</UiButton>
    </div>

    <template v-else-if="(isScheduleMissing || isClosedDay) && (viewMode === 'day' || viewMode === 'slots')">
      <SchedulerClosedDay :time-zone-id="context.organization.timeZoneId" :date="selectedDate" :schedule="schedule?.weeklySchedule ?? []" :can-manage="schedule?.canManage === true"
        @select-date="selectedDate = $event" />
      <template v-if="sortedItems.length">
        <h3 class="scheduler-without-time">{{ isScheduleMissing ? t('scheduler.recordsSelectedDate') : t('scheduler.recordsClosedDay') }} · {{ sortedItems.length }}</h3>
        <SchedulerList :items="sortedItems" :organization-id="context.organization.id" :can-write="canWrite"
          @changed="(item, status) => applyMaintenanceStatus(item, status)" @error="message => errorMessage = message" />
      </template>
    </template>

    <template v-else-if="viewMode === 'day'">
      <SchedulerDayCalendar :items="[...items, ...nextDayItems]" :date="selectedDate" :posts="postColumns"
        :start="workdayStart" :end="workdayEnd" :can-create="canWrite"
        @create="createFromCalendar" />
      <template v-if="noTimeItems.length">
        <h3 class="scheduler-without-time">{{ t('scheduler.withoutTime', { count: noTimeItems.length }) }}</h3>
        <SchedulerList :items="noTimeItems" :organization-id="context.organization.id" :can-write="canWrite"
          @changed="(item, status) => applyMaintenanceStatus(item, status)" @error="message => errorMessage = message" />
      </template>
    </template>

    <template v-else-if="viewMode === 'slots'">
      <SchedulerSlots :items="[...items, ...nextDayItems]" :date="selectedDate" :posts="postColumns"
        :intervals="slotIntervals" :can-create="canWrite" @create="createFromCalendar" />
      <template v-if="noTimeItems.length">
        <h3 class="scheduler-without-time">{{ t('scheduler.withoutTime', { count: noTimeItems.length }) }}</h3>
        <SchedulerList :items="noTimeItems" :organization-id="context.organization.id" :can-write="canWrite"
          @changed="(item, status) => applyMaintenanceStatus(item, status)" @error="message => errorMessage = message" />
      </template>
    </template>

    <SchedulerList v-else-if="sortedItems.length" :items="sortedItems" :organization-id="context.organization.id" :can-write="canWrite"
      @changed="(item, status) => applyMaintenanceStatus(item, status)" @error="message => errorMessage = message" />
    <div v-else class="fleet-empty">
      {{ t('scheduler.emptyDay') }}
    </div>
  </UiCard>
</template>

<style scoped>
.scheduler-without-time { margin: 22px 0 12px; font-size: 14px; color: var(--crm-text-muted, #667085); }
.scheduler-hours { color: var(--crm-text-muted, #667085); font-size: 13px; margin: 8px 0; }
.scheduler-hours-label { display: block; color: var(--crm-text-muted, #667085); font-size: 10px; padding: 2px 6px; }
.scheduler-slot--outside { background: var(--crm-surface-soft, #f4f6f8); }
</style>

<script setup lang="ts">
import { renderSchedulerCalendarEvent, schedulerCalendarStatus, SchedulerDayRecords, SchedulerRecordPreview, getSchedulerDayInterval, getVehicleBrandLogo, scheduleBusinessHours, type WorkspaceWorkingDay } from '@opencrm/workspace'
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiLoader, formatDateOnly, formatLocalDateTime, useLocalization } from '@opencrm/ui'
import { formatLocaleDate } from '@opencrm/ui/i18n'
import { fullCalendarLocaleCode, getFullCalendarLocales } from '@opencrm/workspace/calendarLocale'
import '@opencrm/workspace/schedulerMessages'
import { loadMaintenanceCalendar, type AutoServiceMaintenanceCalendarItem } from '@/api/autoserviceBff'

const props = withDefaults(defineProps<{
  calendarUrl: string
  timeZoneId?: string | null
  organizationId: string
  storageKey: string
  includeAllStatuses?: boolean
  canCreate?: boolean
  initialDate?: string
  weeklySchedule?: WorkspaceWorkingDay[]
  emptyText?: string
}>(), {
  includeAllStatuses: false,
  canCreate: false,
  initialDate: ''
})

const emit = defineEmits<{
  day: [date: string]
  navigate: [date: string]
  create: [slot: { date: string, start?: string, end?: string, endDate?: string, location?: string }]
}>()
const { t, locale } = useLocalization()

const calendarElement = ref<HTMLElement | null>(null)
const errorMessage = ref('')
const isLoading = ref(false)
const calendarItems = ref<AutoServiceMaintenanceCalendarItem[]>([])
const selectedDate = ref(normalizeDate(props.initialDate))
const isDayModalOpen = ref(false)
const previewItem = ref<AutoServiceMaintenanceCalendarItem | null>(null)
watch(() => props.organizationId, () => { previewItem.value = null })
let calendarRequest = 0
const limitWarning = ref('')
let calendar: InstanceType<NonNullable<typeof window.FullCalendar>['Calendar']> | null = null

watch(locale, value => {
  calendar?.setOption('locale', fullCalendarLocaleCode(value))
  calendar?.setOption('noEventsText', props.emptyText || t('maintenanceCalendar.noRecords'))
  limitWarning.value = calendarItems.value.length >= 500
    ? t('maintenanceCalendar.limitWarning', { count: 500 })
    : ''
})

watch(() => props.weeklySchedule, () => {
  calendar?.setOption('businessHours', scheduleBusinessHours(props.weeklySchedule))
}, { deep: true })

function normalizeDate(value?: string | null) {
  return value?.slice(0, 10) || formatDateOnly(undefined, props.timeZoneId || 'UTC')
}

function normalizeTime(value?: string | null) {
  const match = value?.match(/^(\d{1,2}):(\d{2})/)
  return match ? `${match[1].padStart(2, '0')}:${match[2]}` : ''
}

function addMinutes(date: Date, minutes: number) {
  const next = new Date(date)
  next.setMinutes(next.getMinutes() + minutes)
  return next
}

function formatDate(value: Date) {
  const year = value.getFullYear()
  const month = String(value.getMonth() + 1).padStart(2, '0')
  const day = String(value.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function formatDateTime(value: Date) {
  return `${formatDate(value)}T${String(value.getHours()).padStart(2, '0')}:${String(value.getMinutes()).padStart(2, '0')}:00`
}

function getRangeEnd(value: Date) {
  const end = new Date(value)
  end.setDate(end.getDate() - 1)
  return formatDate(end)
}

function resolveDetailHash(item: AutoServiceMaintenanceCalendarItem) {
  return `#maintenance/${item.id}`
}

function isOnDate(item: AutoServiceMaintenanceCalendarItem, date: string) {
  if (item.plannedStartTime) return getSchedulerDayInterval(item, date) !== null
  return normalizeDate(item.date) <= date && normalizeDate(item.plannedEndDate || item.date) >= date
}

function openDetail(item: AutoServiceMaintenanceCalendarItem) {
  previewItem.value = item
}

function updateSelectedDayClass() {
  calendarElement.value?.querySelectorAll<HTMLElement>('.fc-daygrid-day[data-date]').forEach(cell => {
    cell.classList.toggle('autoservice-calendar__selected-day', cell.dataset.date === selectedDate.value)
  })
}

function resolveEventClass(item: AutoServiceMaintenanceCalendarItem) {
  if (item.status === 4) {
    return 'autoservice-calendar-event--muted'
  }

  if (item.handedOverAt || item.readyForHandoverAt || item.status === 3) {
    return 'autoservice-calendar-event--done'
  }

  if (item.status === 2) {
    return 'autoservice-calendar-event--progress'
  }

  return 'autoservice-calendar-event--planned'
}

function mapEvent(item: AutoServiceMaintenanceCalendarItem) {
  const date = normalizeDate(item.date)
  const endDate = normalizeDate(item.plannedEndDate || item.date)
  const startTime = normalizeTime(item.plannedStartTime)
  const endTime = normalizeTime(item.plannedEndTime)
  const title = item.vehicleDisplayName || item.typeName || ''

  if (!startTime) {
    const exclusiveEnd = new Date(`${endDate}T00:00:00`)
    exclusiveEnd.setDate(exclusiveEnd.getDate() + 1)
    return {
      id: item.id,
      title,
      start: date,
      end: formatDate(exclusiveEnd),
      allDay: true,
      url: resolveDetailHash(item),
      classNames: [resolveEventClass(item), schedulerCalendarStatus(item)],
      extendedProps: item
    }
  }

  const start = new Date(`${date}T${startTime}:00`)
  const end = endTime ? new Date(`${endDate}T${endTime}:00`) : addMinutes(start, 60)

  return {
    id: item.id,
    title,
    start: formatDateTime(start),
    end: formatDateTime(end),
    allDay: false,
    url: resolveDetailHash(item),
    classNames: [resolveEventClass(item), schedulerCalendarStatus(item)],
    extendedProps: item
  }
}

function getStoredView() {
  try {
    const value = window.localStorage?.getItem(props.storageKey)
    if (value === 'listWeek') return 'timeGridWeek'
    return value && ['dayGridMonth', 'timeGridWeek', 'timeGridDay'].includes(value)
      ? value
      : 'dayGridMonth'
  } catch {
    return 'dayGridMonth'
  }
}

function saveStoredView(viewName: string) {
  try {
    window.localStorage?.setItem(props.storageKey, viewName)
  } catch {
    // Calendar view preference is optional.
  }
}

onMounted(() => {
  if (!calendarElement.value || !window.FullCalendar?.Calendar) {
    errorMessage.value = t('maintenanceCalendar.calendarUnavailable')
    return
  }

  calendar = new window.FullCalendar.Calendar(calendarElement.value, {
    locale: fullCalendarLocaleCode(locale.value),
    locales: getFullCalendarLocales(),
    firstDay: 1,
    initialDate: props.initialDate || formatDateOnly(undefined, props.timeZoneId || 'UTC'),
    // Календарь оперирует местными датами и часами записей.
    now: () => new Date(formatLocalDateTime(new Date(), props.timeZoneId || 'UTC')),
    initialView: getStoredView(),
    height: 680,
    fixedWeekCount: true,
    nowIndicator: true,
    slotEventOverlap: false,
    eventMinHeight: 56,
    eventShortHeight: 56,
    moreLinkClick: (arg: { date: Date, jsEvent: Event }) => {
      const trigger = (arg.jsEvent.target as HTMLElement).closest<HTMLElement>('.fc-more-link')
      trigger?.focus()
      selectedDate.value = formatDate(arg.date)
      isDayModalOpen.value = true
      updateSelectedDayClass()
      return false
    },
    moreLinkDidMount: (arg: { el: HTMLElement }) => {
      arg.el.tabIndex = 0
      arg.el.setAttribute('role', 'button')
      arg.el.addEventListener('keydown', event => {
        if (event.key !== 'Enter' && event.key !== ' ') return
        event.preventDefault()
        arg.el.click()
      })
    },
    moreLinkContent: (arg: { num: number }) => t('maintenanceCalendar.moreLink', { count: arg.num }),
    headerToolbar: {
      left: 'prev,next today',
      center: 'title',
      right: 'dayGridMonth,timeGridWeek,timeGridDay'
    },
    views: {
      dayGridMonth: { dayMaxEvents: 2 },
      timeGridWeek: { allDaySlot: true, eventMaxStack: 2 },
      timeGridDay: { allDaySlot: true }
    },
    slotMinTime: '00:00:00',
    slotMaxTime: '24:00:00',
    scrollTime: props.weeklySchedule?.find(day => day.isWorking)?.startTime || '08:00:00',
    slotDuration: '00:30:00',
    businessHours: scheduleBusinessHours(props.weeklySchedule),
    slotLabelFormat: { hour: '2-digit', minute: '2-digit', hour12: false },
    eventTimeFormat: { hour: '2-digit', minute: '2-digit', hour12: false },
    noEventsText: props.emptyText || t('maintenanceCalendar.noRecords'),
    events: async (
      range: { start: Date, end: Date },
      success: (events: ReturnType<typeof mapEvent>[]) => void,
      failure: (error: Error) => void
    ) => {
      const request = ++calendarRequest
      isLoading.value = true

      try {
        errorMessage.value = ''
        const items = await loadMaintenanceCalendar(
          props.calendarUrl,
          formatDate(range.start),
          getRangeEnd(range.end),
          { includeAllStatuses: props.includeAllStatuses }
        )
        if (request !== calendarRequest) {
          success(items.map(mapEvent))
          return
        }
        calendarItems.value = items
        limitWarning.value = items.length >= 500 ? t('maintenanceCalendar.limitWarning', { count: 500 }) : ''
        success(items.map(mapEvent))
      } catch (error) {
        const message = error instanceof Error ? error.message : t('maintenanceCalendar.calendarLoadError')
        if (request === calendarRequest) errorMessage.value = message
        failure(new Error(message))
      } finally {
        if (request === calendarRequest) isLoading.value = false
      }
    },
    datesSet: (arg: { view: { type: string, calendar: { getDate: () => Date, scrollToTime: (time: string) => void } } }) => {
      saveStoredView(arg.view.type)
      const height = arg.view.type === 'dayGridMonth' ? 'auto' : 680
      calendar?.setOption('height', height)
      if (arg.view.type.startsWith('timeGrid')) {
        requestAnimationFrame(() => {
          if (calendar?.view?.type === arg.view.type) {
            arg.view.calendar.scrollToTime(props.weeklySchedule?.find(day => day.isWorking)?.startTime || '08:00:00')
          }
        })
      }
      const visibleDate = formatDate(arg.view.calendar.getDate())
      emit('navigate', visibleDate)
      if (arg.view.type === 'dayGridMonth' && visibleDate.slice(0, 7) !== selectedDate.value.slice(0, 7)) {
        selectedDate.value = visibleDate
      }
      updateSelectedDayClass()
    },
    dayCellDidMount: (arg: { date: Date, el: HTMLElement }) => {
      arg.el.tabIndex = 0
      arg.el.setAttribute('aria-label', formatLocaleDate(arg.date, { dateStyle: 'full' }))
      arg.el.addEventListener('keydown', event => {
        if (event.target !== arg.el || event.key !== 'Enter' && event.key !== ' ') return
        event.preventDefault()
        selectedDate.value = formatDate(arg.date)
        isDayModalOpen.value = true
        updateSelectedDayClass()
      })
    },
    dayCellClassNames: (arg: { date: Date }) =>
      formatDate(arg.date) === selectedDate.value ? ['autoservice-calendar__selected-day'] : [],
    dateClick: (arg: { date: Date, jsEvent: Event, view: { type: string } }) => {
      if (arg.view.type === 'dayGridMonth') {
        selectedDate.value = formatDate(arg.date)
        isDayModalOpen.value = true
        updateSelectedDayClass()
        return
      }

      if (!props.canCreate) {
        return
      }

      arg.jsEvent.preventDefault()
      const date = formatDate(arg.date)
      const start = arg.date.getHours() || arg.date.getMinutes()
        ? `${String(arg.date.getHours()).padStart(2, '0')}:${String(arg.date.getMinutes()).padStart(2, '0')}`
        : undefined
      const endDate = start ? addMinutes(arg.date, 60) : undefined
      emit('create', {
        date,
        start,
        end: endDate ? `${String(endDate.getHours()).padStart(2, '0')}:${String(endDate.getMinutes()).padStart(2, '0')}` : undefined,
        endDate: endDate ? formatDate(endDate) : undefined
      })
    },
    eventClick: (arg: { event: { extendedProps: AutoServiceMaintenanceCalendarItem }, jsEvent: MouseEvent }) => {
      if (arg.jsEvent.ctrlKey || arg.jsEvent.metaKey || arg.jsEvent.shiftKey || arg.jsEvent.altKey) return
      arg.jsEvent.preventDefault()
      openDetail(arg.event.extendedProps)
    },
    eventContent: (arg: { isStart?: boolean, event: { title: string, start: Date | null, extendedProps: AutoServiceMaintenanceCalendarItem }, view: { type: string } }) => {
      const item = arg.event.extendedProps
      const root = document.createElement('div')
      const heading = document.createElement('div')
      const title = document.createElement('div')

      root.className = 'autoservice-calendar-event'
      heading.className = 'autoservice-calendar-event__heading'
      title.className = 'autoservice-calendar-event__title'
      title.textContent = arg.event.title || t('maintenanceCalendar.recordFallback')
      const logo = getVehicleBrandLogo(item.brandLogoKey)

      if (logo) {
        const image = document.createElement('img')
        image.className = 'autoservice-calendar-event__logo'
        image.src = logo.src
        image.alt = t('maintenanceCalendar.logoAlt', { name: logo.name })
        heading.appendChild(image)
      }

      if (arg.view.type === 'dayGridMonth') {
        const time = document.createElement('span')
        time.className = 'autoservice-calendar-event__time'
        time.textContent = normalizeTime(item.plannedStartTime) || '—'
        heading.append(time, title)
        root.appendChild(heading)
        return { domNodes: [root] }
      }

      return renderSchedulerCalendarEvent(item, arg.view.type, arg.isStart)
    }
  })

  calendar.render()
})

onBeforeUnmount(() => {
  calendarRequest++
  calendar?.destroy()
  calendar = null
})
</script>

<template>
  <div class="autoservice-calendar-shell" :aria-busy="isLoading">
    <div v-if="errorMessage" class="autoservice-state autoservice-state-error">{{ errorMessage }}</div>
    <div v-if="isLoading" class="autoservice-calendar-loading">
      <UiLoader :text="t('maintenanceCalendar.loading')" />
    </div>
    <p v-if="limitWarning" role="status" class="autoservice-calendar__day-empty">{{ limitWarning }}</p>
    <div ref="calendarElement" class="autoservice-calendar crm-maintenance-calendar"></div>
    <SchedulerRecordPreview :item="previewItem" @close="previewItem = null" />
    <SchedulerDayRecords :open="isDayModalOpen" :date="selectedDate" :items="calendarItems.filter(item => isOnDate(item, selectedDate))"
      :organization-id="organizationId" :can-create="canCreate" @close="isDayModalOpen = false"
      @day="date => emit('day', date)" @create="slot => emit('create', slot)" />
  </div>
</template>

<style scoped>
.autoservice-calendar :deep(.fc-dayGridMonth-view .fc-daygrid-event .autoservice-calendar-event) { padding: 1px 3px; width: 100%; }
.autoservice-calendar :deep(.fc-dayGridMonth-view .fc-daygrid-day-frame) { height: 108px; min-height: 108px; }
.autoservice-calendar :deep(.fc-dayGridMonth-view .fc-daygrid-body tr) { height: 108px; }
.autoservice-calendar :deep(.fc-dayGridMonth-view .fc-daygrid-day-number) { padding: 2px 5px; font-size: 12px; line-height: 18px; color: var(--crm-text, #121212); }
.autoservice-calendar :deep(.fc-dayGridMonth-view .autoservice-calendar__selected-day .fc-daygrid-day-frame) { background: color-mix(in srgb, #e67337 8%, var(--crm-surface, #fff)); box-shadow: inset 0 0 0 2px #e67337; }
.autoservice-calendar :deep(.fc-dayGridMonth-view .fc-daygrid-event) { padding: 0; min-height: 0 !important; margin: 2px 4px; }
.autoservice-calendar :deep(.autoservice-calendar-event__heading) { display: flex; align-items: center; gap: 4px; min-width: 0; }
.autoservice-calendar :deep(.autoservice-calendar-event__logo) { width: 16px; height: 16px; padding: 1px; border-radius: 50%; background: #fff; object-fit: contain; flex: 0 0 16px; }
.autoservice-calendar :deep(.autoservice-calendar-event__time) { flex: 0 0 auto; font-size: 10px; font-weight: 800; }
.autoservice-calendar :deep(.autoservice-calendar-event__title) { min-width: 0; font-size: 11px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.autoservice-calendar__day-empty { margin: 0; padding: 12px; color: var(--crm-text-muted, #667085); }
.autoservice-calendar :deep(.fc-timegrid-more-link) { background: var(--crm-surface-soft, #f4f6f8); color: var(--crm-text, #202833); border: 1px solid var(--crm-line, #dce2e9); }
</style>



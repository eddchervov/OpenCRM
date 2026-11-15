<script setup lang="ts">
import { renderSchedulerCalendarEvent, schedulerCalendarStatus, SchedulerDayRecords, SchedulerRecordPreview, getSchedulerDayInterval, getVehicleBrandLogo, scheduleBusinessHours, type WorkspaceWorkingDay } from '@opencrm/workspace'
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiLoader, formatDateOnly, formatLocalDateTime } from '@opencrm/ui'
import { loadMaintenanceCalendar, type DetailingMaintenanceCalendarItem, type DetailingMaintenanceMove } from '@/api/detailingBff'

const props = withDefaults(defineProps<{
  calendarUrl: string
  timeZoneId?: string | null
  organizationId: string
  storageKey: string
  includeAllStatuses?: boolean
  canCreate?: boolean
  canMove?: boolean
  initialDate?: string
  weeklySchedule?: WorkspaceWorkingDay[]
  workingDays?: number[]
  workdayStart?: string | null
  workdayEnd?: string | null
  emptyText?: string
}>(), {
  includeAllStatuses: false,
  canCreate: false,
  canMove: false,
  initialDate: '',
  emptyText: 'Нет записей на выбранный период'
})

const emit = defineEmits<{
  move: [proposal: DetailingMaintenanceMove]
  day: [date: string]
  navigate: [date: string]
  create: [slot: { date: string, start?: string, end?: string, endDate?: string, location?: string }]
}>()

const calendarElement = ref<HTMLElement | null>(null)
const errorMessage = ref('')
const isLoading = ref(false)
const calendarItems = ref<DetailingMaintenanceCalendarItem[]>([])
const selectedDate = ref(normalizeDate(props.initialDate))
const isDayModalOpen = ref(false)
const previewItem = ref<DetailingMaintenanceCalendarItem | null>(null)
watch(() => props.organizationId, () => { previewItem.value = null })
let calendarRequest = 0
const limitWarning = ref('')
let calendar: InstanceType<NonNullable<typeof window.FullCalendar>['Calendar']> | null = null

function getBusinessHours() {
  if (props.weeklySchedule?.length) return scheduleBusinessHours(props.weeklySchedule)
  return props.workingDays?.length && props.workdayStart && props.workdayEnd
    ? { daysOfWeek: props.workingDays, startTime: props.workdayStart, endTime: props.workdayEnd }
    : false
}

watch(() => [props.weeklySchedule, props.workingDays, props.workdayStart, props.workdayEnd], () => {
  calendar?.setOption('businessHours', getBusinessHours())
})

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

function resolveDetailHash(item: DetailingMaintenanceCalendarItem) {
  return `#maintenance/${item.id}`
}

function isOnDate(item: DetailingMaintenanceCalendarItem, date: string) {
  if (item.plannedStartTime) return getSchedulerDayInterval(item, date) !== null
  return normalizeDate(item.date) <= date && normalizeDate(item.plannedEndDate || item.date) >= date
}

function openDetail(item: DetailingMaintenanceCalendarItem) {
  previewItem.value = item
}

function updateSelectedDayClass() {
  calendarElement.value?.querySelectorAll<HTMLElement>('.fc-daygrid-day[data-date]').forEach(cell => {
    cell.classList.toggle('detailing-calendar__selected-day', cell.dataset.date === selectedDate.value)
  })
}

function resolveEventClass(item: DetailingMaintenanceCalendarItem) {
  if (item.status === 4) {
    return 'detailing-calendar-event--muted'
  }

  if (item.handedOverAt || item.readyForHandoverAt || item.status === 3) {
    return 'detailing-calendar-event--done'
  }

  if (item.status === 2) {
    return 'detailing-calendar-event--progress'
  }

  return 'detailing-calendar-event--planned'
}

function mapEvent(item: DetailingMaintenanceCalendarItem) {
  const date = normalizeDate(item.date)
  const endDate = normalizeDate(item.plannedEndDate || item.date)
  const startTime = normalizeTime(item.plannedStartTime)
  const endTime = normalizeTime(item.plannedEndTime)
  const title = item.vehicleDisplayName || item.typeName || 'Запись'

  if (!startTime) {
    const exclusiveEnd = new Date(`${endDate}T00:00:00`)
    exclusiveEnd.setDate(exclusiveEnd.getDate() + 1)
    return {
      id: item.id,
      title,
      start: date,
      end: formatDate(exclusiveEnd),
      allDay: true,
      startEditable: false,
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
    startEditable: canMoveItem(item) && Boolean(endTime),
    url: resolveDetailHash(item),
    classNames: [resolveEventClass(item), schedulerCalendarStatus(item)],
    extendedProps: item
  }
}

function canMoveItem(item: DetailingMaintenanceCalendarItem) {
  return props.canMove && item.canReschedule && (item.status === 1 || item.status === 2)
}

function requestMove(item: DetailingMaintenanceCalendarItem, start?: Date, end?: Date) {
  if (!canMoveItem(item)) return
  emit('move', {
    item,
    date: start ? formatDate(start) : normalizeDate(item.date),
    endDate: end ? formatDate(end) : normalizeDate(item.plannedEndDate || item.date),
    start: start ? formatDateTime(start).slice(11, 16) : normalizeTime(item.plannedStartTime),
    end: end ? formatDateTime(end).slice(11, 16) : normalizeTime(item.plannedEndTime),
    location: item.location || ''
  })
}

defineExpose({ refresh: () => calendar?.refetchEvents() })

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
    errorMessage.value = 'Календарь не загружен.'
    return
  }

  calendar = new window.FullCalendar.Calendar(calendarElement.value, {
    locale: 'ru',
    allDayText: 'Без времени',
    firstDay: 1,
    initialDate: props.initialDate || formatDateOnly(undefined, props.timeZoneId || 'UTC'),
    // Календарь оперирует местными датами и часами записей.
    now: () => new Date(formatLocalDateTime(new Date(), props.timeZoneId || 'UTC')),
    initialView: getStoredView(),
    height: 680,
    fixedWeekCount: true,
    nowIndicator: true,
    eventDurationEditable: false,
    eventAllow: (drop: { allDay: boolean }) => calendar?.view?.type === 'dayGridMonth' || !drop.allDay,
    eventDrop: (arg: { event: { start: Date | null, end: Date | null, extendedProps: DetailingMaintenanceCalendarItem }, revert: () => void }) => {
      const { start, end, extendedProps } = arg.event
      arg.revert()
      if (start && end) requestMove(extendedProps, start, end)
    },
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
    moreLinkContent: (arg: { num: number }) => `+${arg.num} еще`,
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
    scrollTime: props.workdayStart || '08:00:00',
    businessHours: getBusinessHours(),
    slotDuration: '00:30:00',
    slotLabelFormat: { hour: '2-digit', minute: '2-digit', hour12: false },
    eventTimeFormat: { hour: '2-digit', minute: '2-digit', hour12: false },
    buttonText: {
      today: 'К текущей дате',
      month: 'Месяц',
      week: 'Неделя',
      day: 'День'
    },
    noEventsContent: props.emptyText,
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
        limitWarning.value = items.length >= 500 ? 'Достигнут лимит 500 записей. Выберите меньший период, чтобы проверить все записи.' : ''
        success(items.map(mapEvent))
      } catch (error) {
        const message = error instanceof Error ? error.message : 'Не удалось загрузить календарь.'
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
      arg.el.setAttribute('aria-label', new Intl.DateTimeFormat('ru-RU', { dateStyle: 'full' }).format(arg.date))
      arg.el.addEventListener('keydown', event => {
        if (event.target !== arg.el || event.key !== 'Enter' && event.key !== ' ') return
        event.preventDefault()
        selectedDate.value = formatDate(arg.date)
        isDayModalOpen.value = true
        updateSelectedDayClass()
      })
    },
    dayCellClassNames: (arg: { date: Date }) =>
      formatDate(arg.date) === selectedDate.value ? ['detailing-calendar__selected-day'] : [],
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
    eventClick: (arg: { event: { extendedProps: DetailingMaintenanceCalendarItem }, jsEvent: MouseEvent }) => {
      if (arg.jsEvent.ctrlKey || arg.jsEvent.metaKey || arg.jsEvent.shiftKey || arg.jsEvent.altKey) return
      arg.jsEvent.preventDefault()
      openDetail(arg.event.extendedProps)
    },
    eventContent: (arg: { isStart?: boolean, event: { title: string, start: Date | null, extendedProps: DetailingMaintenanceCalendarItem }, view: { type: string } }) => {
      const item = arg.event.extendedProps
      const root = document.createElement('div')
      const heading = document.createElement('div')
      const title = document.createElement('div')

      root.className = 'detailing-calendar-event'
      heading.className = 'detailing-calendar-event__heading'
      title.className = 'detailing-calendar-event__title'
      title.textContent = arg.event.title || 'Запись'
      const time = normalizeTime(item.plannedStartTime)
      const logo = getVehicleBrandLogo(item.brandLogoKey)

      if (logo) {
        const image = document.createElement('img')
        image.className = 'detailing-calendar-event__logo'
        image.src = logo.src
        image.alt = `Логотип ${logo.name}`
        heading.appendChild(image)
      }

      if (arg.view.type === 'dayGridMonth') {
        const timeLabel = document.createElement('span')
        timeLabel.className = 'detailing-calendar-event__time'
        timeLabel.textContent = time || '—'
        heading.append(timeLabel, title)
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
  <div class="detailing-calendar-shell" :aria-busy="isLoading">
    <div v-if="errorMessage" class="detailing-state detailing-state-error">{{ errorMessage }}</div>
    <div v-if="isLoading" class="detailing-calendar-loading">
      <UiLoader text="Загружаем записи..." />
    </div>
    <p v-if="limitWarning" role="status" class="detailing-calendar__day-empty">{{ limitWarning }}</p>
    <div ref="calendarElement" class="detailing-calendar crm-maintenance-calendar"></div>
    <SchedulerRecordPreview :item="previewItem" @close="previewItem = null" />
    <SchedulerDayRecords :open="isDayModalOpen" :date="selectedDate" :items="calendarItems.filter(item => isOnDate(item, selectedDate))"
      :organization-id="organizationId" :can-create="canCreate" @close="isDayModalOpen = false"
      @day="date => emit('day', date)" @create="slot => emit('create', slot)" />
  </div>
</template>

<style scoped>
.detailing-calendar :deep(.detailing-calendar-event__move) {
  border: 0;
  padding: 2px 0;
  background: transparent;
  color: inherit;
  font: inherit;
  font-size: 11px;
  text-decoration: underline;
  cursor: pointer;
}
.detailing-calendar :deep(.detailing-calendar-event__move:focus-visible) {
  outline: 2px solid currentColor;
  outline-offset: 2px;
}
.detailing-calendar :deep(.fc-dayGridMonth-view .fc-daygrid-event .detailing-calendar-event) { padding: 1px 3px; width: 100%; }
.detailing-calendar :deep(.fc-dayGridMonth-view .fc-daygrid-day-frame) { height: 108px; min-height: 108px; }
.detailing-calendar :deep(.fc-dayGridMonth-view .fc-daygrid-body tr) { height: 108px; }
.detailing-calendar :deep(.fc-dayGridMonth-view .fc-daygrid-day-number) { padding: 2px 5px; font-size: 12px; line-height: 18px; color: var(--crm-text, #121212); }
.detailing-calendar :deep(.fc-dayGridMonth-view .detailing-calendar__selected-day .fc-daygrid-day-frame) { background: color-mix(in srgb, #e67337 8%, var(--crm-surface, #fff)); box-shadow: inset 0 0 0 2px #e67337; }
.detailing-calendar :deep(.fc-dayGridMonth-view .fc-daygrid-event) { padding: 0; min-height: 0 !important; margin: 2px 4px; }
.detailing-calendar :deep(.detailing-calendar-event__heading) { display: flex; align-items: center; gap: 4px; min-width: 0; }
.detailing-calendar :deep(.detailing-calendar-event__logo) { width: 16px; height: 16px; padding: 1px; border-radius: 50%; background: #fff; object-fit: contain; flex: 0 0 16px; }
.detailing-calendar :deep(.detailing-calendar-event__time) { flex: 0 0 auto; font-size: 10px; font-weight: 800; }
.detailing-calendar :deep(.detailing-calendar-event__title) { min-width: 0; font-size: 11px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.detailing-calendar__day-empty { margin: 0; padding: 12px; color: var(--crm-text-muted, #667085); }
.detailing-calendar :deep(.fc-timegrid-more-link) { background: var(--crm-surface-soft, #f4f6f8); color: var(--crm-text, #202833); border: 1px solid var(--crm-line, #dce2e9); }
</style>

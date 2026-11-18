<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { formatDateOnly, useLocalization } from '@opencrm/ui'
import type { ShiftWeek } from '../api/shifts'
import '../shiftMessages'

type Day = { key: string, title: string }
type Interval = { start: number, end: number }
type WorkBlock = Interval & { maintenanceId: string, vehicleName: string, isOutsideShift: boolean, lane: number }
type Row = { key: string, title: string, shifts: Interval[], works: WorkBlock[], unscheduled: Array<{ maintenanceId: string, vehicleName: string }>, planned: number, outside: number, free: number, lanes: number }

const props = defineProps<{ week: ShiftWeek, days: Day[] }>()
const { t } = useLocalization()
const mode = ref<'employee' | 'team'>('employee')
const selectedEmployeeId = ref('')
const selectedDay = ref('')

watch(() => props.week.employees, employees => {
  if (!employees.some(employee => employee.id === selectedEmployeeId.value)) {
    selectedEmployeeId.value = employees[0]?.id ?? ''
  }
}, { immediate: true })
watch(() => props.days, days => {
  if (!days.some(day => day.key === selectedDay.value)) {
    const today = formatDateOnly(undefined, props.week.timeZoneId || 'UTC')
    selectedDay.value = days.find(day => day.key === today)?.key ?? days[0]?.key ?? ''
  }
}, { immediate: true })

function localTimestamp(value: string) {
  return Date.UTC(Number(value.slice(0, 4)), Number(value.slice(5, 7)) - 1, Number(value.slice(8, 10)),
    Number(value.slice(11, 13) || 0), Number(value.slice(14, 16) || 0))
}

function clippedInterval(start: string, end: string, day: string): Interval | null {
  const dayStart = localTimestamp(day)
  const dayEnd = dayStart + 24 * 60 * 60 * 1000
  const from = Math.max(localTimestamp(start), dayStart)
  const to = Math.min(localTimestamp(end), dayEnd)
  return to > from ? { start: (from - dayStart) / 60000, end: (to - dayStart) / 60000 } : null
}

function merge(intervals: Interval[]): Interval[] {
  const merged: Interval[] = []
  for (const interval of [...intervals].sort((left, right) => left.start - right.start)) {
    const previous = merged[merged.length - 1]
    if (previous && interval.start <= previous.end) previous.end = Math.max(previous.end, interval.end)
    else merged.push({ ...interval })
  }
  return merged
}

function duration(intervals: Interval[]) {
  return merge(intervals).reduce((total, interval) => total + interval.end - interval.start, 0)
}

function overlap(first: Interval[], second: Interval[]) {
  return merge(first).flatMap(left => merge(second).map(right => ({ start: Math.max(left.start, right.start), end: Math.min(left.end, right.end) })))
    .filter(interval => interval.end > interval.start)
}

function buildRow(employeeId: string, day: Day, title: string): Row {
  const shifts = props.week.shifts.filter(shift => shift.employeeId === employeeId && shift.isPublished && !shift.isCancelled)
    .map(shift => clippedInterval(shift.startLocal, shift.endLocal, day.key)).filter((interval): interval is Interval => interval !== null)
  const assignments = props.week.assignments.filter(assignment => assignment.employeeId === employeeId && assignment.localStartDate === day.key)
  const works = assignments.flatMap(assignment => assignment.segments.map(segment => {
    const interval = clippedInterval(segment.startLocal, segment.endLocal, day.key)
    return interval ? { ...interval, maintenanceId: assignment.maintenanceId, vehicleName: assignment.vehicleName, isOutsideShift: segment.isOutsideShift, lane: 0 } : null
  })).filter((work): work is WorkBlock => work !== null).sort((left, right) => left.start - right.start || left.end - right.end)
  const laneEnds: number[] = []
  for (const work of works) {
    let lane = laneEnds.findIndex(end => end <= work.start)
    if (lane < 0) lane = laneEnds.length
    laneEnds[lane] = work.end
    work.lane = lane
  }
  const unscheduled = assignments.filter(assignment => !assignment.segments.length)
    .map(assignment => ({ maintenanceId: assignment.maintenanceId, vehicleName: assignment.vehicleName }))
  const planned = duration(works)
  const outside = duration(works.filter(work => work.isOutsideShift))
  const free = Math.max(0, duration(shifts) - duration(overlap(shifts, works)))
  return { key: `${employeeId}-${day.key}`, title, shifts, works, unscheduled, planned, outside, free, lanes: laneEnds.length }
}

const rows = computed(() => mode.value === 'employee'
  ? props.days.map(day => buildRow(selectedEmployeeId.value, day, day.title))
  : props.week.employees.map(employee => buildRow(employee.id, props.days.find(day => day.key === selectedDay.value) ?? props.days[0], employee.name)))
const range = computed(() => {
  const intervals = rows.value.flatMap(row => [...row.shifts, ...row.works])
  if (!intervals.length) return { start: 8 * 60, end: 20 * 60 }
  return { start: Math.max(0, Math.floor(Math.min(...intervals.map(interval => interval.start)) / 60) * 60), end: Math.min(1440, Math.ceil(Math.max(...intervals.map(interval => interval.end)) / 60) * 60) }
})
const hours = computed(() => Array.from({ length: Math.floor((range.value.end - range.value.start) / 60) + 1 }, (_, index) => range.value.start / 60 + index))
const totals = computed(() => rows.value.reduce((result, row) => ({ planned: result.planned + row.planned, outside: result.outside + row.outside, free: result.free + row.free }), { planned: 0, outside: 0, free: 0 }))

function position(interval: Interval) {
  const width = range.value.end - range.value.start
  return { left: `${(interval.start - range.value.start) / width * 100}%`, width: `${(interval.end - interval.start) / width * 100}%` }
}

function time(minutes: number) {
  return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`
}

function hoursText(minutes: number) {
  return t('shiftLoad.hours', { hours: Math.floor(minutes / 60), minutes: minutes % 60 })
}
</script>

<template>
  <section class="load">
    <div class="load__controls">
      <div class="load__modes" role="group" :aria-label="t('shiftLoad.viewMode')">
        <button type="button" :class="{ active: mode === 'employee' }" :aria-pressed="mode === 'employee'" @click="mode = 'employee'">{{ t('shiftLoad.oneEmployee') }}</button>
        <button type="button" :class="{ active: mode === 'team' }" :aria-pressed="mode === 'team'" @click="mode = 'team'">{{ t('shiftLoad.allEmployees') }}</button>
      </div>
      <label v-if="mode === 'employee'">{{ t('shiftLoad.employee') }}<select v-model="selectedEmployeeId"><option v-for="employee in week.employees" :key="employee.id" :value="employee.id">{{ employee.name }}</option></select></label>
      <label v-else>{{ t('shiftLoad.day') }}<select v-model="selectedDay"><option v-for="day in days" :key="day.key" :value="day.key">{{ day.title }}</option></select></label>
    </div>
    <p v-if="!week.canViewAssignments" class="load__note">{{ t('shiftLoad.assignmentsAccess') }}</p>
    <div v-else class="load__totals">
      <span>{{ t('shiftLoad.planned') }} <strong>{{ hoursText(totals.planned) }}</strong></span>
      <span>{{ t('shiftLoad.freeInShift') }} <strong>{{ hoursText(totals.free) }}</strong></span>
      <span :class="{ 'load__warning': totals.outside }">{{ t('shiftLoad.outsideShift') }} <strong>{{ hoursText(totals.outside) }}</strong></span>
    </div>
    <div v-if="week.canViewAssignments" class="load__scroll">
      <div class="load__chart">
        <div class="load__axis"><div></div><div class="load__scale"><span v-for="hour in hours" :key="hour" :style="{ left: `${(hour * 60 - range.start) / (range.end - range.start) * 100}%` }">{{ time(hour * 60) }}</span></div></div>
        <div v-for="row in rows" :key="row.key" class="load__row">
          <strong class="load__label">{{ row.title }}</strong>
          <div class="load__track" :style="{ height: `${Math.max(44, 20 + row.lanes * 30)}px` }">
            <span v-for="hour in hours" :key="hour" class="load__hour" :style="{ left: `${(hour * 60 - range.start) / (range.end - range.start) * 100}%` }"></span>
            <div v-for="(shift, index) in row.shifts" :key="`shift-${index}`" class="load__shift" :style="position(shift)" :title="t('shiftLoad.shiftTitle', { start: time(shift.start), end: time(shift.end) })"></div>
            <a v-for="(work, index) in row.works" :key="`work-${index}`" class="load__work" :class="{ 'load__work--outside': work.isOutsideShift }" :style="{ ...position(work), top: `${17 + work.lane * 30}px` }" :href="`#maintenance/${work.maintenanceId}`" :title="`${work.vehicleName}: ${time(work.start)}–${time(work.end)}${work.isOutsideShift ? t('shiftLoad.outsideShiftTitle') : ''}`">{{ time(work.start) }}–{{ time(work.end) }} · {{ work.vehicleName }}</a>
          </div>
          <div v-if="row.unscheduled.length" class="load__unscheduled"><span>{{ t('shiftLoad.noTime') }}</span><a v-for="work in row.unscheduled" :key="work.maintenanceId" :href="`#maintenance/${work.maintenanceId}`">{{ work.vehicleName }}</a></div>
        </div>
      </div>
    </div>
    <p v-if="week.canViewAssignments" class="load__note">{{ t('shiftLoad.note') }}</p>
  </section>
</template>

<style scoped>
.load { color: var(--crm-text, #242b36); }
.load__controls, .load__modes, .load__totals { display: flex; align-items: center; flex-wrap: wrap; gap: 8px; }
.load__controls { margin: 12px 0; }
.load__controls label { display: flex; align-items: center; gap: 8px; font-size: 12px; }
.load__controls button, .load__controls select { border: 1px solid var(--crm-border, #dce2e9); border-radius: 6px; background: var(--crm-surface, #fff); color: inherit; padding: 7px 9px; cursor: pointer; }
.load__controls button.active { background: var(--crm-surface-soft, #f4f5f7); border-color: var(--crm-accent, #d75b18); }
.load__totals { gap: 8px 18px; font-size: 12px; margin-bottom: 12px; }
.load__warning { color: #db4747; }
.load__scroll { overflow-x: auto; }
.load__chart { min-width: 720px; }
.load__axis, .load__row { display: grid; grid-template-columns: 135px minmax(0, 1fr); gap: 12px; }
.load__axis { height: 26px; color: var(--crm-text-muted, #687386); font-size: 11px; }
.load__scale { position: relative; margin: 0 34px 0 0; }
.load__scale span { position: absolute; transform: translateX(-50%); }
.load__row { border-top: 1px solid var(--crm-border, #dce2e9); padding: 7px 0; }
.load__label { font-size: 12px; padding-top: 6px; overflow: hidden; text-overflow: ellipsis; }
.load__track { position: relative; margin: 0 34px 0 0; }
.load__hour { position: absolute; top: 0; bottom: 0; width: 1px; background: var(--crm-border, #dce2e9); opacity: .6; pointer-events: none; }
.load__shift { position: absolute; top: 0; height: 100%; border-top: 2px solid var(--crm-accent, #d75b18); border-radius: 3px; background: color-mix(in srgb, var(--crm-accent, #d75b18) 12%, transparent); }
.load__work { position: absolute; box-sizing: border-box; height: 26px; padding: 4px 6px; border: 1px solid var(--crm-accent, #d75b18); border-radius: 5px; background: var(--crm-surface-soft, #f4f5f7); color: inherit; font-size: 11px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; text-decoration: none; z-index: 1; }
.load__work--outside { border-color: #db4747; background: #db474733; }
.load__work:hover, .load__work:focus-visible { z-index: 2; outline: 2px solid var(--crm-accent, #d75b18); }
.load__unscheduled { grid-column: 2; display: flex; gap: 6px; flex-wrap: wrap; font-size: 11px; color: var(--crm-text-muted, #687386); }
.load__unscheduled a { color: var(--crm-text, #242b36); }
.load__note { color: var(--crm-text-muted, #687386); font-size: 11px; margin: 10px 0 0; }
@media (max-width: 700px) { .load__controls { align-items: stretch; } .load__controls label { width: 100%; } .load__controls select { flex: 1; } }
</style>

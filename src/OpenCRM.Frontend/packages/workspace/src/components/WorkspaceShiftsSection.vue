<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { UiButton, UiDatePicker, UiLoader, UiModal, UiTimePicker, formatDateOnly } from '@opencrm/ui'
import WorkspaceShiftLoadSection from './WorkspaceShiftLoadSection.vue'
import { cancelShift, copyPreviousShiftWeek, loadShiftWeek, saveShift, type Shift, type ShiftWeek } from '../api/shifts'
import type { WorkspaceContext } from '../types'
import { organizationTimeZoneName as timeZoneName } from '../utils/organizationTimeZone'
import { combineShiftLocalDateTime, splitShiftLocalDateTime } from '../shiftLocalDateTime'

const props = withDefaults(defineProps<{ context: WorkspaceContext, previewOnly?: boolean }>(), { previewOnly: false })
const today = new Date(`${formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')}T12:00:00`)
const monday = new Date(today.getFullYear(), today.getMonth(), today.getDate() - ((today.getDay() + 6) % 7))
const weekStart = ref(monday.toLocaleDateString('sv-SE'))
const data = ref<ShiftWeek | null>(null)
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const status = ref('')
const editing = ref<Shift | null>(null)
const formOpen = ref(false)
const employeeId = ref('')
const startDate = ref('')
const startTime = ref('')
const endDate = ref('')
const endTime = ref('')
const plannedBreakMinutes = ref(0)
const reason = ref('')
const activeTab = ref<'schedule' | 'load'>('schedule')
let loadRequestId = 0
const days = computed(() => Array.from({ length: 7 }, (_, index) => {
  const date = new Date(`${weekStart.value}T12:00:00`)
  date.setDate(date.getDate() + index)
  return { key: date.toLocaleDateString('sv-SE'), title: date.toLocaleDateString('ru-RU', { weekday: 'short', day: 'numeric', month: 'short' }) }
}))
const canManage = computed(() => data.value?.canManage === true && props.context.workspaceAccess?.isReadOnly === false)
const mobileShifts = computed(() => data.value?.shifts.filter(shift => !shift.isCancelled).sort((left, right) => left.startLocal.localeCompare(right.startLocal)) ?? [])

async function load() {
  if (props.previewOnly) return

  const requestId = ++loadRequestId
  loading.value = true
  data.value = null
  status.value = ''
  try {
    const week = await loadShiftWeek(props.context.organization.id, weekStart.value)
    if (requestId !== loadRequestId) return
    data.value = week
    error.value = ''
  } catch (cause) {
    if (requestId !== loadRequestId) return
    error.value = cause instanceof Error ? cause.message : 'Не удалось загрузить смены.'
  } finally {
    if (requestId === loadRequestId) loading.value = false
  }
}

function moveWeek(weeks: number) {
  const date = new Date(`${weekStart.value}T12:00:00`)
  date.setDate(date.getDate() + weeks * 7)
  weekStart.value = date.toLocaleDateString('sv-SE')
}

function nextDay(day: string) {
  const date = new Date(`${day}T12:00:00`)
  date.setDate(date.getDate() + 1)
  return date.toLocaleDateString('sv-SE')
}

function shiftsFor(employeeIdValue: string, day: string) {
  return data.value?.shifts.filter(shift => shift.employeeId === employeeIdValue && !shift.isCancelled && shift.startLocal < `${nextDay(day)}T00:00` && shift.endLocal > `${day}T00:00`) ?? []
}

function openForm(shift?: Shift, day?: string, personId?: string) {
  if (!canManage.value) return
  error.value = ''
  editing.value = shift ?? null
  employeeId.value = shift?.employeeId ?? personId ?? data.value?.employees.find(employee => employee.isActive)?.id ?? ''
  const start = splitShiftLocalDateTime(shift?.startLocal ?? `${day ?? weekStart.value}T09:00`)
  const end = splitShiftLocalDateTime(shift?.endLocal ?? `${day ?? weekStart.value}T18:00`)
  startDate.value = start.date
  startTime.value = start.time
  endDate.value = end.date
  endTime.value = end.time
  plannedBreakMinutes.value = shift?.plannedBreakMinutes ?? 60
  reason.value = ''
  formOpen.value = true
}

async function run(action: () => Promise<unknown>, onSuccess?: () => void) {
  if (!canManage.value || saving.value) return
  saving.value = true
  try {
    await action()
    await load()
    formOpen.value = false
    onSuccess?.()
    error.value = ''
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : 'Не удалось сохранить смены.'
  } finally {
    saving.value = false
  }
}

function save() {
  const startLocal = combineShiftLocalDateTime(startDate.value, startTime.value)
  const endLocal = combineShiftLocalDateTime(endDate.value, endTime.value)
  if (!canManage.value || !employeeId.value || !startLocal || !endLocal) return
  run(() => saveShift(props.context.organization.id, editing.value?.id ?? null, {
    employeeId: employeeId.value,
    startLocal,
    endLocal,
    plannedBreakMinutes: plannedBreakMinutes.value,
    version: editing.value?.version,
    reason: reason.value
  }))
}

function copyPreviousWeek() {
  let copiedCount = 0
  run(async () => {
    const result = await copyPreviousShiftWeek(props.context.organization.id, weekStart.value)
    copiedCount = result.count
  }, () => {
    status.value = copiedCount > 0
      ? `Добавлено смен: ${copiedCount}.`
      : 'В прошлой неделе нет смен для копирования.'
  })
}

function cancel() {
  if (!canManage.value || !editing.value) return
  run(() => cancelShift(props.context.organization.id, editing.value!, reason.value))
}

onMounted(() => {
  if (!props.previewOnly) void load()
})
watch([weekStart, () => props.context.organization.id], () => {
  if (props.previewOnly) return

  void load()
})
</script>

<template>
  <div class="shifts">
    <div class="shifts__toolbar">
      <div class="shifts__week">
        <button type="button" aria-label="Предыдущая неделя" :disabled="previewOnly" @click="moveWeek(-1)">‹</button>
        <strong>{{ days[0]?.title }} — {{ days[6]?.title }}</strong>
        <button type="button" aria-label="Следующая неделя" :disabled="previewOnly" @click="moveWeek(1)">›</button>
      </div>
      <div v-if="previewOnly || (canManage && data?.timeZoneId && activeTab === 'schedule')" class="shifts__actions">
        <UiButton :disabled="previewOnly || saving" @click="copyPreviousWeek">Повторить прошлую неделю</UiButton>
        <UiButton :disabled="previewOnly || saving || !data?.employees.some(employee => employee.isActive)" @click="openForm()">Добавить смену</UiButton>
      </div>
    </div>

    <div v-if="previewOnly" class="shifts__preview" aria-hidden="true">
      <div class="shifts__tabs"><span>График смен</span><span>Загрузка</span></div>
      <p class="shifts__zone">Время организации</p>
      <div class="shifts__scroll">
        <table>
          <thead><tr><th>Сотрудник</th><th v-for="day in days" :key="day.key">{{ day.title }}</th></tr></thead>
          <tbody>
            <tr v-for="index in 3" :key="index">
              <th><i class="shifts__preview-line"></i></th>
              <td v-for="day in days" :key="day.key"><i v-if="(index + days.indexOf(day)) % 3 !== 0" class="shifts__preview-shift"></i></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
    <p v-else-if="error" class="shifts__error" role="alert">{{ error }}</p>
    <p v-if="!previewOnly && status" role="status">{{ status }}</p>
    <UiLoader v-if="!previewOnly && loading && !data" />
    <template v-else-if="!previewOnly && data">
      <div class="shifts__tabs" role="tablist" aria-label="Смены">
        <button type="button" role="tab" :aria-selected="activeTab === 'schedule'" :class="{ active: activeTab === 'schedule' }" @click="activeTab = 'schedule'">График смен</button>
        <button type="button" role="tab" :aria-selected="activeTab === 'load'" :class="{ active: activeTab === 'load' }" @click="activeTab = 'load'">Загрузка</button>
      </div>
      <template v-if="data.timeZoneId">
        <p class="shifts__zone">Время организации: {{ timeZoneName(data.timeZoneId) }} <a v-if="context.shell.menu?.some(item => item.code === 'settings' && item.isVisible)" class="shifts__zone-action" href="#settings?tab=profile">Настройки часового пояса</a></p>
        <WorkspaceShiftLoadSection v-if="activeTab === 'load' && data.employees.length" :week="data" :days="days" />
        <p v-else-if="activeTab === 'load'" class="shifts__empty">Добавьте исполнителей в разделе «Команда», чтобы посмотреть загрузку.</p>
        <template v-if="activeTab === 'schedule'">
        <p v-if="!data.employees.length" class="shifts__empty">Добавьте исполнителей в разделе «Команда», чтобы составить график.</p>
        <div v-else class="shifts__scroll">
          <table>
            <thead><tr><th>Сотрудник</th><th v-for="day in days" :key="day.key">{{ day.title }}</th></tr></thead>
            <tbody>
              <tr v-for="employee in data.employees" :key="employee.id">
                <th>{{ employee.name }}<small v-if="!employee.isActive"> · архив</small></th>
                <td v-for="day in days" :key="day.key">
                  <button v-for="shift in shiftsFor(employee.id, day.key)" :key="shift.id" type="button" class="shifts__entry" :class="{ 'shifts__entry--draft': !shift.isPublished }" :disabled="!canManage" @click="openForm(shift)">
                    {{ shift.startLocal.slice(11, 16) }}–{{ shift.endLocal.slice(11, 16) }}<small v-if="shift.localStartDate !== day.key">Начало: {{ shift.localStartDate }}</small><small v-if="!shift.isPublished">Не опубликована · нажмите, чтобы опубликовать</small><small v-if="shift.timeZoneId !== data.timeZoneId">{{ timeZoneName(shift.timeZoneId) }}</small>
                  </button>
                  <button v-if="canManage && employee.isActive" type="button" class="shifts__add" :aria-label="`Добавить смену: ${employee.name}, ${day.title}`" @click="openForm(undefined, day.key, employee.id)">+</button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="shifts__mobile">
          <p v-if="!mobileShifts.length" class="shifts__empty">На этой неделе смен пока нет.</p>
          <button v-for="shift in mobileShifts" :key="shift.id" type="button" class="shifts__mobile-entry" :disabled="!canManage" @click="openForm(shift)">
            <strong>{{ new Date(shift.startLocal).toLocaleDateString('ru-RU', { weekday: 'short', day: 'numeric', month: 'short' }) }}</strong>
            <span>{{ shift.employeeName }}</span>
            <span>{{ shift.startLocal.slice(11, 16) }}–{{ shift.endLocal.slice(11, 16) }}</span>
            <small v-if="!shift.isPublished">Не опубликована · нажмите, чтобы опубликовать</small><small v-if="shift.timeZoneId !== data.timeZoneId">{{ timeZoneName(shift.timeZoneId) }}</small>
          </button>
        </div>
        </template>
      </template>
      <p v-else class="shifts__empty">Часовой пояс организации пока не настроен. <a v-if="context.shell.menu?.some(item => item.code === 'settings' && item.isVisible)" href="#settings?tab=profile">Открыть общие настройки</a></p>
    </template>
    <UiModal :open="formOpen" :title="editing ? 'Изменить смену' : 'Добавить смену'" @close="formOpen = false">
      <form class="shifts__form" @submit.prevent="save">
        <label>Сотрудник<select v-model="employeeId" required><option v-for="employee in data?.employees.filter(item => item.isActive || item.id === editing?.employeeId)" :key="employee.id" :value="employee.id">{{ employee.name }}</option></select></label>
        <div class="shifts__date-time">
          <UiDatePicker v-model="startDate" label="Начало · дата" :disabled="saving" required />
          <UiTimePicker v-model="startTime" label="Начало · время" :disabled="saving" :step="60" required />
        </div>
        <div class="shifts__date-time">
          <UiDatePicker v-model="endDate" label="Конец · дата" :disabled="saving" required />
          <UiTimePicker v-model="endTime" label="Конец · время" :disabled="saving" :step="60" required />
        </div>
        <label>Перерыв, мин<input v-model.number="plannedBreakMinutes" type="number" min="0" required /></label>
        <label v-if="editing?.isPublished">Причина изменения<input v-model="reason" required /></label>
        <p v-if="error" class="shifts__error" role="alert">{{ error }}</p>
        <div class="shifts__form-actions">
          <button v-if="editing" type="button" :disabled="!canManage || saving || (editing.isPublished && !reason.trim())" @click="cancel">Отменить смену</button>
          <button type="button" @click="formOpen = false">Закрыть</button>
          <UiButton type="submit" :disabled="!canManage || saving || (editing?.isPublished && !reason.trim()) || !data?.employees.some(item => item.id === employeeId && item.isActive)">{{ editing?.isPublished ? 'Сохранить изменения' : 'Сохранить и опубликовать' }}</UiButton>
        </div>
      </form>
    </UiModal>
  </div>
</template>

<style scoped>
.shifts { color: var(--crm-text, #242b36); }
.shifts__tabs { display: flex; gap: 4px; border-bottom: 1px solid var(--crm-border, #dce2e9); margin-bottom: 12px; }
.shifts__tabs button { border: 0; border-bottom: 2px solid transparent; background: transparent; color: var(--crm-text-muted, #687386); padding: 9px 12px; cursor: pointer; }
.shifts__tabs button.active { color: var(--crm-text, #242b36); border-bottom-color: var(--crm-accent, #d75b18); }
.shifts__toolbar, .shifts__actions, .shifts__week, .shifts__form-actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.shifts__toolbar { justify-content: space-between; margin-bottom: 12px; }
.shifts__week button, .shifts__add, .shifts__form-actions > button { border: 1px solid var(--crm-border, #dce2e9); background: var(--crm-surface, #fff); color: inherit; border-radius: 6px; cursor: pointer; padding: 5px 9px; }
.shifts__zone { color: var(--crm-text-muted, #687386); font-size: 12px; margin: 0 0 10px; }
.shifts__zone-action { border: 0; background: transparent; color: inherit; text-decoration: underline; cursor: pointer; }
.shifts__zone-hint { flex-basis: 100%; color: var(--crm-text-muted, #687386); }
.shifts__error { color: #ef7777; }
.shifts__scroll { overflow-x: auto; }
.shifts table { width: 100%; border-collapse: collapse; table-layout: fixed; min-width: 720px; }
.shifts th, .shifts td { border-bottom: 1px solid var(--crm-border, #dce2e9); padding: 8px; text-align: left; vertical-align: top; }
.shifts thead th { color: var(--crm-text-muted, #687386); font-size: 12px; }
.shifts tbody th { width: 160px; }
.shifts__entry { display: block; border: 1px solid var(--crm-border, #dce2e9); color: inherit; background: var(--crm-surface, #fff); border-radius: 6px; padding: 5px; width: 100%; text-align: left; cursor: pointer; margin-bottom: 4px; }
.shifts__entry--draft { opacity: .65; }
.shifts__entry small { display: block; font-size: 10px; }
.shifts__add { border-color: transparent; background: transparent; color: var(--crm-text-muted, #687386); opacity: .45; width: 100%; }
.shifts td:hover .shifts__add, .shifts__add:focus-visible { border-style: dashed; border-color: var(--crm-border, #dce2e9); opacity: 1; }
.shifts__mobile { display: none; }
.shifts__setup { display: flex; align-items: end; gap: 10px; }
.shifts__setup label, .shifts__form label { display: grid; gap: 5px; font-size: 12px; }
.shifts__date-time { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px; }
.shifts select, .shifts input { min-height: 34px; border: 1px solid var(--crm-border, #dce2e9); border-radius: 6px; padding: 5px; background: var(--crm-surface, #fff); color: inherit; }
.shifts__form { display: grid; gap: 12px; width: 100%; }
.shifts__form-actions { justify-content: flex-end; }
:global(.ui-modal__panel:has(.shifts__form)) { display: flex; flex-direction: column; max-height: min(90dvh, 720px); }
:global(.ui-modal__panel:has(.shifts__form) > .ui-modal__header) { flex-shrink: 0; }
:global(.ui-modal__panel:has(.shifts__form) > .ui-modal__body) { min-height: 0; overflow-y: auto; }
.shifts__form :deep(.ui-time-picker__popover) { position: static; inset: auto; width: min(220px, 100%); margin-top: 6px; }
.shifts__form :deep(.ui-time-picker__popover--end) { right: auto; left: auto; }
.shifts__form :deep(.ui-date-picker > .vc-popover-content-wrapper) { position: static !important; inset: auto !important; transform: none !important; }
.shifts__preview { opacity: .62; }
.shifts__preview .shifts__tabs span { border-bottom: 2px solid transparent; color: var(--crm-text-muted, #687386); padding: 9px 12px; }
.shifts__preview .shifts__tabs span:first-child { border-bottom-color: var(--crm-accent, #d75b18); color: var(--crm-text, #242b36); }
.shifts__preview-line, .shifts__preview-shift { display: block; height: 12px; border-radius: 5px; background: var(--crm-border, #dce2e9); }
.shifts__preview-line { width: 100px; }
.shifts__preview-shift { height: 24px; opacity: .8; }
@media (max-width: 700px) { .shifts__scroll { display: none; } .shifts__mobile { display: grid; gap: 7px; } .shifts__mobile-entry { display: grid; grid-template-columns: 1fr auto; gap: 3px 8px; width: 100%; text-align: left; border: 1px solid var(--crm-border, #dce2e9); border-radius: 6px; padding: 9px; background: var(--crm-surface, #fff); color: inherit; } .shifts__mobile-entry strong { grid-column: 1 / -1; } .shifts__toolbar { align-items: stretch; } .shifts__setup { align-items: stretch; flex-direction: column; } .shifts__date-time { grid-template-columns: minmax(0, 1fr); } }
</style>

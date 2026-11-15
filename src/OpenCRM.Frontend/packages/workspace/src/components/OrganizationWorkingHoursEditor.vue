<script setup lang="ts">
import { UiButton, UiTimePicker } from '@opencrm/ui'
import type { WorkspaceWorkingDay } from '../api/setup'
import { workingWeek } from '../workingHours'

const schedule = defineModel<WorkspaceWorkingDay[]>({ required: true })
defineProps<{ disabled?: boolean }>()

function enableDay(day: WorkspaceWorkingDay) {
  if (!day.isWorking) return
  day.startTime ??= '09:00'
  day.endTime ??= '19:00'
}

function addBreak(day: WorkspaceWorkingDay) {
  const [hour, minute] = (day.startTime ?? '09:00').split(':').map(Number)
  const [endHour, endMinute] = (day.endTime ?? '19:00').split(':').map(Number)
  const start = hour * 60 + minute, end = endHour * 60 + endMinute
  const middle = Math.floor((start + end) / 2)
  const format = (value: number) => String(Math.floor(value / 60)).padStart(2, '0') + ':' + String(value % 60).padStart(2, '0')
  day.breaks.push({ startTime: format(middle), endTime: format(Math.min(middle + 30, end)) })
}
</script>

<template>
  <div class="working-hours">
    <div class="working-hours-heading" aria-hidden="true"><span>День недели</span><span>Открытие</span><span>Закрытие</span><span /></div>
    <template v-for="weekDay in workingWeek" :key="weekDay.id">
      <div v-for="day in schedule.filter(item => item.dayOfWeek === weekDay.id)" :key="day.dayOfWeek" class="working-hours-day" :class="{ 'is-closed': !day.isWorking }">
        <label class="working-hours-name"><input v-model="day.isWorking" type="checkbox" :disabled="disabled" @change="enableDay(day)" />{{ weekDay.name }}</label>
        <template v-if="day.isWorking">
          <div class="working-hours-times">
            <div class="working-hours-time"><span aria-hidden="true">Открытие</span><UiTimePicker class="working-hours-picker" :model-value="day.startTime?.slice(0, 5) ?? ''" :label="weekDay.name + ': открытие'" placeholder="ЧЧ:ММ" :step="60" required :disabled="disabled" @update:model-value="day.startTime = $event" /></div>
            <div class="working-hours-time"><span aria-hidden="true">Закрытие</span><UiTimePicker class="working-hours-picker" :model-value="day.endTime?.slice(0, 5) ?? ''" :label="weekDay.name + ': закрытие'" placeholder="ЧЧ:ММ" :step="60" align="end" required :disabled="disabled" @update:model-value="day.endTime = $event" /></div>
          </div>
          <UiButton class="working-hours-add" variant="ghost" :disabled="disabled" :aria-label="weekDay.name + ': добавить перерыв'" @click="addBreak(day)">＋ Перерыв</UiButton>
          <div v-if="day.breaks.length" class="working-hours-breaks">
            <div v-for="(pause, index) in day.breaks" :key="index" class="working-hours-break">
              <div class="working-hours-break-field"><span aria-hidden="true">Перерыв с</span><UiTimePicker class="working-hours-picker" :model-value="pause.startTime.slice(0, 5)" :label="weekDay.name + ': начало перерыва ' + (index + 1)" placeholder="ЧЧ:ММ" :step="60" required :disabled="disabled" @update:model-value="pause.startTime = $event" /></div>
              <div class="working-hours-break-field"><span aria-hidden="true">До</span><UiTimePicker class="working-hours-picker" :model-value="pause.endTime.slice(0, 5)" :label="weekDay.name + ': конец перерыва ' + (index + 1)" placeholder="ЧЧ:ММ" :step="60" align="end" required :disabled="disabled" @update:model-value="pause.endTime = $event" /></div>
              <button type="button" class="working-hours-remove" :disabled="disabled" :aria-label="weekDay.name + ': удалить перерыв ' + (index + 1)" @click="day.breaks.splice(index, 1)">×</button>
            </div>
          </div>
        </template>
        <span v-else class="working-hours-closed">Выходной</span>
      </div>
    </template>
  </div>
</template>

<style scoped>
.working-hours { container-type:inline-size; --hours-columns:minmax(140px, 1fr) minmax(0, 120px) minmax(0, 120px) 110px; }
.working-hours-heading, .working-hours-day { display:grid; grid-template-columns:var(--hours-columns); align-items:center; gap:10px 14px; }
.working-hours-heading { padding:0 0 10px; color:var(--crm-text-muted, #667085); font-size:12px; }
.working-hours-day { min-height:38px; padding:12px 0; border-top:1px solid var(--crm-line, #dce2e9); }
.working-hours-name { display:flex; align-items:center; gap:9px; min-width:0; color:var(--crm-text, #202733); font-weight:600; font-size:13px; }
.working-hours-name input { flex-shrink:0; width:16px; height:16px; margin:0; accent-color:var(--opencrm-accent, #e67337); }
.working-hours-times { display:grid; grid-column:2 / 4; grid-template-columns:repeat(2, minmax(0, 1fr)); gap:14px; }
.working-hours-time { min-width:0; }
.working-hours-time > span { display:none; }
.working-hours-picker :deep(.ui-field-label) { position:absolute; width:1px; height:1px; padding:0; overflow:hidden; clip-path:inset(50%); white-space:nowrap; }
.working-hours .working-hours-add { grid-column:4; justify-self:end; min-height:32px; padding:4px 8px; color:var(--crm-text-muted, #667085); font-size:12px; font-weight:500; }
.working-hours-breaks { display:grid; grid-column:2 / -1; gap:10px; padding:10px 12px; border-radius:6px; background:var(--crm-surface-soft, #f4f6f8); }
.working-hours-break { display:grid; grid-template-columns:minmax(0, 120px) minmax(0, 120px) 30px; align-items:end; gap:10px; }
.working-hours-break-field > span { color:var(--crm-text-muted, #667085); font-size:11px; }
.working-hours-break-field { min-width:0; }
.working-hours-break-field > span { display:block; margin-bottom:5px; }
.working-hours-remove { width:30px; height:36px; padding:0; border:0; border-radius:6px; background:none; color:var(--crm-text-muted, #667085); font-size:22px; cursor:pointer; }
.working-hours-remove:hover { color:var(--crm-danger-text, #b42318); background:var(--crm-surface, #fff); }
.working-hours-closed { grid-column:2 / -1; justify-self:start; padding:5px 9px; border-radius:5px; background:var(--crm-surface-soft, #f4f6f8); color:var(--crm-text-muted, #667085); font-size:12px; }
.working-hours .is-closed .working-hours-name { color:var(--crm-text-muted, #667085); font-weight:500; }
.working-hours input:focus-visible, .working-hours-remove:focus-visible { outline:2px solid var(--opencrm-accent, #e67337); outline-offset:2px; }
@container (max-width:560px) {
  .working-hours-heading { display:none; }
  .working-hours-day { grid-template-columns:minmax(0, 1fr) auto; padding:14px 0; }
  .working-hours-day:first-of-type { border-top:0; }
  .working-hours-times { grid-column:1 / -1; grid-row:2; gap:10px; }
  .working-hours-time > span { display:block; margin-bottom:5px; color:var(--crm-text-muted, #667085); font-size:11px; }
  .working-hours .working-hours-add { grid-column:2; grid-row:1; }
  .working-hours-breaks { grid-column:1 / -1; padding:10px; }
  .working-hours-break { grid-template-columns:minmax(0, 1fr) minmax(0, 1fr) 24px; gap:8px; }
  .working-hours-remove { width:24px; }
  .working-hours-closed { grid-column:2; justify-self:end; }
}
</style>

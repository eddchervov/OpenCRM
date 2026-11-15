<script setup lang="ts">
import { computed } from 'vue'
import { UiButton, formatDateOnly } from '@opencrm/ui'
import type { WorkspaceWorkingDay } from '../api/setup'
import { hasWorkingSchedule, nextWorkingDay } from '../workingHours'

const props = defineProps<{
  date: string
  schedule: WorkspaceWorkingDay[]
  canManage: boolean
  timeZoneId?: string | null
}>()
const emit = defineEmits<{ selectDate: [date: string] }>()
const next = computed(() => nextWorkingDay(props.date, props.schedule))
const isScheduleMissing = computed(() => !hasWorkingSchedule(props.schedule))
const title = computed(() => {
  if (isScheduleMissing.value) return props.canManage ? 'Настройте график работы' : 'График работы не настроен'

  return props.date === formatDateOnly(undefined, props.timeZoneId || 'UTC') ? 'Сегодня выходной' : 'В этот день выходной'
})
const dateLabel = computed(() => new Date(props.date + 'T12:00:00').toLocaleDateString('ru-RU', {
  day: 'numeric', month: 'long', weekday: 'long'
}))
const nextLabel = computed(() => {
  if (!next.value) return ''
  const date = new Date(next.value.date + 'T12:00:00').toLocaleDateString('ru-RU', {
    weekday: 'short', day: 'numeric', month: 'long'
  })

  return date + ' · ' + next.value.intervals.map(interval => interval.startTime + '–' + interval.endTime).join(', ')
})
</script>

<template>
  <section class="scheduler-closed" :aria-label="isScheduleMissing ? 'График работы не настроен' : 'Выходной день'">
    <svg class="scheduler-closed__icon" viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
      <path d="M10 3v6m12-6v6M5 13h22M17 28H7a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h18a2 2 0 0 1 2 2v9" />
      <path d="M25 19a6 6 0 1 0 5 8 5 5 0 0 1-5-8Z" />
    </svg>
    <div class="scheduler-closed__body">
      <h3>{{ title }}</h3>
      <p v-if="isScheduleMissing">{{ canManage ? 'Укажите рабочие дни и часы организации, чтобы планировать записи по графику.' : 'Попросите администратора организации указать рабочие дни и часы.' }}</p>
      <p v-else>{{ dateLabel }} — выходной по графику организации.</p>
      <div v-if="next" class="scheduler-closed__next">
        <UiButton @click="emit('selectDate', next!.date)">К ближайшему рабочему дню</UiButton>
        <span>{{ nextLabel }}</span>
      </div>
      <p v-else-if="!isScheduleMissing">Ближайший рабочий день в графике не указан.</p>
    </div>
    <a v-if="canManage" class="scheduler-closed__settings" href="#settings?tab=workflow">Настроить график</a>
  </section>
</template>

<style scoped>
.scheduler-closed { display: flex; align-items: flex-start; gap: 20px; margin-top: 20px; padding: 24px; border: 1px solid color-mix(in srgb, var(--crm-accent, #cc5515) 40%, var(--crm-line, #dce2e9)); border-radius: 9px; background: var(--crm-surface-soft, #f4f6f8); color: var(--crm-text, #202833); }
.scheduler-closed__icon { flex: 0 0 38px; width: 38px; height: 38px; margin-top: 2px; color: var(--crm-accent, #cc5515); }
.scheduler-closed__body { flex: 1; min-width: 0; }
.scheduler-closed h3 { margin: 0; font-size: 20px; }
.scheduler-closed p { margin: 9px 0 0; color: var(--crm-text-muted, #667085); font-size: 14px; }
.scheduler-closed__next { display: flex; align-items: center; flex-wrap: wrap; gap: 12px 20px; margin-top: 18px; }
.scheduler-closed__next span { color: var(--crm-text-muted, #667085); font-size: 13px; }
.scheduler-closed__settings { flex-shrink: 0; margin-top: 7px; color: var(--crm-accent, #cc5515); font-size: 13px; text-decoration: none; }
.scheduler-closed__settings:hover { text-decoration: underline; }
.scheduler-closed__settings:focus-visible { outline: 2px solid var(--crm-accent, #cc5515); outline-offset: 4px; }
@media (max-width: 800px) {
  .scheduler-closed { flex-wrap: wrap; gap: 14px; padding: 18px; }
  .scheduler-closed__body { flex-basis: calc(100% - 52px); }
  .scheduler-closed__settings { margin: 0 0 0 52px; }
  .scheduler-closed h3 { font-size: 18px; }
}
</style>

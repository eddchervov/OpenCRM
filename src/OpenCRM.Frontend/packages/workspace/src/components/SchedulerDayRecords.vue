<script setup lang="ts">
import { nextTick } from 'vue'
import { UiButton } from '@opencrm/ui'
import { formatLocaleDate, translate as t } from '@opencrm/ui/i18n'
import '../schedulerCalendarMessages'
import SchedulerBookingDialog from './SchedulerBookingDialog.vue'
import SchedulerList from './SchedulerList.vue'
import type { SchedulerCalendarItem } from '../utils/schedulerDay'

const props = defineProps<{
  open: boolean
  date: string
  items: SchedulerCalendarItem[]
  organizationId: string
  canCreate: boolean
}>()
const emit = defineEmits<{ close: []; day: [date: string]; create: [slot: { date: string }] }>()
async function openDay() {
  emit('close')
  await nextTick()
  emit('day', props.date)
}
async function create() {
  emit('close')
  await nextTick()
  emit('create', { date: props.date })
}

function formatDate(date: string) {
  return formatLocaleDate(new Date(`${date.slice(0, 10)}T12:00:00Z`), { dateStyle: 'long', timeZone: 'UTC' })
}
</script>

<template>
  <SchedulerBookingDialog :open="open" :title="t('schedulerCalendar.dayRecordsTitle', { date: formatDate(date) })" @close="emit('close')">
    <div class="scheduler-day-records">
      <SchedulerList :items="items" :organization-id="organizationId" :can-write="false" />
      <footer>
        <UiButton variant="secondary" @click="openDay">{{ t('schedulerCalendar.openDay') }}</UiButton>
        <UiButton v-if="canCreate" variant="primary" @click="create">{{ t('schedulerCalendar.newRecord') }}</UiButton>
      </footer>
    </div>
  </SchedulerBookingDialog>
</template>

<style scoped>
.scheduler-day-records { padding: 6px 20px 20px; min-width: 0; }
.scheduler-day-records footer { display: flex; justify-content: flex-end; flex-wrap: wrap; gap: 10px; margin-top: 18px; }
.scheduler-day-records footer :deep(.ui-button--primary) { color: #fff; background: #d85b19; border-color: #d85b19; }
@media (max-width: 600px) { .scheduler-day-records { padding: 4px 12px 16px; } }
</style>

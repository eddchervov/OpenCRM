<script setup lang="ts">
import { computed } from 'vue'
import { UiButton } from '@opencrm/ui'
import { formatLocaleDate, translate as t } from '@opencrm/ui/i18n'
import '../schedulerMessages'
import '../schedulerControlMessages'
import '../schedulerCalendarMessages'
import SchedulerBookingDialog from './SchedulerBookingDialog.vue'
import { getVehicleBrandLogo } from '../utils/vehicleBrandLogo'
import type { SchedulerCalendarItem } from '../utils/schedulerDay'

const props = defineProps<{ item: SchedulerCalendarItem | null }>()
const emit = defineEmits<{ close: [] }>()
const logo = computed(() => getVehicleBrandLogo(props.item?.brandLogoKey))
const status = computed(() => {
  const item = props.item
  if (!item) return ''
  if (item.handedOverAt) return t('scheduler.handedOver')
  if (item.readyForHandoverAt) return t('scheduler.ready')

  const statusKeys: Record<number, string> = {
    1: 'scheduler.scheduled',
    2: 'scheduler.inProgress',
    3: 'scheduler.completed',
    4: 'scheduler.cancelled'
  }

  return statusKeys[item.status] ? t(statusKeys[item.status]!) : item.statusName || t('schedulerControl.statusNotSpecified')
})

function formatDate(date: string) {
  const dateOnly = new Date(`${date.slice(0, 10)}T12:00:00Z`)

  return formatLocaleDate(dateOnly, { dateStyle: 'short', timeZone: 'UTC' })
}
</script>

<template>
  <SchedulerBookingDialog :open="!!item" :title="t('schedulerCalendar.previewTitle')" @close="emit('close')">
    <div v-if="item" class="scheduler-record-preview">
      <div class="scheduler-record-preview__vehicle">
        <img v-if="logo" :src="logo.src" :alt="t('schedulerControl.logoAlt', { name: logo.name })">
        <div><h3>{{ item.vehicleDisplayName || t('schedulerCalendar.record') }}</h3><span v-if="item.registrationPlate || item.vehicleSubtitle">{{ item.registrationPlate || item.vehicleSubtitle }}</span></div>
      </div>
      <dl class="scheduler-record-preview__facts">
        <div><dt>{{ t('schedulerCalendar.status') }}</dt><dd>{{ status }}</dd></div>
        <div><dt>{{ t('schedulerCalendar.start') }}</dt><dd>{{ formatDate(item.date) }} · {{ item.plannedStartTime?.slice(0, 5) || t('schedulerCalendar.noTime') }}</dd></div>
        <div v-if="item.plannedEndTime || item.plannedEndDate && item.plannedEndDate.slice(0, 10) !== item.date.slice(0, 10)"><dt>{{ t('schedulerCalendar.end') }}</dt><dd>{{ formatDate(item.plannedEndDate || item.date) }}<template v-if="item.plannedEndTime"> · {{ item.plannedEndTime.slice(0, 5) }}</template></dd></div>
        <div><dt>{{ t('schedulerCalendar.location') }}</dt><dd>{{ item.location || t('schedulerCalendar.unassigned') }}</dd></div>
        <div><dt>{{ t('schedulerCalendar.employees') }}</dt><dd>{{ item.employeeNames?.join(', ') || t('schedulerCalendar.employeesUnassigned') }}</dd></div>
      </dl>
      <section class="scheduler-record-preview__services">
        <h4 class="ui-section-header">{{ t('schedulerCalendar.services') }}</h4>
        <ul v-if="item.serviceNames?.length"><li v-for="(service, index) in item.serviceNames" :key="index">{{ service }}</li></ul>
        <p v-else>{{ t('schedulerCalendar.servicesUnspecified') }}</p>
      </section>
      <footer>
        <UiButton variant="secondary" @click="emit('close')">{{ t('schedulerCalendar.close') }}</UiButton>
        <UiButton :href="`#maintenance/${item.id}`">{{ t('schedulerCalendar.openMaintenance') }}</UiButton>
      </footer>
    </div>
  </SchedulerBookingDialog>
</template>

<style scoped>
.scheduler-record-preview { display: grid; gap: 20px; padding: 4px 24px 20px; color: var(--crm-text, #202833); overflow-wrap: anywhere; }
.scheduler-record-preview__vehicle { display: flex; align-items: center; gap: 12px; }
.scheduler-record-preview__vehicle img { flex: 0 0 40px; width: 40px; height: 40px; padding: 4px; border-radius: 8px; object-fit: contain; background: #fff; }
.scheduler-record-preview__vehicle h3 { margin: 0 0 6px; font-size: 19px; }
.scheduler-record-preview__vehicle span { font-size: 13px; color: var(--crm-text-muted, #667085); }
.scheduler-record-preview__facts { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px 24px; margin: 0; font-size: 13px; }
.scheduler-record-preview__facts dt { color: var(--crm-text-muted, #667085); }
.scheduler-record-preview__facts dd { margin: 5px 0 0; }
.scheduler-record-preview__services h4 { margin: 0; font-size: 14px; }
.scheduler-record-preview__services ul { margin: 0; padding: 0; list-style: none; }
.scheduler-record-preview__services li { padding: 12px 0; font-size: 13px; line-height: 1.5; }
.scheduler-record-preview__services li:not(:last-child) { border-bottom: 1px solid var(--crm-line, #dce2e9); }
.scheduler-record-preview__services p { font-size: 13px; color: var(--crm-text-muted, #667085); }
.scheduler-record-preview footer { display: flex; justify-content: flex-end; flex-wrap: wrap; gap: 10px; padding-top: 16px; border-top: 1px solid var(--crm-line, #dce2e9); }
@media (max-width: 600px) {
  .scheduler-record-preview { padding: 4px 12px 16px; }
  .scheduler-record-preview__facts { grid-template-columns: minmax(0, 1fr); }
}
</style>

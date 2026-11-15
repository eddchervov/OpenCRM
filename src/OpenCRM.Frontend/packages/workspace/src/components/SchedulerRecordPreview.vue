<script setup lang="ts">
import { computed } from 'vue'
import { UiButton } from '@opencrm/ui'
import SchedulerBookingDialog from './SchedulerBookingDialog.vue'
import { getVehicleBrandLogo } from '../utils/vehicleBrandLogo'
import type { SchedulerCalendarItem } from '../utils/schedulerDay'

const props = defineProps<{ item: SchedulerCalendarItem | null }>()
const emit = defineEmits<{ close: [] }>()
const logo = computed(() => getVehicleBrandLogo(props.item?.brandLogoKey))
const status = computed(() => props.item?.status === 4 ? props.item.statusName || 'Отменено'
  : props.item?.handedOverAt ? 'Выдано' : props.item?.readyForHandoverAt ? 'Готово к выдаче'
    : props.item?.statusName || ({ 1: 'Запланировано', 2: 'В работе', 3: 'Выполнено', 4: 'Отменено' }[props.item?.status ?? 0] ?? 'Статус не указан'))

function formatDate(date: string) {
  return date.slice(0, 10).split('-').reverse().join('.')
}
</script>

<template>
  <SchedulerBookingDialog :open="!!item" title="Просмотр записи" @close="emit('close')">
    <div v-if="item" class="scheduler-record-preview">
      <div class="scheduler-record-preview__vehicle">
        <img v-if="logo" :src="logo.src" :alt="`Логотип ${logo.name}`">
        <div><h3>{{ item.vehicleDisplayName || 'Запись' }}</h3><span v-if="item.registrationPlate || item.vehicleSubtitle">{{ item.registrationPlate || item.vehicleSubtitle }}</span></div>
      </div>
      <dl class="scheduler-record-preview__facts">
        <div><dt>Статус</dt><dd>{{ status }}</dd></div>
        <div><dt>Начало</dt><dd>{{ formatDate(item.date) }} · {{ item.plannedStartTime?.slice(0, 5) || 'Без времени' }}</dd></div>
        <div v-if="item.plannedEndTime || item.plannedEndDate && item.plannedEndDate.slice(0, 10) !== item.date.slice(0, 10)"><dt>Окончание</dt><dd>{{ formatDate(item.plannedEndDate || item.date) }}<template v-if="item.plannedEndTime"> · {{ item.plannedEndTime.slice(0, 5) }}</template></dd></div>
        <div><dt>Пост / место</dt><dd>{{ item.location || 'Не назначен' }}</dd></div>
        <div><dt>Сотрудники</dt><dd>{{ item.employeeNames?.join(', ') || 'Не назначены' }}</dd></div>
      </dl>
      <section class="scheduler-record-preview__services">
        <h4 class="ui-section-header">Услуги</h4>
        <ul v-if="item.serviceNames?.length"><li v-for="(service, index) in item.serviceNames" :key="index">{{ service }}</li></ul>
        <p v-else>Услуги не указаны.</p>
      </section>
      <footer>
        <UiButton variant="secondary" @click="emit('close')">Закрыть</UiButton>
        <UiButton :href="`#maintenance/${item.id}`">Открыть обслуживание</UiButton>
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

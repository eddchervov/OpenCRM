<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { UiButton, UiDatePicker, UiSelect, useLocalization } from '@opencrm/ui'
import { formatLocaleDate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/schedulerMessages'
import { BookingTimeFields } from '@opencrm/workspace'
import { changeMaintenanceWorkflow } from '@opencrm/workspace/api/maintenance'
import type { DetailingCatalogItem, DetailingMaintenanceMove } from '@/api/detailingBff'

const props = defineProps<{
  organizationId: string
  move: DetailingMaintenanceMove
  posts: DetailingCatalogItem[]
  canWrite: boolean
}>()
const { t } = useLocalization()
const emit = defineEmits<{ close: [], saved: [] }>()
const dialog = ref<HTMLDialogElement | null>(null)
const timeFields = ref<InstanceType<typeof BookingTimeFields> | null>(null)
const date = ref(props.move.date)
const endDate = ref(props.move.endDate)
const start = ref(props.move.start)
const end = ref(props.move.end)
const location = ref(props.move.location)
const saving = ref(false)
const error = ref('')
const postOptions = computed(() => {
  const names = props.posts.filter(post => post.isActive).map(post => post.name)
  if (props.move.item.location) names.push(props.move.item.location)
  return [{ value: '', label: t('maintenanceCalendar.noPost') }, ...Array.from(new Set(names)).map(name => ({ value: name, label: name }))]
})
const previousPeriod = computed(() => {
  const item = props.move.item
  const formatDate = (value: string) => {
    const [year, month, day] = value.slice(0, 10).split('-').map(Number)
    return formatLocaleDate(new Date(Date.UTC(year, month - 1, day, 12)), { dateStyle: 'short', timeZone: 'UTC' })
  }
  const startDate = item.date.slice(0, 10)
  const endDate = (item.plannedEndDate || item.date).slice(0, 10)
  const from = formatDate(startDate)
  const to = formatDate(endDate)
  const noTime = t('maintenanceCalendar.noTime')
  const period = `${from} ${item.plannedStartTime?.slice(0, 5) || noTime} — ${endDate !== startDate ? `${to} ` : ''}${item.plannedEndTime?.slice(0, 5) || noTime}`

  return t('maintenanceCalendar.now', { period, location: item.location || t('maintenanceCalendar.noPost') })
})

watch(date, (value, previous) => {
  const offset = Date.parse(`${value}T00:00:00Z`) - Date.parse(`${previous}T00:00:00Z`)
  const nextEnd = Date.parse(`${endDate.value}T00:00:00Z`) + offset
  if (Number.isFinite(nextEnd)) endDate.value = new Date(nextEnd).toISOString().slice(0, 10)
})

function close() {
  if (saving.value) return
  dialog.value?.close()
  emit('close')
}

async function save() {
  if (saving.value || !props.canWrite || !props.move.item.canReschedule) return
  error.value = ''
  if (!date.value || !endDate.value || !start.value || !end.value) {
    error.value = t('maintenanceCalendar.invalidPeriod')
    return
  }
  saving.value = true
  try {
    if (!await timeFields.value?.validate()) return
    await changeMaintenanceWorkflow(props.organizationId, props.move.item.id, {
      action: 'reschedule', version: props.move.item.version,
      plannedDate: date.value, plannedEndDate: endDate.value,
      plannedStartTime: `${start.value}:00`, plannedEndTime: `${end.value}:00`,
      location: location.value || ''
    })
    emit('saved')
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : t('maintenanceCalendar.moveError')
  } finally {
    saving.value = false
  }
}

onMounted(() => dialog.value?.showModal())
</script>

<template>
  <Teleport to="body">
    <dialog ref="dialog" class="maintenance-move" aria-labelledby="maintenance-move-title" @cancel.prevent="close">
      <header class="ui-modal__header">
        <h2 id="maintenance-move-title">{{ t('maintenanceCalendar.moveTitle') }}</h2>
        <button type="button" class="ui-modal__close" :aria-label="t('maintenanceCalendar.close')" :disabled="saving" @click="close">×</button>
      </header>
      <form @submit.prevent="save">
        <div class="ui-modal__body maintenance-move__body">
          <div class="maintenance-move__previous">
            <strong>{{ move.item.vehicleDisplayName }}</strong>
            <span>{{ move.item.typeName }}</span>
            <span>{{ previousPeriod }}</span>
          </div>
          <div class="maintenance-move__dates">
            <UiDatePicker v-model="date" :label="t('maintenanceCalendar.startDate')" required :disabled="saving" />
            <UiDatePicker v-model="endDate" :label="t('maintenanceCalendar.endDate')" required :disabled="saving" />
          </div>
          <UiSelect v-model="location" :label="t('maintenanceCalendar.postLabel')" :options="postOptions" :disabled="saving" />
          <BookingTimeFields ref="timeFields" v-model:start="start" v-model:end="end" v-model:end-date="endDate"
            :organization-id="organizationId" :date="date" :location="location || ''"
            :exclude-maintenance-id="move.item.id" :disabled="saving" />
          <div v-if="error" class="maintenance-move__error" role="alert">{{ error }}</div>
        </div>
        <footer class="ui-modal__footer">
          <UiButton variant="secondary" :disabled="saving" @click="close">{{ t('maintenanceCalendar.cancel') }}</UiButton>
          <UiButton type="submit" :disabled="saving || !canWrite">{{ saving ? t('maintenanceCalendar.moving') : t('maintenanceCalendar.confirmMove') }}</UiButton>
        </footer>
      </form>
    </dialog>
  </Teleport>
</template>

<style scoped>
.maintenance-move {
  width: min(540px, calc(100vw - 24px));
  max-height: calc(100dvh - 32px);
  padding: 0;
  border: 1px solid var(--crm-line, #dce2e9);
  border-radius: 12px;
  background: var(--crm-surface, #fff);
  color: var(--crm-text, #202733);
}
.maintenance-move::backdrop { background: rgb(0 0 0 / 55%); }
.maintenance-move__body { display: grid; gap: 16px; }
.maintenance-move__previous { display: grid; gap: 4px; font-size: 13px; overflow-wrap: anywhere; }
.maintenance-move__previous span { color: var(--crm-text-muted, #667085); }
.maintenance-move__dates { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
.maintenance-move__error {
  padding: 10px 12px;
  border-radius: 6px;
  color: var(--crm-danger-text, #b42318);
  background: var(--crm-danger-surface, #fff3f2);
}
@media (max-width: 420px) { .maintenance-move__dates { grid-template-columns: 1fr; } }
</style>

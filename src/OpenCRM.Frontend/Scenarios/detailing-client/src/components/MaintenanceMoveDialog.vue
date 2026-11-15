<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { UiButton, UiDatePicker, UiSelect } from '@opencrm/ui'
import { BookingTimeFields } from '@opencrm/workspace'
import { changeMaintenanceWorkflow } from '@opencrm/workspace/api/maintenance'
import type { DetailingCatalogItem, DetailingMaintenanceMove } from '@/api/detailingBff'

const props = defineProps<{
  organizationId: string
  move: DetailingMaintenanceMove
  posts: DetailingCatalogItem[]
  canWrite: boolean
}>()
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
  return [{ value: '', label: 'Без поста' }, ...Array.from(new Set(names)).map(name => ({ value: name, label: name }))]
})
const previousPeriod = computed(() => {
  const item = props.move.item
  const from = item.date.slice(0, 10).split('-').reverse().join('.')
  const to = (item.plannedEndDate || item.date).slice(0, 10).split('-').reverse().join('.')
  return `${from} ${item.plannedStartTime?.slice(0, 5) || 'без времени'} — ${to !== from ? to + ' ' : ''}${item.plannedEndTime?.slice(0, 5) || 'без времени'}`
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
    error.value = 'Укажите даты и время начала и окончания.'
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
    error.value = cause instanceof Error ? cause.message : 'Не удалось перенести запись.'
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
        <h2 id="maintenance-move-title">Перенести запись</h2>
        <button type="button" class="ui-modal__close" aria-label="Закрыть" :disabled="saving" @click="close">×</button>
      </header>
      <form @submit.prevent="save">
        <div class="ui-modal__body maintenance-move__body">
          <div class="maintenance-move__previous">
            <strong>{{ move.item.vehicleDisplayName }}</strong>
            <span>{{ move.item.typeName }}</span>
            <span>Сейчас: {{ previousPeriod }} · {{ move.item.location || 'Без поста' }}</span>
          </div>
          <div class="maintenance-move__dates">
            <UiDatePicker v-model="date" label="Дата начала" required :disabled="saving" />
            <UiDatePicker v-model="endDate" label="Дата окончания" required :disabled="saving" />
          </div>
          <UiSelect v-model="location" label="Пост / место" :options="postOptions" :disabled="saving" />
          <BookingTimeFields ref="timeFields" v-model:start="start" v-model:end="end" v-model:end-date="endDate"
            :organization-id="organizationId" :date="date" :location="location || ''"
            :exclude-maintenance-id="move.item.id" :disabled="saving" />
          <div v-if="error" class="maintenance-move__error" role="alert">{{ error }}</div>
        </div>
        <footer class="ui-modal__footer">
          <UiButton variant="secondary" :disabled="saving" @click="close">Отмена</UiButton>
          <UiButton type="submit" :disabled="saving || !canWrite">{{ saving ? 'Переносим…' : 'Подтвердить перенос' }}</UiButton>
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

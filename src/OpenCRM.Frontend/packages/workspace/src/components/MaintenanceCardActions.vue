<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton, UiDatePicker, UiSelect, UiModal, UiTextarea, formatDateOnly } from '@opencrm/ui'
import { loadScenarioCatalog, type WorkspaceScenarioCatalogItem } from '../api/scenarioCatalog'
import BookingTimeFields from './BookingTimeFields.vue'
import { bookingDayOffset } from '../api/bookingSlots'
import { changeMaintenanceWorkflow, type WorkspaceMaintenance, type WorkspaceMaintenanceWorkflowAction } from '../api/maintenance'

type ActionCode = WorkspaceMaintenanceWorkflowAction['action']
type Action = { code: ActionCode, label: string, description: string, confirm: string }
const props = defineProps<{ organizationId: string, maintenance: WorkspaceMaintenance, timeZoneId?: string | null, disabled?: boolean, hideTrigger?: boolean, simpleWorkflow?: boolean }>()
const emit = defineEmits<{ changed: [] }>()
const actions: Action[] = [
  { code: 'simple_handover', label: 'Выдать', description: 'Подтвердите выполнение услуг и передачу автомобиля клиенту. Выполнение и выдача сохранятся одним действием. Оплата учитывается отдельно.', confirm: 'Выдать' },
  { code: 'start', label: 'В работу', description: 'Начать работу по записи.', confirm: 'Взять в работу' },
  { code: 'complete', label: 'Готов к выдаче', description: 'Завершить работы и отметить готовность к выдаче. Дата выполнения будет зафиксирована сегодня.', confirm: 'Завершить работы' },
  { code: 'mark_ready', label: 'Отметить готовность', description: 'Работы уже выполнены. Отметьте готовность, если техника ещё у вас и ожидает выдачи.', confirm: 'Готов к выдаче' },
  { code: 'handover', label: 'Выдать технику', description: 'Подтвердите, что техника передана получателю. Время выдачи сохранится в истории.', confirm: 'Подтвердить выдачу' },
  { code: 'undo_handover', label: 'Отменить выдачу', description: 'Исправить ошибочную отметку выдачи. Работы останутся выполненными, техника вернётся в ожидающие выдачи.', confirm: 'Отменить выдачу' },
  { code: 'reschedule', label: 'Перенести запись', description: 'Изменить плановую дату и время, сохранив статус.', confirm: 'Перенести запись' },
  { code: 'return_to_planned', label: 'Вернуть в запланированные', description: 'Если работу начали по ошибке. Дата, время и данные записи сохранятся.', confirm: 'Вернуть в запланированные' },
  { code: 'return_to_work', label: 'Вернуть в работу', description: 'Отменить завершение. Фактическая дата очистится, связанный пробег будет пересчитан. Оплаты, материалы и фото сохранятся.', confirm: 'Вернуть в работу' },
  { code: 'cancel', label: 'Отменить запись', description: 'Освободить время на посту. Запись и её история сохранятся.', confirm: 'Отменить запись' },
  { code: 'reopen', label: 'Возобновить запись', description: 'Выбрать дату и вернуть отменённую запись в запланированные.', confirm: 'Возобновить запись' }
]
const available = computed(() => {
  if (props.simpleWorkflow) {
    const codes: ActionCode[] = props.maintenance.handedOverAt ? ['undo_handover']
      : props.maintenance.status === 1 || props.maintenance.status === 2 ? ['simple_handover', 'reschedule', 'cancel']
        : props.maintenance.status === 3 ? ['simple_handover']
          : props.maintenance.status === 4 ? ['reopen'] : []
    return actions.filter(action => codes.includes(action.code))
  }
  const codes: ActionCode[] = props.maintenance.status === 1 ? ['start', 'complete', 'reschedule', 'cancel']
    : props.maintenance.status === 2 ? ['complete', 'reschedule', 'return_to_planned', 'cancel']
      : props.maintenance.status === 3 ? (props.maintenance.handedOverAt ? ['undo_handover']
        : props.maintenance.readyForHandoverAt ? ['handover', 'return_to_work'] : ['mark_ready', 'return_to_work'])
        : props.maintenance.status === 4 ? ['reopen'] : []
  return actions.filter(action => codes.includes(action.code))
})
const primary = computed(() => available.value.find(action => action.code === (
  props.simpleWorkflow ? (props.maintenance.status === 4 ? 'reopen' : 'simple_handover')
    : props.maintenance.status === 1 ? 'start' : props.maintenance.status === 2 ? 'complete'
    : props.maintenance.status === 3 ? (props.maintenance.handedOverAt || props.maintenance.readyForHandoverAt ? null : 'mark_ready')
      : 'reopen'
)))
const others = computed(() => available.value.filter(action => action !== primary.value))
const open = ref(false)
const selected = ref<Action | null>(null)
const busy = ref(false)
const error = ref('')
const reason = ref('')
const date = ref('')
const endDate = ref('')
const start = ref('')
const end = ref('')
const location = ref('')
const posts = ref<WorkspaceScenarioCatalogItem[]>([])
const loadingPosts = ref(false)
const postError = ref('')
const postOptions = computed(() => [...new Set(['', location.value, ...posts.value.filter(item => item.isActive).map(item => item.name)])]
  .map(value => ({ value, label: value || 'Без поста' })))
async function loadPosts() {
  loadingPosts.value = true
  postError.value = ''
  try { posts.value = (await loadScenarioCatalog(props.organizationId)).workPosts ?? [] }
  catch (failure) { postError.value = failure instanceof Error ? failure.message : 'Не удалось загрузить посты.' }
  finally { loadingPosts.value = false }
}
const content = ref<HTMLElement | null>(null)
const bookingTimes = ref<InstanceType<typeof BookingTimeFields> | null>(null)
const needsSchedule = computed(() => selected.value?.code === 'reschedule' || selected.value?.code === 'reopen')
const needsReason = computed(() => ['return_to_planned', 'return_to_work', 'cancel', 'undo_handover'].includes(selected.value?.code ?? ''))
let opener: HTMLElement | null = null

function show(action: Action | null = null) {
  if (busy.value || props.disabled) return
  if (!open.value) opener = document.activeElement as HTMLElement | null
  selected.value = action
  reason.value = ''
  error.value = ''
  date.value = (props.maintenance.plannedDate ?? props.maintenance.date).slice(0, 10)
  const duration = bookingDayOffset((props.maintenance.plannedDate ?? props.maintenance.date).slice(0, 10), (props.maintenance.plannedEndDate ?? props.maintenance.plannedDate ?? props.maintenance.date).slice(0, 10))
  endDate.value = new Date(Date.parse(`${date.value}T00:00:00Z`) + duration * 60000).toISOString().slice(0, 10)
  start.value = props.maintenance.plannedStartTime?.slice(0, 5) ?? ''
  end.value = props.maintenance.plannedEndTime?.slice(0, 5) ?? ''
  location.value = props.maintenance.location ?? ''
  if (action?.code === 'reschedule' || action?.code === 'reopen') void loadPosts()
  open.value = true
  void nextTick(() => content.value?.focus())
}

function close() {
  if (busy.value) return
  open.value = false
  void nextTick(() => opener?.isConnected && opener.focus())
}

function handleKey(event: KeyboardEvent) {
  if (!open.value) return
  if (event.key === 'Escape') {
    event.preventDefault()
    close()
    return
  }
  if (event.key !== 'Tab') return
  const panel = content.value?.closest('.ui-modal__panel')
  const elements = Array.from(panel?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), textarea:not(:disabled), [tabindex="0"]') ?? [])
    .filter(element => element.getClientRects().length > 0)
  const first = elements[0], last = elements.at(-1)
  if (event.shiftKey && (document.activeElement === first || document.activeElement === content.value)) {
    event.preventDefault()
    last?.focus()
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault()
    first?.focus()
  }
}

async function submit() {
  if (!selected.value || busy.value || props.disabled) return
  if (!available.value.some(action => action.code === selected.value?.code)) {
    error.value = 'Статус записи изменился. Обновите карточку.'
    return
  }
  if (needsSchedule.value && !date.value) {
    error.value = 'Выберите плановую дату.'
    return
  }
  if (selected.value.code === 'undo_handover' && !reason.value.trim()) {
    error.value = 'Укажите причину отмены выдачи.'
    return
  }
  busy.value = true
  error.value = ''
  try {
    if (needsSchedule.value && bookingTimes.value && !await bookingTimes.value.validate()) return
    const payload: WorkspaceMaintenanceWorkflowAction = { action: selected.value.code, version: props.maintenance.version, reason: reason.value.trim() || null }
    if (needsSchedule.value) {
      if (loadingPosts.value || postError.value) return
      payload.location = location.value
      payload.plannedDate = date.value
      payload.plannedEndDate = endDate.value || date.value
      payload.plannedStartTime = start.value ? `${start.value}:00` : null
      payload.plannedEndTime = end.value ? `${end.value}:00` : null
    }
    if (selected.value.code === 'complete') {
      payload.completedDate = formatDateOnly(undefined, props.timeZoneId || 'UTC')
      payload.mileage = props.maintenance.mileage ?? null
    }
    await changeMaintenanceWorkflow(props.organizationId, props.maintenance.id, payload)
    open.value = false
    emit('changed')
  } catch (failure) {
    error.value = failure instanceof Error ? failure.message : 'Не удалось изменить запись.'
  } finally {
    busy.value = false
  }
}

defineExpose({ show })

watch(() => props.maintenance.id, () => { open.value = false })
onMounted(() => document.addEventListener('keydown', handleKey))
onBeforeUnmount(() => document.removeEventListener('keydown', handleKey))
watch(date, (value, previous) => {
  if (!value || !previous || !endDate.value) return
  const duration = bookingDayOffset(previous, endDate.value)
  endDate.value = new Date(Date.parse(`${value}T00:00:00Z`) + duration * 60000).toISOString().slice(0, 10)
}, { flush: 'sync' })
</script>

<template>
  <div v-if="available.length" class="record-actions">
    <UiButton v-if="primary && !hideTrigger" class="record-actions__primary" :disabled="disabled || busy" @click="show(primary)">
      {{ primary.label }}
    </UiButton>
    <UiButton v-if="others.length && !hideTrigger" variant="secondary" :disabled="disabled || busy" aria-haspopup="dialog" @click="show()">
      Действия <svg class="record-actions__chevron" viewBox="0 0 16 16" width="12" height="12" fill="none" aria-hidden="true"><path d="m4 6 4 4 4-4" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" /></svg>
    </UiButton>
    <UiModal :open="open" :title="selected?.label ?? 'Действия с записью'" @close="close">
      <div ref="content" class="workflow-dialog" tabindex="-1" :aria-busy="busy">
        <div v-if="!selected" class="workflow-dialog__actions">
          <button v-for="action in hideTrigger ? available : others" :key="action.code" type="button" class="workflow-dialog__action"
            :class="{ 'workflow-dialog__action--danger': action.code === 'cancel' }" @click="show(action)">
            <strong>{{ action.label }}</strong>
            <span>{{ action.description }}</span>
          </button>
        </div>
        <template v-else>
          <p class="workflow-dialog__description">{{ selected.description }}</p>
          <p v-if="selected.code === 'return_to_work' && maintenance.linkedDefects.length" class="workflow-dialog__description">
            Статусы связанных неисправностей сохранятся. При необходимости измените их отдельно.
          </p>
          <p v-if="(selected.code === 'handover' || selected.code === 'simple_handover') && maintenance.debtAmount > 0" class="workflow-dialog__balance">
            Осталось оплатить: <strong>{{ new Intl.NumberFormat('ru-RU', { style: 'currency', currency: 'RUB' }).format(maintenance.debtAmount) }}</strong>.
            Выдача не изменяет оплату.
          </p>
          <div v-if="needsSchedule" class="workflow-dialog__schedule">
            <UiDatePicker v-model="date" label="Дата начала" :disabled="busy" required />
            <UiDatePicker v-model="endDate" label="Дата окончания" :disabled="busy" required />
            <UiSelect v-model="location" label="Пост / место" :options="postOptions" :disabled="busy || loadingPosts || !!postError" />
            <p v-if="postError" role="alert">{{ postError }} <button type="button" @click="loadPosts">Повторить</button></p>
            <BookingTimeFields ref="bookingTimes" v-model:start="start" v-model:end="end"
              :organization-id="organizationId" :date="date" :end-date="endDate" :location="location"
              :exclude-maintenance-id="maintenance.id" :disabled="busy" />
          </div>
          <UiTextarea v-if="needsReason" v-model="reason" :label="selected.code === 'undo_handover' ? 'Причина отмены выдачи' : 'Комментарий к изменению'" :required="selected.code === 'undo_handover'" :placeholder="selected.code === 'undo_handover' ? 'Например, отметили по ошибке' : 'Необязательно'" :rows="2" :disabled="busy" />
          <div v-if="error" class="workflow-dialog__error" role="alert">{{ error }}</div>
        </template>
      </div>
      <template #footer>
        <UiButton v-if="hideTrigger && selected" variant="secondary" :disabled="busy" @click="show()">К действиям</UiButton>
        <UiButton v-else variant="secondary" :disabled="busy" @click="close">Закрыть</UiButton>
        <UiButton v-if="selected" :disabled="busy || (needsSchedule && (loadingPosts || !!postError))" :class="{ 'workflow-dialog__cancel': selected.code === 'cancel' }" @click="submit">
          {{ busy ? 'Сохраняем…' : selected.confirm }}
        </UiButton>
      </template>
    </UiModal>
  </div>
</template>

<style scoped>
.record-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.record-actions__chevron {
  display: block;
  flex-shrink: 0;
}

.record-actions__primary {
  background: var(--crm-accent, #c45112);
  border-color: var(--crm-accent, #c45112);
  color: #fff;
}
.workflow-dialog {
  outline: none;
  min-width: 0;
}
.workflow-dialog__actions {
  display: grid;
  gap: 8px;
}
.workflow-dialog__action {
  display: grid;
  gap: 5px;
  width: 100%;
  padding: 12px;
  text-align: left;
  font: inherit;
  border: 1px solid var(--crm-line, #dfe4eb);
  border-radius: 8px;
  color: var(--crm-text, #203246);
  background: var(--crm-surface, #fff);
  cursor: pointer;
}
.workflow-dialog__action:hover, .workflow-dialog__action:focus-visible {
  background: var(--crm-surface-soft, #f2f5f8);
  border-color: var(--crm-accent, #c45112);
}
.workflow-dialog__action span {
  color: var(--crm-text-muted, #66758c);
  font-size: 12px;
  line-height: 1.5;
}
.workflow-dialog__action--danger strong {
  color: var(--crm-danger-text, #a12b24);
}
.workflow-dialog__description {
  margin: 0 0 16px;
  line-height: 1.55;
  overflow-wrap: anywhere;
}
.workflow-dialog__balance {
  padding: 12px;
  border: 1px solid var(--crm-line, #dfe4eb);
  border-radius: 8px;
  background: var(--crm-surface-soft, #f2f5f8);
  line-height: 1.5;
}
.workflow-dialog__schedule {
  display: grid;
  gap: 12px;
  margin-bottom: 16px;
}
.workflow-dialog__error {
  padding: 10px 12px;
  margin-top: 12px;
  background: var(--crm-danger-surface, #fff3f2);
  border: 1px solid var(--crm-danger-border, #e4b2ae);
  border-radius: 6px;
  color: var(--crm-danger-text, #a12b24);
}
.workflow-dialog__cancel {
  background: var(--crm-danger-surface, #fff3f2);
  border-color: var(--crm-danger-border, #e4b2ae);
  color: var(--crm-danger-text, #a12b24);
}
</style>

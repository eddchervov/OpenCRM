<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiLoader } from '@opencrm/ui'
import { loadMaintenanceVisitReminder, saveMaintenanceVisitReminder, type MaintenanceVisitReminder as MaintenanceVisitReminderModel } from '../api/visitReminders'

const props = defineProps<{
  organizationId: string
  maintenanceId: string
  refreshKey?: number
  previewOnly?: boolean
}>()
const emit = defineEmits<{ changed: [] }>()

const reminder = ref<MaintenanceVisitReminderModel | null>(null)
const isLoading = ref(false)
const isSaving = ref(false)
const error = ref('')
const channelName = (code: string) => ({
  auto: 'Telegram, иначе MAX',
  telegram: 'Telegram',
  max: 'MAX',
  both: 'Telegram и MAX'
}[code] ?? code)
const statusLabel = computed(() => ({
  scheduled: 'Запланировано', queued: 'В очереди', processing: 'Отправляется', retrying: 'Повторная попытка',
  sent: 'Принято провайдером', simulated: 'Тестовая отправка', disabled: 'Выключено', failed: 'Нужна проверка',
  skipped: 'Отправка отменена', no_channel: 'Нет подключения', past: 'Визит уже наступил',
  not_planned: 'Не требуется', time_zone_required: 'Проверьте время'
}[reminder.value?.statusCode ?? ''] ?? 'Напоминание'))
const statusDetail = computed(() => reminder.value?.statusCode === 'scheduled'
  ? reminder.value.statusText.replace(/^Запланировано:\s*/, '') : reminder.value?.statusText)

let requestId = 0
let requestController: AbortController | undefined

async function load() {
  const currentRequest = ++requestId
  requestController?.abort()
  if (props.previewOnly) {
    reminder.value = null
    error.value = ''
    isLoading.value = false
    return
  }
  requestController = new AbortController()
  isLoading.value = true
  error.value = ''
  try {
    const result = await loadMaintenanceVisitReminder(props.organizationId, props.maintenanceId, requestController.signal)
    if (currentRequest === requestId) reminder.value = result
  } catch (caught) {
    if (currentRequest === requestId && !(caught instanceof DOMException && caught.name === 'AbortError')) {
      error.value = caught instanceof Error ? caught.message : 'Не удалось загрузить напоминание.'
    }
  } finally {
    if (currentRequest === requestId) isLoading.value = false
  }
}

async function setEnabled(enabled: boolean) {
  const current = reminder.value
  if (props.previewOnly || !current?.canEdit || isSaving.value) return
  const currentOrganizationId = props.organizationId
  const currentMaintenanceId = props.maintenanceId
  const currentRequest = ++requestId
  requestController?.abort()
  requestController = new AbortController()
  isSaving.value = true
  error.value = ''
  try {
    const response = await saveMaintenanceVisitReminder(currentOrganizationId, currentMaintenanceId, {
      enabled,
      version: current.version
    }, requestController.signal)
    if (currentRequest === requestId
      && currentOrganizationId === props.organizationId
      && currentMaintenanceId === props.maintenanceId) {
      reminder.value = response
      emit('changed')
    }
  } catch (caught) {
    if (currentRequest === requestId) {
      if (caught instanceof DOMException && caught.name === 'AbortError') return
      const message = caught instanceof Error ? caught.message : 'Не удалось изменить напоминание.'
      await load()
      if (currentOrganizationId === props.organizationId && currentMaintenanceId === props.maintenanceId) {
        error.value = message
      }
    }
  } finally {
    if (currentRequest === requestId) isSaving.value = false
    else if (currentOrganizationId === props.organizationId && currentMaintenanceId === props.maintenanceId) isSaving.value = false
  }
}

watch(() => [props.organizationId, props.maintenanceId, props.refreshKey, props.previewOnly], () => {
  isSaving.value = false
  return load()
}, { immediate: true })
onBeforeUnmount(() => {
  requestId += 1
  requestController?.abort()
})
</script>

<template>
  <section class="record-visit-reminder" aria-labelledby="maintenance-visit-reminder-title">
    <header class="record-visit-reminder__heading">
      <div class="record-visit-reminder__identity">
        <span class="record-visit-reminder__icon" aria-hidden="true"><svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="8.5" /><path d="M12 7v5l3 2" /></svg></span>
        <div>
          <h4 id="maintenance-visit-reminder-title">Напоминание о визите</h4>
          <p v-if="previewOnly" class="record-visit-reminder__detail">Автоматически напомните клиенту о записи.</p>
          <p class="record-visit-reminder__detail" v-if="reminder">{{ statusDetail }}</p>
        </div>
      </div>
      <div class="record-visit-reminder__controls">
        <UiButton v-if="previewOnly" variant="secondary" disabled>Настроить</UiButton>
        <span v-if="reminder" class="record-visit-reminder__status" :class="{ 'record-visit-reminder__status--error': reminder.statusCode === 'failed', 'record-visit-reminder__status--muted': reminder.statusCode === 'disabled' }">{{ statusLabel }}</span>
        <UiButton v-if="reminder?.canEdit" variant="secondary" :disabled="isLoading || isSaving" @click="setEnabled(!reminder.enabled)">
          {{ isSaving ? 'Сохраняем…' : reminder.enabled ? 'Отключить' : 'Включить' }}
        </UiButton>
      </div>
    </header>
    <div v-if="previewOnly" class="record-visit-reminder__message">
      <div class="record-visit-reminder__message-label"><span>Предпросмотр сообщения</span><span>Telegram / MAX</span></div>
      <p class="record-visit-reminder__preview">Напоминаем о вашей записи на завтра. Будем ждать вас в назначенное время!</p>
    </div>
    <UiLoader v-else-if="isLoading && !reminder" text="Загружаем напоминание…" />
    <p v-else-if="error" class="ui-field__error" role="alert">{{ error }} <UiButton variant="secondary" @click="load">Повторить</UiButton></p>
    <template v-else-if="reminder">
      <div v-if="reminder.preview" class="record-visit-reminder__message">
        <div class="record-visit-reminder__message-label"><span>Предпросмотр сообщения</span><span>{{ channelName(reminder.channelCode) }}</span></div>
        <p class="record-visit-reminder__preview">{{ reminder.preview }}</p>
      </div>
      <p v-else class="record-visit-reminder__detail">Для просмотра текста нужны права на просмотр клиентов и автомобилей.</p>
    </template>
  </section>
</template>

<style scoped>
.record-visit-reminder { display:grid; gap:16px; min-width:0; }
.record-visit-reminder__heading { display:flex; align-items:flex-start; justify-content:space-between; gap:14px; flex-wrap:wrap; }
.record-visit-reminder__identity { display:flex; align-items:flex-start; gap:12px; min-width:0; flex:1 1 280px; }
.record-visit-reminder__identity > div { min-width:0; }
.record-visit-reminder__icon { display:grid; place-items:center; width:36px; height:36px; flex:none; border-radius:10px; color:var(--crm-accent, #b8551e); background:var(--crm-surface-soft, #f6f7f9); }
.record-visit-reminder__icon svg { width:22px; height:22px; fill:none; stroke:currentColor; stroke-width:1.6; stroke-linecap:round; stroke-linejoin:round; }
.record-visit-reminder h4 { margin:0; color:var(--crm-text, #263142); font-size:14px; font-weight:600; line-height:1.5; }
.record-visit-reminder__detail { margin:4px 0 0; font-size:12px; font-weight:400; color:var(--crm-text-muted, #667085); line-height:1.6; overflow-wrap:anywhere; }
.record-visit-reminder__controls { display:flex; align-items:center; gap:10px; flex-wrap:wrap; }
.record-visit-reminder__status { padding:4px 9px; border-radius:6px; font-size:12px; color:var(--crm-text, #263142); background:var(--crm-surface-soft, #f6f7f9); }
.record-visit-reminder__status--muted { color:var(--crm-text-muted, #667085); }
.record-visit-reminder__status--error { color:var(--crm-danger-text, #b42318); }
.record-visit-reminder__message { display:grid; gap:10px; padding:16px 18px; border-left:3px solid var(--crm-accent, #b8551e); border-radius:0 9px 9px 0; background:var(--crm-surface-soft, #f6f7f9); }
.record-visit-reminder__message-label { display:flex; align-items:center; justify-content:space-between; flex-wrap:wrap; gap:8px; color:var(--crm-text-muted, #667085); font-size:12px; }
.record-visit-reminder__preview { margin:0; color:var(--crm-text, #263142); font-size:13px; font-weight:400; line-height:1.7; white-space:pre-wrap; overflow-wrap:anywhere; }
@media (max-width:520px) { .record-visit-reminder__identity { flex-basis:100%; } .record-visit-reminder__controls { width:100%; justify-content:space-between; } .record-visit-reminder__message { padding:12px 14px; } }
</style>

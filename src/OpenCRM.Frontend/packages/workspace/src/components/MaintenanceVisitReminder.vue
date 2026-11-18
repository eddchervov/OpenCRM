<script setup lang="ts">
import { useLocalization, formattingCulture } from '@opencrm/ui/i18n'
import '../notificationMessages'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiLoader } from '@opencrm/ui'
import { loadMaintenanceVisitReminder, saveMaintenanceVisitReminder, type MaintenanceVisitReminder as MaintenanceVisitReminderModel } from '../api/visitReminders'

const { t, te } = useLocalization()

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
  auto: t('notifications.autoChannel'),
  telegram: 'Telegram',
  max: 'MAX',
  both: t('notifications.bothChannels')
}[code] ?? code)
const statusLabel = computed(() => ({
  scheduled: t('notifications.scheduled'), queued: t('notifications.queued'), processing: t('notifications.processing'), retrying: t('notifications.retrying'),
  sent: t('notifications.acceptedProvider'), simulated: t('notifications.simulatedSend'), disabled: t('notifications.disabled'), failed: t('notifications.needsReview'),
  skipped: t('notifications.sendCancelled'), no_channel: t('notifications.noChannel'), past: t('notifications.pastVisit'),
  not_planned: t('notifications.notRequired'), time_zone_required: t('notifications.checkTime')
}[reminder.value?.statusCode ?? ''] ?? t('notifications.reminder')))
const statusDetail = computed(() => {
  const current = reminder.value
  if (!current) return ''
  if (current.statusCode === 'scheduled' && current.scheduledAtUtc && current.timeZoneId) {
    const date = new Intl.DateTimeFormat(formattingCulture.value, { dateStyle: 'short', timeStyle: 'short', timeZone: current.timeZoneId }).format(new Date(current.scheduledAtUtc))

    return t('notifications.detail_scheduled', { date })
  }
  const code = current.statusDetailCode
  const key = `notifications.detail_${['queued', 'processing', 'retrying'].includes(code ?? '') ? 'queued' : code}`

  return code && te(key) ? t(key) : current.statusText
})

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
      error.value = caught instanceof Error ? caught.message : t('notifications.reminderLoadError')
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
      const message = caught instanceof Error ? caught.message : t('notifications.reminderSaveError')
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
          <h4 id="maintenance-visit-reminder-title">{{ t('notifications.visitReminder') }}</h4>
          <p v-if="previewOnly" class="record-visit-reminder__detail">{{ t('notifications.automaticReminderHint') }}</p>
          <p class="record-visit-reminder__detail" v-if="reminder">{{ statusDetail }}</p>
        </div>
      </div>
      <div class="record-visit-reminder__controls">
        <UiButton v-if="previewOnly" variant="secondary" disabled>{{ t('notifications.configure') }}</UiButton>
        <span v-if="reminder" class="record-visit-reminder__status" :class="{ 'record-visit-reminder__status--error': reminder.statusCode === 'failed', 'record-visit-reminder__status--muted': reminder.statusCode === 'disabled' }">{{ statusLabel }}</span>
        <UiButton v-if="reminder?.canEdit" variant="secondary" :disabled="isLoading || isSaving" @click="setEnabled(!reminder.enabled)">
          {{ isSaving ? t('notifications.saving') : reminder.enabled ? t('notifications.disable') : t('notifications.enable') }}
        </UiButton>
      </div>
    </header>
    <div v-if="previewOnly" class="record-visit-reminder__message">
      <div class="record-visit-reminder__message-label"><span>{{ t('notifications.preview') }}</span><span>Telegram / MAX</span></div>
      <p class="record-visit-reminder__preview">{{ t('notifications.demoReminder') }}</p>
    </div>
    <UiLoader v-else-if="isLoading && !reminder" :text="t('notifications.loadingReminder')" />
    <p v-else-if="error" class="ui-field__error" role="alert">{{ error }} <UiButton variant="secondary" @click="load">{{ t('notifications.retry') }}</UiButton></p>
    <template v-else-if="reminder">
      <div v-if="reminder.preview" class="record-visit-reminder__message">
        <div class="record-visit-reminder__message-label"><span>{{ t('notifications.preview') }}</span><span>{{ channelName(reminder.channelCode) }}</span></div>
        <p class="record-visit-reminder__preview">{{ reminder.preview }}</p>
      </div>
      <p v-else class="record-visit-reminder__detail">{{ t('notifications.previewPermissions') }}</p>
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

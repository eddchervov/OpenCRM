<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiLoader, UiSelect, UiTextarea, useLocalization } from '@opencrm/ui'
import { formatLocaleDate } from '@opencrm/ui/i18n'
import '../notificationMessages'
import { loadVisitReminderSettings, saveVisitReminderSettings, type VisitReminderChannelCode, type VisitReminderSettings } from '../api/visitReminders'

const props = defineProps<{
  organizationId: string
  canWrite: boolean
}>()

const emit = defineEmits<{
  'busy-changed': [isBusy: boolean]
}>()
const { t } = useLocalization()

const settings = ref<VisitReminderSettings | null>(null)
const hoursBefore = ref('off')
const channelCode = ref<VisitReminderChannelCode>('auto')
const template = ref('')
const isLoading = ref(true)
const isSaving = ref(false)
const error = ref('')
const success = ref(false)
let requestId = 0
let requestController: AbortController | undefined
const canSave = computed(() => !isSaving.value
  && Boolean(template.value.trim())
  && (hoursBefore.value === 'off' || Boolean(settings.value?.timeZoneId)))
const previewValues = computed<Record<string, string>>(() => ({
  organization: t('visitReminder.sampleOrganization'),
  client: t('visitReminder.sampleClient'),
  date: formatLocaleDate('2026-10-25T00:00:00Z', { timeZone: 'UTC', dateStyle: 'long' }),
  time: '10:30',
  vehicle: t('visitReminder.sampleVehicle')
}))
const preview = computed(() => template.value.replace(/\{(organization|client|date|time|vehicle)\}/g, (placeholder, name: string) =>
  previewValues.value[name] ?? placeholder))
const hasChanges = computed(() => Boolean(settings.value) && (hoursBefore.value !== (settings.value!.hoursBefore?.toString() ?? 'off')
  || channelCode.value !== settings.value!.channelCode || template.value !== settings.value!.template))
const hoursOptions = computed(() => [
  { value: 'off', label: t('visitReminder.off') },
  { value: '2', label: t('visitReminder.twoHours') },
  { value: '24', label: t('visitReminder.oneDay') },
  { value: '48', label: t('visitReminder.twoDays') }
])
const channelOptions = computed(() => [
  { value: 'auto', label: t('visitReminder.autoChannel') },
  { value: 'telegram', label: t('visitReminder.telegram') },
  { value: 'max', label: t('visitReminder.max') },
  { value: 'both', label: t('visitReminder.bothChannels') }
])
const templateVariables = computed(() => [
  { name: 'organization', label: t('visitReminder.organization') },
  { name: 'client', label: t('visitReminder.client') },
  { name: 'date', label: t('visitReminder.date') },
  { name: 'time', label: t('visitReminder.time') },
  { name: 'vehicle', label: t('visitReminder.vehicle') }
])

function applySettings(value: VisitReminderSettings) {
  settings.value = value
  hoursBefore.value = value.hoursBefore?.toString() ?? 'off'
  channelCode.value = value.channelCode
  template.value = value.template
}

function insertVariable(name: string) {
  const separator = template.value && !/\s$/.test(template.value) ? ' ' : ''
  if (template.value.length + separator.length + name.length + 2 > 1000) return

  template.value = `${template.value}${separator}{${name}}`
}

function canInsertVariable(name: string) {
  const separator = template.value && !/\s$/.test(template.value) ? ' ' : ''

  return template.value.length + separator.length + name.length + 2 <= 1000
}

function variableToken(name: string) {
  return `{${name}}`
}

async function load() {
  const currentRequest = ++requestId
  requestController?.abort()
  requestController = new AbortController()
  isLoading.value = true
  error.value = ''
  settings.value = null
  try {
    const response = await loadVisitReminderSettings(props.organizationId, requestController.signal)
    if (currentRequest === requestId) applySettings(response)
  } catch (caught) {
    if (currentRequest === requestId && !(caught instanceof DOMException && caught.name === 'AbortError')) {
      error.value = caught instanceof Error ? caught.message : t('visitReminder.loadError')
    }
  } finally {
    if (currentRequest === requestId) isLoading.value = false
  }
}

async function save() {
  if (!props.canWrite || isSaving.value) return
  isSaving.value = true
  const currentRequest = ++requestId
  requestController?.abort()
  requestController = new AbortController()
  const organizationId = props.organizationId
  emit('busy-changed', true)
  error.value = ''
  success.value = false
  try {
    const response = await saveVisitReminderSettings(organizationId, {
      hoursBefore: hoursBefore.value === 'off' ? null : Number(hoursBefore.value),
      channelCode: channelCode.value,
      template: template.value
    }, requestController.signal)
    if (currentRequest !== requestId || organizationId !== props.organizationId) return
    applySettings(response)
    success.value = true
  } catch (caught) {
    if (currentRequest === requestId && organizationId === props.organizationId) {
      error.value = caught instanceof Error ? caught.message : t('visitReminder.saveError')
    }
  } finally {
    if (currentRequest === requestId) {
      isSaving.value = false
      emit('busy-changed', false)
    }
  }
}

watch(() => props.organizationId, (organizationId, previousOrganizationId) => {
  if (previousOrganizationId !== undefined) {
    isSaving.value = false
    emit('busy-changed', false)
  }
  return load()
}, { immediate: true })
onBeforeUnmount(() => {
  requestId += 1
  requestController?.abort()
  emit('busy-changed', false)
})
</script>

<template>
  <section class="organization-panel organization-visit-reminders" aria-labelledby="visit-reminders-title">
    <header class="ui-section-header">
      <h3 id="visit-reminders-title">{{ t('visitReminder.title') }}</h3>
    </header>
    <UiLoader v-if="isLoading" :text="t('visitReminder.loading')" />
    <div v-else-if="error && !settings" class="visit-reminders__load-error" role="alert">
      <p>{{ error }}</p>
      <UiButton variant="secondary" type="button" @click="load">{{ t('visitReminder.retry') }}</UiButton>
    </div>
    <form v-else-if="settings" class="organization-form" @submit.prevent="save">
      <p v-if="!settings.timeZoneId" class="visit-reminders__timezone" role="status">
        {{ t('visitReminder.timezoneRequired') }}
      </p>
      <div class="visit-reminders__schedule">
        <UiSelect v-model="hoursBefore" :label="t('visitReminder.when')" :options="hoursOptions" :disabled="!canWrite || isSaving" :searchable="false" />
        <UiSelect v-model="channelCode" :label="t('visitReminder.channel')" :options="channelOptions" :disabled="!canWrite || isSaving" :searchable="false" />
      </div>
      <section class="visit-reminders__template" aria-labelledby="visit-reminders-template-title">
        <div class="visit-reminders__template-heading">
          <h4 id="visit-reminders-template-title">{{ t('visitReminder.messageText') }}</h4>
          <span>{{ template.length }}/1000</span>
        </div>
        <div class="visit-reminders__variables" :aria-label="t('visitReminder.insertVariable')">
          <button
            v-for="variable in templateVariables"
            :key="variable.name"
            type="button"
            :disabled="!canWrite || isSaving || !canInsertVariable(variable.name)"
            :aria-label="t('visitReminder.insertField', { field: variable.label })"
            @click="insertVariable(variable.name)"
          >{{ variable.label }} <code>{{ variableToken(variable.name) }}</code></button>
        </div>
        <UiTextarea v-model="template" label="" labelled-by="visit-reminders-template-title" :rows="4" :maxlength="1000" :disabled="!canWrite || isSaving" />
        <div class="visit-reminders__preview" aria-live="polite">
          <div class="visit-reminders__preview-heading">
            <h4>{{ t('visitReminder.example') }}</h4>
            <span>{{ t('visitReminder.demoData') }}</span>
          </div>
          <p>{{ preview || t('visitReminder.previewEmpty') }}</p>
        </div>
      </section>
      <footer class="visit-reminders__footer">
        <p v-if="error" class="visit-reminders__error" role="alert">{{ error }}</p>
        <p v-else-if="success && !hasChanges" class="visit-reminders__success" role="status">{{ t('visitReminder.saved') }}</p>
        <span v-else class="visit-reminders__status">{{ t(hasChanges ? 'visitReminder.unsaved' : 'visitReminder.afterSave') }}</span>
        <UiButton v-if="canWrite" type="submit" :disabled="!canSave">{{ t(isSaving ? 'visitReminder.saving' : 'visitReminder.save') }}</UiButton>
        <span v-else class="visit-reminders__readonly">{{ t('visitReminder.readOnly') }}</span>
      </footer>
    </form>
  </section>
</template>

<style scoped>
.organization-visit-reminders { min-width: 0; padding: 18px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 10px; background: var(--crm-surface, #fff); }
.organization-visit-reminders > .ui-section-header { margin: 0 0 16px; padding-bottom: 12px; border-bottom: 1px solid var(--crm-section-header-border, var(--crm-line, #dce2e9)); }
.organization-visit-reminders h3 { margin: 0; color: var(--crm-text, #202631); font-size: 15px; font-weight: 600; }
.organization-form { display: grid; gap: 16px; min-width: 0; }
.visit-reminders__schedule { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 14px; }
.visit-reminders__timezone { margin: 0; padding: 10px 12px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; background: var(--crm-surface-soft, #f5f6f8); color: var(--crm-text-muted, #687386); font-size: 12px; line-height: 1.45; }
.visit-reminders__template { display: grid; gap: 10px; min-width: 0; }
.visit-reminders__template-heading, .visit-reminders__preview-heading { display: flex; align-items: baseline; justify-content: space-between; gap: 10px; min-width: 0; }
.visit-reminders__template-heading h4, .visit-reminders__preview-heading h4 { margin: 0; color: var(--crm-text, #202631); font-size: 13px; font-weight: 600; }
.visit-reminders__template-heading span, .visit-reminders__preview-heading span { flex: 0 0 auto; color: var(--crm-text-muted, #687386); font-size: 11px; }
.visit-reminders__variables { display: flex; flex-wrap: wrap; gap: 6px; min-width: 0; }
.visit-reminders__variables button { display: inline-flex; flex-wrap: wrap; align-items: center; gap: 5px; max-width: 100%; padding: 6px 8px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 7px; background: var(--crm-surface, #fff); color: var(--crm-text, #202631); font: inherit; font-size: 11px; cursor: pointer; }
.visit-reminders__variables button:hover:not(:disabled) { border-color: var(--crm-accent, #e87532); background: var(--crm-surface-soft, #fff4eb); }
.visit-reminders__variables button:disabled { cursor: default; opacity: .6; }
.visit-reminders__variables code { color: var(--crm-text-muted, #687386); font-family: inherit; font-size: 10px; overflow-wrap: anywhere; }
.visit-reminders__template :deep(textarea) { min-width: 0; resize: vertical; }
.visit-reminders__preview { display: grid; gap: 8px; min-width: 0; padding: 12px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; background: var(--crm-surface-soft, #f5f6f8); }
.visit-reminders__preview p { margin: 0; color: var(--crm-text, #202631); font-size: 12px; line-height: 1.5; white-space: pre-wrap; overflow-wrap: anywhere; }
.visit-reminders__footer { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 10px; padding-top: 12px; border-top: 1px solid var(--crm-line, #dce2e9); }
.visit-reminders__footer p { flex: 1 1 220px; margin: 0; font-size: 12px; line-height: 1.4; }
.visit-reminders__status, .visit-reminders__readonly { color: var(--crm-text-muted, #687386); font-size: 12px; }
.visit-reminders__error { color: var(--crm-danger-text, #a12622); }
.visit-reminders__success { color: var(--crm-text, #263142); }
.visit-reminders__load-error { display: flex; align-items: center; flex-wrap: wrap; gap: 10px; }
.visit-reminders__load-error p { flex: 1 1 220px; margin: 0; color: var(--crm-danger-text, #a12622); font-size: 13px; }
@media (max-width: 560px) {
  .organization-visit-reminders { padding: 14px; }
  .visit-reminders__schedule { grid-template-columns: minmax(0, 1fr); gap: 12px; }
  .visit-reminders__footer { align-items: stretch; }
  .visit-reminders__footer :deep(.ui-button) { width: 100%; }
}
</style>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton, UiLoader } from '@opencrm/ui'
import { useLocalization } from '@opencrm/ui'
import { formattingCulture } from '@opencrm/ui/i18n'
import '../maintenanceMediaMessages'
import { loadMaintenanceHistory, type MaintenanceHistoryEvent } from '../api/maintenance'

const props = defineProps<{ organizationId: string; maintenanceId: string; title: string }>()
const { t } = useLocalization()
const emit = defineEmits<{ close: [] }>()
const dialog = ref<HTMLDialogElement | null>(null)
const events = ref<MaintenanceHistoryEvent[]>([])
const loading = ref(false)
const error = ref('')
let request: AbortController | undefined
let opener: HTMLElement | null = null

async function load() {
  request?.abort()
  const active = new AbortController()
  request = active
  events.value = []
  loading.value = true
  error.value = ''
  try {
    const result = await loadMaintenanceHistory(props.organizationId, props.maintenanceId, active.signal)
    if (!active.signal.aborted) events.value = result
  } catch (caught) {
    if (!active.signal.aborted) error.value = caught instanceof Error ? caught.message : t('maintenanceHistory.loadError')
  } finally {
    if (request === active) loading.value = false
  }
}

function formatTime(event: MaintenanceHistoryEvent) {
  return new Intl.DateTimeFormat(formattingCulture.value, {
    timeZone: event.timeZoneId || undefined,
    year: 'numeric', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit'
  }).format(new Date(event.createdAt))
}

function descriptionLines(event: MaintenanceHistoryEvent) {
  // Исторические сводки создания разделяют поля точкой с запятой.
  const separator = event.eventType === 'created' ? /;\s*|\r?\n/ : /\r?\n/
  return event.description?.split(separator).filter(line => line.trim()) ?? []
}

function closeFromBackdrop(event: MouseEvent) {
  const element = dialog.value
  if (!element || event.target !== element) return
  const bounds = element.getBoundingClientRect()
  if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) emit('close')
}

watch(() => [props.organizationId, props.maintenanceId], () => { void load() })
onMounted(() => {
  opener = document.activeElement instanceof HTMLElement ? document.activeElement : null
  dialog.value?.showModal()
  void load()
})
onBeforeUnmount(() => {
  request?.abort()
  dialog.value?.close()
  if (opener?.isConnected) opener.focus()
})
</script>

<template>
  <Teleport to="body">
    <dialog ref="dialog" class="maintenance-history" aria-labelledby="maintenance-history-title" @cancel.prevent="emit('close')" @click="closeFromBackdrop">
      <header class="maintenance-history__header ui-form-header">
        <div><h2 id="maintenance-history-title">{{ t('maintenanceHistory.title') }}</h2><p>{{ title }}</p></div>
        <button type="button" class="maintenance-history__close" :aria-label="t('maintenanceHistory.close')" autofocus @click="emit('close')">×</button>
      </header>
      <div class="maintenance-history__body" :aria-busy="loading">
        <UiLoader v-if="loading" :text="t('maintenanceHistory.loading')" />
        <div v-else-if="error" class="maintenance-history__error" role="alert"><p>{{ error }}</p><UiButton variant="secondary" @click="load">{{ t('maintenanceHistory.retry') }}</UiButton></div>
        <template v-else>
          <p class="maintenance-history__count">{{ t('maintenanceHistory.eventCount', { count: events.length }) }}</p>
          <ol v-if="events.length" class="maintenance-history__events" :aria-label="t('maintenanceHistory.events')">
            <li v-for="event in events" :key="event.id" class="maintenance-history__event">
              <header><strong>{{ event.title }}</strong><time :datetime="event.createdAt">{{ formatTime(event) }}</time></header>
              <div v-if="event.description" class="maintenance-history__details"><p v-for="(line, index) in descriptionLines(event)" :key="index">{{ line }}</p></div>
              <a v-if="event.sourceInquiryId" :href="'#inquiries/' + event.sourceInquiryId" @click="emit('close')">{{ t('maintenanceHistory.openInquiry') }}</a>
              <small v-if="event.actorDisplayName">{{ event.actorDisplayName }}</small>
            </li>
          </ol>
          <p v-else class="maintenance-history__empty">{{ t('maintenanceHistory.empty') }}</p>
        </template>
      </div>
      <footer class="maintenance-history__footer"><UiButton variant="secondary" @click="emit('close')">{{ t('maintenanceHistory.closeDialog') }}</UiButton></footer>
    </dialog>
  </Teleport>
</template>

<style scoped>
.maintenance-history { box-sizing: border-box; width: min(760px, calc(100vw - 32px)); max-height: min(88dvh, 900px); margin: auto; padding: 0; border: 1px solid var(--crm-line, #dfe3ea); border-radius: 14px; background: var(--crm-surface, #fff); color: var(--crm-text, #263142); box-shadow: 0 24px 80px #0003; }
.maintenance-history[open] { display: flex; flex-direction: column; }
.maintenance-history::backdrop { background: var(--crm-backdrop, #10182880); }
.maintenance-history__header { display: flex; align-items: start; justify-content: space-between; gap: 16px; padding: 20px 24px; border-bottom: 1px solid var(--crm-line, #dfe3ea); }
.maintenance-history__header h2 { margin: 0; font-size: 18px; }
.maintenance-history__header p { margin: 6px 0 0; color: var(--crm-text-muted, #667085); font-size: 13px; overflow-wrap: anywhere; }
.maintenance-history__close { flex: 0 0 32px; width: 32px; height: 32px; border: 1px solid var(--crm-line, #dfe3ea); border-radius: 8px; background: var(--crm-surface, #fff); color: var(--crm-text-muted, #667085); font-size: 23px; cursor: pointer; }
.maintenance-history__body { min-height: 0; overflow: auto; padding: 20px 24px; }
.maintenance-history__count, .maintenance-history__empty { margin: 0 0 20px; color: var(--crm-text-muted, #667085); font-size: 12px; }
.maintenance-history__events { margin: 0; padding: 0; list-style: none; }
.maintenance-history__event { position: relative; display: grid; gap: 8px; padding: 0 0 24px 24px; margin-left: 5px; border-left: 1px solid var(--crm-line, #dfe3ea); font-size: 13px; }
.maintenance-history__event:last-child { padding-bottom: 0; border-left-color: transparent; }
.maintenance-history__event::before { position: absolute; content: ''; left: -5px; top: 4px; width: 9px; height: 9px; border-radius: 50%; background: var(--crm-accent, #df6c22); box-shadow: 0 0 0 4px var(--crm-surface, #fff); }
.maintenance-history__event header { display: flex; justify-content: space-between; align-items: baseline; gap: 8px 16px; flex-wrap: wrap; }
.maintenance-history__event p { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; line-height: 1.55; }
.maintenance-history__details { display: grid; gap: 6px; padding: 12px 14px; border-radius: 6px; background: var(--crm-surface-soft, #f5f6f8); }
.maintenance-history__event time, .maintenance-history__event small { color: var(--crm-text-muted, #667085); font-size: 12px; overflow-wrap: anywhere; }
.maintenance-history__event a { color: var(--crm-accent, #df6c22); justify-self: start; }
.maintenance-history__error { color: var(--crm-danger-text, #bd4141); font-size: 13px; }
.maintenance-history__footer { display: flex; justify-content: end; padding: 14px 24px; border-top: 1px solid var(--crm-line, #dfe3ea); }
@media (max-width: 550px) { .maintenance-history { width: calc(100vw - 20px); max-height: 92dvh; } .maintenance-history__header, .maintenance-history__body { padding: 16px; } .maintenance-history__footer { padding: 12px 16px; } }
</style>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton, UiLoader, UiPagination } from '@opencrm/ui'
import type { WorkspaceContext } from '../types'
import { loadInquiryHistory, type Inquiry, type InquiryHistory } from '../api/inquiries'
import InquiryTimeline from './InquiryTimeline.vue'
import InquiryNoteComposer from './InquiryNoteComposer.vue'

const props = defineProps<{ context: WorkspaceContext; inquiry: Inquiry }>()
const emit = defineEmits<{ close: []; updated: [inquiry: Inquiry] }>()
const dialog = ref<HTMLDialogElement | null>(null)
const history = ref<InquiryHistory | null>(null)
const page = ref(1)
const loading = ref(false)
const saving = ref(false)
const error = ref('')
let request: AbortController | undefined
let opener: HTMLElement | null = null

async function loadHistory() {
  request?.abort()
  const active = new AbortController()
  request = active
  loading.value = true
  error.value = ''
  try {
    const result = await loadInquiryHistory(props.context.organization.id, props.inquiry.id, page.value, active.signal)
    if (!active.signal.aborted) history.value = result
  } catch (caught) {
    if (!active.signal.aborted) error.value = caught instanceof Error ? caught.message : 'Не удалось загрузить историю.'
  } finally {
    if (request === active) loading.value = false
  }
}

function close() { if (!saving.value) emit('close') }

function closeFromBackdrop(event: MouseEvent) {
  const element = dialog.value
  if (!element || event.target !== element) return
  const bounds = element.getBoundingClientRect()
  if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) close()
}

watch(() => props.inquiry.version, () => { page.value = 1; void loadHistory() })

onMounted(() => {
  opener = document.activeElement instanceof HTMLElement ? document.activeElement : null
  dialog.value?.showModal()
  void loadHistory()
})
onBeforeUnmount(() => {
  request?.abort()
  dialog.value?.close()
  if (opener?.isConnected) opener.focus()
})
</script>

<template>
  <Teleport to="body">
    <dialog ref="dialog" class="inquiry-history" aria-labelledby="inquiry-history-title" @cancel.prevent="close" @click="closeFromBackdrop">
      <header class="inquiry-history__header ui-form-header">
        <div><h2 id="inquiry-history-title">История обращения</h2><p>{{ inquiry.contactName }}</p></div>
        <button type="button" class="inquiry-history__close" aria-label="Закрыть историю" :disabled="saving" autofocus @click="close">×</button>
      </header>
      <div class="inquiry-history__body">
        <div class="inquiry-history__toolbar"><span>{{ history ? 'Событий: ' + history.totalCount : 'События и заметки' }}</span></div>
        <InquiryNoteComposer :context="context" :inquiry="inquiry" @busy="saving = $event" @updated="emit('updated', $event)" />
        <div v-if="error" class="inquiry-history__error" role="alert"><p>{{ error }}</p><UiButton variant="secondary" @click="loadHistory">Повторить</UiButton></div>
        <UiLoader v-if="loading" />
        <InquiryTimeline v-else-if="history" :events="history.items" :time-zone-id="inquiry.timeZoneId" :members="context.organization.members" />
      </div>
      <footer class="inquiry-history__footer"><UiPagination v-model="page" :total-items="history?.totalCount || 0" :items-per-page="10" :disabled="loading || saving" label="История обращения" @update:model-value="loadHistory" /><UiButton variant="secondary" :disabled="saving" @click="close">Закрыть</UiButton></footer>
    </dialog>
  </Teleport>
</template>

<style scoped>
.inquiry-history { width: min(740px, calc(100vw - 32px)); max-height: min(88dvh, 900px); margin: auto; padding: 0; border: 1px solid var(--crm-line, #dfe3ea); border-radius: 14px; background: var(--crm-surface, #fff); color: var(--crm-text, #263142); box-shadow: 0 24px 80px #0003; }
.inquiry-history[open] { display: flex; flex-direction: column; }
.inquiry-history::backdrop { background: #10182880; }
.inquiry-history__header { display: flex; align-items: start; justify-content: space-between; gap: 16px; padding: 20px 24px; border-bottom: 1px solid var(--crm-line, #dfe3ea); }
.inquiry-history__header h2 { margin: 0; font-size: 18px; }
.inquiry-history__header p { margin: 6px 0 0; color: var(--crm-text-muted, #667085); font-size: 13px; overflow-wrap: anywhere; }
.inquiry-history__close { flex: 0 0 32px; width: 32px; height: 32px; border: 1px solid var(--crm-line, #dfe3ea); border-radius: 8px; background: var(--crm-surface, #fff); color: var(--crm-text-muted, #667085); font-size: 23px; cursor: pointer; }
.inquiry-history__body { min-height: 0; overflow: auto; padding: 20px 24px; }
.inquiry-history__toolbar { display: flex; justify-content: space-between; align-items: center; gap: 12px; margin-bottom: 12px; color: var(--crm-text-muted, #667085); font-size: 12px; }
.inquiry-history__body > :deep(.inquiry-note) { margin-bottom: 24px; }
.inquiry-history__error { margin-bottom: 16px; color: var(--crm-danger, #c64a4a); font-size: 13px; }
.inquiry-history__footer { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 14px 24px; border-top: 1px solid var(--crm-line, #dfe3ea); }
.inquiry-history__footer > :last-child { margin-left: auto; }
@media (max-width: 550px) { .inquiry-history { width: calc(100vw - 20px); max-height: 92dvh; } .inquiry-history__header, .inquiry-history__body { padding: 16px; } .inquiry-history__footer { padding: 12px 16px; flex-wrap: wrap; } .inquiry-history__toolbar { align-items: start; } }
</style>

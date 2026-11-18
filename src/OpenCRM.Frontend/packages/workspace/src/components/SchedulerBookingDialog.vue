<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, useId, watch } from 'vue'
import { useLocalization } from '@opencrm/ui'
import '../schedulerControlMessages'

const props = withDefaults(defineProps<{
  open: boolean
  busy?: boolean
  dirty?: boolean
  dismissOnBackdrop?: boolean
  title?: string
}>(), {
  busy: false,
  dirty: false,
  dismissOnBackdrop: true
})
const { t } = useLocalization()
const dialogTitle = computed(() => props.title ?? t('schedulerControl.newAppointment'))

const emit = defineEmits<{
  close: []
}>()

const dialog = ref<HTMLDialogElement | null>(null)
const discardPending = ref(false)
const keepEditingButton = ref<HTMLButtonElement | null>(null)
const editor = ref<HTMLElement | null>(null)
const titleId = useId()
let previousEditorFocus: HTMLElement | null = null
let previousFocus: HTMLElement | null = null
let previousBodyOverflow = ''
let hasPageLock = false

function show() {
  const element = dialog.value
  if (!element || element.open) return

  previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
  previousBodyOverflow = document.body.style.overflow
  hasPageLock = true
  document.body.style.overflow = 'hidden'
  element.showModal()
}

function restorePage() {
  if (!hasPageLock) return

  document.body.style.overflow = previousBodyOverflow
  hasPageLock = false
  if (previousFocus?.isConnected) previousFocus.focus()
  previousFocus = null
}

async function close() {
  if (props.busy) return
  if (props.dirty) {
    if (!discardPending.value) {
      previousEditorFocus = document.activeElement instanceof HTMLElement && editor.value?.contains(document.activeElement)
        ? document.activeElement
        : null
    }
    discardPending.value = true
    await nextTick()
    keepEditingButton.value?.focus()
    return
  }
  dialog.value?.close()
}

async function keepEditing() {
  discardPending.value = false
  await nextTick()
  const target = previousEditorFocus?.isConnected
    ? previousEditorFocus
    : editor.value?.querySelector<HTMLElement>('input:not([disabled]), textarea:not([disabled]), select:not([disabled]), button:not([disabled])')
  target?.focus()
}

function discard() {
  discardPending.value = false
  dialog.value?.close()
}

defineExpose({ close })

function onCancel(event: Event) {
  event.preventDefault()
  if (discardPending.value) {
    keepEditing()
    return
  }
  close()
}

function onClose() {
  restorePage()
  emit('close')
}

watch(() => props.open, async isOpen => {
  if (isOpen) {
    discardPending.value = false
    await nextTick()
    if (!props.open) return
    show()
    return
  }

  if (dialog.value?.open) dialog.value.close()
  restorePage()
}, { immediate: true })

onBeforeUnmount(() => {
  if (dialog.value?.open) dialog.value.close()
  restorePage()
})
</script>

<template>
  <Teleport to="body">
    <dialog
      v-if="open"
      ref="dialog"
      class="scheduler-booking-dialog"
      :aria-labelledby="titleId"
      @cancel="onCancel"
      @close="onClose"
      @click.self="dismissOnBackdrop && close()"
    >
      <header class="scheduler-booking-dialog__header ui-form-header">
        <h2 :id="titleId">{{ dialogTitle }}</h2>
        <button type="button" class="scheduler-booking-dialog__close" :aria-label="t('schedulerControl.closeDialog')" :disabled="busy" @click="close">×</button>
      </header>
      <div class="scheduler-booking-dialog__content">
        <div v-if="discardPending" class="scheduler-booking-dialog__confirmation" role="alertdialog" :aria-label="t('schedulerControl.unsavedAppointment')">
          <p>{{ t('schedulerControl.unsavedPrompt') }}</p>
          <div class="scheduler-booking-dialog__confirmation-actions">
            <button ref="keepEditingButton" type="button" class="ui-button ui-button--primary btn btn-dark" @click="keepEditing">{{ t('schedulerControl.continueEditing') }}</button>
            <button type="button" class="ui-button ui-button--secondary btn btn-outline-dark" @click="discard">{{ t('schedulerControl.closeWithoutSaving') }}</button>
          </div>
        </div>
        <div v-show="!discardPending" ref="editor" class="scheduler-booking-dialog__editor"><slot /></div>
      </div>
    </dialog>
  </Teleport>
</template>

<style>
.scheduler-booking-dialog {
  width: min(1040px, calc(100vw - 40px));
  max-width: none;
  max-height: min(92vh, 980px);
  padding: 0;
  overflow: hidden;
  color: var(--crm-text, #202833);
  background: var(--crm-surface, #fff);
  border: 1px solid var(--crm-line, #dce2e9);
  border-radius: 16px;
  box-shadow: 0 24px 80px #11182740;
}

.scheduler-booking-dialog::backdrop {
  background: #10182080;
  backdrop-filter: blur(3px);
}

.scheduler-booking-dialog__header {
  position: sticky;
  top: 0;
  z-index: 2;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
  min-height: 68px;
  padding: 12px 24px;
  background: var(--crm-form-header-bg, #fff7f0);
  border-bottom: 1px solid var(--crm-form-header-border, #e1e6ed);
}

.scheduler-booking-dialog__header h2 {
  margin: 0;
  font-size: 19px;
}

.scheduler-booking-dialog__close {
  display: grid;
  width: 38px;
  height: 38px;
  place-items: center;
  padding: 0;
  color: var(--crm-text-muted, #64748b);
  background: transparent;
  border: 1px solid var(--crm-line, #dce2e9);
  border-radius: 10px;
  font-size: 24px;
  line-height: 1;
  cursor: pointer;
}

.scheduler-booking-dialog__close:hover:not(:disabled) {
  color: var(--crm-text, #202833);
  background: var(--crm-surface-soft, #f7f9fb);
}

.scheduler-booking-dialog__close:disabled {
  opacity: .5;
  cursor: not-allowed;
}

.scheduler-booking-dialog__content {
  max-height: calc(min(92vh, 980px) - 68px);
  overflow: auto;
  overscroll-behavior: contain;
}

.scheduler-booking-dialog__editor > .entity-card-section {
  margin: 0;
}

.scheduler-booking-dialog__confirmation {
  padding: 20px 24px;
}

.scheduler-booking-dialog__confirmation-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}

.scheduler-booking-dialog__content .quick-editor.ui-card {
  border: 0;
  border-radius: 0;
  box-shadow: none;
}

.scheduler-booking-dialog__content .quick-editor > .ui-card__body {
  padding: 22px 24px;
}

.scheduler-booking-dialog__content .quick-editor-layout {
  grid-template-columns: minmax(0, 1fr) minmax(300px, 360px);
  gap: 26px;
}

.scheduler-booking-dialog__content .quick-editor-visit {
  position: sticky;
  top: 12px;
}

.scheduler-booking-dialog__content .quick-editor-footer {
  margin: 18px -24px -22px;
  padding: 14px 24px 18px;
}

@media (max-width: 800px) {
  .scheduler-booking-dialog {
    width: calc(100vw - 20px);
    max-height: 96vh;
    border-radius: 12px;
  }

  .scheduler-booking-dialog__content {
    max-height: calc(96vh - 64px);
  }

  .scheduler-booking-dialog__header {
    min-height: 64px;
    padding: 10px 16px;
  }

  .scheduler-booking-dialog__content .quick-editor > .ui-card__body {
    padding: 18px 16px;
  }

  .scheduler-booking-dialog__content .quick-editor-layout {
    grid-template-columns: minmax(0, 1fr);
    gap: 20px;
  }

  .scheduler-booking-dialog__content .quick-editor-visit {
    position: static;
  }

  .scheduler-booking-dialog__content .quick-editor-footer {
    margin: 18px -16px -18px;
    padding: 12px 16px 14px;
  }
}

@media (max-width: 480px) {
  .scheduler-booking-dialog {
    width: 100vw;
    max-height: 100dvh;
    height: 100dvh;
    border: 0;
    border-radius: 0;
  }

  .scheduler-booking-dialog__content {
    max-height: calc(100dvh - 64px);
  }
}
</style>

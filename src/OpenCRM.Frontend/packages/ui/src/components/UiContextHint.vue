<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { autoUpdate, flip, offset, shift, useFloating } from '@floating-ui/vue'
import { useLocalization } from '../i18n'

const props = withDefaults(defineProps<{
  active: boolean
  text: string
  label?: string
}>(), {
  label: ''
})

const { t, locale } = useLocalization()

const isOpen = ref(false)
const isDismissed = ref(false)
const reference = ref<HTMLElement | null>(null)
const floating = ref<HTMLElement | null>(null)
const hintId = `ui-context-hint-${Math.random().toString(36).slice(2)}`

const { floatingStyles } = useFloating(reference, floating, {
  placement: 'top',
  strategy: 'fixed',
  whileElementsMounted: autoUpdate,
  middleware: [offset(8), flip(), shift({ padding: 8 })]
})

function close(returnFocus = false) {
  isOpen.value = false
  if (returnFocus) {
    void nextTick(() => reference.value?.focus())
  }
}

function dismiss() {
  isDismissed.value = true
  close(true)
}

function toggle() {
  isOpen.value = !isOpen.value
}

function handlePointerDown(event: PointerEvent) {
  const target = event.target as Node
  if (reference.value?.contains(target) || floating.value?.contains(target)) {
    return
  }

  close()
}

function addDocumentListeners() {
  if (typeof document === 'undefined') {
    return
  }

  document.addEventListener('pointerdown', handlePointerDown)
  document.addEventListener('keydown', handleKeydown)
}

function removeDocumentListeners() {
  if (typeof document === 'undefined') {
    return
  }

  document.removeEventListener('pointerdown', handlePointerDown)
  document.removeEventListener('keydown', handleKeydown)
}

function handleKeydown(event: KeyboardEvent) {
  if (event.key !== 'Escape' || !isOpen.value) {
    return
  }

  event.preventDefault()
  close(true)
}

watch(() => props.active, (active) => {
  if (!active) {
    isDismissed.value = false
    close()
    return
  }

  if (!isDismissed.value) {
    isOpen.value = true
  }
}, { immediate: true })

watch(isOpen, (open) => {
  if (open) {
    addDocumentListeners()
    return
  }

  removeDocumentListeners()
}, { immediate: true })

onBeforeUnmount(removeDocumentListeners)
</script>

<template>
  <span v-if="active" class="ui-context-hint-anchor">
    <button
      ref="reference"
      type="button"
      class="ui-context-hint-trigger"
      :aria-label="label"
      :aria-controls="hintId"
      :aria-expanded="isOpen"
      @click="toggle"
    >
      <span aria-hidden="true">i</span>
    </button>
  </span>

  <div
    v-if="active && isOpen"
    :id="hintId"
    ref="floating"
    class="ui-context-hint"
    role="region"
    aria-live="polite"
    :aria-label="label || t('common.hint')"
    :style="floatingStyles"
  >
    <span>{{ text }}</span>
    <button type="button" class="ui-context-hint__close" :aria-label="t('common.closeHint', { label: (label || t('common.hint')).toLocaleLowerCase(locale) })" @click="dismiss">
      <span aria-hidden="true">×</span>
    </button>
  </div>
</template>

<style scoped>
.ui-context-hint-anchor {
  display: inline-flex;
  vertical-align: middle;
}

.ui-context-hint-trigger,
.ui-context-hint__close {
  display: inline-grid;
  place-items: center;
  flex: 0 0 auto;
  width: 22px;
  height: 22px;
  padding: 0;
  border: 1px solid var(--crm-line, #d9dee7);
  border-radius: 50%;
  background: var(--crm-surface, #fff);
  color: var(--crm-text-muted, #66758c);
  cursor: pointer;
  font: inherit;
  line-height: 1;
}

.ui-context-hint-trigger span {
  font-size: 13px;
  font-weight: 700;
}

.ui-context-hint {
  z-index: 1100;
  display: flex;
  align-items: flex-start;
  gap: 10px;
  width: max-content;
  max-width: min(300px, calc(100vw - 16px));
  padding: 10px 12px;
  border: 1px solid var(--crm-line, #d9dee7);
  border-radius: 8px;
  background: var(--crm-surface, #fff);
  color: var(--crm-text, #243044);
  box-shadow: 0 8px 24px rgb(23 35 52 / 16%);
  font-size: 12px;
  line-height: 1.45;
  overflow-wrap: anywhere;
}

.ui-context-hint__close {
  width: 20px;
  height: 20px;
  margin: -2px -3px 0 0;
  font-size: 18px;
}

.ui-context-hint-trigger:focus-visible,
.ui-context-hint__close:focus-visible {
  outline: 2px solid var(--crm-accent, #c45112);
  outline-offset: 2px;
}
</style>

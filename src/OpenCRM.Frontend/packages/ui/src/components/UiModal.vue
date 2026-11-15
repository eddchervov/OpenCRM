<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { activateModalFocus } from '../modalFocus'

const props = withDefaults(defineProps<{
  open?: boolean
  title?: string
}>(), {
  open: false,
  title: ''
})

const emit = defineEmits<{
  close: []
}>()

const dialogRoot = ref<HTMLElement | null>(null)
let deactivateModalFocus: (() => void) | null = null

function deactivateModal() {
  if (!deactivateModalFocus) {
    return
  }

  deactivateModalFocus()
  deactivateModalFocus = null
}

watch(() => props.open, async isOpen => {
  if (!isOpen) {
    deactivateModal()
    return
  }

  await nextTick()
  if (!props.open || !dialogRoot.value || typeof document === 'undefined') {
    return
  }

  deactivateModalFocus = activateModalFocus(dialogRoot.value, () => emit('close'))
}, { flush: 'post', immediate: true })

onBeforeUnmount(deactivateModal)
</script>

<template>
  <Teleport to="body">
    <div v-if="open" ref="dialogRoot" class="ui-modal" role="dialog" aria-modal="true" :aria-label="title || 'Диалоговое окно'" tabindex="-1">
      <button class="ui-modal__backdrop" type="button" aria-label="Закрыть" @click="emit('close')" />
      <section class="ui-modal__panel">
        <header class="ui-modal__header ui-form-header">
          <h2>{{ title }}</h2>
          <button class="ui-modal__close" type="button" aria-label="Закрыть" @click="emit('close')">×</button>
        </header>
        <div class="ui-modal__body">
          <slot />
        </div>
        <footer v-if="$slots.footer" class="ui-modal__footer">
          <slot name="footer" />
        </footer>
      </section>
    </div>
  </Teleport>
</template>

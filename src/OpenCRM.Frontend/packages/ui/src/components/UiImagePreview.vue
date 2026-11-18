<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import { useLocalization } from '../i18n'

const { t } = useLocalization()

const props = withDefaults(defineProps<{
  src?: string | null
  title?: string
}>(), {
  src: null,
  title: ''
})
const emit = defineEmits<{ close: [] }>()
const dialog = ref<HTMLDialogElement | null>(null)
const closeButton = ref<HTMLButtonElement | null>(null)

watch(() => props.src, async () => {
  await nextTick()
  if (props.src && !dialog.value?.open) {
    dialog.value?.showModal()
  } else if (!props.src && dialog.value?.open) {
    dialog.value.close()
  }
}, { immediate: true, flush: 'post' })
</script>

<template>
  <Teleport to="body">
    <dialog
      ref="dialog"
      class="image-preview"
      :aria-label="title || t('common.photo')"
      @keydown.tab.prevent="closeButton?.focus()"
      @close="emit('close')"
      @click.self="dialog?.close()"
    >
      <div class="image-preview__content">
        <header class="image-preview__header">
          <h2>{{ title || t('common.photo') }}</h2>
          <button ref="closeButton" type="button" :aria-label="t('common.close')" autofocus @click="dialog?.close()">×</button>
        </header>
        <img v-if="src" :src="src" :alt="title || t('common.photo')" class="image-preview__image">
      </div>
    </dialog>
  </Teleport>

</template>

<style scoped>
.image-preview {
  width: min(1100px, calc(100vw - 32px));
  max-width: none;
  max-height: calc(100dvh - 32px);
  margin: auto;
  padding: 0;
  border: 1px solid var(--crm-line, #dce2e9);
  border-radius: 10px;
  background: var(--crm-surface, #fff);
  color: var(--crm-text, #202833);
}

.image-preview::backdrop {
  background: rgba(0, 0, 0, .75);
}

.image-preview__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--crm-line, #dce2e9);
}

.image-preview__header h2 {
  margin: 0;
  font-size: 15px;
}

.image-preview__header button {
  width: 36px;
  height: 36px;
  padding: 0;
  border: 1px solid var(--crm-line, #dce2e9);
  border-radius: 6px;
  background: var(--crm-surface-soft, #f5f7f9);
  color: inherit;
  font-size: 24px;
  cursor: pointer;
}

.image-preview__header button:focus-visible {
  outline: 2px solid #e87532;
  outline-offset: 2px;
}

.image-preview__image {
  display: block;
  width: 100%;
  max-height: calc(100dvh - 110px);
  object-fit: contain;
}
</style>

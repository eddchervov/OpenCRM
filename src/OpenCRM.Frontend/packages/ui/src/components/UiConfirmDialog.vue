<script setup lang="ts">
import UiButton from './UiButton.vue'
import UiModal from './UiModal.vue'

withDefaults(defineProps<{
  open?: boolean
  title?: string
  message?: string
  confirmText?: string
  cancelText?: string
  busy?: boolean
  tone?: 'danger' | 'default'
}>(), {
  open: false,
  title: 'Подтверждение',
  message: '',
  confirmText: 'Подтвердить',
  cancelText: 'Отмена',
  busy: false,
  tone: 'default'
})

const emit = defineEmits<{
  close: []
  confirm: []
}>()
</script>

<template>
  <UiModal :open="open" :title="title" @close="emit('close')">
    <p class="ui-confirm-dialog__message">{{ message }}</p>

    <template #footer>
      <UiButton variant="secondary" :disabled="busy" @click="emit('close')">{{ cancelText }}</UiButton>
      <UiButton
        :class="{ 'ui-confirm-dialog__danger': tone === 'danger' }"
        :disabled="busy"
        @click="emit('confirm')"
      >
        {{ busy ? 'Выполняем...' : confirmText }}
      </UiButton>
    </template>
  </UiModal>
</template>

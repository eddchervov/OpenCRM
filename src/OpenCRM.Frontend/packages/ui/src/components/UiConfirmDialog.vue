<script setup lang="ts">
import UiButton from './UiButton.vue'
import UiModal from './UiModal.vue'
import { useLocalization } from '../i18n'

const { t } = useLocalization()

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
  title: '',
  message: '',
  confirmText: '',
  cancelText: '',
  busy: false,
  tone: 'default'
})

const emit = defineEmits<{
  close: []
  confirm: []
}>()
</script>

<template>
  <UiModal :open="open" :title="title || t('common.confirmation')" @close="emit('close')">
    <p class="ui-confirm-dialog__message">{{ message }}</p>

    <template #footer>
      <UiButton variant="secondary" :disabled="busy" @click="emit('close')">{{ cancelText || t('common.cancel') }}</UiButton>
      <UiButton
        :class="{ 'ui-confirm-dialog__danger': tone === 'danger' }"
        :disabled="busy"
        @click="emit('confirm')"
      >
        {{ busy ? t('common.saving') : confirmText || t('common.confirm') }}
      </UiButton>
    </template>
  </UiModal>
</template>

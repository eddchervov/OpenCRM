<script setup lang="ts">
import UiButton from './UiButton.vue'
import UiModal from './UiModal.vue'
import { useLocalization } from '../i18n'

const { t } = useLocalization()

withDefaults(defineProps<{
  open?: boolean
  title?: string
  message?: string
  closeText?: string
  tone?: 'danger' | 'default'
}>(), {
  open: false,
  title: '',
  message: '',
  closeText: '',
  tone: 'default'
})

const emit = defineEmits<{
  close: []
}>()
</script>

<template>
  <UiModal :open="open" :title="title || t('common.message')" @close="emit('close')">
    <p class="ui-alert-dialog__message" :class="{ 'ui-alert-dialog__message--danger': tone === 'danger' }">
      {{ message }}
    </p>

    <template #footer>
      <UiButton :class="{ 'ui-alert-dialog__danger': tone === 'danger' }" @click="emit('close')">
        {{ closeText || t('common.understood') }}
      </UiButton>
    </template>
  </UiModal>
</template>

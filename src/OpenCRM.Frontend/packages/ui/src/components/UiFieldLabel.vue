<script setup lang="ts">
import UiTooltip from './UiTooltip.vue'
import { useLocalization } from '../i18n'

const { t } = useLocalization()

withDefaults(defineProps<{
  label?: string
  required?: boolean
  help?: string
  forId?: string
}>(), {
  label: '',
  required: false,
  help: '',
  forId: undefined
})
</script>

<template>
  <component :is="forId ? 'label' : 'span'" v-if="label" class="ui-field-label" :for="forId">
    <span class="ui-field-label__text">
      {{ label }}<em v-if="required" class="ui-field__required">*</em>
    </span>
    <UiTooltip v-if="help" :text="help" placement="top">
      <button type="button" class="ui-field-label__help" :aria-label="t('common.help', { label })">?</button>
    </UiTooltip>
  </component>
</template>

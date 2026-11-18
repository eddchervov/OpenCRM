<script setup lang="ts">
import UiFieldLabel from './UiFieldLabel.vue'
import { useLocalization } from '../i18n'

const { t } = useLocalization()

const model = defineModel<string>({ default: '' })

withDefaults(defineProps<{
  id?: string
  label?: string
  labelHelp?: string
  placeholder?: string
  disabled?: boolean
  required?: boolean
  min?: number
  max?: number
  step?: number
}>(), {
  id: undefined,
  label: '',
  labelHelp: '',
  placeholder: '0',
  disabled: false,
  required: false,
  min: 0,
  max: 999999999,
  step: 1
})

function normalizeValue(value: string | number | null | undefined) {
  const normalized = String(value ?? '').replace(',', '.').trim()
  if (!normalized) {
    return ''
  }

  const parsed = Number(normalized)
  if (!Number.isFinite(parsed)) {
    return ''
  }

  return String(parsed)
}

function handleBlur() {
  model.value = normalizeValue(model.value)
}

</script>

<template>
  <label class="ui-field ui-money-input">
    <UiFieldLabel :label="label || t('common.price')" :required="required" :help="labelHelp" />
    <input
      v-model="model"
      :id="id"
      type="number"
      inputmode="decimal"
      :placeholder="placeholder"
      :disabled="disabled"
      :required="required"
      :min="min"
      :max="max"
      :step="step"
      @blur="handleBlur"
    />
  </label>
</template>

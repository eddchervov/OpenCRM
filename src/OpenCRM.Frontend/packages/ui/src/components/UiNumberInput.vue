<script setup lang="ts">
import UiFieldLabel from './UiFieldLabel.vue'

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
  integer?: boolean
}>(), {
  id: undefined,
  label: '',
  labelHelp: '',
  placeholder: '0',
  disabled: false,
  required: false,
  min: 0,
  max: 999999999,
  step: 1,
  integer: false
})

function normalizeValue(value: string | number | null | undefined) {
  const raw = String(value ?? '').replace(',', '.').trim()
  if (!raw) {
    return ''
  }

  const parsed = Number(raw)
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
  <label class="ui-field ui-number-input">
    <UiFieldLabel :label="label" :required="required" :help="labelHelp" />
    <input
      v-model="model"
      :id="id"
      type="number"
      :inputmode="integer ? 'numeric' : 'decimal'"
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

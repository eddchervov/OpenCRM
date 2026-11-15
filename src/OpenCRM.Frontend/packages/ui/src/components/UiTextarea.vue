<script setup lang="ts">
import UiFieldLabel from './UiFieldLabel.vue'

const model = defineModel<string>({ default: '' })

withDefaults(defineProps<{
  id?: string
  label?: string
  labelledBy?: string
  describedBy?: string
  labelHelp?: string
  placeholder?: string
  rows?: number
  maxlength?: number
  disabled?: boolean
  required?: boolean
}>(), {
  id: undefined,
  label: '',
  labelledBy: undefined,
  describedBy: undefined,
  labelHelp: '',
  placeholder: '',
  rows: 4,
  maxlength: undefined,
  disabled: false,
  required: false
})
</script>

<template>
  <label class="ui-field">
    <UiFieldLabel :label="label" :required="required" :help="labelHelp" />
    <textarea v-model="model" :id="id" :aria-labelledby="labelledBy" :aria-describedby="describedBy" :placeholder="placeholder" :rows="rows" :maxlength="maxlength" :disabled="disabled" :required="required" />
  </label>
</template>

<script setup lang="ts">
import { onBeforeUnmount, ref, useId, watch } from 'vue'
import { MaskInput, type MaskInputOptions } from 'maska'
import UiFieldLabel from './UiFieldLabel.vue'

const fieldId = useId()
const model = defineModel<string>({ default: '' })
const input = ref<HTMLInputElement>()
let inputMask: MaskInput | undefined

const props = withDefaults(defineProps<{
  id?: string
  label?: string
  labelHelp?: string
  placeholder?: string
  type?: string
  disabled?: boolean
  required?: boolean
  maxlength?: number | string
  inputmode?: 'text' | 'numeric' | 'decimal'
  hint?: string
  error?: string
  mask?: string | MaskInputOptions
}>(), {
  id: undefined,
  label: '',
  labelHelp: '',
  placeholder: '',
  type: 'text',
  disabled: false,
  required: false,
  maxlength: undefined,
  inputmode: undefined,
  hint: '',
  error: '',
  mask: undefined
})

watch([input, () => props.mask], ([element, mask]) => {
  inputMask?.destroy()
  inputMask = element && mask ? new MaskInput(element, typeof mask === 'string' ? { mask } : mask) : undefined
}, { flush: 'post' })

onBeforeUnmount(() => inputMask?.destroy())
</script>

<template>
  <label class="ui-field">
    <UiFieldLabel :label="label" :required="required" :help="labelHelp" />
    <input ref="input" v-model="model" :id="id || fieldId" :inputmode="inputmode" :aria-label="label || undefined" :aria-describedby="error || hint ? `${fieldId}-help` : undefined" :type="type" :placeholder="placeholder" :disabled="disabled" :required="required" :maxlength="maxlength == null ? undefined : Number(maxlength)" :aria-invalid="!!error || undefined" />
    <span v-if="error" :id="`${fieldId}-help`" class="ui-field__error" role="alert">{{ error }}</span>
    <small v-else-if="hint" :id="`${fieldId}-help`" class="ui-field__hint">{{ hint }}</small>
  </label>
</template>

<style scoped>
.ui-field__hint { color: var(--crm-text-muted, #667085); font-size: 12px; font-weight: 400; }
</style>

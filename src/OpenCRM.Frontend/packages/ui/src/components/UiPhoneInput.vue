<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, useId, watch } from 'vue'
import intlTelInput, { type Iti } from 'intl-tel-input/intlTelInputWithUtils'
import { ru } from 'intl-tel-input/i18n'
import 'intl-tel-input/styles'
import UiFieldLabel from './UiFieldLabel.vue'

const model = defineModel<string>({ default: '' })
const fieldId = useId()

const props = withDefaults(defineProps<{
  id?: string
  label?: string
  labelHelp?: string
  placeholder?: string
  disabled?: boolean
  initialCountry?: string
  required?: boolean
  error?: string
}>(), {
  id: undefined,
  label: 'Телефон',
  labelHelp: '',
  placeholder: '',
  disabled: false,
  initialCountry: 'ru',
  required: false,
  error: ''
})

const emit = defineEmits<{
  validityChange: [isValid: boolean | null]
}>()

const inputRef = ref<HTMLInputElement | null>(null)
let phoneInput: Iti | null = null
let isSyncingFromModel = false

function syncModelFromPlugin() {
  if (!inputRef.value || !phoneInput || isSyncingFromModel) {
    return
  }

  const formatted = phoneInput.getNumber('E164')
  model.value = formatted || inputRef.value.value
  emit('validityChange', phoneInput.isValidNumber())
}

onMounted(() => {
  if (!inputRef.value) {
    return
  }

  phoneInput = intlTelInput(inputRef.value, {
    initialCountry: props.initialCountry as never,
    countryNameLocale: 'ru',
    separateDialCode: true,
    nationalMode: false,
    formatAsYouType: true,
    formatOnDisplay: true,
    i18n: ru
  })

  if (model.value) {
    phoneInput.setNumber(model.value)
  }

  inputRef.value.addEventListener('input', syncModelFromPlugin)
  inputRef.value.addEventListener('countrychange', syncModelFromPlugin)
})

onBeforeUnmount(() => {
  if (inputRef.value) {
    inputRef.value.removeEventListener('input', syncModelFromPlugin)
    inputRef.value.removeEventListener('countrychange', syncModelFromPlugin)
  }

  phoneInput?.destroy()
  phoneInput = null
})

watch(model, value => {
  if (!phoneInput || !inputRef.value || inputRef.value.value === value) {
    return
  }

  isSyncingFromModel = true
  phoneInput.setNumber(value)
  isSyncingFromModel = false
})

watch(() => props.disabled, value => {
  phoneInput?.setDisabled(value)
})
</script>

<template>
  <label class="ui-field ui-phone-input">
    <UiFieldLabel :label="label" :required="required" :help="labelHelp" />
    <input ref="inputRef" :id="id || fieldId" :aria-label="label || undefined" :aria-invalid="!!error || undefined" :aria-describedby="error ? `${fieldId}-help` : undefined" type="tel" :placeholder="placeholder" :disabled="disabled" :required="required" autocomplete="tel" />
    <span v-if="error" :id="`${fieldId}-help`" class="ui-field__error" role="alert">{{ error }}</span>
  </label>
</template>

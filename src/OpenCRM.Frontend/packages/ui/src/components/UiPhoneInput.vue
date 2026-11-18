<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, useId, watch } from 'vue'
import intlTelInput, { type Iti, type SomeOptions } from 'intl-tel-input/intlTelInputWithUtils'
import { en, ru, zh } from 'intl-tel-input/i18n'
import 'intl-tel-input/styles'
import UiFieldLabel from './UiFieldLabel.vue'
import { localeCultures, useLocalization, type UiLocale } from '../i18n'

const model = defineModel<string>({ default: '' })
const fieldId = useId()
const { t, locale } = useLocalization()
const phoneMessages: Record<UiLocale, SomeOptions['i18n']> = { ru, en, 'zh-Hans': zh }

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
  label: '',
  labelHelp: '',
  placeholder: '',
  disabled: false,
  initialCountry: '',
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

function initializePhoneInput(initialCountry = props.initialCountry) {
  if (!inputRef.value) {
    return
  }

  phoneInput = intlTelInput(inputRef.value, {
    initialCountry: initialCountry as never,
    countryNameLocale: localeCultures[locale.value],
    separateDialCode: true,
    nationalMode: false,
    formatAsYouType: true,
    formatOnDisplay: true,
    i18n: phoneMessages[locale.value]
  })

  if (model.value) {
    phoneInput.setNumber(model.value)
  }

  phoneInput.setDisabled(props.disabled)
  inputRef.value.addEventListener('input', syncModelFromPlugin)
  inputRef.value.addEventListener('countrychange', syncModelFromPlugin)
}

function destroyPhoneInput() {
  if (inputRef.value) {
    inputRef.value.removeEventListener('input', syncModelFromPlugin)
    inputRef.value.removeEventListener('countrychange', syncModelFromPlugin)
  }

  phoneInput?.destroy()
  phoneInput = null
}

onMounted(() => initializePhoneInput())
onBeforeUnmount(destroyPhoneInput)

watch(locale, () => {
  const input = inputRef.value
  if (!input || !phoneInput) {
    return
  }

  const country = phoneInput.getSelectedCountryData()?.iso2
  const value = input.value
  const isFocused = document.activeElement === input
  const selectionStart = input.selectionStart
  const selectionEnd = input.selectionEnd

  destroyPhoneInput()
  initializePhoneInput(country || props.initialCountry)
  input.value = value

  if (isFocused) {
    input.focus()
    input.setSelectionRange(selectionStart, selectionEnd)
  }
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
    <UiFieldLabel :label="label || t('common.phone')" :required="required" :help="labelHelp" />
    <input ref="inputRef" :id="id || fieldId" :aria-label="label || t('common.phone')" :aria-invalid="!!error || undefined" :aria-describedby="error ? `${fieldId}-help` : undefined" type="tel" :placeholder="placeholder" :disabled="disabled" :required="required" autocomplete="tel" />
    <span v-if="error" :id="`${fieldId}-help`" class="ui-field__error" role="alert">{{ error }}</span>
  </label>
</template>

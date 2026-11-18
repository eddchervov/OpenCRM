<script setup lang="ts">
import { computed, ref, useId } from 'vue'
import { DatePicker } from 'v-calendar'
import 'v-calendar/style.css'
import { formatDateOnly, parseDateOnly } from '../dateOnly'
import { workspaceTheme } from '../workspaceTheme'
import UiFieldLabel from './UiFieldLabel.vue'
import { useLocalization, formattingCulture } from '../i18n'
import { getDateInputMasks, isImpossibleDateInput } from '../dateInputFormat'

const { t } = useLocalization()
const inputMasks = computed(() => getDateInputMasks(formattingCulture.value))

const model = defineModel<string>({ default: '' })
const fieldId = useId()
const manualInputError = ref('')
const dateInput = ref<HTMLInputElement | null>(null)

const props = withDefaults(defineProps<{
  id?: string
  label?: string
  labelHelp?: string
  placeholder?: string
  disabled?: boolean
  enableTimePicker?: boolean
  required?: boolean
  error?: string
}>(), {
  id: undefined,
  label: '',
  labelHelp: '',
  placeholder: '',
  disabled: false,
  enableTimePicker: false,
  required: false,
  error: ''
})

function toModelValue(value: Date) {
  return props.enableTimePicker
    ? value.toISOString()
    : formatDateOnly(value)
}

function clearManualInputError() {
  manualInputError.value = ''
  dateInput.value?.setCustomValidity('')
}

function clearEmptyInput(event: Event) {
  const input = event.target as HTMLInputElement
  if (input.value.trim() === '') {
    clearManualInputError()
    input.setCustomValidity('')
    model.value = ''
  }
}

function hasImpossibleDate(value: string) {
  return isImpossibleDateInput(value, formattingCulture.value)
}

function guardImpossibleDateInput(inputEvents: Record<string, (event: Event) => void>) {
  return Object.fromEntries(Object.entries(inputEvents).map(([name, handler]) => {
    if (!['input', 'change'].includes(name.replace(/^on/, '').toLowerCase())) {
      return [name, handler]
    }

    return [name, (event: Event) => {
      const input = event.target as HTMLInputElement
      if (hasImpossibleDate(input.value)) {
        manualInputError.value = t('common.invalidDate')
        input.setCustomValidity(manualInputError.value)
        model.value = ''
        return
      }

      clearManualInputError()
      input.setCustomValidity('')
      handler(event)
    }]
  }))
}

const pickerValue = computed<Date | null>({
  get() {
    if (!model.value) {
      return null
    }

    return parseDateOnly(model.value)
  },
  set(value) {
    clearManualInputError()
    model.value = value ? toModelValue(value) : ''
  }
})

const ariaDescribedBy = computed(() => [
  props.error ? `${fieldId}-help` : '',
  manualInputError.value ? `${fieldId}-input-help` : ''
].filter(Boolean).join(' ') || undefined)
</script>

<template>
  <label class="ui-field ui-date-picker">
    <UiFieldLabel :label="label || t('common.date')" :required="required" :help="labelHelp" />
    <DatePicker
      v-model="pickerValue"
      :is-dark="workspaceTheme === 'dark'"
      :mode="enableTimePicker ? 'dateTime' : 'date'"
      :disabled="disabled"
      :locale="formattingCulture"
      :masks="inputMasks"
      :is24hr="true"
      :input-debounce="0"
      color="orange"
      :popover="{ placement: 'bottom-start', visibility: 'click' }"
    >
      <template #header-prev-button>
        <svg width="24" height="24" viewBox="0 0 24 24" role="img" :aria-label="t('common.previousMonth')">
          <path d="m14 6-6 6 6 6" fill="none" stroke="currentColor" stroke-width="2" />
        </svg>
      </template>
      <template #header-next-button>
        <svg width="24" height="24" viewBox="0 0 24 24" role="img" :aria-label="t('common.nextMonth')">
          <path d="m10 6 6 6-6 6" fill="none" stroke="currentColor" stroke-width="2" />
        </svg>
      </template>
      <template #default="{ inputValue, inputEvents }">
        <input
          ref="dateInput"
          :id="id || fieldId"
          class="ui-date-picker__input"
          :aria-label="label || undefined"
          :aria-invalid="!!error || !!manualInputError || undefined"
          :aria-describedby="ariaDescribedBy"
          :value="inputValue"
          :placeholder="placeholder || t('common.chooseDate')"
          :disabled="disabled"
          :required="required"
          @input="clearEmptyInput"
          v-on="guardImpossibleDateInput(inputEvents)"
        />
      </template>
    </DatePicker>
    <span v-if="error" :id="`${fieldId}-help`" class="ui-field__error" role="alert">{{ error }}</span>
    <span v-if="manualInputError" :id="`${fieldId}-input-help`" class="ui-field__error" role="alert">{{ manualInputError }}</span>
  </label>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, useId, watch } from 'vue'
import UiFieldLabel from './UiFieldLabel.vue'

const model = defineModel<string>({ default: '' })
const fieldId = useId()

const props = withDefaults(defineProps<{
  id?: string
  label?: string
  labelHelp?: string
  placeholder?: string
  disabled?: boolean
  required?: boolean
  align?: 'start' | 'end'
  step?: number
  unavailableReason?: (value: string) => string
}>(), {
  id: undefined,
  label: 'Время',
  labelHelp: '',
  placeholder: 'Выберите время',
  disabled: false,
  required: false,
  align: 'start',
  step: 300,
  unavailableReason: undefined
})

const root = ref<HTMLElement | null>(null)
const input = ref<HTMLInputElement | null>(null)
const hoursColumn = ref<HTMLElement | null>(null)
const minutesColumn = ref<HTMLElement | null>(null)
const isOpen = ref(false)
const optionHint = ref('')
const inputValue = ref(formatTime(model.value))
const shouldKeepEmpty = ref(false)

const minuteStep = computed(() => {
  const minutes = Math.max(1, Math.round(props.step / 60))
  return 60 % minutes === 0 ? minutes : 5
})

const hourOptions = Array.from({ length: 24 }, (_, index) => index)
const minuteOptions = computed(() =>
  Array.from({ length: Math.ceil(60 / minuteStep.value) }, (_, index) => index * minuteStep.value)
    .filter(value => value < 60)
)
const selectedParts = computed(() => parseTime(model.value) ?? parseTime(inputValue.value))
const selectedHours = computed(() => selectedParts.value?.hours ?? 9)
const selectedMinutes = computed(() => {
  const minutes = selectedParts.value?.minutes ?? 0
  return minuteOptions.value.reduce((best, option) =>
    Math.abs(option - minutes) < Math.abs(best - minutes) ? option : best,
    minuteOptions.value[0] ?? 0
  )
})
const displayValue = computed(() => formatTime(model.value) || toTimeString(selectedHours.value, selectedMinutes.value))

function toTimeString(hours: number, minutes: number) {
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}`
}

function parseTime(value?: string | null) {
  const match = value?.trim().match(/^(\d{1,2})(?::|\.| )?(\d{2})?$/)
  if (!match) {
    return null
  }

  const hours = Number(match[1])
  const minutes = match[2] === undefined ? 0 : Number(match[2])
  if (
    !Number.isInteger(hours) ||
    !Number.isInteger(minutes) ||
    hours < 0 ||
    hours > 23 ||
    minutes < 0 ||
    minutes > 59
  ) {
    return null
  }

  return { hours, minutes }
}

function formatTime(value?: string | null) {
  const parsed = parseTime(value)
  return parsed ? toTimeString(parsed.hours, parsed.minutes) : ''
}

function commitValue(value: string) {
  const formatted = formatTime(value)
  if (!formatted || props.unavailableReason?.(formatted)) {
    return
  }

  inputValue.value = formatted
  model.value = formatted
  shouldKeepEmpty.value = false
}

function updateTypedValue(event: Event) {
  const value = (event.target as HTMLInputElement).value
  inputValue.value = value.replace(/[^\d:. ]/g, '').slice(0, 5)

  const parsed = parseTime(inputValue.value)
  if (parsed && /[:. ]?\d{2}$/.test(inputValue.value.trim())) {
    model.value = toTimeString(parsed.hours, parsed.minutes)
  } else if (!inputValue.value.trim()) {
    model.value = ''
  }
}

function normalizeTypedValue() {
  if (!inputValue.value.trim()) {
    model.value = ''
    return
  }

  const formatted = formatTime(inputValue.value)
  if (formatted) {
    inputValue.value = formatted
    model.value = formatted
  } else {
    inputValue.value = formatTime(model.value)
  }
}

function minuteReason(hour: number, minute: number) {
  return props.unavailableReason?.(toTimeString(hour, minute)) || ''
}

const hourReasons = computed(() => hourOptions.map(hour => {
  const reasons = minuteOptions.value.map(minute => minuteReason(hour, minute))
  return reasons.every(Boolean) ? [...new Set(reasons)].join('\n') : ''
}))

function chooseHour(hour: number) {
  if (hourReasons.value[hour]) return
  const minute = minuteReason(hour, selectedMinutes.value)
    ? minuteOptions.value.find(value => !minuteReason(hour, value))
    : selectedMinutes.value
  if (minute !== undefined) commitValue(toTimeString(hour, minute))
}

function chooseMinute(minutes: number) {
  commitValue(toTimeString(selectedHours.value, minutes))
}

async function scrollSelectedIntoView() {
  await nextTick()
  hoursColumn.value
    ?.querySelector<HTMLElement>(`[data-hour="${selectedHours.value}"]`)
    ?.scrollIntoView({ block: 'center' })
  minutesColumn.value
    ?.querySelector<HTMLElement>(`[data-minute="${selectedMinutes.value}"]`)
    ?.scrollIntoView({ block: 'center' })
}

async function openPicker() {
  if (props.disabled) {
    return
  }

  optionHint.value = ''
  shouldKeepEmpty.value = false
  isOpen.value = true
  await scrollSelectedIntoView()
}

function closePicker() {
  isOpen.value = false
  normalizeTypedValue()
}

function clearValue() {
  model.value = ''
  inputValue.value = ''
  shouldKeepEmpty.value = true
}

function confirmPicker() {
  if (!model.value && !inputValue.value.trim() && !shouldKeepEmpty.value) {
    commitValue(displayValue.value)
  }

  closePicker()
  shouldKeepEmpty.value = false
}

function handleDocumentPointerDown(event: PointerEvent) {
  if (!isOpen.value || root.value?.contains(event.target as Node)) {
    return
  }

  closePicker()
}

function handleFocusOut(event: FocusEvent) {
  if (!root.value?.contains(event.relatedTarget as Node | null)) closePicker()
}

function handleKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    event.preventDefault()
    closePicker()
    return
  }

  if (event.key === 'ArrowDown') {
    event.preventDefault()
    void openPicker()
  }
}

watch(model, (value) => {
  const formatted = formatTime(value)
  if (formatted !== inputValue.value) {
    inputValue.value = formatted
  }
})

watch(isOpen, (open) => {
  if (open) {
    document.addEventListener('pointerdown', handleDocumentPointerDown)
    void nextTick(() => {
      input.value?.focus()
      void scrollSelectedIntoView()
    })
  } else {
    document.removeEventListener('pointerdown', handleDocumentPointerDown)
  }
})

onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', handleDocumentPointerDown)
})
</script>

<template>
  <div ref="root" class="ui-field ui-date-picker ui-time-picker" @focusout="handleFocusOut">
    <UiFieldLabel :label="label" :required="required" :help="labelHelp" :for-id="id || fieldId" />
    <span class="ui-time-picker__control">
      <input
        ref="input"
        :id="id || fieldId"
        class="ui-date-picker__input ui-time-picker__input"
        :value="inputValue"
        :placeholder="placeholder"
        :disabled="disabled"
        :required="required"
        inputmode="numeric"
        autocomplete="off"
        role="combobox"
        :aria-expanded="isOpen"
        @focus="openPicker"
        @click="openPicker"
        @input="updateTypedValue"
        @blur="normalizeTypedValue"
        @keydown="handleKeydown"
      />
      <button
        type="button"
        class="ui-time-picker__button"
        :disabled="disabled"
        aria-label="Выбрать время"
        @click.prevent="openPicker"
      >
        <span></span>
      </button>
    </span>

    <div v-if="isOpen && !disabled" class="ui-time-picker__popover" :class="{ 'ui-time-picker__popover--end': align === 'end' }">
      <div class="ui-time-picker__wheel-value">{{ displayValue }}</div>
      <div class="ui-time-picker__columns">
        <div ref="hoursColumn" class="ui-time-picker__column" aria-label="Часы">
          <button
            v-for="hour in hourOptions"
            :key="hour"
            type="button"
            class="ui-time-picker__option"
            :class="{ active: selectedHours === hour }"
            :data-hour="hour"
            :aria-disabled="Boolean(hourReasons[hour])"
            :title="hourReasons[hour]"
            @mouseenter="optionHint = hourReasons[hour] || ''"
            @focus="optionHint = hourReasons[hour] || ''"
            @click="chooseHour(hour)"
          >
            {{ String(hour).padStart(2, '0') }}
          </button>
        </div>
        <div class="ui-time-picker__separator">:</div>
        <div ref="minutesColumn" class="ui-time-picker__column" aria-label="Минуты">
          <button
            v-for="minute in minuteOptions"
            :key="minute"
            type="button"
            class="ui-time-picker__option"
            :class="{ active: selectedMinutes === minute }"
            :data-minute="minute"
            :aria-disabled="Boolean(minuteReason(selectedHours, minute))"
            :title="minuteReason(selectedHours, minute)"
            @mouseenter="optionHint = minuteReason(selectedHours, minute)"
            @focus="optionHint = minuteReason(selectedHours, minute)"
            @click="chooseMinute(minute)"
          >
            {{ String(minute).padStart(2, '0') }}
          </button>
        </div>
      </div>
      <div v-if="optionHint" class="ui-time-picker__hint" role="status">{{ optionHint }}</div>
      <div class="ui-time-picker__actions">
        <button type="button" class="ui-time-picker__action" @click="clearValue">Очистить</button>
        <button type="button" class="ui-time-picker__action ui-time-picker__action--primary" @click="confirmPicker">Готово</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.ui-time-picker__popover--end {
  right: 0;
  left: auto;
}

.ui-time-picker__option[aria-disabled="true"] {
  color: var(--crm-text-muted, #66758c);
  background: transparent;
  text-decoration: line-through;
  cursor: not-allowed;
}
.ui-time-picker__option:focus-visible {
  outline: 2px solid var(--opencrm-accent);
  outline-offset: -2px;
}

.ui-time-picker__hint {
  padding: 8px 10px;
  max-height: 100px;
  overflow-y: auto;
  font-size: 12px;
  line-height: 1.4;
  white-space: pre-line;
  overflow-wrap: anywhere;
  color: var(--crm-text, #243044);
}
</style>

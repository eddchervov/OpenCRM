<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { UiInput, useLocalization } from '@opencrm/ui'
import '../localization'
import { registrationPlateFormatsForCountry } from '../internationalRegistrationPlate'
import {
  createVehicleRegistrationPlateMask,
  getVehicleRegistrationPlateFormat,
  loadVehicleRegistrationPlateFormats,
  normalizeRegistrationPlate,
  prepareRegistrationPlatePaste,
  validateRegistrationPlate,
  type VehicleRegistrationPlateFormats
} from '../vehicleRegistrationPlate'

const model = defineModel<string>({ default: '' })
const allowOtherFormat = defineModel<boolean>('allowOtherFormat', { default: false })
const props = withDefaults(defineProps<{
  kind?: string | number
  id?: string
  label?: string
  placeholder?: string
  disabled?: boolean
  required?: boolean
  error?: string
  inferOtherFormat?: boolean
  countryCode?: string | null
}>(), {
  kind: 1,
  label: '',
  placeholder: '',
  disabled: false,
  required: false,
  error: '',
  inferOtherFormat: false
})

const config = ref<VehicleRegistrationPlateFormats>()
const { t } = useLocalization()
const effectiveConfig = computed(() => config.value && registrationPlateFormatsForCountry(config.value, props.countryCode))
const configError = ref('')
const pastedNumberError = ref('')
const keepRawInput = ref(false)
const format = computed(() => effectiveConfig.value && getVehicleRegistrationPlateFormat(props.kind ?? 1, effectiveConfig.value))
const mask = computed(() => effectiveConfig.value && !allowOtherFormat.value && !keepRawInput.value
  ? createVehicleRegistrationPlateMask(props.kind ?? 1, effectiveConfig.value)
  : undefined)
const inputError = computed(() => props.error || configError.value || pastedNumberError.value)
const effectivePlaceholder = computed(() => props.placeholder || format.value?.example || t('regional.freePlate'))
const inputMaxLength = computed(() => effectiveConfig.value?.maxLength)

onMounted(async () => {
  try {
    config.value = await loadVehicleRegistrationPlateFormats()
    if (props.inferOtherFormat && model.value && format.value &&
      validateRegistrationPlate(model.value, props.kind ?? 1, config.value)) {
      allowOtherFormat.value = true
    }
  } catch (error) {
    configError.value = error instanceof Error ? error.message : t('regional.plateLoad')
  }
})

watch(() => props.kind, () => {
  pastedNumberError.value = ''
  keepRawInput.value = Boolean(model.value && config.value && !allowOtherFormat.value &&
    validateRegistrationPlate(model.value, props.kind ?? 1, config.value))
})

function handlePaste(event: ClipboardEvent) {
  const pastedValue = event.clipboardData?.getData('text')
  if (pastedValue == null || !effectiveConfig.value) return

  const input = event.target as HTMLInputElement
  const selectionStart = input.selectionStart ?? input.value.length
  const selectionEnd = input.selectionEnd ?? selectionStart
  const result = prepareRegistrationPlatePaste(
    pastedValue,
    props.kind ?? 1,
    effectiveConfig.value,
    allowOtherFormat.value,
    input.value,
    selectionStart,
    selectionEnd
  )
  if (!result.preventDefault) return

  event.preventDefault()
  model.value = result.value
  pastedNumberError.value = result.error
    ? t('regional.platePaste')
    : ''
  keepRawInput.value = Boolean(result.error)
  if (!result.error) {
    void nextTick(() => input.setSelectionRange(result.selectionStart ?? result.value.length, result.selectionStart ?? result.value.length))
  }
}

function handleInput() {
  if (!keepRawInput.value || !config.value) return

  if (!model.value) {
    pastedNumberError.value = ''
    keepRawInput.value = false
    return
  }

  const normalized = normalizeRegistrationPlate(model.value, config.value)
  if (!validateRegistrationPlate(normalized, props.kind ?? 1, config.value)) {
    model.value = normalized
    pastedNumberError.value = ''
    keepRawInput.value = false
  }
}

function handleOtherFormatChange(event: Event) {
  const isOtherFormat = (event.target as HTMLInputElement).checked
  allowOtherFormat.value = isOtherFormat

  if (!isOtherFormat && model.value && config.value && format.value &&
    validateRegistrationPlate(model.value, props.kind ?? 1, config.value)) {
    pastedNumberError.value = t('regional.plateMask')
    keepRawInput.value = true
    return
  }

  pastedNumberError.value = ''
  keepRawInput.value = false
}
</script>

<template>
  <div class="vehicle-plate-input">
    <UiInput
      v-model="model"
      :id="id"
      :label="label || t('regional.plate')"
      :placeholder="effectivePlaceholder"
      :disabled="disabled || !config"
      :required="required"
      :maxlength="inputMaxLength"
      :error="inputError"
      :mask="mask"
      @paste="handlePaste"
      @input="handleInput"
    />
    <label v-if="format" class="vehicle-plate-input__other">
      <input type="checkbox" :checked="allowOtherFormat" :disabled="disabled || !config" @change="handleOtherFormatChange">
      <span>{{ t('regional.otherPlate') }}</span>
    </label>
  </div>
</template>

<style scoped>
.vehicle-plate-input__other { align-items: center; color: var(--crm-text-muted, #667085); display: inline-flex; font-size: 12px; gap: 7px; margin-top: 5px; }
.vehicle-plate-input__other input { accent-color: var(--crm-accent, #f47b36); }
</style>

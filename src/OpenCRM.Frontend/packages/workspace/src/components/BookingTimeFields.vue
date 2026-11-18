<script setup lang="ts">
import '../bookingTimeMessages'
import { translate as t } from '@opencrm/ui/i18n'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { UiContextHint, UiTimePicker } from '@opencrm/ui'
import { formatServiceDuration } from '../serviceDuration'
import {
  bookingDayOffset, bookingEndMinute, describeOccupiedSlot, findBookingConflicts, loadOccupiedBookingSlots, timeMinutes,
  type OccupiedBookingSlot
} from '../api/bookingSlots'

const start = defineModel<string>('start', { default: '' })
const end = defineModel<string>('end', { default: '' })
const endDate = defineModel<string>('endDate', { default: '' })
const props = defineProps<{
  organizationId: string
  date: string
  durationMinutes?: number | null
  autoDuration?: boolean
  location: string
  excludeMaintenanceId?: string
  disabled?: boolean
  cancelled?: boolean
}>()

const manualEnd = ref(!props.autoDuration)
let suggestedEnd = false
const canCalculate = computed(() => Boolean(props.durationMinutes && props.date && timeMinutes(start.value) !== null))

function useManualEnd() {
  manualEnd.value = true
  suggestedEnd = false
}

function applyDuration() {
  if (manualEnd.value || props.disabled || props.cancelled) return
  if (!canCalculate.value) {
    if (suggestedEnd) {
      end.value = ''
      endDate.value = props.date
      suggestedEnd = false
    }
    return
  }
  const timestamp = Date.parse(`${props.date}T00:00:00Z`) + (timeMinutes(start.value)! + props.durationMinutes!) * 60000
  const calculated = new Date(timestamp)
  if (!Number.isFinite(timestamp) || calculated.getUTCFullYear() > 9999) return
  const iso = calculated.toISOString()
  endDate.value = iso.slice(0, 10)
  end.value = iso.slice(11, 16)
  suggestedEnd = true
}

function recalculate() {
  manualEnd.value = false
  applyDuration()
}

watch(() => [props.durationMinutes, props.date, start.value], applyDuration, { immediate: true, flush: 'post' })

const slots = ref<OccupiedBookingSlot[]>([])
const loading = ref(false)
const error = ref('')
const checked = ref(false)
const enabled = computed(() => Boolean(props.organizationId && props.date && props.location.trim() && !props.cancelled))
const dayOffset = computed(() => bookingDayOffset(props.date, endDate.value || props.date))
const intervalError = computed(() => {
  const from = timeMinutes(start.value)
  const to = timeMinutes(end.value)
  if (dayOffset.value < 0) return t('bookingTime.endDate')
  if (dayOffset.value > 0 && (from === null || to === null)) return t('bookingTime.multiTimes')
  if (to !== null && from === null) return t('bookingTime.startRequired')
  return from !== null && to !== null && dayOffset.value + to <= from ? t('bookingTime.endAfter') : ''
})
const unknownDurationHintText = computed(() => {
  if (dayOffset.value > 0) {
    return t('bookingTime.multiManual')
  }

  if (end.value) {
    return t('bookingTime.unknownEnd')
  }

  if (start.value) {
    return t('bookingTime.unknownDefault')
  }

  return t('bookingTime.unknownManual')
})
const conflicts = computed(() => {
  const minute = timeMinutes(start.value)
  return minute === null ? [] : findBookingConflicts(slots.value, minute, bookingEndMinute(minute, end.value, dayOffset.value))
})
const feedback = ref<HTMLElement | null>(null)
let request: AbortController | undefined
let timer: ReturnType<typeof setTimeout> | undefined

function startUnavailable(value: string) {
  if (!enabled.value) return ''
  if (!checked.value) return loading.value ? t('bookingTime.checking') : t('bookingTime.unchecked')
  const minute = timeMinutes(value)
  const occupied = slots.value.filter(slot => minute !== null && minute >= slot.startMinute && minute < slot.endMinute)

  return occupied.map(describeOccupiedSlot).join('\n')
}

function endUnavailable(value: string) {
  if (!enabled.value) return ''
  if (!checked.value) return loading.value ? t('bookingTime.checking') : t('bookingTime.unchecked')
  const from = timeMinutes(start.value)
  const to = timeMinutes(value)
  if (from === null || to === null) return ''
  if (dayOffset.value + to <= from) return t('bookingTime.endAfterShort')

  return findBookingConflicts(slots.value, from, dayOffset.value + to).map(describeOccupiedSlot).join('\n')
}

async function refresh() {
  clearTimeout(timer)
  request?.abort()
  if (!enabled.value) return true
  const current = new AbortController()
  request = current
  loading.value = true
  checked.value = false
  error.value = ''
  try {
    const result = await loadOccupiedBookingSlots(
      props.organizationId, props.date, props.location.trim(), props.excludeMaintenanceId, current.signal, endDate.value || props.date
    )
    if (current.signal.aborted) return false
    slots.value = result
    checked.value = true

    return true
  } catch {
    if (!current.signal.aborted) error.value = t('bookingTime.loadError')

    return false
  } finally {
    if (!current.signal.aborted) loading.value = false
  }
}

async function validate() {
  if (!start.value && !end.value && !intervalError.value) return true
  if (intervalError.value) {
    feedback.value?.focus()
    return false
  }
  const loaded = await refresh()
  if (loaded && !intervalError.value && !conflicts.value.length) return true
  feedback.value?.scrollIntoView({ block: 'nearest' })
  feedback.value?.focus()

  return false
}

function getValidationError() {
  if (intervalError.value) return intervalError.value
  if (error.value) return error.value
  if (conflicts.value.length) return t('bookingTime.conflict', { slots: conflicts.value.map(describeOccupiedSlot).join('; ') })

  return t('bookingTime.checkInterval')
}

watch(() => [props.organizationId, props.date, endDate.value, props.location, props.excludeMaintenanceId, props.cancelled], () => {
  clearTimeout(timer)
  request?.abort()
  slots.value = []
  error.value = ''
  checked.value = false
  loading.value = enabled.value
  if (enabled.value) timer = setTimeout(() => { void refresh() }, 200)
}, { immediate: true, flush: 'sync' })

onBeforeUnmount(() => {
  clearTimeout(timer)
  request?.abort()
})

defineExpose({ validate, getValidationError, useManualEnd })
</script>

<template>
  <div class="booking-time-fields">
    <div class="booking-time-fields__inputs">
      <UiTimePicker v-model="start" :label="t('bookingTime.start')" :disabled="disabled" :unavailable-reason="startUnavailable" />
      <div class="booking-time-fields__end">
        <UiTimePicker v-model="end" @update:model-value="useManualEnd" :label="t('bookingTime.end')" :disabled="disabled" :unavailable-reason="endUnavailable" />
        <UiContextHint
          :active="durationMinutes === null"
          :label="t('bookingTime.why')"
          :text="unknownDurationHintText"
        />
      </div>
    </div>
    <div v-if="durationMinutes !== undefined" class="booking-time-fields__duration" aria-live="polite">
      <span v-if="durationMinutes != null">{{ t('bookingTime.duration', { duration: formatServiceDuration(durationMinutes) }) }}</span>
      <button v-if="manualEnd && canCalculate" type="button" :disabled="disabled || cancelled" @click="recalculate">{{ t('bookingTime.recalculate') }}</button>
    </div>
    <div v-if="enabled || intervalError" ref="feedback" class="booking-time-fields__availability" tabindex="-1" aria-live="polite">
      <div v-if="intervalError" class="booking-time-fields__error" role="alert">{{ intervalError }}</div>
      <span v-if="loading" role="status">{{ t('bookingTime.checking') }}</span>
      <div v-else-if="error" class="booking-time-fields__error" role="alert">
        {{ error }} <button type="button" :disabled="disabled" @click="refresh">{{ t('bookingTime.retry') }}</button>
      </div>
      <template v-else-if="checked">
        <div v-if="conflicts.length" class="booking-time-fields__error" role="alert">
          {{ t('bookingTime.occupied') }}
          <div v-for="slot in conflicts" :key="slot.id">{{ describeOccupiedSlot(slot) }}</div>
        </div>
        <div v-if="!conflicts.length && !intervalError && start && end" class="booking-time-fields__free">
          <span aria-hidden="true">✓</span> {{ t('bookingTime.free') }}
        </div>
        <details v-if="slots.length">
          <summary>{{ t('bookingTime.slots', { count: slots.length }) }}</summary>
          <ul><li v-for="slot in slots" :key="slot.id">{{ describeOccupiedSlot(slot) }}</li></ul>
        </details>
        <span v-else-if="!start || !end">{{ t('bookingTime.periodFree') }}</span>
      </template>
    </div>
  </div>
</template>

<style scoped>
.booking-time-fields {
  min-width: 0;
}

.booking-time-fields__end :deep(.ui-time-picker__popover) {
  left: auto;
  right: 0;
}

.booking-time-fields__inputs {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.booking-time-fields__end {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: end;
  gap: 6px;
  min-width: 0;
}

.booking-time-fields__availability {
  margin-top: 8px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--crm-text-muted, #66758c);
  overflow-wrap: anywhere;
}

.booking-time-fields__error {
  padding: 8px 10px;
  margin-bottom: 8px;
  border: 1px solid var(--crm-danger-border, #e4b2ae);
  border-radius: 6px;
  background: var(--crm-danger-surface, #fff3f2);
  color: var(--crm-danger-text, #a12b24);
  font-weight: 500;
}

.booking-time-fields__availability button,
.booking-time-fields__duration button {
  border: 0;
  padding: 0;
  background: none;
  color: inherit;
  text-decoration: underline;
  cursor: pointer;
  font: inherit;
}

.booking-time-fields__duration {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 12px;
  margin-top: 8px;
  font-size: 12px;
  color: var(--crm-text-muted, #66758c);
}

summary {
  cursor: pointer;
}

ul {
  margin: 6px 0 0;
  padding-left: 18px;
}

li + li {
  margin-top: 4px;
}
.booking-time-fields__free {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
  padding: 10px 12px;
  border: 1px solid color-mix(in srgb, #38856c 35%, var(--crm-line, #e1e6ed));
  border-radius: 8px;
  background: color-mix(in srgb, #38856c 9%, var(--crm-surface, #fff));
  color: var(--crm-text, #28735b);
  font-weight: 600;
}
</style>

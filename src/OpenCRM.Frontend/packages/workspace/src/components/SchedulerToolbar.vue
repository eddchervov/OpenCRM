<script setup lang="ts">
import { computed } from 'vue'
import { UiDatePicker, formatDateOnly, parseDateOnly } from '@opencrm/ui'

const selectedDate = defineModel<string>('date', { required: true })
const workdayStart = defineModel<string>('start', { required: true })
const workdayEnd = defineModel<string>('end', { required: true })
const rangeDays = defineModel<number>('rangeDays', { default: 7 })

const props = defineProps<{ viewMode: 'day' | 'slots' | 'list' | 'calendar'; timeZoneId?: string | null }>()
const emit = defineEmits<{ 'update:viewMode': [value: 'day' | 'slots' | 'list' | 'calendar'] }>()

const rangeEnd = computed({
  get() {
    const date = parseDateOnly(selectedDate.value)
    if (!date) return ''

    date.setDate(date.getDate() + rangeDays.value - 1)
    return formatDateOnly(date)
  },
  set(value: string) {
    if (!parseDateOnly(value)) return
    if (value < selectedDate.value) selectedDate.value = value
    rangeDays.value = getRangeDays(selectedDate.value, value)
  }
})
const rangeStart = computed({
  get: () => selectedDate.value,
  set(value: string) {
    if (!parseDateOnly(value)) return
    const end = rangeEnd.value
    selectedDate.value = value
    rangeDays.value = value > end ? 1 : getRangeDays(value, end)
  }
})

function getRangeDays(start: string, end: string) {
  return Math.round((Date.parse(`${end}T00:00:00Z`) - Date.parse(`${start}T00:00:00Z`)) / 86400000) + 1
}

function showToday() {
  selectedDate.value = formatDateOnly(undefined, props.timeZoneId || 'UTC')
  rangeDays.value = 1
}

const modes = [
  { value: 'day', title: 'Посты', icon: 'M8 2v4 M16 2v4 M3 10h18 M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Z' },
  { value: 'slots', title: 'Слоты', icon: 'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z' },
  { value: 'list', title: 'Список', icon: 'M8 6h13 M8 12h13 M8 18h13 M3 6h.01 M3 12h.01 M3 18h.01' },
  { value: 'calendar', title: 'Календарь', icon: 'M8 2v4 M16 2v4 M3 10h18 M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Z' }
] as const

function moveDate(offset: number) {
  const date = parseDateOnly(selectedDate.value)
  if (!date) {
    return
  }

  date.setDate(date.getDate() + offset)
  selectedDate.value = formatDateOnly(date)
}
</script>

<template>
  <div class="booking-toolbar">
    <div class="booking-toolbar__modes" role="group" aria-label="Вид расписания">
      <button
        v-for="mode in modes"
        :key="mode.value"
        type="button"
        :aria-pressed="viewMode === mode.value"
        @click="emit('update:viewMode', mode.value)"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true"><path :d="mode.icon" /></svg>
        {{ mode.title }}
      </button>
    </div>

    <div v-if="viewMode === 'list'" class="booking-toolbar__date booking-toolbar__interval" role="group" aria-label="Интервал записей">
      <UiDatePicker v-model="rangeStart" label="С" />
      <UiDatePicker v-model="rangeEnd" label="По" />
      <button type="button" class="booking-toolbar__today" @click="showToday">Сегодня</button>
    </div>
    <div v-else-if="viewMode !== 'calendar'" class="booking-toolbar__date">
      <button type="button" class="booking-toolbar__arrow" aria-label="Предыдущий день" @click="moveDate(-1)">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m14 6-6 6 6 6" /></svg>
      </button>
      <UiDatePicker v-model="selectedDate" label="Дата" />
      <button type="button" class="booking-toolbar__arrow" aria-label="Следующий день" @click="moveDate(1)">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m10 6 6 6-6 6" /></svg>
      </button>
      <button type="button" class="booking-toolbar__today" @click="selectedDate = formatDateOnly(undefined, props.timeZoneId || 'UTC')">Сегодня</button>
    </div>

    <div v-if="$slots.default" class="booking-toolbar__actions"><slot /></div>
  </div>
</template>

<style scoped>
.booking-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 14px 20px;
  padding-bottom: 18px;
  border-bottom: 1px solid var(--crm-line, #e5e7eb);
}
.booking-toolbar svg {
  width: 16px;
  height: 16px;
  flex: 0 0 auto;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.7;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.booking-toolbar button {
  font: inherit;
  font-size: 13px;
  cursor: pointer;
}
.booking-toolbar button:focus-visible {
  outline: 2px solid #e87532;
  outline-offset: 3px;
}
.booking-toolbar__modes {
  display: flex;
  gap: 3px;
  padding: 4px;
  border: 1px solid var(--crm-line, #dce1e7);
  border-radius: 9px;
  background: var(--crm-surface-soft, #f1f3f6);
}
.booking-toolbar__modes button {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  min-height: 34px;
  padding: 6px 12px;
  border: 1px solid transparent;
  border-radius: 6px;
  color: var(--crm-text-muted, #606b7c);
  background: transparent;
  font-weight: 600;
  white-space: nowrap;
}
.booking-toolbar__modes button:hover {
  color: var(--crm-text, #202833);
  background: var(--crm-surface, #fff);
}
.booking-toolbar__modes button[aria-pressed="true"] {
  background: var(--crm-text, #202833);
  color: var(--crm-surface, #fff);
  box-shadow: 0 1px 3px var(--crm-shadow, #0000001a);
}
.booking-toolbar__date,
.booking-toolbar__hours {
  display: flex;
  align-items: flex-end;
  gap: 6px;
  min-width: 0;
}
.booking-toolbar__date :deep(.ui-field) {
  width: 142px;
  min-width: 0;
}
.booking-toolbar__interval { align-items: center; gap: 12px; }
.booking-toolbar__interval :deep(.ui-field) { display: flex; align-items: center; gap: 8px; width: auto; }
.booking-toolbar__interval :deep(.ui-date-picker__input) { width: 142px; min-width: 0; flex: 1; }
.booking-toolbar__hours :deep(.ui-field) {
  width: 88px;
  min-width: 0;
}
.booking-toolbar :deep(.ui-field input) {
  min-height: 36px;
  height: 36px;
  padding-block: 6px;
}
.booking-toolbar__hours > span {
  display: grid;
  place-items: center;
  height: 36px;
  color: var(--crm-text-muted, #606b7c);
}
.booking-toolbar__arrow,
.booking-toolbar__today {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 36px;
  padding: 0 8px;
  border: 1px solid var(--crm-line, #dce1e7);
  border-radius: 6px;
  background: var(--crm-surface, #fff);
  color: var(--crm-text, #202833);
}
.booking-toolbar__arrow:hover,
.booking-toolbar__today:hover {
  background: var(--crm-surface-soft, #f1f3f6);
}
.booking-toolbar__actions {
  display: flex;
  align-items: center;
  min-height: 44px;
}
.booking-toolbar__actions :deep(.ui-button) {
  gap: 7px;
  white-space: nowrap;
}
.booking-toolbar .booking-toolbar__actions :deep(.ui-button.ui-button--primary) {
  background: #c94f16;
  border-color: #c94f16;
  color: #fff;
}
.booking-toolbar .booking-toolbar__actions :deep(.ui-button.ui-button--primary:hover) {
  background: #ad4010;
  border-color: #ad4010;
}
@media (max-width: 600px) {
  .booking-toolbar {
    gap: 12px;
  }
  .booking-toolbar__modes {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    min-width: 0;
    width: 100%;
  }
  .booking-toolbar__actions,
  .booking-toolbar__actions :deep(.ui-button) {
    width: 100%;
  }
  .booking-toolbar__modes button {
    flex: 1;
    padding-inline: 5px;
    gap: 5px;
  }
  .booking-toolbar__date {
    width: 100%;
    gap: 4px;
    flex-wrap: wrap;
  }
  .booking-toolbar__date :deep(.ui-field) {
    flex: 1;
    width: 0;
  }
  .booking-toolbar__interval :deep(.ui-field) {
    flex: 1 1 140px;
    width: auto;
  }
  .booking-toolbar__arrow,
  .booking-toolbar__today {
    padding-inline: 6px;
  }
}
</style>

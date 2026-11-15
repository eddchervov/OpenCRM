<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { UiButton, UiCard, UiDatePicker, UiLoader, formatDateOnly, parseDateOnly } from '@opencrm/ui'
import { WorkspacePageHelpButton } from '@opencrm/workspace'
import { loadWorkJournal, type WorkJournalData } from '@/api/workJournal'
import type { AutoServiceWorkspaceContext } from '@/api/autoserviceBff'
import MaintenanceCalendar from './MaintenanceCalendar.vue'

const props = defineProps<{
  context: AutoServiceWorkspaceContext
}>()

const today = formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')
const weekAgoDate = parseDateOnly(today)!
weekAgoDate.setDate(weekAgoDate.getDate() - 6)
const weekAgo = formatDateOnly(weekAgoDate)
const dateFrom = ref(weekAgo)
const dateTo = ref(today)
const data = ref<WorkJournalData | null>(null)
const isLoading = ref(false)
const errorMessage = ref('')
const viewMode = ref<'list' | 'calendar'>('list')
const viewModeStorageKey = computed(() => `opencrm-autoservice-journal:${props.context.organization.id}:view-mode`)

const summary = computed(() => data.value ? [
  { label: 'Выполнено', value: data.value.completedMaintenancesCount },
  { label: 'Клиентов', value: data.value.servedClientsCount }
] : [])

function formatDateTime(value: string) {
  const localMatch = value.match(/^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/)
  if (localMatch && !/(Z|[+-]\d{2}:\d{2})$/i.test(value)) {
    const date = `${localMatch[3]}.${localMatch[2]}`
    const time = `${localMatch[4]}:${localMatch[5]}`
    return time === '00:00' ? date : `${date}, ${time}`
  }

  return new Intl.DateTimeFormat('ru-RU', {
    timeZone: props.context.organization.timeZoneId || 'UTC',
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  }).format(parseDateOnly(value) ?? new Date(value))
}

function formatMoney(value?: number | null) {
  if (value === null || value === undefined) {
    return ''
  }

  return new Intl.NumberFormat('ru-RU', { style: 'currency', currency: 'RUB', maximumFractionDigits: 0 }).format(value)
}

function resolveHashUrl(url?: string | null) {
  if (!url) {
    return undefined
  }

  const hashIndex = url.indexOf('#')
  if (hashIndex >= 0) {
    return url.slice(hashIndex).replace(/^#\//, '#')
  }

  const maintenanceMatch = url.match(/\/maintenance\/([0-9a-f-]+)/i)
  if (maintenanceMatch) {
    return `#maintenance/${maintenanceMatch[1]}`
  }

  const vehicleMatch = url.match(/\/vehicle\/([0-9a-f-]+)/i)
  if (vehicleMatch) {
    return `#vehicles/${vehicleMatch[1]}`
  }

  return url
}

async function loadItems() {
  isLoading.value = true
  errorMessage.value = ''

  try {
    data.value = await loadWorkJournal(props.context.organization.id, {
      dateFrom: dateFrom.value,
      dateTo: dateTo.value,
      tab: 'works'
    })
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось загрузить историю работ.'
  } finally {
    isLoading.value = false
  }
}

function loadViewMode() {
  try {
    const value = window.localStorage?.getItem(viewModeStorageKey.value)
    if (value === 'list' || value === 'calendar') {
      viewMode.value = value
    }
  } catch {
    viewMode.value = 'list'
  }
}

function setViewMode(mode: 'list' | 'calendar') {
  viewMode.value = mode

  try {
    window.localStorage?.setItem(viewModeStorageKey.value, mode)
  } catch {
    // Недоступное хранилище не мешает переключению вида.
  }
}

onMounted(() => {
  loadViewMode()
  void loadItems()
})
watch([dateFrom, dateTo], loadItems)
</script>

<template>
  <UiCard page class="workspace-operations workspace-journal" title="История работ">
    <template #actions>
      <WorkspacePageHelpButton section-code="work-journal" title="История работ" />
    </template>
    <div class="journal-toolbar">
      <template v-if="viewMode === 'list'">
        <UiDatePicker v-model="dateFrom" label="С" />
        <UiDatePicker v-model="dateTo" label="По" />
      </template>
      <div class="scheduler-view-toggle journal-view-toggle" role="group" aria-label="Вид истории работ">
        <UiButton variant="secondary" :class="{ active: viewMode === 'list' }" :aria-pressed="viewMode === 'list'" @click="setViewMode('list')">Список</UiButton>
        <UiButton variant="secondary" :class="{ active: viewMode === 'calendar' }" :aria-pressed="viewMode === 'calendar'" @click="setViewMode('calendar')">Календарь</UiButton>
      </div>
    </div>

    <MaintenanceCalendar :time-zone-id="context.organization.timeZoneId"
      v-if="viewMode === 'calendar'"
      :calendar-url="context.links.api.calendar"
      :organization-id="context.organization.id"
      :storage-key="`opencrm-autoservice-calendar:${context.organization.id}:journal`"
      include-all-statuses
      empty-text="Нет работ на выбранный период"
    />

    <UiLoader v-else-if="isLoading" class="journal-loader" text="Загружаем историю..." />
    <div v-else-if="errorMessage" class="autoservice-state autoservice-state-error">{{ errorMessage }}</div>

    <template v-else-if="data">
      <div class="scheduler-summary">
        <span v-for="item in summary" :key="item.label"><b>{{ item.value }}</b> {{ item.label }}</span>
      </div>

      <div class="journal-list">
        <a
          v-for="item in data.workItems"
          :key="`${item.occurredAt}-${item.title}`"
          class="journal-entry"
          :href="resolveHashUrl(item.detailUrl)"
        >
          <span class="journal-entry__date">{{ formatDateTime(item.occurredAt) }}</span>
          <span class="journal-entry__work"><strong>{{ item.title }}</strong><small>{{ item.vehicleDisplayName }}<template v-if="item.clientName"> · {{ item.clientName }}</template></small></span>
          <span class="journal-entry__result">
            {{ item.resultText || item.kindName }}
            <strong v-if="item.price !== null && item.price !== undefined">{{ formatMoney(item.price) }}</strong>
          </span>
        </a>
        <div v-if="data.workItems.length === 0" class="autoservice-empty">За выбранный период выполненных работ нет.</div>
      </div>
    </template>
  </UiCard>
</template>



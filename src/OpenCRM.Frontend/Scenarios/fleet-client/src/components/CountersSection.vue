<script setup lang="ts">
import { useLocalization, formattingCulture } from '@opencrm/ui/i18n'
import '@opencrm/workspace/counterMessages'
import { WorkspaceAccessNotice } from '@opencrm/workspace'
import { computed, onMounted, reactive, ref } from 'vue'
import { UiButton, UiCard, UiDatePicker, UiLoader, UiNumberInput, UiSelect, UiTextarea, formatDateOnly } from '@opencrm/ui'
import { WorkspacePageHelpButton } from '@opencrm/workspace'
import {
  deleteCounter,
  loadCounterChart,
  loadCounterHistory,
  loadCounterVehicles,
  saveCounter,
  type FleetCounterEntry,
  type FleetCounterVehicle
} from '@/api/counters'
import type { FleetWorkspaceContext } from '@/api/fleetBff'

const { t } = useLocalization()

const props = defineProps<{
  context: FleetWorkspaceContext
}>()

const pageSize = 10
const isLoading = ref(false)
const isSaving = ref(false)
const errorMessage = ref('')
const successMessage = ref('')
const vehicles = ref<FleetCounterVehicle[]>([])
const history = ref<FleetCounterEntry[]>([])
const chartItems = ref<FleetCounterEntry[]>([])
const page = ref(1)
const totalCount = ref(0)
const vehiclesWithCountersCount = ref(0)
const selectedVehicleId = ref('')

const filters = reactive({
  sort: 'updated_desc'
})

const form = reactive({
  entryId: '',
  vehicleId: '',
  value: '',
  recordedAt: formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC'),
  comment: ''
})

const canWrite = computed(() => props.context.workspaceAccess?.isReadOnly !== true)
const readOnlyMessage = computed(() => props.context.workspaceAccess?.readOnlyMessage ?? '')
const selectedVehicle = computed(() => vehicles.value.find(vehicle => vehicle.vehicleId === selectedVehicleId.value) ?? null)
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))
const vehicleOptions = computed(() => [
  { value: '', get label() { return t('counters.chooseVehicle') } },
  ...vehicles.value.map(vehicle => ({
    value: vehicle.vehicleId,
    label: `${vehicle.vehicleDisplayName}${vehicle.vehicleSubtitle ? ` · ${vehicle.vehicleSubtitle}` : ''}`
  }))
])
const sortOptions = [
  { value: 'updated_desc', get label() { return t('counters.recent') } },
  { value: 'updated_asc', get label() { return t('counters.oldUpdates') } },
  { value: 'name_asc', get label() { return t('counters.byName') } },
  { value: 'value_desc', get label() { return t('counters.byValue') } }
]
const historySortOptions = [
  { value: 'recorded_desc', get label() { return t('counters.newest') } },
  { value: 'recorded_asc', get label() { return t('counters.oldest') } },
  { value: 'value_desc', get label() { return t('counters.byValue') } }
]
const historySort = ref('recorded_desc')

async function loadData() {
  isLoading.value = true
  errorMessage.value = ''

  try {
    const vehiclePage = await loadCounterVehicles(props.context.organization.id, {
      page: page.value,
      pageSize,
      sort: filters.sort
    })
    vehicles.value = vehiclePage.items
    totalCount.value = vehiclePage.totalCount
    vehiclesWithCountersCount.value = vehiclePage.vehiclesWithCountersCount

    if (!selectedVehicleId.value && vehicles.value.length > 0) {
      selectedVehicleId.value = vehicles.value[0].vehicleId
      form.vehicleId = selectedVehicleId.value
    }

    await loadSelectedVehicleData()
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('counters.loadError')
  } finally {
    isLoading.value = false
  }
}

async function loadSelectedVehicleData() {
  if (!selectedVehicleId.value) {
    history.value = []
    chartItems.value = []
    return
  }

  const [historyPage, chart] = await Promise.all([
    loadCounterHistory(props.context.organization.id, {
      page: 1,
      pageSize: 12,
      vehicleId: selectedVehicleId.value,
      sort: historySort.value
    }),
    loadCounterChart(props.context.organization.id, selectedVehicleId.value)
  ])
  history.value = historyPage.items
  chartItems.value = chart
}

function selectVehicle(vehicleId: string) {
  selectedVehicleId.value = vehicleId
  form.entryId = ''
  form.vehicleId = vehicleId
  form.value = ''
  form.recordedAt = formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')
  form.comment = ''
  void loadSelectedVehicleData()
}

function editEntry(entry: FleetCounterEntry) {
  form.entryId = entry.id
  form.vehicleId = entry.vehicleId
  form.value = String(entry.value)
  form.recordedAt = entry.recordedAt.slice(0, 10)
  form.comment = entry.comment ?? ''
  selectedVehicleId.value = entry.vehicleId
}

async function saveEntry() {
  if (!canWrite.value) {
    errorMessage.value = readOnlyMessage.value
    return
  }

  const parsedValue = Number(form.value)
  if (!form.vehicleId || !Number.isFinite(parsedValue) || parsedValue < 0 || !form.recordedAt) {
    errorMessage.value = t('counters.required')
    return
  }

  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''

  try {
    await saveCounter(props.context.organization.id, {
      entryId: form.entryId || null,
      vehicleId: form.vehicleId,
      value: Math.trunc(parsedValue),
      recordedAt: form.recordedAt,
      comment: form.comment || null
    })
    successMessage.value = t('counters.saved')
    form.entryId = ''
    form.value = ''
    form.comment = ''
    await loadData()
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('counters.saveError')
  } finally {
    isSaving.value = false
  }
}

async function removeEntry(entry: FleetCounterEntry) {
  if (!canWrite.value || !entry.canManage || !window.confirm(t('counters.deleteConfirm'))) {
    return
  }

  isSaving.value = true
  errorMessage.value = ''

  try {
    await deleteCounter(props.context.organization.id, entry.id)
    successMessage.value = t('counters.deleted')
    await loadData()
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('counters.deleteError')
  } finally {
    isSaving.value = false
  }
}

function goToPage(nextPage: number) {
  page.value = Math.min(Math.max(1, nextPage), totalPages.value)
  void loadData()
}

function unitLabel(unit: number) {
  return unit === 1 ? t('counters.engineHours') : t('counters.km')
}

function formatValue(value: number | null | undefined) {
  return value == null ? t('counters.noReadings') : new Intl.NumberFormat(formattingCulture.value, { maximumFractionDigits: 20 }).format(value)
}

function sourceName(entry: FleetCounterEntry) {
  if (entry.sourceType === 'manual') return t('counters.manual')
  if (entry.sourceType === 'maintenance') return t('counters.maintenance')

  return entry.sourceName
}

function formatDate(value?: string | null, calendarDate = false) {
  if (!value) {
    return '-'
  }
  const timeZone = calendarDate ? 'UTC' : props.context.organization.timeZoneId || 'UTC'
  const date = calendarDate ? new Date(`${value.slice(0, 10)}T00:00:00Z`) : new Date(value)

  return new Intl.DateTimeFormat(formattingCulture.value, { timeZone }).format(date)
}

function chartHeight(item: FleetCounterEntry) {
  const max = Math.max(...chartItems.value.map(entry => entry.value), 1)
  return `${Math.max(8, Math.round((item.value / max) * 100))}%`
}

onMounted(loadData)
</script>

<template>
  <section class="fleet-entity-section workspace-operations">
    <UiCard page :title="t('counters.title')">
      <template #actions>
        <WorkspacePageHelpButton section-code="counters" :title="t('counters.title')" />
      </template>
      <WorkspaceAccessNotice :can-manage-billing="context.organization.canManageBilling === true" v-if="readOnlyMessage" :message="readOnlyMessage" />

      <div class="fleet-toolbar">
        <UiSelect v-model="filters.sort" :label="t('counters.vehicleSort')" :options="sortOptions" :searchable="false" />
        <UiSelect v-model="historySort" :label="t('counters.history')" :options="historySortOptions" :searchable="false" />
        <UiButton variant="secondary" :disabled="isLoading" @click="loadData">{{ t('counters.refresh') }}</UiButton>
      </div>

      <div class="fleet-metrics">
        <span>{{ t('counters.vehicles') }} <strong>{{ totalCount }}</strong></span>
        <span>{{ t('counters.withReadings') }} <strong>{{ vehiclesWithCountersCount }}</strong></span>
        <span v-if="selectedVehicle">{{ t('counters.current') }} <strong>{{ formatValue(selectedVehicle.currentValue) }} {{ unitLabel(selectedVehicle.mileageUnit) }}</strong></span>
      </div>

      <div v-if="successMessage" class="fleet-state fleet-state-success">{{ successMessage }}</div>
      <div v-if="errorMessage" class="fleet-state fleet-state-error">{{ errorMessage }}</div>
      <UiLoader v-if="isLoading" :text="t('counters.loading')" />

      <div v-else class="fleet-split-grid">
        <div class="fleet-list">
          <article
            v-for="vehicle in vehicles"
            :key="vehicle.vehicleId"
            class="fleet-list-item"
            :class="{ active: selectedVehicleId === vehicle.vehicleId }"
          >
            <button type="button" class="fleet-list-item__main" @click="selectVehicle(vehicle.vehicleId)">
              <strong>{{ vehicle.vehicleDisplayName }}</strong>
              <small>{{ vehicle.vehicleSubtitle }}</small>
              <span>
                {{ formatValue(vehicle.currentValue) }}
                <template v-if="vehicle.currentValue !== null && vehicle.currentValue !== undefined">{{ unitLabel(vehicle.mileageUnit) }}</template>
                <template v-if="vehicle.updatedAt"> · {{ formatDate(vehicle.updatedAt) }}</template>
              </span>
            </button>
          </article>

          <div v-if="vehicles.length === 0" class="fleet-empty">{{ t('counters.noVehicles') }}</div>

          <div class="fleet-pagination">
            <UiButton variant="secondary" :disabled="page <= 1 || isLoading" @click="goToPage(page - 1)">{{ t('counters.back') }}</UiButton>
            <span>{{ page }} / {{ totalPages }}</span>
            <UiButton variant="secondary" :disabled="page >= totalPages || isLoading" @click="goToPage(page + 1)">{{ t('counters.next') }}</UiButton>
          </div>
        </div>

        <aside class="fleet-detail-panel">
          <form class="fleet-form-grid" @submit.prevent="saveEntry">
            <UiSelect v-model="form.vehicleId" class="fleet-form-wide" :label="t('counters.vehicle')" :options="vehicleOptions" required :disabled="isSaving || !canWrite" />
            <UiNumberInput v-model="form.value" :label="t('counters.reading')" integer :disabled="isSaving || !canWrite" required />
            <UiDatePicker v-model="form.recordedAt" :label="t('counters.date')" :disabled="isSaving || !canWrite" required />
            <UiTextarea v-model="form.comment" class="fleet-form-wide" :label="t('counters.comment')" :rows="3" :disabled="isSaving || !canWrite" />
            <div class="fleet-form-actions">
              <UiButton type="submit" :disabled="isSaving || !canWrite">{{ form.entryId ? t('counters.save') : t('counters.add') }}</UiButton>
              <UiButton type="button" variant="secondary" :disabled="isSaving" @click="form.entryId = ''; form.value = ''; form.comment = ''">{{ t('counters.reset') }}</UiButton>
            </div>
          </form>

          <section class="fleet-subsection">
            <h4>{{ t('counters.trend') }}</h4>
            <div v-if="chartItems.length > 0" class="fleet-counter-chart">
              <span
                v-for="item in chartItems.slice().reverse().slice(-24)"
                :key="`${item.id}-${item.timelineType}`"
                :style="{ height: chartHeight(item) }"
                :title="`${formatDate(item.recordedAt, true)} · ${formatValue(item.value)} ${unitLabel(item.mileageUnit)}`"
              ></span>
            </div>
            <div v-else class="fleet-empty">{{ t('counters.chartHint') }}</div>
          </section>

          <section class="fleet-subsection">
            <h4>{{ t('counters.history') }}</h4>
            <div v-for="entry in history" :key="`${entry.id}-${entry.timelineType}`" class="fleet-history-row">
              <strong>{{ formatValue(entry.value) }} {{ unitLabel(entry.mileageUnit) }}</strong>
              <span>{{ formatDate(entry.recordedAt, true) }} · {{ sourceName(entry) }}</span>
              <small>{{ entry.comment }}</small>
              <div v-if="entry.canManage" class="fleet-row-actions">
                <UiButton variant="ghost" :disabled="isSaving || !canWrite" @click="editEntry(entry)">{{ t('counters.edit') }}</UiButton>
                <UiButton variant="ghost" :disabled="isSaving || !canWrite" @click="removeEntry(entry)">{{ t('counters.delete') }}</UiButton>
              </div>
            </div>
            <div v-if="history.length === 0" class="fleet-empty">{{ t('counters.emptyHistory') }}</div>
          </section>
        </aside>
      </div>
    </UiCard>
  </section>
</template>

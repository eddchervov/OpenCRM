<script setup lang="ts">
import { formattingCulture } from '@opencrm/ui/i18n'
import { useWorkspaceCurrency } from '../workspaceCurrency'
import { useLocalization } from '@opencrm/ui'
import '../schedulerControlMessages'

import { computed, onMounted, ref } from 'vue'
import { loadVehicleRegistrationPlateFormats, matchesRegistrationPlateSearch, type VehicleRegistrationPlateFormats } from '../vehicleRegistrationPlate'
import { UiSelect } from '@opencrm/ui'
import MaintenanceWorkflowActions from './MaintenanceWorkflowActions.vue'
import { getVehicleBrandLogo } from '../utils/vehicleBrandLogo'

const workspaceCurrency = useWorkspaceCurrency()
const { t } = useLocalization()

const plateFormats = ref<VehicleRegistrationPlateFormats | null>(null)
onMounted(async () => { plateFormats.value = await loadVehicleRegistrationPlateFormats().catch(() => null) })

interface SchedulerListItem {
  id: string
  vehicleId: string
  date: string
  vehicleDisplayName: string
  vehicleSubtitle?: string | null
  registrationPlate?: string | null
  brandLogoKey?: string | null
  serviceNames?: string[]
  employeeNames?: string[]
  typeName?: string | null
  status: number
  statusName?: string | null
  readyForHandoverAt?: string | null
  handedOverAt?: string | null
  plannedStartTime?: string | null
  plannedEndDate?: string | null
  plannedEndTime?: string | null
  location?: string | null
  price?: number | null
  currencyCode?: string | null
  detailUrl?: string | null
}

const props = defineProps<{
  items: SchedulerListItem[]
  organizationId: string
  canWrite: boolean
  simpleWorkflow?: boolean
}>()

const emit = defineEmits<{
  changed: [item: SchedulerListItem, status: number]
  error: [message: string]
}>()

const search = ref('')
const selectedLocation = ref('')
const selectedStatus = ref('')
function formatMoney(value: number, currencyCode = workspaceCurrency.value) {
  return new Intl.NumberFormat(formattingCulture.value, { style: 'currency', currency: currencyCode, maximumFractionDigits: 2 }).format(value)
}
const dateFormatter = computed(() => new Intl.DateTimeFormat(formattingCulture.value, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }))
const normalizedQuery = computed(() => search.value.trim().toLocaleLowerCase(formattingCulture.value))
const noLocationValue = '__no-location__'
const locationValue = (item: SchedulerListItem) => item.location?.trim() || noLocationValue
const locationLabel = (location: string) => location === noLocationValue ? t('schedulerControl.noPost') : location
const locations = computed(() => [...new Set(props.items.map(locationValue))].sort((left, right) => locationLabel(left).localeCompare(locationLabel(right), formattingCulture.value)))
const statuses = computed(() => {
  const options = new Map<string, { value: string, label: string, status: number }>()
  for (const item of props.items) {
    const value = statusFilterValue(item)
    if (!options.has(value)) options.set(value, { value, label: statusText(item), status: item.status })
  }

  return [...options.values()].sort((left, right) => left.status - right.status || left.label.localeCompare(right.label, formattingCulture.value))
})
const locationOptions = computed(() => [{ value: '', label: t('schedulerControl.allLocations') }, ...locations.value.map(location => ({ value: location, label: locationLabel(location) }))])
const statusOptions = computed(() => [{ value: '', label: t('schedulerControl.allStatuses') }, ...statuses.value])
const filteredItems = computed(() => props.items
  .filter(item => !selectedLocation.value || locationValue(item) === selectedLocation.value)
  .filter(item => !selectedStatus.value || statusFilterValue(item) === selectedStatus.value)
  .filter(matchesSearch)
  .sort((left, right) => left.date.slice(0, 10).localeCompare(right.date.slice(0, 10))
    || (left.plannedStartTime || '').localeCompare(right.plannedStartTime || '')
    || left.vehicleDisplayName.localeCompare(right.vehicleDisplayName, formattingCulture.value)))
const dateGroups = computed(() => {
  const groups = new Map<string, SchedulerListItem[]>()
  for (const item of filteredItems.value) {
    const date = item.date.slice(0, 10)
    const items = groups.get(date) ?? []
    items.push(item)
    groups.set(date, items)
  }

  return [...groups].map(([date, items]) => ({ date, label: formatDate(date), items }))
})

function formatDate(value: string) {
  const [year, month, day] = value.split('-').map(Number)
  if (!year || !month || !day) return value
  return dateFormatter.value.format(new Date(year, month - 1, day))
}

function matchesSearch(item: SchedulerListItem) {
  if (!normalizedQuery.value) return true
  const searchable = [
    item.vehicleDisplayName,
    item.vehicleSubtitle,
    item.registrationPlate,
    item.typeName,
    item.location,
    ...(item.serviceNames ?? []),
    ...(item.employeeNames ?? [])
  ].join(' ').toLocaleLowerCase(formattingCulture.value)

  return searchable.includes(normalizedQuery.value)
    || matchesRegistrationPlateSearch(item.registrationPlate, normalizedQuery.value, plateFormats.value)
}

function services(item: SchedulerListItem) {
  return item.serviceNames?.length ? item.serviceNames : item.typeName ? [item.typeName] : []
}

function statusClass(item: SchedulerListItem) {
  if (item.handedOverAt || item.readyForHandoverAt || item.status === 3) return 'schedule-list__status--green'
  if (item.status === 2) return 'schedule-list__status--orange'
  if (item.status === 4) return 'schedule-list__status--muted'
  return 'schedule-list__status--blue'
}

function statusText(item: SchedulerListItem) {
  if (item.handedOverAt) return t('schedulerControl.handedOver')
  if (item.readyForHandoverAt) return t('schedulerControl.ready')
  const names: Record<number, string> = {
    1: t('schedulerControl.scheduled'), 2: t('schedulerControl.inProgress'), 3: t('schedulerControl.completed'), 4: t('schedulerControl.cancelled')
  }

  return names[item.status] ?? t('schedulerControl.statusNotSpecified')
}

function statusFilterValue(item: SchedulerListItem) {
  if (item.handedOverAt) return 'handedOver'
  if (item.readyForHandoverAt) return 'ready'
  return String(item.status)
}

function timeText(item: SchedulerListItem) {
  const start = item.plannedStartTime?.slice(0, 5)
  const end = item.plannedEndTime?.slice(0, 5)
  if (!start) return t('schedulerControl.noTime')
  if (!end) return start
  if (item.plannedEndDate && item.plannedEndDate.slice(0, 10) !== item.date.slice(0, 10)) return `${start} — ${formatDate(item.plannedEndDate.slice(0, 10))}, ${end}`
  return `${start}–${end}`
}

function brandLogo(item: SchedulerListItem) {
  return getVehicleBrandLogo(item.brandLogoKey)
}
</script>

<template>
  <section class="schedule-list" :aria-label="t('schedulerControl.listLabel')">
    <div class="schedule-list__toolbar">
      <label class="schedule-list__search">
        <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="10.8" cy="10.8" r="6.8" /><path d="m16 16 5 5" /></svg>
        <span class="visually-hidden">{{ t('schedulerControl.searchLabel') }}</span>
        <input v-model="search" type="search" :placeholder="t('schedulerControl.searchPlaceholder')">
      </label>
      <UiSelect v-model="selectedLocation" class="schedule-list__filter" :options="locationOptions" :searchable="false" :aria-label="t('schedulerControl.filterLocation')" />
      <UiSelect v-model="selectedStatus" class="schedule-list__filter" :options="statusOptions" :searchable="false" :aria-label="t('schedulerControl.filterStatus')" />
      <span class="schedule-list__count">{{ t('schedulerControl.rowsCount', { count: filteredItems.length }) }}</span>
    </div>

    <p v-if="!filteredItems.length" class="schedule-list__empty">
      {{ t(items.length ? 'schedulerControl.emptyFiltered' : 'schedulerControl.emptyList') }}
    </p>

    <section v-for="group in dateGroups" :key="group.date" class="schedule-list__group" :aria-label="group.label">
      <header class="schedule-list__group-heading ui-section-header">
        <h3>{{ group.label }}</h3>
        <span>{{ group.items.length }}</span>
      </header>
      <div class="schedule-list__table-wrap">
        <table class="schedule-list__table">
          <thead>
            <tr>
              <th scope="col">{{ t('schedulerControl.time') }}</th>
              <th scope="col">{{ t('schedulerControl.vehicle') }}</th>
              <th scope="col">{{ t('schedulerControl.work') }}</th>
              <th scope="col">{{ t('schedulerControl.locationAssignee') }}</th>
              <th scope="col">{{ t('schedulerControl.status') }}</th>
              <th scope="col" class="schedule-list__amount">{{ t('schedulerControl.cost') }}</th>
              <th v-if="canWrite" scope="col" class="schedule-list__actions-heading"><span class="visually-hidden">{{ t('schedulerControl.actions') }}</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in group.items" :key="item.id">
              <td class="schedule-list__time" :data-label="t('schedulerControl.time')">
                <strong>{{ timeText(item) }}</strong>
              </td>
              <th scope="row" class="schedule-list__vehicle" :data-label="t('schedulerControl.vehicle')">
                <a :href="item.detailUrl || `#maintenance/${item.id}`">
                  <img v-if="brandLogo(item)" :src="brandLogo(item)!.src" :alt="t('schedulerControl.logoAlt', { name: brandLogo(item)!.name })">
                  <span class="schedule-list__vehicle-info">
                    <strong>{{ item.vehicleDisplayName }}</strong>
                    <span v-if="item.registrationPlate">{{ item.registrationPlate }}</span>
                    <span v-if="item.vehicleSubtitle">{{ item.vehicleSubtitle }}</span>
                  </span>
                </a>
              </th>
              <td class="schedule-list__services" :data-label="t('schedulerControl.work')">
                <span v-for="(service, index) in services(item)" :key="`${item.id}-${index}`">{{ service }}</span>
                <span v-if="!services(item).length" class="schedule-list__muted">{{ t('schedulerControl.noWorks') }}</span>
              </td>
              <td class="schedule-list__assignment" :data-label="t('schedulerControl.locationAssignee')">
                <strong>{{ locationLabel(locationValue(item)) }}</strong>
                <span v-if="item.employeeNames?.length">{{ item.employeeNames.join(', ') }}</span>
                <span v-else class="schedule-list__muted">{{ t('schedulerControl.unassigned') }}</span>
              </td>
              <td class="schedule-list__state" :data-label="t('schedulerControl.status')">
                <span class="schedule-list__status" :class="statusClass(item)">{{ statusText(item) }}</span>
              </td>
              <td class="schedule-list__amount" :data-label="t('schedulerControl.cost')">{{ item.price == null ? '—' : formatMoney(item.price, item.currencyCode ?? workspaceCurrency) }}</td>
              <td v-if="canWrite" class="schedule-list__actions" :data-label="t('schedulerControl.actions')">
                <MaintenanceWorkflowActions :organization-id="organizationId" :maintenance-id="item.id" :status="item.status" :simple-workflow="simpleWorkflow" compact
                  @changed="status => emit('changed', item, status)" @error="message => emit('error', message)" />
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </section>
</template>

<style scoped>
.schedule-list {
  container-type: inline-size;
  min-width: 0;
  margin-top: 16px;
  color: var(--crm-text, #202733);
}

.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

.schedule-list__toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 16px;
}

.schedule-list__search {
  display: flex;
  align-items: center;
  min-width: 0;
  height: 38px;
  border: 1px solid var(--crm-line, #e3e6eb);
  border-radius: 8px;
  background: var(--crm-surface, #fff);
}

.schedule-list__search {
  flex: 1 1 280px;
  gap: 8px;
  padding: 0 10px;
}

.schedule-list__search:focus-within {
  border-color: var(--crm-accent, #c45112);
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--crm-accent, #c45112) 18%, transparent);
}

.schedule-list__search svg {
  width: 17px;
  height: 17px;
  flex: 0 0 auto;
  fill: none;
  stroke: var(--crm-text-muted, #667085);
  stroke-width: 1.8;
}

.schedule-list__search input {
  width: 100%;
  min-width: 0;
  height: 100%;
  border: 0;
  outline: 0;
  background: transparent;
  color: var(--crm-text, #202733);
  font: inherit;
  font-size: 13px;
}

.schedule-list__filter {
  flex: 0 1 180px;
  min-width: 0;
  margin: 0;
}

.schedule-list__filter :deep(.ui-select) {
  --ms-min-height: 38px;
  --ms-font-size: 13px;
  --ms-radius: 8px;
}

.schedule-list__count {
  flex: 0 0 auto;
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
  white-space: nowrap;
}

.schedule-list__group {
  min-width: 0;
  margin-top: 18px;
}

.schedule-list__group-heading {
  display: flex;
  align-items: center;
  gap: 9px;
  margin-bottom: 8px;
}

.schedule-list__group-heading h3 {
  margin: 0;
  color: var(--crm-text, #202733);
  font-size: 14px;
  font-weight: 700;
  text-transform: capitalize;
}

.schedule-list__group-heading span {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 21px;
  height: 21px;
  padding: 0 6px;
  border-radius: 11px;
  background: var(--crm-surface-soft, #f4f6f8);
  color: var(--crm-text-muted, #667085);
  font-size: 11px;
}

.schedule-list__table-wrap {
  overflow: auto hidden;
  border: 1px solid var(--crm-line, #e3e6eb);
  border-radius: 9px;
}

.schedule-list__table {
  width: 100%;
  border-collapse: collapse;
  color: var(--crm-text, #202733);
  font-size: 12px;
  line-height: 1.4;
}

.schedule-list__table th, .schedule-list__table td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--crm-line, #e3e6eb);
  text-align: left;
  vertical-align: middle;
}

.schedule-list__table thead {
  background: var(--crm-surface-soft, #f4f6f8);
  color: var(--crm-text-muted, #667085);
}

.schedule-list__table thead th {
  font-size: 11px;
  font-weight: 600;
  white-space: nowrap;
}

.schedule-list__table tbody tr:last-child > * {
  border-bottom: 0;
}

.schedule-list__table tbody tr:hover, .schedule-list__table tbody tr:focus-within {
  background: var(--crm-surface-soft, #f4f6f8);
}

.schedule-list__time {
  min-width: 105px;
  color: var(--crm-text, #202733);
  font-variant-numeric: tabular-nums;
  font-weight: 700;
  white-space: nowrap;
}

.schedule-list__vehicle {
  min-width: 180px;
  font-weight: 400;
}

.schedule-list__vehicle a {
  display: flex;
  align-items: center;
  gap: 9px;
  color: inherit;
  text-decoration: none;
}

.schedule-list__vehicle a:hover strong {
  color: var(--crm-accent, #c45112);
  text-decoration: underline;
}

.schedule-list__vehicle a:focus-visible {
  outline: 2px solid var(--crm-accent, #c45112);
  outline-offset: 3px;
  border-radius: 4px;
}

.schedule-list__vehicle img {
  width: 28px;
  height: 28px;
  padding: 2px;
  border-radius: 50%;
  background: #fff;
  object-fit: contain;
  flex: 0 0 auto;
}

.schedule-list__vehicle-info {
  display: grid;
  gap: 3px;
  min-width: 0;
}

.schedule-list__services, .schedule-list__assignment {
  min-width: 130px;
}

.schedule-list__vehicle-info strong, .schedule-list__assignment strong {
  font-size: 12px;
  font-weight: 650;
}

.schedule-list__vehicle-info span, .schedule-list__assignment span, .schedule-list__services span {
  color: var(--crm-text-muted, #667085);
  overflow-wrap: anywhere;
}

.schedule-list__services span:not(:first-child) {
  font-size: 11px;
}

.schedule-list__services span, .schedule-list__assignment > * {
  display: block;
}

.schedule-list__muted {
  color: var(--crm-text-muted, #667085);
}

.schedule-list__status {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 8px;
  border: 1px solid color-mix(in srgb, var(--schedule-status-color) 60%, var(--crm-line, #e3e6eb));
  border-radius: 999px;
  background: color-mix(in srgb, var(--schedule-status-color) 9%, var(--crm-surface, #fff));
  color: var(--crm-text, #202733);
  font-size: 11px;
  white-space: nowrap;
}

.schedule-list__status::before {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--schedule-status-color);
  content: '';
}

.schedule-list__status--blue { --schedule-status-color: #4779ae; }
.schedule-list__status--orange { --schedule-status-color: #bd5a20; }
.schedule-list__status--green { --schedule-status-color: #38a58c; }
.schedule-list__status--muted { --schedule-status-color: #8b929e; color: var(--crm-text-muted, #667085); background: var(--crm-surface-soft, #f4f6f8); }

.schedule-list__table .schedule-list__amount {
  text-align: right;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.schedule-list__actions-heading, .schedule-list__actions {
  width: 1%;
  white-space: nowrap;
}

.schedule-list__actions :deep(.maintenance-workflow-actions) {
  display: flex;
  gap: 6px;
  flex-wrap: nowrap;
}

.schedule-list__actions :deep(.ui-button) {
  min-height: 30px;
  padding: 6px 9px;
  font-size: 11px;
}

.schedule-list__empty {
  margin: 0;
  padding: 22px 14px;
  border: 1px dashed var(--crm-line, #e3e6eb);
  border-radius: 8px;
  color: var(--crm-text-muted, #667085);
  text-align: center;
  font-size: 13px;
}

@container (max-width: 900px) {
  .schedule-list__table, .schedule-list__table tbody {
    display: block;
  }

  .schedule-list__table thead { display: none; }

  .schedule-list__table tbody {
    padding: 4px;
  }

  .schedule-list__table tbody tr {
    display: grid;
    grid-template-columns: minmax(65px, .55fr) minmax(0, 1fr) minmax(0, .9fr) auto;
    gap: 10px 12px;
    align-items: center;
    padding: 10px;
    border-bottom: 1px solid var(--crm-line, #e3e6eb);
    border-radius: 7px;
  }

  .schedule-list__table tbody tr:last-child { border-bottom: 0; }

  .schedule-list__table td, .schedule-list__table th {
    min-width: 0;
    padding: 0;
    border: 0;
  }

  .schedule-list__table td::before {
    display: block;
    margin-bottom: 3px;
    color: var(--crm-text-muted, #667085);
    content: attr(data-label);
    font-size: 10px;
    font-weight: 500;
  }

  .schedule-list__time { grid-column: 1; grid-row: 1; min-width: 0; white-space: normal; }
  .schedule-list__vehicle { grid-column: 2; grid-row: 1 / span 2; }
  .schedule-list__services { grid-column: 3; grid-row: 1 / span 2; }
  .schedule-list__assignment { grid-column: 1 / span 2; grid-row: 3; }
  .schedule-list__state { grid-column: 3; grid-row: 3; }
  .schedule-list__amount { grid-column: 4; grid-row: 1; }
  .schedule-list__actions { grid-column: 4; grid-row: 2 / span 2; }
}

@container (max-width: 600px) {
  .schedule-list__toolbar { flex-wrap: wrap; }
  .schedule-list__search { flex: 1 1 100%; }
  .schedule-list__filter { flex: 1 1 calc(50% - 6px); }
  .schedule-list__count { margin-left: auto; }

  .schedule-list__table tbody tr {
    grid-template-columns: minmax(0, 1fr) auto;
    gap: 10px 12px;
    padding: 11px 9px;
  }

  .schedule-list__time { grid-column: 1; grid-row: 1; }
  .schedule-list__vehicle { grid-column: 1 / -1; grid-row: 2; }
  .schedule-list__services { grid-column: 1 / -1; grid-row: 3; }
  .schedule-list__assignment { grid-column: 1 / -1; grid-row: 4; }
  .schedule-list__state { grid-column: 2; grid-row: 1; }
  .schedule-list__amount { grid-column: 1; grid-row: 5; text-align: left !important; }
  .schedule-list__actions { grid-column: 2; grid-row: 5; }
  .schedule-list__vehicle img { width: 26px; height: 26px; }
}
</style>

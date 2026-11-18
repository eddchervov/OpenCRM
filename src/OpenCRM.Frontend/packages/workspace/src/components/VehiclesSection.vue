<script setup lang="ts">
import { getVehicleDisplayName } from '../api/vehicles'
import WorkspaceAccessNotice from './WorkspaceAccessNotice.vue'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton, UiCard, UiLoader, UiPagination, UiSearchInput, UiSelect } from '@opencrm/ui'
import { useLocalization } from '@opencrm/ui'
import { formattingCulture } from '@opencrm/ui/i18n'
import '../vehicleOverviewMessages'
import {
  loadVehiclePhotos,
  loadVehiclesPage,
  type WorkspaceVehicle,
  type WorkspaceVehicleBrandFilter
} from '../api/vehicles'
import type { WorkspaceContext } from '../types'
import WorkspacePageHelpButton from './WorkspacePageHelpButton.vue'
import { getVehicleBrandLogo } from '../utils/vehicleBrandLogo'

const props = defineProps<{
  context: WorkspaceContext
}>()
const { t } = useLocalization()

const vehicles = ref<WorkspaceVehicle[]>([])
const brandFilters = ref<WorkspaceVehicleBrandFilter[]>([])
const kindValues = ref<number[]>([])
const photosByVehicleId = ref<Record<string, string>>({})
const page = ref(1)
const pageSize = ref(10)
const selectedPageSize = ref('10')
const totalCount = ref(0)
const allCount = ref(0)
const searchQuery = ref('')
const selectedBrandFilter = ref('all')
const selectedKind = ref('all')
const isArchiveMode = ref(false)
const viewMode = ref<'cards' | 'list'>('list')
const isLoading = ref(false)
const hasLoaded = ref(false)
const canViewMaintenance = ref(false)
const errorMessage = ref('')
const failedPhotos = ref<Record<string, boolean>>({})
let searchTimer: number | undefined
let latestRequest = 0
let isStateRestored = false

const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / Math.max(1, pageSize.value))))
const canWrite = computed(() => props.context.workspaceAccess?.isReadOnly !== true)
const canCreateVehicle = computed(() => canWrite.value && props.context.shell.quickActions?.some(action =>
  action.code === 'create-vehicle' && action.isVisible
) === true)
const readOnlyMessage = computed(() => props.context.workspaceAccess?.readOnlyMessage ?? '')
const billingHref = computed(() => '#billing')
const createVehicleUrl = computed(() => '#vehicles/new')
const vehiclesTitle = computed(() => props.context.shell.scenario?.terms?.vehicles ?? t('vehicleOverview.vehiclesTitle'))
const listStateStorageKey = computed(() => `opencrm-workspace:${props.context.user.id}:${props.context.organization.id}:vehicles:list-state:v1`)
const hasActiveFilters = computed(() => Boolean(
  searchQuery.value ||
  selectedBrandFilter.value !== 'all' ||
  selectedKind.value !== 'all' ||
  isArchiveMode.value
))
const brandFilterOptions = computed(() => [
  { value: 'all', label: t('vehicleOverview.allVehicles') },
  ...brandFilters.value.map(brand => ({ value: brand.id, label: brand.name }))
])
const kindFilterOptions = computed(() => [
  { value: 'all', label: t('vehicleOverview.allKinds') },
  ...kindValues.value.map(kind => ({ value: String(kind), label: getVehicleKindName(kind) }))
])
const pageSizeOptions = [
  { value: '10', label: '10' },
  { value: '25', label: '25' },
  { value: '50', label: '50' }
]
const paginationInfo = computed(() => {
  if (totalCount.value === 0) {
    return t('vehicleOverview.noRecords')
  }

  const from = (page.value - 1) * pageSize.value + 1
  const to = Math.min(page.value * pageSize.value, totalCount.value)
  return t('vehicleOverview.range', { from, to, count: totalCount.value })
})

function getVehicleKindName(kind: number) {
  const names: Record<number, string> = {
    1: t('vehicleOverview.kind1'),
    2: t('vehicleOverview.kind2'),
    3: t('vehicleOverview.kind3'),
    4: t('vehicleOverview.kind4'),
    5: t('vehicleOverview.kind5'),
    6: t('vehicleOverview.kind6'),
    7: t('vehicleOverview.kind7'),
    8: t('vehicleOverview.kind8'),
    9: t('vehicleOverview.kind9'),
    10: t('vehicleOverview.kind10'),
    99: t('vehicleOverview.kind99')
  }

  return names[kind] ?? t('vehicleOverview.otherVehicle')
}

function getVehicleTitle(vehicle: WorkspaceVehicle) {
  return [getVehicleDisplayName(vehicle), vehicle.yearIssue].filter(Boolean).join(' ')
}

function getVehicleName(vehicle: WorkspaceVehicle) {
  return vehicle.organizationClientName || ''
}

function getVehicleMeta(vehicle: WorkspaceVehicle) {
  return [
    vehicle.registrationPlate ? t('vehicleOverview.plate', { plate: vehicle.registrationPlate }) : '',
    vehicle.vin ? t('vehicleOverview.vin', { vin: vehicle.vin }) : ''
  ].filter(Boolean)
}

function getPhotoUrl(vehicle: WorkspaceVehicle) {
  if (failedPhotos.value[vehicle.id]) {
    return ''
  }

  return photosByVehicleId.value[vehicle.id] || vehicle.photoUrl || ''
}

function getBrandLogo(vehicle: WorkspaceVehicle) {
  return getVehicleBrandLogo(vehicle.brandLogoKey)
}

function onPhotoError(vehicle: WorkspaceVehicle) {
  failedPhotos.value = { ...failedPhotos.value, [vehicle.id]: true }
}

function getVehicleUrl(vehicle: WorkspaceVehicle) {
  return `#vehicles/${vehicle.id}`
}

function formatDate(value?: string | null) {
  if (!value) {
    return t('vehicleOverview.notMaintained')
  }

  const dateOnlyMatch = value.match(/^(\d{4})-(\d{2})-(\d{2})/)
  if (dateOnlyMatch) {
    return new Intl.DateTimeFormat(formattingCulture.value, { dateStyle: 'short', timeZone: 'UTC' }).format(new Date(`${dateOnlyMatch[1]}-${dateOnlyMatch[2]}-${dateOnlyMatch[3]}T00:00:00Z`))
  }

  return new Intl.DateTimeFormat(formattingCulture.value, {
    day: '2-digit',
    month: 'short',
    year: 'numeric'
  }).format(new Date(value))
}

function getCurrentMaintenanceStatus(status?: number | null) {
  if (status === 3) {
    return t('vehicleOverview.readyForHandover')
  }

  if (status === 2) {
    return t('vehicleOverview.inProgress')
  }

  if (status === 1) {
    return t('vehicleOverview.planned')
  }

  return t('vehicleOverview.noActiveWork')
}

function restoreListState() {
  isStateRestored = false
  searchQuery.value = ''
  selectedBrandFilter.value = 'all'
  selectedKind.value = 'all'
  isArchiveMode.value = false
  page.value = 1
  pageSize.value = 10
  selectedPageSize.value = '10'
  viewMode.value = 'list'

  try {
    const savedState = window.localStorage?.getItem(listStateStorageKey.value)
    if (!savedState) {
      return
    }

    const state = JSON.parse(savedState) as {
      searchQuery?: string
      selectedBrandFilter?: string
      selectedKind?: string
      isArchiveMode?: boolean
      viewMode?: 'cards' | 'list'
      page?: number
      pageSize?: number
    }

    searchQuery.value = state.searchQuery ?? ''
    selectedBrandFilter.value = state.selectedBrandFilter ?? 'all'
    selectedKind.value = state.selectedKind ?? 'all'
    isArchiveMode.value = state.isArchiveMode === true
    viewMode.value = state.viewMode === 'cards' ? 'cards' : 'list'
    pageSize.value = [10, 25, 50].includes(Number(state.pageSize)) ? Number(state.pageSize) : 10
    selectedPageSize.value = String(pageSize.value)
    page.value = Math.max(1, Number(state.page) || 1)
  } catch {
    // Список работает и без сохранения состояния в браузере.
  } finally {
    isStateRestored = true
  }
}

function saveListState() {
  if (!isStateRestored) {
    return
  }

  try {
    window.localStorage?.setItem(listStateStorageKey.value, JSON.stringify({
      searchQuery: searchQuery.value,
      selectedBrandFilter: selectedBrandFilter.value,
      selectedKind: selectedKind.value,
      isArchiveMode: isArchiveMode.value,
      viewMode: viewMode.value,
      page: page.value,
      pageSize: pageSize.value
    }))
  } catch {
    // Список работает и без сохранения состояния в браузере.
  }
}

async function loadVehicles() {
  const request = ++latestRequest
  const organizationId = props.context.organization.id
  const stateStorageKey = listStateStorageKey.value
  isLoading.value = true
  errorMessage.value = ''
  photosByVehicleId.value = {}

  try {
    const data = await loadVehiclesPage(organizationId, page.value, pageSize.value, {
      isArchived: isArchiveMode.value,
      brandFilter: selectedBrandFilter.value,
      kind: selectedKind.value,
      searchQuery: searchQuery.value.trim()
    })

    if (request !== latestRequest || organizationId !== props.context.organization.id || stateStorageKey !== listStateStorageKey.value) {
      return
    }

    vehicles.value = data.items
    brandFilters.value = data.userBrands
    kindValues.value = data.userKinds
    page.value = data.page
    pageSize.value = data.pageSize
    totalCount.value = data.totalCount
    allCount.value = data.allCount
    hasLoaded.value = true
    canViewMaintenance.value = data.canViewMaintenance === true

    failedPhotos.value = {}
    try {
      const photos = await loadVehiclePhotos(organizationId, data.items.map(vehicle => vehicle.id))
      if (request === latestRequest && organizationId === props.context.organization.id && stateStorageKey === listStateStorageKey.value) {
        photosByVehicleId.value = Object.fromEntries(
          Object.entries(photos).map(([vehicleId, vehiclePhotos]) => [vehicleId, vehiclePhotos[0]?.url ?? ''])
        )
      }
    } catch {
      if (request === latestRequest && stateStorageKey === listStateStorageKey.value) {
        photosByVehicleId.value = {}
      }
    }
  } catch (error) {
    if (request === latestRequest && stateStorageKey === listStateStorageKey.value) {
      errorMessage.value = error instanceof Error ? error.message : t('vehicleOverview.loadVehiclesError')
    }
  } finally {
    if (request === latestRequest && stateStorageKey === listStateStorageKey.value) {
      isLoading.value = false
    }
  }
}

function resetFilters() {
  searchQuery.value = ''
  selectedBrandFilter.value = 'all'
  selectedKind.value = 'all'
  isArchiveMode.value = false
  page.value = 1
  void loadVehicles()
}

function changePage(nextPage: number) {
  page.value = Math.min(Math.max(1, nextPage), totalPages.value)
  void loadVehicles()
}

function setViewMode(mode: 'cards' | 'list') {
  viewMode.value = mode
  saveViewMode()
}

function saveViewMode() {
  saveListState()
}

function toggleArchiveMode() {
  isArchiveMode.value = !isArchiveMode.value
  page.value = 1
  void loadVehicles()
}

watch(searchQuery, () => {
  window.clearTimeout(searchTimer)
  if (!isStateRestored) {
    return
  }

  searchTimer = window.setTimeout(() => {
    page.value = 1
    void loadVehicles()
  }, 350)
}, { flush: 'sync' })

watch([selectedBrandFilter, selectedKind], () => {
  if (!isStateRestored) {
    return
  }

  page.value = 1
  void loadVehicles()
}, { flush: 'sync' })

watch(selectedPageSize, () => {
  if (!isStateRestored) {
    return
  }

  pageSize.value = Number(selectedPageSize.value) || 10
  page.value = 1
  void loadVehicles()
}, { flush: 'sync' })

watch([searchQuery, selectedBrandFilter, selectedKind, isArchiveMode, page, pageSize, viewMode], saveListState, { flush: 'sync' })

watch(listStateStorageKey, () => {
  latestRequest++
  window.clearTimeout(searchTimer)
  isStateRestored = false
  vehicles.value = []
  photosByVehicleId.value = {}
  failedPhotos.value = {}
  restoreListState()
  void loadVehicles()
})

onMounted(() => {
  restoreListState()
  void loadVehicles()
})

onBeforeUnmount(() => {
  window.clearTimeout(searchTimer)
  latestRequest++
})
</script>

<template>
  <section class="vehicles-section">
    <UiCard page :title="vehiclesTitle" class="vehicles-list-card">
      <template #actions>
        <UiButton v-if="canCreateVehicle" class="directory-create" :href="createVehicleUrl">＋ {{ t('vehicleOverview.addVehicle') }}</UiButton>
        <WorkspacePageHelpButton section-code="vehicles" :title="vehiclesTitle" />
      </template>

      <WorkspaceAccessNotice :can-manage-billing="context.organization.canManageBilling === true" v-if="readOnlyMessage" :message="readOnlyMessage" :href="billingHref" />

      <div class="vehicles-filters">
        <UiSearchInput v-model="searchQuery" :placeholder="t('vehicleOverview.searchPlaceholder')" />
        <UiSelect v-model="selectedBrandFilter" :options="brandFilterOptions" :aria-label="t('vehicleOverview.carBrand')" />
        <UiSelect v-model="selectedKind" :options="kindFilterOptions" :aria-label="t('vehicleOverview.vehicleKind')" />
        <UiButton variant="secondary" class="vehicles-reset" :disabled="isLoading || !hasActiveFilters" @click="resetFilters">
          {{ t('vehicleOverview.reset') }}
        </UiButton>
      </div>

      <div class="vehicles-view-toolbar">
        <div class="vehicles-status-toggle" role="group" :aria-label="t('vehicleOverview.vehicleStatus')">
          <UiButton
            variant="secondary"
            class="vehicles-view-button"
            :class="{ active: !isArchiveMode }"
            :disabled="isLoading"
            @click="isArchiveMode ? toggleArchiveMode() : undefined"
          >
            {{ t('vehicleOverview.active') }}
          </UiButton>
          <UiButton
            variant="secondary"
            class="vehicles-view-button"
            :class="{ active: isArchiveMode }"
            :disabled="isLoading"
            @click="!isArchiveMode ? toggleArchiveMode() : undefined"
          >
            {{ t('vehicleOverview.archive') }}
          </UiButton>
        </div>

        <div class="vehicles-view-toggle" role="group" :aria-label="t('vehicleOverview.viewMode')">
          <UiButton variant="secondary" class="vehicles-view-button" :class="{ active: viewMode === 'list' }"
            :aria-pressed="viewMode === 'list'" @click="setViewMode('list')">{{ t('vehicleOverview.listView') }}</UiButton>
          <UiButton variant="secondary" class="vehicles-view-button" :class="{ active: viewMode === 'cards' }"
            :aria-pressed="viewMode === 'cards'" @click="setViewMode('cards')">{{ t('vehicleOverview.cardView') }}</UiButton>
        </div>
      </div>

      <UiLoader v-if="isLoading && !hasLoaded" class="vehicles-loader" :text="t('vehicleOverview.loadingVehicles')" />
      <div v-else-if="errorMessage" class="detailing-state detailing-state-error">{{ errorMessage }}</div>

      <div v-else class="vehicles-list-shell">
        <div v-if="isLoading" class="client-list-overlay">
          <UiLoader compact :text="t('vehicleOverview.refreshingVehicles')" />
        </div>

        <div v-if="vehicles.length > 0 && viewMode === 'cards'" class="vehicles-grid">
          <article v-for="vehicle in vehicles" :key="vehicle.id" class="vehicle-card">
            <a class="vehicle-image-container" :href="getVehicleUrl(vehicle)" :aria-label="getVehicleTitle(vehicle)">
              <img v-if="getPhotoUrl(vehicle)" class="vehicle-image" :src="getPhotoUrl(vehicle)" :alt="getVehicleTitle(vehicle)">
              <div v-else class="vehicle-placeholder">
                <span>{{ t('vehicleOverview.carShort') }}</span>
              </div>
            </a>

            <div class="vehicle-card-body">
              <h3 class="vehicle-title">
                <a :href="getVehicleUrl(vehicle)">{{ getVehicleTitle(vehicle) }}</a>
              </h3>
              <div v-if="getVehicleName(vehicle)" class="vehicle-name">{{ getVehicleName(vehicle) }}</div>
              <div class="vehicle-meta">
                <span v-for="meta in getVehicleMeta(vehicle)" :key="meta">{{ meta }}</span>
              </div>
              <p v-if="vehicle.description" class="vehicle-description">{{ vehicle.description }}</p>
              <div class="vehicle-card-footer">
                <UiButton class="btn-view-maintenance" :href="getVehicleUrl(vehicle)">
                  {{ t('vehicleOverview.open') }}
                </UiButton>
              </div>
            </div>
          </article>
        </div>

        <div v-else-if="vehicles.length > 0" class="vehicle-directory">
          <table class="vehicle-directory__table has-actions" :class="{ 'has-maintenance': canViewMaintenance }" :aria-label="vehiclesTitle">
            <thead><tr><th scope="col">{{ t('vehicleOverview.equipment') }}</th><th scope="col">{{ t('vehicleOverview.ownerDepartment') }}</th><th v-if="canViewMaintenance" scope="col">{{ t('vehicleOverview.maintenance') }}</th><th scope="col" :aria-label="t('vehicleOverview.actions')"></th></tr></thead>
            <tbody>
              <tr v-for="vehicle in vehicles" :key="vehicle.id">
                <th scope="row" class="vehicle-directory__identity">
                  <div class="vehicle-directory__vehicle">
                    <div class="vehicle-directory__photo" aria-hidden="true">
                      <img v-if="getPhotoUrl(vehicle)" :src="getPhotoUrl(vehicle)" alt="" loading="lazy" @error="onPhotoError(vehicle)" />
                      <img v-else-if="getBrandLogo(vehicle)" class="vehicle-directory__brand-logo" :src="getBrandLogo(vehicle)!.src" alt="" loading="lazy" />
                      <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="m5 9 2-5h10l2 5M4 9h16v9H4zM7 13h1m8 0h1M6 18v2m12-2v2" /></svg>
                    </div>
                    <div>
                      <a class="vehicle-directory__name" :href="getVehicleUrl(vehicle)">{{ getVehicleTitle(vehicle) }}</a>
                      <span v-if="vehicle.name" class="vehicle-directory__unit">{{ vehicle.name }}</span>
                      <span class="vehicle-directory__plate">{{ vehicle.registrationPlate || t('vehicleOverview.noPlate') }}</span>
                      <small v-if="vehicle.vin" class="vehicle-directory__vin">VIN: {{ vehicle.vin }}</small>
                    </div>
                  </div>
                </th>
                <td class="vehicle-directory__owner" :data-label="t('vehicleOverview.ownerDepartment')"><span :class="{ 'vehicle-directory__muted': !vehicle.organizationClientName }">{{ vehicle.organizationClientName || t('vehicleOverview.notSpecified') }}</span><small class="vehicle-directory__kind">{{ getVehicleKindName(vehicle.kind) }}</small></td>
                <td v-if="canViewMaintenance" class="vehicle-directory__maintenance" :data-label="t('vehicleOverview.maintenance')">
                  <span class="vehicle-directory__status" :class="vehicle.currentMaintenanceStatus === 2 ? 'is-progress' : vehicle.currentMaintenanceStatus === 1 ? 'is-planned' : vehicle.currentMaintenanceStatus === 3 ? 'is-ready' : 'is-idle'">{{ getCurrentMaintenanceStatus(vehicle.currentMaintenanceStatus) }}</span>
                  <small v-if="vehicle.currentMaintenanceName" class="vehicle-directory__kind">{{ vehicle.currentMaintenanceName }}</small>
                  <small v-if="vehicle.currentMaintenanceStatus === 1 && vehicle.currentMaintenanceDate" class="vehicle-directory__kind">{{ formatDate(vehicle.currentMaintenanceDate) }}</small>
                  <div v-if="vehicle.totalWorkCount" class="vehicle-directory__progress" :aria-label="t('vehicleOverview.workProgressLabel', { completed: vehicle.completedWorkCount ?? 0, total: vehicle.totalWorkCount })">
                    <span>{{ t('vehicleOverview.workProgress', { completed: vehicle.completedWorkCount ?? 0, total: vehicle.totalWorkCount }) }}</span>
                    <span class="vehicle-directory__progress-track"><span :style="{ width: `${Math.min(100, Math.round(((vehicle.completedWorkCount ?? 0) / vehicle.totalWorkCount) * 100))}%` }"></span></span>
                  </div>
                  <small class="vehicle-directory__kind">{{ t('vehicleOverview.lastMaintenance', { date: formatDate(vehicle.lastMaintenanceDate) }) }}</small>
                </td>
                <td class="vehicle-directory__actions">
                  <UiButton variant="secondary" :href="getVehicleUrl(vehicle)">{{ t('vehicleOverview.open') }}</UiButton>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div v-else class="detailing-empty">
          {{ hasActiveFilters ? t('vehicleOverview.noMatches') : t('vehicleOverview.emptyTitle', { title: vehiclesTitle }) }}
        </div>
      </div>

      <div v-if="vehicles.length > 0" class="vehicles-pagination">
        <div class="vehicles-pagination__summary">
          <span aria-live="polite">{{ paginationInfo }}</span>
          <div class="vehicles-pagination__page-size">
            <span>{{ t('vehicleOverview.perPage') }}</span>
            <UiSelect v-model="selectedPageSize" :options="pageSizeOptions" :searchable="false" :aria-label="t('vehicleOverview.pageSize')" />
          </div>
        </div>
        <UiPagination
          :model-value="page"
          :total-items="totalCount"
          :items-per-page="pageSize"
          :max-pages-shown="3"
          :disabled="isLoading"
          :label="t('vehicleOverview.pagination')"
          @update:model-value="changePage"
        />
      </div>
    </UiCard>
  </section>
</template>

<style scoped>
.vehicles-section {
  container-type: inline-size;
}

.vehicles-section .vehicles-filters {
  gap: 10px;
  padding: 0;
  margin-bottom: 16px;
  background: transparent;
  border: 0;
}

.vehicles-section .vehicles-filters :deep(.ui-search-input) {
  flex: 1 1 240px;
  max-width: 560px;
}

.vehicles-section .vehicles-filters :deep(.ui-select-field) {
  flex: 0 1 160px;
  min-width: 0;
}

.vehicles-section .vehicles-view-toolbar {
  flex-direction: row;
  flex-wrap: wrap;
  align-items: center;
  padding-bottom: 16px;
  border-bottom: 1px solid var(--crm-line, #e3e6eb);
  gap: 12px;
}

.vehicles-section .vehicles-status-toggle,
.vehicles-section .vehicles-view-toggle {
  width: auto;
  box-shadow: none;
}

.vehicles-section .vehicles-view-toggle {
  background: var(--crm-surface-soft, #f4f6f8);
  border: 1px solid var(--crm-line, #e3e6eb);
  border-radius: 8px;
  padding: 3px;
  gap: 3px;
}

.vehicles-view-toggle .vehicles-view-button {
  border-color: transparent;
  background: transparent;
  color: var(--crm-text-muted, #667085);
  box-shadow: none;
}

.vehicles-view-toggle .vehicles-view-button.active {
  background: var(--crm-text, #202733);
  color: var(--crm-surface, #fff);
}

.vehicles-section .vehicles-pagination {
  flex-direction: row;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  font-size: 12px;
}

.vehicles-pagination__summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 16px;
}

.vehicles-pagination__page-size {
  display: flex;
  align-items: center;
  gap: 10px;
}

.vehicles-pagination__page-size :deep(.ui-select-field) {
  width: 76px;
}

.vehicle-directory {
  container-type: inline-size;
  min-width: 0;
  margin-top: 16px;
}

.vehicle-directory__table {
  width: 100%;
  border-collapse: collapse;
  table-layout: fixed;
  color: var(--crm-text, #202733);
  font-size: 13px;
  line-height: 1.5;
}

.vehicle-directory__table th, .vehicle-directory__table td {
  padding: 16px;
  border-bottom: 1px solid var(--crm-line, #e3e6eb);
  text-align: left;
  vertical-align: middle;
  overflow-wrap: anywhere;
}

.vehicle-directory__table thead {
  background: var(--crm-surface-soft, #f4f6f8);
  color: var(--crm-text-muted, #667085);
}

.vehicle-directory__table thead th {
  padding-top: 10px;
  padding-bottom: 10px;
  font-size: 12px;
  font-weight: 500;
}

.vehicle-directory__table.has-maintenance.has-actions thead th:nth-child(1) { width: 32%; }
.vehicle-directory__table.has-maintenance.has-actions thead th:nth-child(2) { width: 26%; }
.vehicle-directory__table.has-maintenance.has-actions thead th:nth-child(3) { width: 30%; }
.vehicle-directory__table.has-maintenance.has-actions thead th:nth-child(4) { width: 12%; }
.vehicle-directory__table:not(.has-maintenance).has-actions thead th:nth-child(1) { width: 50%; }
.vehicle-directory__table:not(.has-maintenance).has-actions thead th:nth-child(2) { width: 38%; }
.vehicle-directory__table:not(.has-maintenance).has-actions thead th:nth-child(3) { width: 12%; }

.vehicle-directory__table tbody tr:hover, .vehicle-directory__table tbody tr:focus-within {
  background: var(--crm-surface-soft, #f4f6f8);
}

.vehicle-directory__vehicle {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.vehicle-directory__vehicle > div:last-child {
  min-width: 0;
}

.vehicle-directory__photo {
  display: grid;
  place-items: center;
  width: 52px;
  height: 42px;
  flex-shrink: 0;
  border-radius: 6px;
  overflow: hidden;
  background: var(--crm-surface-soft, #f4f6f8);
  color: var(--crm-text-muted, #667085);
}

.vehicle-directory__photo img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.vehicle-directory__photo img.vehicle-directory__brand-logo {
  padding: 8px;
  object-fit: contain;
  background: #fff;
}

.vehicle-directory__photo svg {
  width: 26px;
  height: 26px;
}

.vehicle-directory__name {
  display: block;
  color: var(--crm-text, #202733);
  font-size: 14px;
  font-weight: 700;
  text-decoration: none;
  margin-bottom: 5px;
}

.vehicle-directory__name:hover {
  color: var(--crm-accent, #c45112);
  text-decoration: underline;
}

.vehicle-directory__name:focus-visible {
  outline: 2px solid var(--crm-accent, #c45112);
  outline-offset: 3px;
}

.vehicle-directory__plate {
  display: inline-block;
  padding: 1px 6px;
  border: 1px solid var(--crm-line, #e3e6eb);
  border-radius: 4px;
  background: var(--crm-surface, #fff);
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
}

.vehicle-directory__unit, .vehicle-directory__kind {
  display: block;
  margin: 2px 0 4px;
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
  font-weight: 400;
}

.vehicle-directory__status {
  display: inline-flex;
  align-items: center;
  min-height: 24px;
  padding: 2px 8px;
  border: 1px solid var(--crm-line, #e3e6eb);
  border-radius: 999px;
  font-size: 12px;
  font-weight: 600;
  white-space: nowrap;
}

.vehicle-directory__status.is-progress {
  border-color: var(--crm-warning, #e87532);
  color: var(--crm-warning, #c45112);
}

.vehicle-directory__status.is-planned {
  border-color: var(--crm-info, #4e8ec8);
  color: var(--crm-info, #3475ad);
}

.vehicle-directory__status.is-ready {
  border-color: var(--crm-success, #31947d);
  color: var(--crm-success, #24856f);
}

.vehicle-directory__status.is-idle {
  color: var(--crm-text-muted, #667085);
}

.vehicle-directory__progress {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 5px 0;
  color: var(--crm-text-muted, #667085);
  font-size: 11px;
}

.vehicle-directory__progress-track {
  display: block;
  flex: 1;
  min-width: 25px;
  height: 4px;
  overflow: hidden;
  border-radius: 999px;
  background: var(--crm-line, #e3e6eb);
}

.vehicle-directory__progress-track > span {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: var(--crm-accent, #c45112);
}

.vehicle-directory__muted {
  color: var(--crm-text-muted, #667085);
  font-weight: 400;
  font-size: 12px;
}

.vehicle-directory__vin {
  display: block;
  margin-top: 5px;
  color: var(--crm-text-muted, #667085);
  font-variant-numeric: tabular-nums;
  font-size: 11px;
  font-weight: 400;
  overflow-wrap: anywhere;
}

.vehicle-directory__actions {
  white-space: nowrap;
}

.vehicle-directory__actions :deep(.ui-button) {
  min-height: 34px;
  padding: 6px 9px;
  margin: 2px;
}

@container (max-width: 1000px) {
  .vehicle-directory__table, .vehicle-directory__table tbody {
    display: block;
  }
  .vehicle-directory__table thead {
    display: none;
  }
  .vehicle-directory__table tbody {
    display: grid;
    gap: 10px;
  }
  .vehicle-directory__table tbody tr {
    display: grid;
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) auto;
    grid-template-areas: "identity maintenance action" "owner maintenance action";
    align-items: start;
    gap: 8px 16px;
    padding: 14px;
    border: 1px solid var(--crm-line, #e3e6eb);
    border-radius: 8px;
  }
  .vehicle-directory__table td, .vehicle-directory__table th {
    display: block;
    min-width: 0;
    border: 0;
    padding: 0;
  }
  .vehicle-directory__identity { grid-area: identity; }
  .vehicle-directory__owner { grid-area: owner; }
  .vehicle-directory__maintenance { grid-area: maintenance; }
  .vehicle-directory__table .vehicle-directory__actions {
    grid-area: action;
    align-self: end;
    justify-self: end;
  }
  .vehicle-directory__actions :deep(.ui-button) {
    margin: 0;
  }
  .vehicle-directory__table td[data-label]::before {
    content: attr(data-label);
    display: block;
    margin-bottom: 3px;
    color: var(--crm-text-muted, #667085);
    font-size: 11px;
  }
  .vehicle-directory__progress {
    max-width: 230px;
    flex-wrap: wrap;
  }
  .vehicle-directory__table:not(.has-maintenance) tbody tr {
    grid-template-columns: minmax(0, 1fr) auto;
    grid-template-areas: "identity action" "owner action";
  }
}

@container (max-width: 600px) {
  .vehicles-section .vehicles-pagination {
    flex-direction: column;
    align-items: start;
  }
  .vehicle-directory__table tbody tr,
  .vehicle-directory__table:not(.has-maintenance) tbody tr {
    grid-template-columns: minmax(0, 1fr) auto;
    grid-template-areas: "identity action" "owner owner" "maintenance maintenance";
    gap: 10px 12px;
    padding: 12px;
  }
  .vehicle-directory__table:not(.has-maintenance) tbody tr {
    grid-template-areas: "identity action" "owner owner";
  }
  .vehicle-directory__table .vehicle-directory__actions {
    align-self: start;
  }
}

@container (max-width: 380px) {
  .vehicle-directory__table tbody tr,
  .vehicle-directory__table:not(.has-maintenance) tbody tr {
    grid-template-columns: minmax(0, 1fr) auto;
    grid-template-areas: "identity identity" "owner owner" "maintenance maintenance" "action action";
  }
  .vehicle-directory__table:not(.has-maintenance) tbody tr {
    grid-template-areas: "identity identity" "owner owner" "action action";
  }
}

@media (max-width: 600px) {
  .vehicles-section,
  .vehicles-section :deep(.vehicles-list-card) {
    min-width: 0;
  }
  .vehicles-section :deep(.vehicles-list-card > .ui-card__header) {
    min-width: 0;
    flex-direction: column;
    align-items: stretch;
    gap: 10px;
    padding: 14px 15px;
  }
  .vehicles-section :deep(.vehicles-list-card .ui-card__title) {
    width: 100%;
  }
  .vehicles-section :deep(.vehicles-list-card .ui-card__title h2) {
    overflow-wrap: anywhere;
  }
  .vehicles-section :deep(.vehicles-list-card .ui-card__actions) {
    width: 100%;
    min-width: 0;
    justify-content: stretch;
  }
  .vehicles-section :deep(.vehicles-list-card .ui-card__actions .ui-button) {
    flex: 1;
    width: auto;
    min-width: 0;
  }
  .vehicles-section .vehicles-filters :deep(.ui-search-input) {
    flex-basis: 100%;
    max-width: none;
  }
  .vehicles-section .vehicles-filters :deep(.ui-select-field) {
    flex: 1 1 140px;
  }
  .vehicles-section .vehicles-view-toolbar {
    align-items: flex-start;
  }
  .vehicle-directory__vehicle {
    gap: 10px;
  }
  .vehicle-directory__photo {
    width: 48px;
    height: 40px;
  }
}
</style>

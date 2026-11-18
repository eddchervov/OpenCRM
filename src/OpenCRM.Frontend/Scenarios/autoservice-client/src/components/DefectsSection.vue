<script setup lang="ts">
import { useLocalization, formattingCulture } from '@opencrm/ui/i18n'
import '@opencrm/workspace/defectMessages'
import { getVehicleDisplayName } from '@opencrm/workspace'
import { WorkspaceAccessNotice } from '@opencrm/workspace'
import { computed, onMounted, reactive, ref } from 'vue'
import { UiButton, UiCard, UiInput, UiLoader, UiSelect, UiTextarea } from '@opencrm/ui'
import { WorkspacePageHelpButton, loadVehiclesPage, type WorkspaceVehicle } from '@opencrm/workspace'
import {
  createDefect,
  deleteDefect,
  linkDefectMaintenance,
  loadDefect,
  loadDefectHistory,
  loadDefectMaintenanceOptions,
  loadDefects,
  unlinkDefectMaintenance,
  updateDefect,
  updateDefectStatus,
  type FleetDefect,
  type FleetDefectHistoryItem,
  type FleetDefectMaintenanceOption
} from '@/api/defects'
import type { AutoServiceWorkspaceContext } from '@/api/autoserviceBff'

const { t } = useLocalization()

const props = defineProps<{
  context: AutoServiceWorkspaceContext
}>()

const pageSize = 10
const isLoading = ref(false)
const isSaving = ref(false)
const errorMessage = ref('')
const successMessage = ref('')
const page = ref(1)
const totalCount = ref(0)
const openCount = ref(0)
const resolvedCount = ref(0)
const defects = ref<FleetDefect[]>([])
const vehicles = ref<WorkspaceVehicle[]>([])
const selectedDefect = ref<FleetDefect | null>(null)
const historyItems = ref<FleetDefectHistoryItem[]>([])
const maintenanceOptions = ref<FleetDefectMaintenanceOption[]>([])
const selectedMaintenanceId = ref('')
const isEditing = ref(false)

const filters = reactive({
  searchQuery: '',
  statusCode: '',
  vehicleId: '',
  sort: 'created_desc'
})

const form = reactive({
  id: '',
  vehicleId: '',
  title: '',
  description: '',
  severityCode: 'medium',
  statusCode: 'new'
})

const canWrite = computed(() => props.context.workspaceAccess?.isReadOnly !== true)
const readOnlyMessage = computed(() => props.context.workspaceAccess?.readOnlyMessage ?? '')
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))
const vehicleOptions = computed(() => [
  { value: '', get label() { return t('defects.allVehicles') } },
  ...vehicles.value.map(vehicle => ({
    value: vehicle.id,
    label: getVehicleDisplayName(vehicle, true)
  }))
])
const formVehicleOptions = computed(() => vehicleOptions.value.filter(option => option.value))
const statusOptions = [
  { value: '', get label() { return t('defects.allStatuses') } },
  { value: 'new', get label() { return t('defects.newStatus') } },
  { value: 'in_progress', get label() { return t('defects.inProgress') } },
  { value: 'deferred', get label() { return t('defects.deferred') } },
  { value: 'resolved', get label() { return t('defects.resolved') } }
]
const formStatusOptions = statusOptions.filter(option => option.value)
const severityOptions = [
  { value: 'low', get label() { return t('defects.low') } },
  { value: 'medium', get label() { return t('defects.medium') } },
  { value: 'high', get label() { return t('defects.high') } },
  { value: 'critical', get label() { return t('defects.critical') } }
]
const sortOptions = [
  { value: 'created_desc', get label() { return t('defects.newest') } },
  { value: 'created_asc', get label() { return t('defects.oldest') } },
  { value: 'severity_desc', get label() { return t('defects.bySeverity') } },
  { value: 'status_asc', get label() { return t('defects.byStatus') } }
]
const maintenanceSelectOptions = computed(() => [
  { value: '', get label() { return t('defects.chooseMaintenance') } },
  ...maintenanceOptions.value.map(item => ({
    value: item.id,
    label: item.text
  }))
])

async function loadVehicles() {
  const result = await loadVehiclesPage(props.context.organization.id, 1, 500)
  vehicles.value = result.items
}

async function loadPage() {
  isLoading.value = true
  errorMessage.value = ''

  try {
    const result = await loadDefects(props.context.organization.id, {
      page: page.value,
      pageSize,
      searchQuery: filters.searchQuery,
      statusCode: filters.statusCode,
      vehicleId: filters.vehicleId,
      sort: filters.sort
    })
    defects.value = result.items
    totalCount.value = result.totalCount
    openCount.value = result.openCount
    resolvedCount.value = result.resolvedCount
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('defects.loadError')
  } finally {
    isLoading.value = false
  }
}

function resetForm() {
  form.id = ''
  form.vehicleId = filters.vehicleId || ''
  form.title = ''
  form.description = ''
  form.severityCode = 'medium'
  form.statusCode = 'new'
  isEditing.value = false
}

function startCreate() {
  selectedDefect.value = null
  historyItems.value = []
  maintenanceOptions.value = []
  resetForm()
  isEditing.value = true
}

function startEdit(defect: FleetDefect) {
  form.id = defect.id
  form.vehicleId = defect.vehicleId
  form.title = defect.title
  form.description = defect.description ?? ''
  form.severityCode = defect.severityCode
  form.statusCode = defect.statusCode
  isEditing.value = true
}

async function openDefect(defectId: string) {
  isLoading.value = true
  errorMessage.value = ''
  selectedMaintenanceId.value = ''

  try {
    const [defect, history, options] = await Promise.all([
      loadDefect(props.context.organization.id, defectId),
      loadDefectHistory(props.context.organization.id, defectId),
      loadDefectMaintenanceOptions(props.context.organization.id, defectId)
    ])
    selectedDefect.value = defect
    historyItems.value = history
    maintenanceOptions.value = options
    startEdit(defect)
    isEditing.value = false
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('defects.openError')
  } finally {
    isLoading.value = false
  }
}

async function saveDefect() {
  if (!canWrite.value) {
    errorMessage.value = readOnlyMessage.value
    return
  }

  if (!form.vehicleId || !form.title.trim()) {
    errorMessage.value = t('defects.vehicleTitleRequired')
    return
  }

  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''

  try {
    const payload = {
      vehicleId: form.vehicleId,
      title: form.title.trim(),
      description: form.description || null,
      severityCode: form.severityCode,
      statusCode: form.statusCode
    }

    if (form.id) {
      await updateDefect(props.context.organization.id, form.id, payload)
      successMessage.value = t('defects.saved')
      await openDefect(form.id)
    } else {
      const result = await createDefect(props.context.organization.id, payload)
      successMessage.value = t('defects.created')
      await loadPage()
      await openDefect(result.id)
    }

    isEditing.value = false
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('defects.saveError')
  } finally {
    isSaving.value = false
  }
}

async function changeStatus(defect: FleetDefect, statusCode: string) {
  if (!canWrite.value) {
    errorMessage.value = readOnlyMessage.value
    return
  }

  isSaving.value = true
  errorMessage.value = ''

  try {
    await updateDefectStatus(props.context.organization.id, defect.id, statusCode)
    await loadPage()
    await openDefect(defect.id)
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('defects.statusError')
  } finally {
    isSaving.value = false
  }
}

async function removeDefect(defect: FleetDefect) {
  if (!canWrite.value || !window.confirm(t('defects.deleteConfirm'))) {
    return
  }

  isSaving.value = true
  errorMessage.value = ''

  try {
    await deleteDefect(props.context.organization.id, defect.id)
    selectedDefect.value = null
    resetForm()
    await loadPage()
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('defects.deleteError')
  } finally {
    isSaving.value = false
  }
}

async function linkMaintenance() {
  if (!selectedDefect.value || !selectedMaintenanceId.value) {
    return
  }

  isSaving.value = true
  errorMessage.value = ''

  try {
    await linkDefectMaintenance(props.context.organization.id, selectedDefect.value.id, selectedMaintenanceId.value)
    await openDefect(selectedDefect.value.id)
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('defects.linkError')
  } finally {
    isSaving.value = false
  }
}

async function unlinkMaintenance(maintenanceId: string) {
  if (!selectedDefect.value || !canWrite.value) {
    return
  }

  isSaving.value = true
  errorMessage.value = ''

  try {
    await unlinkDefectMaintenance(props.context.organization.id, selectedDefect.value.id, maintenanceId)
    await openDefect(selectedDefect.value.id)
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : t('defects.unlinkError')
  } finally {
    isSaving.value = false
  }
}

function applyFilters() {
  page.value = 1
  void loadPage()
}

function goToPage(nextPage: number) {
  page.value = Math.min(Math.max(1, nextPage), totalPages.value)
  void loadPage()
}

function formatDate(value?: string | null, calendarDate = false) {
  if (!value) {
    return '-'
  }
  const timeZone = calendarDate ? 'UTC' : props.context.organization.timeZoneId || 'UTC'
  const date = calendarDate ? new Date(`${value.slice(0, 10)}T00:00:00Z`) : new Date(value)

  return new Intl.DateTimeFormat(formattingCulture.value, { timeZone }).format(date)
}

function statusName(defect: FleetDefect) {
  return statusOptions.find(option => option.value === defect.statusCode)?.label ?? defect.statusName
}

function severityName(defect: FleetDefect) {
  return severityOptions.find(option => option.value === defect.severityCode)?.label ?? defect.severityName
}

function statusTone(defect: FleetDefect) {
  if (defect.isClosed) {
    return 'is-success'
  }
  if (defect.statusCode === 'in_progress') {
    return 'is-warning'
  }
  return 'is-danger'
}

function maintenanceTitle(maintenance: FleetDefect['linkedMaintenances'][number]) {
  return [maintenance.typeName, maintenance.comment].filter(Boolean).join(' · ')
}

onMounted(async () => {
  const query = new URLSearchParams(window.location.hash.split('?')[1] ?? '')
  filters.vehicleId = query.get('vehicleId') ?? ''
  await loadVehicles()
  await loadPage()
})
</script>

<template>
  <section class="fleet-entity-section workspace-operations">
    <UiCard page :title="t('defects.title')">
      <template #actions>
        <WorkspacePageHelpButton section-code="defects" :title="t('defects.title')" />
        <UiButton :disabled="!canWrite || isSaving" @click="startCreate">{{ t('defects.add') }}</UiButton>
      </template>

      <WorkspaceAccessNotice :can-manage-billing="context.organization.canManageBilling === true" v-if="readOnlyMessage" :message="readOnlyMessage" />

      <div class="fleet-toolbar">
        <UiInput v-model="filters.searchQuery" :label="t('defects.search')" :placeholder="t('defects.searchPlaceholder')" />
        <UiSelect v-model="filters.statusCode" :label="t('defects.status')" :options="statusOptions" />
        <UiSelect v-model="filters.vehicleId" :label="t('defects.vehicle')" :options="vehicleOptions" />
        <UiSelect v-model="filters.sort" :label="t('defects.sort')" :options="sortOptions" :searchable="false" />
        <UiButton variant="secondary" :disabled="isLoading" @click="applyFilters">{{ t('defects.show') }}</UiButton>
      </div>

      <div class="fleet-metrics">
        <span>{{ t('defects.total') }} <strong>{{ totalCount }}</strong></span>
        <span>{{ t('defects.open') }} <strong>{{ openCount }}</strong></span>
        <span>{{ t('defects.closed') }} <strong>{{ resolvedCount }}</strong></span>
      </div>

      <div v-if="successMessage" class="fleet-state fleet-state-success">{{ successMessage }}</div>
      <div v-if="errorMessage" class="fleet-state fleet-state-error">{{ errorMessage }}</div>
      <UiLoader v-if="isLoading" :text="t('defects.loading')" />

      <div v-else class="fleet-split-grid">
        <div class="fleet-list">
          <article
            v-for="defect in defects"
            :key="defect.id"
            class="fleet-list-item"
            :class="{ active: selectedDefect?.id === defect.id }"
          >
            <button type="button" class="fleet-list-item__main" @click="openDefect(defect.id)">
              <span class="fleet-badge" :class="statusTone(defect)">{{ statusName(defect) }}</span>
              <strong>{{ defect.title }}</strong>
              <small>{{ defect.vehicleDisplayName }}<span v-if="defect.vehicleSubtitle"> · {{ defect.vehicleSubtitle }}</span></small>
              <span>{{ severityName(defect) }} · {{ formatDate(defect.createdAt) }}</span>
            </button>
            <div class="fleet-list-item__actions">
              <UiButton variant="secondary" :disabled="isSaving || !canWrite || defect.isClosed" @click="changeStatus(defect, 'resolved')">{{ t('defects.close') }}</UiButton>
            </div>
          </article>

          <div v-if="defects.length === 0" class="fleet-empty">{{ t('defects.empty') }}</div>

          <div class="fleet-pagination">
            <UiButton variant="secondary" :disabled="page <= 1 || isLoading" @click="goToPage(page - 1)">{{ t('defects.back') }}</UiButton>
            <span>{{ page }} / {{ totalPages }}</span>
            <UiButton variant="secondary" :disabled="page >= totalPages || isLoading" @click="goToPage(page + 1)">{{ t('defects.next') }}</UiButton>
          </div>
        </div>

        <aside class="fleet-detail-panel">
          <form v-if="isEditing" class="fleet-form-grid" @submit.prevent="saveDefect">
            <h3 class="fleet-form-wide">{{ form.id ? t('defects.editTitle') : t('defects.newTitle') }}</h3>
            <UiSelect v-model="form.vehicleId" :label="t('defects.vehicle')" :options="formVehicleOptions" required :disabled="isSaving || !canWrite" />
            <UiSelect v-model="form.severityCode" :label="t('defects.severity')" :options="severityOptions" :searchable="false" :disabled="isSaving || !canWrite" />
            <UiSelect v-model="form.statusCode" :label="t('defects.status')" :options="formStatusOptions" :searchable="false" :disabled="isSaving || !canWrite" />
            <UiInput v-model="form.title" class="fleet-form-wide" :label="t('defects.name')" required :disabled="isSaving || !canWrite" />
            <UiTextarea v-model="form.description" class="fleet-form-wide" :label="t('defects.description')" :rows="4" :disabled="isSaving || !canWrite" />
            <div class="fleet-form-actions">
              <UiButton type="submit" :disabled="isSaving || !canWrite">{{ t('defects.save') }}</UiButton>
              <UiButton type="button" variant="secondary" :disabled="isSaving" @click="isEditing = false">{{ t('defects.cancel') }}</UiButton>
            </div>
          </form>

          <template v-else-if="selectedDefect">
            <div class="fleet-detail-head">
              <span class="fleet-badge" :class="statusTone(selectedDefect)">{{ statusName(selectedDefect) }}</span>
              <h3>{{ selectedDefect.title }}</h3>
              <p>{{ selectedDefect.vehicleDisplayName }}<span v-if="selectedDefect.vehicleSubtitle"> · {{ selectedDefect.vehicleSubtitle }}</span></p>
            </div>
            <p v-if="selectedDefect.description" class="fleet-detail-text">{{ selectedDefect.description }}</p>
            <div class="fleet-detail-actions">
              <UiButton variant="secondary" :disabled="isSaving || !canWrite" @click="startEdit(selectedDefect)">{{ t('defects.edit') }}</UiButton>
              <UiButton variant="secondary" :disabled="isSaving || !canWrite" @click="changeStatus(selectedDefect, 'in_progress')">{{ t('defects.start') }}</UiButton>
              <UiButton variant="secondary" :disabled="isSaving || !canWrite" @click="changeStatus(selectedDefect, 'resolved')">{{ t('defects.close') }}</UiButton>
              <UiButton variant="ghost" :disabled="isSaving || !canWrite" @click="removeDefect(selectedDefect)">{{ t('defects.delete') }}</UiButton>
            </div>

            <section class="fleet-subsection">
              <h4>{{ t('defects.linkedMaintenance') }}</h4>
              <div v-for="maintenance in selectedDefect.linkedMaintenances" :key="maintenance.id" class="fleet-related-row">
                <span>{{ formatDate(maintenance.date, true) }} · {{ maintenanceTitle(maintenance) }}</span>
                <UiButton variant="ghost" :disabled="isSaving || !canWrite" @click="unlinkMaintenance(maintenance.id)">{{ t('defects.unlink') }}</UiButton>
              </div>
              <div v-if="selectedDefect.linkedMaintenances.length === 0" class="fleet-empty">{{ t('defects.noLinks') }}</div>
              <div class="fleet-inline-action">
                <UiSelect v-model="selectedMaintenanceId" :label="t('defects.maintenance')" :options="maintenanceSelectOptions" :disabled="isSaving || !canWrite" />
                <UiButton variant="secondary" :disabled="!selectedMaintenanceId || isSaving || !canWrite" @click="linkMaintenance">{{ t('defects.link') }}</UiButton>
              </div>
            </section>

            <section class="fleet-subsection">
              <h4>{{ t('defects.history') }}</h4>
              <div v-for="item in historyItems" :key="`${item.createdAt}-${item.title}`" class="fleet-history-row">
                <strong>{{ item.title }}</strong>
                <span>{{ item.description }}</span>
                <small>{{ formatDate(item.createdAt) }}<span v-if="item.actorDisplayName"> · {{ item.actorDisplayName }}</span></small>
              </div>
            </section>
          </template>

          <div v-else class="fleet-empty">{{ t('defects.chooseHint') }}</div>
        </aside>
      </div>
    </UiCard>
  </section>
</template>

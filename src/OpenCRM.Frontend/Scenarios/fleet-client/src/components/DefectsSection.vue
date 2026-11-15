<script setup lang="ts">
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
import type { FleetWorkspaceContext } from '@/api/fleetBff'

const props = defineProps<{
  context: FleetWorkspaceContext
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
  { value: '', label: 'Вся техника' },
  ...vehicles.value.map(vehicle => ({
    value: vehicle.id,
    label: getVehicleDisplayName(vehicle, true)
  }))
])
const formVehicleOptions = computed(() => vehicleOptions.value.filter(option => option.value))
const statusOptions = [
  { value: '', label: 'Все статусы' },
  { value: 'new', label: 'Новые' },
  { value: 'in_progress', label: 'В работе' },
  { value: 'deferred', label: 'Отложены' },
  { value: 'resolved', label: 'Закрыты' }
]
const formStatusOptions = statusOptions.filter(option => option.value)
const severityOptions = [
  { value: 'low', label: 'Низкая' },
  { value: 'medium', label: 'Средняя' },
  { value: 'high', label: 'Высокая' },
  { value: 'critical', label: 'Критичная' }
]
const sortOptions = [
  { value: 'created_desc', label: 'Сначала новые' },
  { value: 'created_asc', label: 'Сначала старые' },
  { value: 'severity_desc', label: 'По критичности' },
  { value: 'status_asc', label: 'По статусу' }
]
const maintenanceSelectOptions = computed(() => [
  { value: '', label: 'Выберите обслуживание' },
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
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось загрузить неисправности.'
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
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось открыть неисправность.'
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
    errorMessage.value = 'Укажите технику и название неисправности.'
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
      successMessage.value = 'Неисправность сохранена.'
      await openDefect(form.id)
    } else {
      const result = await createDefect(props.context.organization.id, payload)
      successMessage.value = 'Неисправность добавлена.'
      await loadPage()
      await openDefect(result.id)
    }

    isEditing.value = false
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось сохранить неисправность.'
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
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось изменить статус.'
  } finally {
    isSaving.value = false
  }
}

async function removeDefect(defect: FleetDefect) {
  if (!canWrite.value || !window.confirm('Удалить неисправность?')) {
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
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось удалить неисправность.'
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
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось привязать обслуживание.'
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
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось удалить связь.'
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

function formatDate(value?: string | null) {
  if (!value) {
    return '-'
  }
  return new Intl.DateTimeFormat('ru-RU').format(new Date(value))
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
  await loadVehicles()
  await loadPage()
})
</script>

<template>
  <section class="fleet-entity-section workspace-operations">
    <UiCard page title="Неисправности">
      <template #actions>
        <WorkspacePageHelpButton section-code="defects" title="Неисправности" />
        <UiButton :disabled="!canWrite || isSaving" @click="startCreate">Добавить</UiButton>
      </template>

      <WorkspaceAccessNotice :can-manage-billing="context.organization.canManageBilling === true" v-if="readOnlyMessage" :message="readOnlyMessage" />

      <div class="fleet-toolbar">
        <UiInput v-model="filters.searchQuery" label="Поиск" placeholder="Название, техника, описание" />
        <UiSelect v-model="filters.statusCode" label="Статус" :options="statusOptions" />
        <UiSelect v-model="filters.vehicleId" label="Техника" :options="vehicleOptions" />
        <UiSelect v-model="filters.sort" label="Сортировка" :options="sortOptions" :searchable="false" />
        <UiButton variant="secondary" :disabled="isLoading" @click="applyFilters">Показать</UiButton>
      </div>

      <div class="fleet-metrics">
        <span>Всего: <strong>{{ totalCount }}</strong></span>
        <span>Открыто: <strong>{{ openCount }}</strong></span>
        <span>Закрыто: <strong>{{ resolvedCount }}</strong></span>
      </div>

      <div v-if="successMessage" class="fleet-state fleet-state-success">{{ successMessage }}</div>
      <div v-if="errorMessage" class="fleet-state fleet-state-error">{{ errorMessage }}</div>
      <UiLoader v-if="isLoading" text="Загружаем неисправности..." />

      <div v-else class="fleet-split-grid">
        <div class="fleet-list">
          <article
            v-for="defect in defects"
            :key="defect.id"
            class="fleet-list-item"
            :class="{ active: selectedDefect?.id === defect.id }"
          >
            <button type="button" class="fleet-list-item__main" @click="openDefect(defect.id)">
              <span class="fleet-badge" :class="statusTone(defect)">{{ defect.statusName }}</span>
              <strong>{{ defect.title }}</strong>
              <small>{{ defect.vehicleDisplayName }}<span v-if="defect.vehicleSubtitle"> · {{ defect.vehicleSubtitle }}</span></small>
              <span>{{ defect.severityName }} · {{ formatDate(defect.createdAt) }}</span>
            </button>
            <div class="fleet-list-item__actions">
              <UiButton variant="secondary" :disabled="isSaving || !canWrite || defect.isClosed" @click="changeStatus(defect, 'resolved')">Закрыть</UiButton>
            </div>
          </article>

          <div v-if="defects.length === 0" class="fleet-empty">Неисправности не найдены.</div>

          <div class="fleet-pagination">
            <UiButton variant="secondary" :disabled="page <= 1 || isLoading" @click="goToPage(page - 1)">Назад</UiButton>
            <span>{{ page }} / {{ totalPages }}</span>
            <UiButton variant="secondary" :disabled="page >= totalPages || isLoading" @click="goToPage(page + 1)">Вперед</UiButton>
          </div>
        </div>

        <aside class="fleet-detail-panel">
          <form v-if="isEditing" class="fleet-form-grid" @submit.prevent="saveDefect">
            <h3 class="fleet-form-wide">{{ form.id ? 'Редактирование неисправности' : 'Новая неисправность' }}</h3>
            <UiSelect v-model="form.vehicleId" label="Техника" :options="formVehicleOptions" required :disabled="isSaving || !canWrite" />
            <UiSelect v-model="form.severityCode" label="Критичность" :options="severityOptions" :searchable="false" :disabled="isSaving || !canWrite" />
            <UiSelect v-model="form.statusCode" label="Статус" :options="formStatusOptions" :searchable="false" :disabled="isSaving || !canWrite" />
            <UiInput v-model="form.title" class="fleet-form-wide" label="Название" required :disabled="isSaving || !canWrite" />
            <UiTextarea v-model="form.description" class="fleet-form-wide" label="Описание" :rows="4" :disabled="isSaving || !canWrite" />
            <div class="fleet-form-actions">
              <UiButton type="submit" :disabled="isSaving || !canWrite">Сохранить</UiButton>
              <UiButton type="button" variant="secondary" :disabled="isSaving" @click="isEditing = false">Отмена</UiButton>
            </div>
          </form>

          <template v-else-if="selectedDefect">
            <div class="fleet-detail-head">
              <span class="fleet-badge" :class="statusTone(selectedDefect)">{{ selectedDefect.statusName }}</span>
              <h3>{{ selectedDefect.title }}</h3>
              <p>{{ selectedDefect.vehicleDisplayName }}<span v-if="selectedDefect.vehicleSubtitle"> · {{ selectedDefect.vehicleSubtitle }}</span></p>
            </div>
            <p v-if="selectedDefect.description" class="fleet-detail-text">{{ selectedDefect.description }}</p>
            <div class="fleet-detail-actions">
              <UiButton variant="secondary" :disabled="isSaving || !canWrite" @click="startEdit(selectedDefect)">Редактировать</UiButton>
              <UiButton variant="secondary" :disabled="isSaving || !canWrite" @click="changeStatus(selectedDefect, 'in_progress')">В работу</UiButton>
              <UiButton variant="secondary" :disabled="isSaving || !canWrite" @click="changeStatus(selectedDefect, 'resolved')">Закрыть</UiButton>
              <UiButton variant="ghost" :disabled="isSaving || !canWrite" @click="removeDefect(selectedDefect)">Удалить</UiButton>
            </div>

            <section class="fleet-subsection">
              <h4>Связанные обслуживания</h4>
              <div v-for="maintenance in selectedDefect.linkedMaintenances" :key="maintenance.id" class="fleet-related-row">
                <span>{{ formatDate(maintenance.date) }} · {{ maintenanceTitle(maintenance) }}</span>
                <UiButton variant="ghost" :disabled="isSaving || !canWrite" @click="unlinkMaintenance(maintenance.id)">Отвязать</UiButton>
              </div>
              <div v-if="selectedDefect.linkedMaintenances.length === 0" class="fleet-empty">Связей пока нет.</div>
              <div class="fleet-inline-action">
                <UiSelect v-model="selectedMaintenanceId" label="Обслуживание" :options="maintenanceSelectOptions" :disabled="isSaving || !canWrite" />
                <UiButton variant="secondary" :disabled="!selectedMaintenanceId || isSaving || !canWrite" @click="linkMaintenance">Привязать</UiButton>
              </div>
            </section>

            <section class="fleet-subsection">
              <h4>История</h4>
              <div v-for="item in historyItems" :key="`${item.createdAt}-${item.title}`" class="fleet-history-row">
                <strong>{{ item.title }}</strong>
                <span>{{ item.description }}</span>
                <small>{{ formatDate(item.createdAt) }}<span v-if="item.actorDisplayName"> · {{ item.actorDisplayName }}</span></small>
              </div>
            </section>
          </template>

          <div v-else class="fleet-empty">Выберите неисправность или создайте новую.</div>
        </aside>
      </div>
    </UiCard>
  </section>
</template>

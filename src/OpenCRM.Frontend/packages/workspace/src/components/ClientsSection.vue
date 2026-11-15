<script setup lang="ts">
import WorkspaceAccessNotice from './WorkspaceAccessNotice.vue'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton, UiCard, UiLoader, UiPagination, UiSearchInput, UiSelect } from '@opencrm/ui'
import {
  loadClientsPage,
  type WorkspaceClient,
  type WorkspaceClientSegment
} from '../api/clients'
import type { WorkspaceContext } from '../types'
import { createMemberSelectOptions } from '../utils/memberSelectOptions'
import WorkspacePageHelpButton from './WorkspacePageHelpButton.vue'

const props = defineProps<{
  context: WorkspaceContext
}>()

const clients = ref<WorkspaceClient[]>([])
const tags = ref<{ id: string; name: string }[]>([])
const segments = ref<WorkspaceClientSegment[]>([])
const page = ref(1)
const pageSize = ref(10)
const selectedPageSize = ref('10')
const totalCount = ref(0)
const allCount = ref(0)
const totalVehiclesCount = ref(0)
const totalMaintenanceCount = ref(0)
const searchQuery = ref('')
const selectedSort = ref('name_asc')
const selectedSegmentCode = ref('all')
const selectedStatusCode = ref('')
const selectedPartyTypeCode = ref('')
const selectedResponsibleUserId = ref('')
const selectedTagId = ref('')
const isArchiveMode = ref(false)
const viewMode = ref<'cards' | 'compact'>('compact')
const isLoading = ref(false)
const hasLoaded = ref(false)
const errorMessage = ref('')
let searchTimer: number | undefined
let latestRequest = 0
let restoringListState = false
const showFilters = ref(false)
const filterCount = computed(() => [selectedPartyTypeCode.value, selectedStatusCode.value, selectedResponsibleUserId.value, selectedTagId.value].filter(Boolean).length)

const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / Math.max(1, pageSize.value))))
const canWrite = computed(() => props.context.workspaceAccess?.isReadOnly !== true)
const readOnlyMessage = computed(() => props.context.workspaceAccess?.readOnlyMessage ?? '')
const billingHref = computed(() => '#billing')
const createClientUrl = computed(() => '#clients/new')
const clientsTitle = computed(() => props.context.shell.scenario?.terms?.clients ?? 'Клиенты')
const vehiclesTitle = computed(() => props.context.shell.scenario?.terms?.vehicles ?? 'Автомобили')
const maintenanceTitle = computed(() => props.context.shell.scenario?.terms?.maintenances ?? 'Услуги')
const isDetailingScenario = computed(() => props.context.shell.scenario?.code === 'detailing')
const viewModeStorageKey = computed(() => `opencrm-workspace:${encodeURIComponent(props.context.organization.id)}:${encodeURIComponent(props.context.user.id)}:clients:view-mode`)
const listStateStorageKey = computed(() => `opencrm-workspace:${encodeURIComponent(props.context.organization.id)}:${encodeURIComponent(props.context.user.id)}:clients:list-state`)
const isCompactView = computed(() => isDetailingScenario.value || viewMode.value === 'compact')
const visibleSegments = computed(() => isDetailingScenario.value ? [] : segments.value)
const showClientVehicles = computed(() => !isDetailingScenario.value)
const hasActiveFilters = computed(() => Boolean(
  searchQuery.value ||
  selectedStatusCode.value ||
  selectedPartyTypeCode.value ||
  selectedResponsibleUserId.value ||
  selectedTagId.value ||
  selectedSegmentCode.value !== 'all' ||
  isArchiveMode.value
))

const paginationInfo = computed(() => {
  if (totalCount.value === 0) {
    return 'Нет записей'
  }

  const from = (page.value - 1) * pageSize.value + 1
  const to = Math.min(page.value * pageSize.value, totalCount.value)
  return `${from}-${to} из ${totalCount.value}`
})

const sortOptions = [
  { value: 'name_asc', label: 'ФИО: А-Я' },
  { value: 'name_desc', label: 'ФИО: Я-А' },
  { value: 'vehicles_desc', label: 'Больше техники' },
  { value: 'maintenance_desc', label: 'Больше обслуживаний' },
  { value: 'recent_desc', label: 'Недавно обслуживались' }
]
const statusFilterOptions = [
  { value: '', label: 'Все статусы' },
  { value: 'active', label: 'Активный' },
  { value: 'lead', label: 'Лид' },
  { value: 'attention', label: 'Требует внимания' },
  { value: 'paused', label: 'Пауза' },
  { value: 'lost', label: 'Потерян' }
]
const partyTypeFilterOptions = [
  { value: '', label: 'Все типы' },
  { value: 'person', label: 'Физлицо' },
  { value: 'company', label: 'Организация' }
]
const pageSizeOptions = [
  { value: '10', label: '10' },
  { value: '25', label: '25' },
  { value: '50', label: '50' }
]
const segmentCodes = ['all', 'leads', 'attention', 'no_responsible', 'no_vehicles', 'no_recent_service']
const responsibleFilterOptions = computed(() => [
  { value: '', label: 'Все ответственные' },
  { value: '__none', label: 'Без ответственного' },
  ...createMemberSelectOptions((props.context.organization.members ?? []).map(member => ({
    ...member,
    value: member.userId
  })))
])
const tagFilterOptions = computed(() => [
  { value: '', label: 'Все теги' },
  ...tags.value.map(tag => ({ value: tag.id, label: tag.name }))
])

function formatDate(value?: string | null) {
  if (!value) {
    return 'нет'
  }

  const dateOnlyMatch = value.match(/^(\d{4})-(\d{2})-(\d{2})/)
  if (dateOnlyMatch) {
    return `${dateOnlyMatch[3]}.${dateOnlyMatch[2]}.${dateOnlyMatch[1]}`
  }

  return new Intl.DateTimeFormat('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric'
  }).format(new Date(value))
}

function partyTypeName(code?: string | null) {
  return code === 'company' ? 'Организация' : code === 'person' ? 'Физлицо' : 'Тип не указан'
}

async function loadClients() {
  const request = ++latestRequest
  isLoading.value = true
  errorMessage.value = ''

  try {
    const data = await loadClientsPage(props.context.organization.id, page.value, pageSize.value, {
      searchQuery: searchQuery.value.trim(),
      sort: selectedSort.value,
      segmentCode: selectedSegmentCode.value,
      isArchived: isArchiveMode.value,
      statusCode: selectedStatusCode.value,
      responsibleUserId: selectedResponsibleUserId.value,
      tagId: selectedTagId.value,
      partyTypeCode: selectedPartyTypeCode.value
    })

    if (request !== latestRequest) {
      return
    }

    const lastPage = Math.max(1, Math.ceil(data.totalCount / data.pageSize))
    if (data.totalCount > 0 && page.value > lastPage) {
      page.value = lastPage
      void loadClients()
      return
    }

    clients.value = data.items
    tags.value = data.tags
    segments.value = data.segments
    page.value = data.page
    pageSize.value = data.pageSize
    totalCount.value = data.totalCount
    allCount.value = data.allCount
    totalVehiclesCount.value = data.totalVehiclesCount
    totalMaintenanceCount.value = data.totalMaintenanceCount
    hasLoaded.value = true
  } catch (error) {
    if (request === latestRequest) {
      errorMessage.value = error instanceof Error ? error.message : 'Не удалось загрузить клиентов.'
    }
  } finally {
    if (request === latestRequest) {
      isLoading.value = false
    }
  }
}

function selectSegment(code: string) {
  selectedSegmentCode.value = code || 'all'
  page.value = 1
  void loadClients()
}

function resetFilters() {
  searchQuery.value = ''
  selectedStatusCode.value = ''
  selectedPartyTypeCode.value = ''
  selectedResponsibleUserId.value = ''
  selectedTagId.value = ''
  selectedSegmentCode.value = 'all'
  isArchiveMode.value = false
  page.value = 1
  void loadClients()
}

function changePage(nextPage: number) {
  page.value = Math.min(Math.max(1, nextPage), totalPages.value)
  void loadClients()
}

function setViewMode(mode: string) {
  viewMode.value = mode === 'compact' ? 'compact' : 'cards'
  saveViewMode()
}

function loadViewMode() {
  viewMode.value = 'compact'
  try {
    const value = window.localStorage?.getItem(viewModeStorageKey.value)
    if (value === 'cards' || value === 'compact') {
      viewMode.value = value
    }
  } catch {
    viewMode.value = 'compact'
  }
}

function restoreListState() {
  restoringListState = true
  try {
    resetListState()
    const stored = window.sessionStorage?.getItem(listStateStorageKey.value)
    if (!stored) {
      return
    }

    const state = JSON.parse(stored)
    if (!state || typeof state !== 'object') {
      return
    }

    searchQuery.value = typeof state.searchQuery === 'string' ? state.searchQuery.slice(0, 200) : ''
    selectedSort.value = sortOptions.some(option => option.value === state.sort) ? state.sort : 'name_asc'
    selectedSegmentCode.value = segmentCodes.includes(state.segmentCode) ? state.segmentCode : 'all'
    selectedStatusCode.value = statusFilterOptions.some(option => option.value === state.statusCode) ? state.statusCode : ''
    selectedPartyTypeCode.value = partyTypeFilterOptions.some(option => option.value === state.partyTypeCode) ? state.partyTypeCode : ''
    selectedResponsibleUserId.value = typeof state.responsibleUserId === 'string' ? state.responsibleUserId.slice(0, 128) : ''
    selectedTagId.value = typeof state.tagId === 'string' ? state.tagId.slice(0, 64) : ''
    isArchiveMode.value = state.isArchiveMode === true
    selectedPageSize.value = pageSizeOptions.some(option => option.value === String(state.pageSize)) ? String(state.pageSize) : '10'
    pageSize.value = Number(selectedPageSize.value)
    page.value = Number.isSafeInteger(state.page) && state.page > 0 ? Math.min(state.page, 1_000_000) : 1
  } catch {
    // Фильтры остаются доступны и без хранилища вкладки.
  } finally {
    restoringListState = false
  }
}

function resetListState() {
  searchQuery.value = ''
  selectedSort.value = 'name_asc'
  selectedSegmentCode.value = 'all'
  selectedStatusCode.value = ''
  selectedPartyTypeCode.value = ''
  selectedResponsibleUserId.value = ''
  selectedTagId.value = ''
  isArchiveMode.value = false
  selectedPageSize.value = '10'
  pageSize.value = 10
  page.value = 1
}

function saveListState() {
  try {
    window.sessionStorage?.setItem(listStateStorageKey.value, JSON.stringify({
      searchQuery: searchQuery.value,
      sort: selectedSort.value,
      segmentCode: selectedSegmentCode.value,
      statusCode: selectedStatusCode.value,
      partyTypeCode: selectedPartyTypeCode.value,
      responsibleUserId: selectedResponsibleUserId.value,
      tagId: selectedTagId.value,
      isArchiveMode: isArchiveMode.value,
      page: page.value,
      pageSize: pageSize.value
    }))
  } catch {
    // Список продолжает работать, если браузер запретил хранилище вкладки.
  }
}

function saveViewMode() {
  try {
    window.localStorage?.setItem(viewModeStorageKey.value, viewMode.value)
  } catch {
    // Список работает и без сохранения вида в браузере.
  }
}

function toggleArchiveMode() {
  isArchiveMode.value = !isArchiveMode.value
  page.value = 1
  void loadClients()
}

restoreListState()

watch(searchQuery, () => {
  if (restoringListState) {
    return
  }

  window.clearTimeout(searchTimer)
  searchTimer = window.setTimeout(() => {
    page.value = 1
    void loadClients()
  }, 350)
}, { flush: 'sync' })

watch(selectedSort, () => {
  if (restoringListState) {
    return
  }

  page.value = 1
  void loadClients()
}, { flush: 'sync' })

watch([selectedStatusCode, selectedPartyTypeCode, selectedResponsibleUserId, selectedTagId], () => {
  if (restoringListState) {
    return
  }

  page.value = 1
  void loadClients()
}, { flush: 'sync' })

watch(selectedPageSize, () => {
  if (restoringListState) {
    return
  }

  pageSize.value = Number(selectedPageSize.value) || 10
  page.value = 1
  void loadClients()
}, { flush: 'sync' })

onMounted(() => {
  loadViewMode()
  void loadClients()
})
watch([() => props.context.organization.id, () => props.context.user.id], () => {
  window.clearTimeout(searchTimer)
  searchTimer = undefined
  restoringListState = true
  loadViewMode()
  restoreListState()
  void loadClients()
})
watch([
  searchQuery,
  selectedSort,
  selectedSegmentCode,
  selectedStatusCode,
  selectedPartyTypeCode,
  selectedResponsibleUserId,
  selectedTagId,
  isArchiveMode,
  page,
  pageSize
], () => {
  if (!restoringListState) {
    saveListState()
  }
}, { flush: 'sync' })
onBeforeUnmount(() => {
  window.clearTimeout(searchTimer)
  saveListState()
  latestRequest++
})
</script>

<template>
  <UiCard page :title="clientsTitle" class="client-directory">
    <template #actions>
      <UiButton class="directory-create" :disabled="context.workspaceAccess?.isReadOnly === true && !context.organization.canManageBilling" :href="canWrite ? createClientUrl : billingHref" :variant="canWrite ? 'primary' : 'secondary'">＋ Добавить клиента</UiButton>
      <WorkspacePageHelpButton section-code="clients" :title="clientsTitle" />
    </template>

    <WorkspaceAccessNotice v-if="readOnlyMessage" :can-manage-billing="context.organization.canManageBilling === true" :message="readOnlyMessage" :href="billingHref" />

    <div class="directory-summary" aria-label="Сводка по клиентам">
      <span>Всего клиентов <b>{{ hasLoaded ? allCount : '—' }}</b></span>
      <span>{{ vehiclesTitle }} <b>{{ hasLoaded ? totalVehiclesCount : '—' }}</b></span>
      <span>{{ maintenanceTitle }} <b>{{ hasLoaded ? totalMaintenanceCount : '—' }}</b></span>
    </div>

    <div class="directory-toolbar">
      <UiSearchInput v-model="searchQuery" id="client-search" placeholder="Имя, телефон или заметка" />
      <label class="directory-sr-only" for="client-search">Поиск клиентов</label>
      <UiSelect v-model="selectedPartyTypeCode" class="directory-toolbar__party-type" :options="partyTypeFilterOptions" :searchable="false" aria-label="Тип клиента" />
      <UiSelect v-model="selectedResponsibleUserId" class="directory-toolbar__responsible" :options="responsibleFilterOptions" :searchable="false" aria-label="Ответственный за клиента" />
      <UiButton class="directory-toolbar__filters" variant="secondary" :aria-expanded="showFilters" aria-controls="client-filters" @click="showFilters = !showFilters">
        Фильтры<span v-if="filterCount"> · {{ filterCount }}</span>
      </UiButton>
      <div class="directory-toggle" role="group" aria-label="Статус клиентов">
        <button type="button" :aria-pressed="!isArchiveMode" :disabled="isLoading" @click="isArchiveMode ? toggleArchiveMode() : undefined">Активные</button>
        <button type="button" :aria-pressed="isArchiveMode" :disabled="isLoading" @click="!isArchiveMode ? toggleArchiveMode() : undefined">Архив</button>
      </div>
    </div>

    <div v-if="showFilters" id="client-filters" class="directory-filters">
      <UiSelect v-model="selectedSort" label="Сортировка" :options="sortOptions" :searchable="false" />
      <UiSelect v-model="selectedStatusCode" label="Статус" :options="statusFilterOptions" :searchable="false" />
      <UiSelect v-model="selectedTagId" label="Тег" :options="tagFilterOptions" />
    </div>

    <div v-if="visibleSegments.length" class="directory-segments" role="group" aria-label="Сегменты клиентов">
      <button v-for="segment in visibleSegments" :key="segment.code" type="button" :aria-pressed="selectedSegmentCode === segment.code" :disabled="isLoading" @click="selectSegment(segment.code)">
        {{ segment.name }} <span>{{ segment.count }}</span>
      </button>
    </div>

    <div v-if="hasActiveFilters || !isDetailingScenario" class="directory-results">
      <div v-if="hasActiveFilters" class="directory-results__reset"><button type="button" :disabled="isLoading" @click="resetFilters">Сбросить фильтры</button></div>
      <div v-if="!isDetailingScenario" class="directory-toggle" role="group" aria-label="Вид списка клиентов">
        <button type="button" :aria-pressed="isCompactView" @click="setViewMode('compact')">Список</button>
        <button type="button" :aria-pressed="!isCompactView" @click="setViewMode('cards')">Плитка</button>
      </div>
    </div>

    <UiLoader v-if="isLoading && !hasLoaded" text="Загружаем клиентов…" />
    <div v-else-if="errorMessage" class="directory-empty" role="alert">
      <p>{{ errorMessage }}</p>
      <UiButton variant="secondary" @click="loadClients">Повторить</UiButton>
    </div>
    <div v-else class="client-list-shell" :aria-busy="isLoading">
      <div v-if="isLoading" class="client-list-overlay"><UiLoader compact text="Обновляем список…" /></div>
      <table v-if="clients.length && isCompactView" class="directory-table">
        <caption class="directory-sr-only">{{ clientsTitle }}</caption>
        <colgroup>
          <col style="width: 25%" />
          <col style="width: 16%" />
          <col style="width: 17%" />
          <col style="width: 11%" />
          <col style="width: 19%" />
          <col style="width: 12%" />
        </colgroup>
        <thead><tr><th scope="col">Клиент</th><th scope="col">Телефон</th><th scope="col">Ответственный за клиента</th><th scope="col">{{ vehiclesTitle }}</th><th scope="col">Последний визит</th><th scope="col"><span class="directory-sr-only">Действие</span></th></tr></thead>
        <tbody>
          <tr v-for="client in clients" :key="client.id">
            <th scope="row" class="directory-identity">
              <a :href="`#clients/${client.id}`">{{ client.name }}</a>
              <div class="directory-client-meta"><span>{{ partyTypeName(client.partyTypeCode) }}</span><span class="directory-status" :data-status="client.statusCode">{{ client.statusName || 'Активный' }}</span></div>
              <div v-if="client.tags?.length" class="directory-tags"><span v-for="tag in client.tags" :key="tag.id">{{ tag.name }}</span></div>
            </th>
            <td data-label="Телефон"><a v-if="client.phone" class="directory-phone" :href="`tel:${client.phone.replace(/[^+\d]/g, '')}`">{{ client.phone }}</a><span v-else class="directory-muted">Не указан</span></td>
            <td data-label="Ответственный за клиента"><span :class="{ 'directory-muted': !client.responsibleDisplayName }">{{ client.responsibleDisplayName || 'Не назначен' }}</span></td>
            <td :data-label="vehiclesTitle" class="directory-count">{{ client.vehiclesCount }}</td>
            <td data-label="Последнее посещение"><span :class="{ 'directory-muted': !client.lastMaintenanceDate }">{{ client.lastMaintenanceDate ? formatDate(client.lastMaintenanceDate) : 'Ещё не было' }}</span><small class="directory-maintenance-count">{{ maintenanceTitle }}: {{ client.maintenanceCount }}</small></td>
            <td data-label="Действие" class="directory-row-action"><UiButton class="directory-open-button" variant="secondary" :href="`#clients/${client.id}`">Открыть</UiButton></td>
          </tr>
        </tbody>
      </table>
        <div v-else-if="clients.length" class="directory-cards">
          <article v-for="client in clients" :key="client.id" class="client-card" :class="{ 'client-card--compact': isCompactView }">
            <div class="client-card__body">
              <div class="client-card__header">
                <h3><a :href="`#clients/${client.id}`">{{ client.name }}</a></h3>
                <span>{{ client.statusName || 'Активный' }}</span>
                <a v-if="client.phone" :href="`tel:${client.phone.replace(/[^+\d]/g, '')}`">{{ client.phone }}</a>
              </div>

              <div v-if="!isCompactView" class="client-card__meta">
                <span v-if="client.responsibleDisplayName">Ответственный за клиента: <b>{{ client.responsibleDisplayName }}</b></span>
                <span v-if="client.comment">{{ client.comment }}</span>
              </div>

              <div v-if="client.tags?.length && !isCompactView" class="client-tags">
                <span v-for="tag in client.tags" :key="tag.id">{{ tag.name }}</span>
              </div>

              <div class="client-counts">
                <span>{{ vehiclesTitle }}: <b>{{ client.vehiclesCount }}</b></span>
                <span>{{ maintenanceTitle }}: <b>{{ client.maintenanceCount }}</b></span>
                <span>Последнее: <b>{{ formatDate(client.lastMaintenanceDate) }}</b></span>
              </div>

              <div v-if="showClientVehicles && client.vehicles?.length && !isCompactView" class="client-vehicles">
                <div v-for="vehicle in client.vehicles" :key="vehicle.id" class="client-vehicle">
                  <div>
                    <strong>{{ vehicle.displayName }}</strong>
                    <span v-if="vehicle.registrationPlate">{{ vehicle.registrationPlate }}</span>
                    <small>{{ maintenanceTitle }}: {{ vehicle.maintenanceCount }}</small>
                  </div>
                  <UiButton variant="secondary" :href="`#vehicles/${vehicle.id}`">
                    Открыть
                  </UiButton>
                </div>
              </div>
            </div>

            <div class="client-card__actions">
              <UiButton :href="`#clients/${client.id}`">
                Открыть
              </UiButton>
            </div>
          </article>
        </div>


      <div v-else class="directory-empty">
        <h3>{{ hasActiveFilters ? (isArchiveMode ? 'В архиве ничего не найдено' : 'Клиенты не найдены') : (canWrite ? 'Добавьте первого клиента' : 'Клиентов пока нет') }}</h3>
        <p>{{ hasActiveFilters ? 'Измените поиск или сбросьте фильтры.' : (canWrite ? 'Сохраните имя и телефон — автомобили и историю можно добавить позже.' : 'Здесь появятся клиенты организации.') }}</p>
        <UiButton v-if="hasActiveFilters" variant="secondary" @click="resetFilters">Сбросить фильтры</UiButton>
        <UiButton v-else-if="canWrite" :href="createClientUrl">Добавить клиента</UiButton>
      </div>
    </div>

    <div v-if="clients.length" class="directory-pagination">
      <div class="directory-pagination-summary">
        <span aria-live="polite">{{ paginationInfo }}</span>
        <div class="directory-page-size"><span>На странице</span><UiSelect v-model="selectedPageSize" :options="pageSizeOptions" :searchable="false" aria-label="Размер страницы" /></div>
      </div>
      <UiPagination :model-value="page" :total-items="totalCount" :items-per-page="pageSize" :max-pages-shown="3" :disabled="isLoading" label="Навигация по страницам клиентов" @update:model-value="changePage" />
    </div>
  </UiCard>
</template>

<style scoped>
.client-directory {
  min-width: 0;
}

.client-directory .client-list-shell {
  margin-top: 0;
}

.client-directory .directory-toolbar + .ui-loader,
.client-directory .directory-filters + .ui-loader,
.client-directory .directory-toolbar + .client-list-shell,
.client-directory .directory-filters + .client-list-shell {
  margin-top: 16px;
}

.directory-summary {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 24px;
  margin-bottom: 20px;
  color: var(--crm-text-muted, #667085);
  font-size: 13px;
}

.directory-summary span {
  display: flex;
  align-items: baseline;
  gap: 8px;
}

.directory-summary b {
  color: var(--crm-text, #202833);
  font-size: 15px;
  font-variant-numeric: tabular-nums;
}

.directory-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

.directory-toolbar > .ui-search-input {
  flex: 1 1 240px;
  max-width: 460px;
}

.directory-toolbar > .ui-select-field {
  flex: 0 1 210px;
}

.directory-toolbar > .directory-toolbar__party-type {
  flex-basis: 145px;
}

.directory-toolbar > .directory-toolbar__responsible {
  flex-basis: 190px;
}

.directory-toggle {
  display: inline-flex;
  align-items: center;
  padding: 3px;
  gap: 2px;
  border: 1px solid var(--crm-line, #dfe3e8);
  border-radius: 8px;
  background: var(--crm-surface-soft, #f2f4f7);
}

.directory-toggle button {
  border: 0;
  border-radius: 5px;
  padding: 7px 12px;
  background: transparent;
  color: var(--crm-text-muted, #667085);
  font-size: 13px;
  cursor: pointer;
}

.directory-toggle button[aria-pressed="true"] {
  background: var(--crm-text, #202833);
  color: var(--crm-surface, #fff);
}

.directory-toggle button:disabled {
  opacity: .6;
  cursor: wait;
}

.directory-filters {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 240px));
  gap: 12px;
  padding: 16px 0 4px;
}

.directory-segments {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 16px;
}

.directory-segments button {
  padding: 6px 10px;
  border: 1px solid var(--crm-line, #dfe3e8);
  border-radius: 6px;
  background: var(--crm-surface, #fff);
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
  cursor: pointer;
}

.directory-segments button[aria-pressed="true"] {
  color: var(--crm-text, #202833);
  border-color: #df773f;
}

.directory-segments span {
  margin-left: 5px;
  font-variant-numeric: tabular-nums;
}

.directory-results {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  min-height: 54px;
  margin-top: 10px;
  font-size: 12px;
  color: var(--crm-text-muted, #667085);
}

.directory-results__reset {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 16px;
}

.directory-results__reset > button {
  border: 0;
  background: transparent;
  color: var(--crm-text, #202833);
  padding: 6px 0;
  text-decoration: underline;
  text-underline-offset: 3px;
  cursor: pointer;
}

.directory-table {
  width: 100%;
  max-width: 100%;
  min-width: 0;
  border-collapse: collapse;
  table-layout: fixed;
  font-size: 13px;
  text-align: left;
}

.directory-table th, .directory-table td {
  min-width: 0;
  padding: 15px 12px;
  vertical-align: middle;
  overflow-wrap: anywhere;
  border-bottom: 1px solid var(--crm-line, #e7eaf0);
}

.directory-table thead th {
  padding-block: 10px;
  color: var(--crm-text-muted, #667085);
  font-weight: 500;
  font-size: 12px;
  background: var(--crm-surface-soft, #f6f7f9);
}

.directory-table thead th:nth-child(4) {
  white-space: nowrap;
}

.directory-table thead th:last-child,
.directory-table .directory-row-action {
  white-space: nowrap;
  overflow-wrap: normal;
}

.directory-row-action :deep(.directory-open-button) {
  width: max-content;
  max-width: 100%;
  min-width: 82px;
  flex: 0 0 auto;
  padding-inline: 9px;
  white-space: nowrap;
}

.directory-table tbody tr:hover {
  background: var(--crm-surface-soft, #f8f9fb);
}

.directory-identity > a {
  font-size: 14px;
  font-weight: 650;
  text-decoration: none;
  color: var(--crm-text, #202833);
}

.directory-client-meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px 10px;
  margin-top: 4px;
  color: var(--crm-text-muted, #667085);
  font-size: 11px;
  font-weight: 400;
}

.directory-table a:not(.ui-button):hover {
  color: #cc591d;
  text-decoration: underline;
}

.directory-phone {
  color: var(--crm-text, #202833);
  text-decoration: none;
  font-variant-numeric: tabular-nums;
}

.directory-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-top: 6px;
}

.directory-tags span {
  max-width: 100%;
  padding: 2px 6px;
  border-radius: 4px;
  background: var(--crm-surface-soft, #f0f2f5);
  color: var(--crm-text-muted, #667085);
  font-size: 11px;
  font-weight: 400;
  overflow-wrap: anywhere;
}

.directory-status {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
}

.directory-status::before {
  content: '';
  flex: 0 0 6px;
  height: 6px;
  border-radius: 50%;
  background: #8793a2;
}

.directory-status[data-status="active"]::before {
  background: #309f86;
}

.directory-status[data-status="attention"]::before, .directory-status[data-status="lead"]::before {
  background: #e47b36;
}

.directory-muted {
  color: var(--crm-text-muted, #667085);
}

.directory-count {
  font-variant-numeric: tabular-nums;
  font-weight: 600;
}

.directory-maintenance-count {
  display: block;
  margin-top: 3px;
  color: var(--crm-text-muted, #667085);
  font-size: 11px;
  font-weight: 400;
}

.directory-cards {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, 320px), 1fr));
  gap: 12px;
}

.directory-cards .client-card {
  display: flex;
  flex-direction: column;
  box-shadow: none;
}

.directory-cards .client-card__header {
  align-items: start;
  flex-direction: column;
}

.directory-cards .client-card__header h3 {
  overflow-wrap: anywhere;
}

.directory-cards .client-card__actions {
  margin-top: auto;
  align-self: start;
}

.directory-empty {
  padding: 40px 16px;
  text-align: center;
  border: 1px dashed var(--crm-line, #dfe3e8);
  border-radius: 8px;
}

.directory-empty h3 {
  font-size: 17px;
}

.directory-empty p {
  color: var(--crm-text-muted, #667085);
  margin: 10px 0 18px;
  font-size: 14px;
}

.directory-pagination {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-top: 18px;
}

.directory-pagination-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 16px;
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
}

.directory-page-size {
  display: flex;
  align-items: center;
  gap: 10px;
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
}

.directory-page-size .ui-select-field {
  width: 76px;
}

.directory-sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip-path: inset(50%);
  white-space: nowrap;
  border: 0;
}

.client-directory button:focus-visible, .client-directory a:focus-visible {
  outline: 2px solid #e87532;
  outline-offset: 3px;
}

@media (max-width: 1100px) {
  .directory-table, .directory-table tbody {
    display: block;
  }
  .directory-table colgroup {
    display: none;
  }
  .directory-table thead {
    position: absolute;
    width: 1px;
    height: 1px;
    overflow: hidden;
    clip-path: inset(50%);
  }
  .directory-table tbody {
    display: grid;
    gap: 10px;
  }
  .directory-table tbody tr {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 12px 16px;
    padding: 14px 16px;
    border: 1px solid var(--crm-line, #e7eaf0);
    border-radius: 8px;
  }
  .directory-table th, .directory-table td {
    display: block;
    border: 0;
    padding: 0;
  }
  .directory-table .directory-identity {
    grid-column: 1 / -1;
  }
  .directory-table td::before {
    display: block;
    content: attr(data-label);
    color: var(--crm-text-muted, #667085);
    font-size: 11px;
    font-weight: 400;
    margin-bottom: 4px;
  }

  .directory-table .directory-row-action {
    grid-column: 1 / -1;
    justify-self: end;
  }
}

@media (max-width: 600px) {
  .directory-summary {
    gap: 8px 16px;
  }
  .directory-toolbar > .ui-search-input {
    flex-basis: 100%;
    max-width: none;
  }
  .directory-toolbar > .ui-select-field {
    flex: 1 1 160px;
  }
  .directory-filters {
    grid-template-columns: 1fr;
  }
  .directory-table tbody tr {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    padding: 12px;
  }
  .directory-table td {
    min-width: 0;
    overflow-wrap: anywhere;
  }
  .directory-pagination {
    align-items: start;
    flex-direction: column;
  }
}

@media (max-width: 390px) {
  .client-directory :deep(.ui-card__header) {
    flex-direction: column;
    align-items: stretch;
    gap: 10px;
  }

  .client-directory :deep(.ui-card__title) {
    width: 100%;
    min-width: 0;
  }

  .client-directory :deep(.ui-card__actions) {
    width: 100%;
    justify-content: flex-start;
  }
}
</style>

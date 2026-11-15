<script setup lang="ts">
import { getVehicleDisplayName } from '../api/vehicles'
import { computed, inject, onMounted, onUnmounted, provide, ref, watch } from 'vue'
import { pageBreadcrumbsKey, UiButton, UiCard, UiConfirmDialog, UiImagePreview, UiLoader } from '@opencrm/ui'
import {
  loadVehicle,
  loadVehicleAlbums,
  loadVehicleHistory,
  loadVehicleMaintenancePage,
  loadVehiclePhotos,
  type WorkspaceVehicle,
  type WorkspaceVehicleAlbum,
  type WorkspaceVehicleHistoryEvent,
  type WorkspaceVehiclePhoto
} from '../api/vehicles'
import type { WorkspaceMaintenance, WorkspaceMaintenanceDetail } from '../api/maintenance'
import type { WorkspaceContext } from '../types'
import { getVehicleBrandLogo } from '../utils/vehicleBrandLogo'
import { getWorkdayProgress } from '../utils/workdayCard'
import { loadWorkProgress, type WorkProgress } from '../api/work'
import WorkspaceProLock from './WorkspaceProLock.vue'

const props = defineProps<{
  context: WorkspaceContext
  vehicleId: string
  action?: string
}>()

const emit = defineEmits<{
  changed: []
}>()

const vehicle = ref<WorkspaceVehicle | null>(null)
const parentBreadcrumbs = inject(pageBreadcrumbsKey, undefined)
const photos = ref<WorkspaceVehiclePhoto[]>([])
const albums = ref<WorkspaceVehicleAlbum[]>([])
const history = ref<WorkspaceVehicleHistoryEvent[]>([])
const maintenanceItems = ref<WorkspaceMaintenance[]>([])
const maintenanceTotalCount = ref(0)
const maintenancePage = ref(1)
const maintenanceHasMore = ref(false)
const maintenanceStatus = ref('')
const maintenanceLoading = ref(false)
const maintenanceError = ref('')
const activeRecords = ref<WorkspaceMaintenance[]>([])
const activeRecordCount = ref(0)
const activeRecordError = ref('')
const progressByMaintenance = ref<Record<string, WorkProgress[]>>({})
const mediaError = ref('')
const historyError = ref('')
const activePanel = ref<'history' | 'albums'>('history')
const showSavedMediaHistory = ref(false)
const isSavedMediaLoading = ref(false)
let latestRequest = 0
let latestMaintenanceRequest = 0
const selectedPhotoIndex = ref(0)
const previewPhotoUrl = ref('')
const expandedMaintenanceId = ref('')
const isLoading = ref(false)
const isSavingVehicle = ref(false)
const errorMessage = ref('')
const actionMessage = ref('')
const deleteConfirmOpen = ref(false)
const exportMenu = ref<HTMLDetailsElement | null>(null)
const exportingOptionKey = ref('')

const canWrite = computed(() => props.context.workspaceAccess?.isReadOnly !== true)
const canEditVehicles = computed(() => props.context.workspaceAccess?.isReadOnly !== true
  && props.context.workspaceAccess?.canEditVehicles === true)
const canArchiveVehicles = computed(() => props.context.workspaceAccess?.isReadOnly !== true
  && props.context.workspaceAccess?.canArchiveVehicles === true)
const canCreateMaintenance = computed(() => props.context.workspaceAccess?.isReadOnly !== true
  && props.context.shell.quickActions?.some(action => action.code === 'create-maintenance' && action.isVisible) === true)
const isAlbumsFeatureLocked = computed(() => props.context.shell.scenario?.modules?.photos === false)
const isAlbumPreview = computed(() => isAlbumsFeatureLocked.value && (!showSavedMediaHistory.value || isSavedMediaLoading.value))
const organizationId = computed(() => props.context.organization.id)
const isDetailingScenario = computed(() => props.context.shell.scenario?.code === 'detailing')
const isJournalView = computed(() => props.action === 'journal')
const brandLogo = computed(() => getVehicleBrandLogo(vehicle.value?.brandLogoKey))
const clientInitials = computed(() => vehicle.value?.organizationClientName?.trim().split(/\s+/).slice(0, 2).map(part => part[0]).join('') || 'К')
const canOpenClients = computed(() => props.context.shell.menu?.some(item => item.code === 'clients' && item.isVisible))
const clientLabel = computed(() => props.context.shell.scenario?.code === 'fleet' ? (props.context.shell.scenario.terms?.clients || 'Связь с организацией') : 'Клиент')
const canOpenDefects = computed(() => !isDetailingScenario.value && props.context.shell.menu?.some(item => item.code === 'defects' && item.isVisible))

const vehicleTitle = computed(() => {
  if (!vehicle.value) {
    return isDetailingScenario.value ? 'Карточка авто' : 'Карточка техники'
  }

  return [getVehicleDisplayName(vehicle.value), vehicle.value.yearIssue].filter(Boolean).join(' ')
})

provide(pageBreadcrumbsKey, computed(() => {
  if (!isJournalView.value) return parentBreadcrumbs?.value ?? []

  return [
    ...(parentBreadcrumbs?.value ?? []),
    { title: vehicleTitle.value, href: `#vehicles/${props.vehicleId}` }
  ]
}))

const currentPhoto = computed(() => photos.value[selectedPhotoIndex.value])
const primaryPhotoUrl = computed(() => currentPhoto.value?.url ?? vehicle.value?.photoUrl ?? '')
const mileageLabel = computed(() => vehicle.value?.mileageUnit === 2 ? 'Наработка' : 'Пробег')
const mileageValue = computed(() => {
  if (vehicle.value?.currentMileage === null || vehicle.value?.currentMileage === undefined) {
    return 'Не заполнено'
  }

  return `${vehicle.value.currentMileage.toLocaleString('ru-RU')} ${vehicle.value.mileageUnit === 2 ? 'м/ч' : 'км'}`
})
const photosUrl = computed(() => {
  if (!vehicle.value) {
    return '#'
  }

  return `#vehicles/${vehicle.value.id}/photos`
})
const albumsUrl = computed(() => {
  if (!vehicle.value) {
    return '#'
  }

  return `#vehicles/${vehicle.value.id}/albums`
})
const editVehicleUrl = computed(() => vehicle.value ? `#vehicles/${vehicle.value.id}/edit` : '#')
const maintenanceCreateUrl = computed(() => {
  if (!vehicle.value) {
    return '#'
  }

  return `#maintenance/new/${vehicle.value.id}`
})
const journalUrl = computed(() => {
  if (!vehicle.value) {
    return '#'
  }

  return `#vehicles/${vehicle.value.id}/journal`
})
const defectsUrl = computed(() => vehicle.value ? `#defects?vehicleId=${encodeURIComponent(vehicle.value.id)}` : '#')
const clientCardUrl = computed(() => canOpenClients.value && vehicle.value?.organizationClientId ? `#clients/${vehicle.value.organizationClientId}` : '')
const countersUrl = computed(() => {
  if (!vehicle.value) {
    return '#'
  }

  const params = new URLSearchParams({ vehicleId: vehicle.value.id })
  if (vehicle.value.organizationClientId) {
    params.set('clientId', vehicle.value.organizationClientId)
  }

  return `#counters?${params.toString()}`
})
const canShowCounters = computed(() => props.context.shell.scenario?.code === 'fleet' && props.context.shell.menu?.some(item => item.code === 'counters' && item.isVisible))
const showMileageStat = computed(() => !isDetailingScenario.value || hasValue(vehicle.value?.currentMileage))
const maintenanceHistoryTitle = computed(() => isDetailingScenario.value ? 'История услуг' : 'История обслуживания')
const addMaintenanceText = computed(() => isDetailingScenario.value ? 'Записать услугу' : 'Добавить обслуживание')
const noMaintenanceText = computed(() => isDetailingScenario.value ? 'По авто пока нет услуг.' : 'По технике пока нет обслуживаний.')
const journalText = computed(() => isDetailingScenario.value ? 'Журнал авто' : 'Журнал техники')
const cardTitle = computed(() => isJournalView.value ? journalText.value : vehicleTitle.value)
const exportOptions = computed(() => [
  { key: 'pdf-photos', format: 'pdf' as const, includePhotos: true, label: 'PDF с фотографиями', icon: 'PDF', tone: 'danger' },
  { key: 'pdf', format: 'pdf' as const, includePhotos: false, label: 'PDF без фотографий', icon: 'PDF', tone: 'danger' },
  { key: 'word-photos', format: 'word' as const, includePhotos: true, label: 'Word с фотографиями', icon: 'DOC', tone: 'primary' },
  { key: 'word', format: 'word' as const, includePhotos: false, label: 'Word без фотографий', icon: 'DOC', tone: 'primary' }
])

function formatDate(value?: string | null, fallback = 'Не указано') {
  if (!value) {
    return fallback
  }

  const dateOnlyMatch = value.match(/^(\d{4})-(\d{2})-(\d{2})/)
  if (dateOnlyMatch) {
    return `${dateOnlyMatch[3]}.${dateOnlyMatch[2]}.${dateOnlyMatch[1]}`
  }

  return new Intl.DateTimeFormat('ru-RU').format(new Date(value))
}

function formatEventDate(value?: string | null) {
  if (!value) return 'Не указано'

  return new Intl.DateTimeFormat('ru-RU', {
    timeZone: props.context.organization.timeZoneId || 'UTC',
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value))
}

function formatTime(value?: string | null) {
  return value ? value.slice(0, 5) : ''
}

function formatMoney(value?: number | null) {
  if (value === null || value === undefined) {
    return 'Не указана'
  }

  return new Intl.NumberFormat('ru-RU', { style: 'currency', currency: 'RUB', maximumFractionDigits: 2 }).format(value)
}

function valueOrEmpty(value?: string | number | null) {
  return value === null || value === undefined || value === '' ? 'Не указано' : value
}

function hasValue(value?: string | number | null) {
  return value !== null && value !== undefined && value !== ''
}

function getVehicleKindName(kind: number) {
  const names: Record<number, string> = {
    1: 'Легковой автомобиль',
    2: 'Грузовой автомобиль',
    3: 'Мотоцикл',
    4: 'Квадроцикл',
    5: 'Багги / UTV',
    6: 'Снегоход',
    7: 'Гидроцикл',
    8: 'Лодка / катер',
    9: 'Спецтехника',
    10: 'Прицеп',
    99: 'Другая техника'
  }

  return names[kind] ?? 'Другая техника'
}

function getMaintenanceTypeName(type: number) {
  const names: Record<number, string> = {
    1: 'ТО',
    2: 'Ремонт',
    3: 'Дозаправка',
    100: 'Другое'
  }

  return names[type] ?? 'Обслуживание'
}

function getMaintenanceStatusName(status: number) {
  const names: Record<number, string> = {
    1: 'Запланировано',
    2: 'В работе',
    3: 'Выполнено',
    4: 'Отменено'
  }

  return names[status] ?? 'Неизвестно'
}

function getRecordStatus(item: WorkspaceMaintenance) {
  if (item.status === 3 && item.handedOverAt) return 'Выдано'
  if (item.status === 3 && item.readyForHandoverAt) return 'Готово к выдаче'
  return getMaintenanceStatusName(item.status)
}

function getRecordTitle(item: WorkspaceMaintenance) {
  return item.maintenanceDetails.filter(detail => !hasMaterial(detail)).map(detail => detail.name).filter(Boolean).join(' · ') || getMaintenanceTypeName(item.type)
}

function hasMaterial(detail: WorkspaceMaintenanceDetail) {
  return Boolean(detail.materialCatalogItemId || detail.materialName || detail.materialQuantity != null)
}

function getRecordProgress(item: WorkspaceMaintenance) {
  const works = progressByMaintenance.value[item.id]
  if (!works) return null
  const services = item.maintenanceDetails.filter(detail => !hasMaterial(detail))
  const completed = new Set(works.filter(work => work.status === 4).map(work => work.detailId))
  return services.length ? getWorkdayProgress(services.length, services.filter(detail => detail.detailId && completed.has(detail.detailId)).length) : null
}

async function loadRecordProgress(items: WorkspaceMaintenance[], request: number) {
  const orgId = organizationId.value
  const vehicleId = props.vehicleId
  const records = [...new Map(items.filter(item => item.status === 2 && !progressByMaintenance.value[item.id]).map(item => [item.id, item])).values()]
  const results = await Promise.allSettled(records.map(async item => [item.id, await loadWorkProgress(orgId, item.id)] as const))
  if (request !== latestRequest || orgId !== organizationId.value || vehicleId !== props.vehicleId) return
  progressByMaintenance.value = { ...progressByMaintenance.value, ...Object.fromEntries(results.filter(result => result.status === 'fulfilled').map(result => result.value)) }
}

async function loadHistory(append = false) {
  const request = ++latestMaintenanceRequest
  const orgId = organizationId.value
  const vehicleId = props.vehicleId
  const page = append ? maintenancePage.value + 1 : 1
  maintenanceLoading.value = true
  maintenanceError.value = ''
  if (!append) {
    maintenanceItems.value = []
    expandedMaintenanceId.value = ''
  }
  try {
    const result = await loadVehicleMaintenancePage(orgId, vehicleId, page, 4, Number(maintenanceStatus.value) || undefined)
    if (request !== latestMaintenanceRequest || orgId !== organizationId.value || vehicleId !== props.vehicleId) return
    maintenanceItems.value = append ? [...maintenanceItems.value, ...result.items] : result.items
    maintenanceTotalCount.value = result.totalCount
    maintenancePage.value = page
    maintenanceHasMore.value = result.hasMore
    void loadRecordProgress(result.items, latestRequest)
  } catch (error) {
    if (request !== latestMaintenanceRequest || orgId !== organizationId.value || vehicleId !== props.vehicleId) return
    maintenanceError.value = error instanceof Error ? error.message : 'Не удалось загрузить историю обслуживания.'
  } finally {
    if (request === latestMaintenanceRequest) maintenanceLoading.value = false
  }
}

function showActiveRecords() {
  activePanel.value = 'history'
  maintenanceStatus.value = '2'
  void loadHistory()
}
function getMaintenanceDateLabel(item: WorkspaceMaintenance) {
  return item.status === 3 ? 'Дата выполнения' : 'Плановая дата'
}

function getMaintenanceDateValue(item: WorkspaceMaintenance) {
  return item.status === 3 ? (item.completedDate ?? item.date) : (item.plannedDate ?? item.date)
}

function getMaintenanceTimeValue(item: WorkspaceMaintenance) {
  const start = formatTime(item.plannedStartTime)
  const end = formatTime(item.plannedEndTime)
  if (item.plannedEndDate && item.plannedEndDate.slice(0, 10) !== (item.plannedDate ?? item.date).slice(0, 10)) {
    return `${formatDate(item.plannedDate ?? item.date)} ${start} — ${formatDate(item.plannedEndDate)} ${end}`
  }

  return start && end ? `${start}-${end}` : start || end || 'Не указано'
}

function getDocumentUrl(format: 'pdf' | 'word', includePhotos: boolean) {
  if (!vehicle.value) {
    return '#'
  }

  const params = new URLSearchParams({
    organizationId: organizationId.value,
    includePhotos: String(includePhotos)
  })

  return `/api/v1/documents/${format}/vehicle/${vehicle.value.id}?${params.toString()}`
}

function closeExportMenuOnOutsideClick(event: PointerEvent) {
  const menu = exportMenu.value
  if (menu?.open && event.target instanceof Node && !menu.contains(event.target)) {
    menu.open = false
  }
}

async function downloadDocument(option: { key: string, format: 'pdf' | 'word', includePhotos: boolean, label: string }) {
  if (exportingOptionKey.value) {
    return
  }

  exportingOptionKey.value = option.key
  actionMessage.value = ''

  try {
    const response = await fetch(getDocumentUrl(option.format, option.includePhotos), {
      credentials: 'same-origin',
      headers: { 'X-Requested-With': 'XMLHttpRequest' }
    })
    if (!response.ok) {
      throw new Error('Не удалось сформировать отчёт.')
    }

    const blob = await response.blob()
    const disposition = response.headers.get('content-disposition') ?? ''
    const encodedName = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1]
    const plainName = disposition.match(/filename="?([^";]+)"?/i)?.[1]
    const fileName = encodedName
      ? decodeURIComponent(encodedName)
      : plainName || `vehicle-report.${option.format === 'word' ? 'docx' : 'pdf'}`
    const objectUrl = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = objectUrl
    anchor.download = fileName
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
    URL.revokeObjectURL(objectUrl)
    if (exportMenu.value) {
      exportMenu.value.open = false
    }
  } catch (error) {
    actionMessage.value = error instanceof Error ? error.message : 'Не удалось сформировать отчёт.'
  } finally {
    exportingOptionKey.value = ''
  }
}

function getAlbumCover(album: WorkspaceVehicleAlbum) {
  const primary = album.photos.find(photo => photo.photoId === album.primaryPhotoId || photo.isPrimary)
  return primary?.url ?? album.photos[0]?.url ?? ''
}

function getAlbumUrl(album: WorkspaceVehicleAlbum) {
  return `#vehicles/${props.vehicleId}/albums/${album.albumId}`
}

function showPreviousPhoto() {
  if (photos.value.length < 2) {
    return
  }

  selectedPhotoIndex.value = selectedPhotoIndex.value === 0 ? photos.value.length - 1 : selectedPhotoIndex.value - 1
}

function showNextPhoto() {
  if (photos.value.length < 2) {
    return
  }

  selectedPhotoIndex.value = selectedPhotoIndex.value === photos.value.length - 1 ? 0 : selectedPhotoIndex.value + 1
}

function toggleMaintenance(item: WorkspaceMaintenance) {
  expandedMaintenanceId.value = expandedMaintenanceId.value === item.id ? '' : item.id
}

async function archiveVehicle() {
  if (!canArchiveVehicles.value || !vehicle.value || isSavingVehicle.value) {
    return
  }

  deleteConfirmOpen.value = true
}

function closeDeleteConfirmation() {
  if (!isSavingVehicle.value) {
    deleteConfirmOpen.value = false
  }
}

async function confirmArchiveVehicle() {
  if (!canArchiveVehicles.value || !vehicle.value || isSavingVehicle.value) {
    return
  }

  isSavingVehicle.value = true
  actionMessage.value = ''

  try {
    const url = new URL(`/api/v1/vehicles/${vehicle.value.id}`, window.location.origin)
    url.searchParams.set('organizationId', organizationId.value)

    const response = await fetch(url.toString(), {
      method: 'DELETE',
      credentials: 'same-origin',
      headers: {
        'X-Requested-With': 'XMLHttpRequest',
        RequestVerificationToken: document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
      }
    })

    if (!response.ok) {
      throw new Error(isDetailingScenario.value ? 'Не удалось удалить авто.' : 'Не удалось удалить технику.')
    }

    window.location.hash = '#vehicles'
  } catch (error) {
    actionMessage.value = error instanceof Error ? error.message : (isDetailingScenario.value ? 'Не удалось удалить авто.' : 'Не удалось удалить технику.')
  } finally {
    isSavingVehicle.value = false
    deleteConfirmOpen.value = false
  }
}

async function restoreVehicle() {
  if (!canArchiveVehicles.value || !vehicle.value || isSavingVehicle.value) {
    return
  }

  isSavingVehicle.value = true
  actionMessage.value = ''

  try {
    const url = new URL(`/api/v1/vehicles/${vehicle.value.id}/restore`, window.location.origin)
    url.searchParams.set('organizationId', organizationId.value)

    const response = await fetch(url.toString(), {
      method: 'POST',
      credentials: 'same-origin',
      headers: {
        Accept: 'application/json',
        'X-Requested-With': 'XMLHttpRequest',
        RequestVerificationToken: document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
      }
    })

    if (!response.ok) {
      const error = await response.json().catch(() => null)
      throw new Error(error?.message ?? (isDetailingScenario.value ? 'Не удалось восстановить авто.' : 'Не удалось восстановить технику.'))
    }

    await loadCard()
  } catch (error) {
    actionMessage.value = error instanceof Error ? error.message : (isDetailingScenario.value ? 'Не удалось восстановить авто.' : 'Не удалось восстановить технику.')
  } finally {
    isSavingVehicle.value = false
  }
}

async function loadCard() {
  const request = ++latestRequest
  ++latestMaintenanceRequest
  const orgId = organizationId.value
  const vehicleId = props.vehicleId
  isLoading.value = true
  errorMessage.value = ''
  actionMessage.value = ''
  vehicle.value = null
  photos.value = []
  albums.value = []
  history.value = []
  activeRecords.value = []
  activeRecordCount.value = 0
  activeRecordError.value = ''
  progressByMaintenance.value = {}
  mediaError.value = ''
  historyError.value = ''
  maintenanceItems.value = []
  maintenanceTotalCount.value = 0
  maintenanceHasMore.value = false
  maintenanceStatus.value = ''
  maintenanceLoading.value = false
  maintenanceError.value = ''
  activePanel.value = 'history'
  selectedPhotoIndex.value = 0
  previewPhotoUrl.value = ''
  expandedMaintenanceId.value = ''
  deleteConfirmOpen.value = false

  try {
    const vehicleData = await loadVehicle(orgId, vehicleId)
    if (request !== latestRequest) return
    vehicle.value = vehicleData
    const results = await Promise.allSettled([
      loadVehicleHistory(orgId, vehicleId),
      loadVehicleMaintenancePage(orgId, vehicleId, 1, 4),
      loadVehicleMaintenancePage(orgId, vehicleId, 1, 3, 2)
    ])
    if (request !== latestRequest) return
    const [journalResult, maintenanceResult, activeResult] = results
    if (journalResult.status === 'fulfilled') history.value = journalResult.value
    else historyError.value = 'Не удалось загрузить журнал изменений.'
    await loadSavedMedia()
    if (request !== latestRequest) return
    if (maintenanceResult.status === 'fulfilled') {
      maintenanceItems.value = maintenanceResult.value.items
      maintenanceTotalCount.value = maintenanceResult.value.totalCount
      maintenancePage.value = 1
      maintenanceHasMore.value = maintenanceResult.value.hasMore
    } else maintenanceError.value = 'Не удалось загрузить историю обслуживания.'
    if (activeResult.status === 'fulfilled') {
      activeRecords.value = activeResult.value.items
      activeRecordCount.value = activeResult.value.totalCount
    } else activeRecordError.value = 'Не удалось загрузить текущие записи.'
    isLoading.value = false
    await loadRecordProgress([...activeRecords.value, ...maintenanceItems.value], request)
  } catch (error) {
    if (request !== latestRequest) return
    errorMessage.value = error instanceof Error ? error.message : (isDetailingScenario.value ? 'Не удалось загрузить карточку авто.' : 'Не удалось загрузить карточку техники.')
  } finally {
    if (request === latestRequest) isLoading.value = false
  }
}

async function loadSavedMedia() {
  const orgId = organizationId.value
  const vehicleId = props.vehicleId
  isSavedMediaLoading.value = true
  try {
    const [photoResult, albumResult] = await Promise.allSettled([
      loadVehiclePhotos(orgId, [vehicleId]),
      isAlbumsFeatureLocked.value && !showSavedMediaHistory.value ? Promise.resolve(null) : loadVehicleAlbums(orgId, vehicleId)
    ])

    if (orgId !== organizationId.value || vehicleId !== props.vehicleId) return
    if (photoResult.status === 'fulfilled') photos.value = photoResult.value[vehicleId] ?? []
    if (albumResult.status === 'fulfilled') albums.value = albumResult.value?.albums ?? []
    if (photoResult.status === 'rejected' || albumResult.status === 'rejected') mediaError.value = 'Часть фотографий или альбомов не удалось загрузить.'
  } finally {
    isSavedMediaLoading.value = false
  }
}

async function toggleSavedMediaHistory() {
  if (!isAlbumsFeatureLocked.value) return
  showSavedMediaHistory.value = !showSavedMediaHistory.value
  if (!showSavedMediaHistory.value) return

  await loadSavedMedia()
}

onMounted(() => {
  document.addEventListener('pointerdown', closeExportMenuOnOutsideClick)
  void loadCard()
})
onUnmounted(() => {
  ++latestRequest
  ++latestMaintenanceRequest
  document.removeEventListener('pointerdown', closeExportMenuOnOutsideClick)
})
watch(() => [organizationId.value, props.vehicleId], () => {
  showSavedMediaHistory.value = false
  void loadCard()
})
</script>

<template>
  <section class="entity-card-section vehicle-profile">
    <UiCard page :title="cardTitle">
      <template #actions>
        <UiButton v-if="isJournalView && vehicle" variant="secondary" :href="`#vehicles/${vehicle.id}`">{{ isDetailingScenario ? 'К автомобилю' : 'К технике' }}</UiButton>
        <template v-else-if="vehicle && !isLoading">
          <UiButton v-if="canCreateMaintenance && !vehicle.isArchived" :href="maintenanceCreateUrl">+ {{ addMaintenanceText }}</UiButton>
          <UiButton v-if="canEditVehicles" variant="secondary" :href="editVehicleUrl">Редактировать</UiButton>
          <details v-if="canArchiveVehicles" class="vehicle-profile__more">
            <summary aria-label="Другие действия">•••</summary>
            <div>
              <UiButton v-if="vehicle.isArchived" variant="secondary" :disabled="isSavingVehicle" @click="restoreVehicle">Восстановить</UiButton>
              <UiButton v-else variant="secondary" :disabled="isSavingVehicle" @click="archiveVehicle">В архив</UiButton>
            </div>
          </details>
        </template>
      </template>
      <UiLoader v-if="isLoading" :text="isDetailingScenario ? 'Загружаем карточку авто...' : 'Загружаем карточку техники...'" />
      <div v-else-if="errorMessage" class="detailing-state detailing-state-error" role="alert">{{ errorMessage }} <button type="button" @click="loadCard">Повторить</button></div>
      <div v-else-if="vehicle && isJournalView">
        <p v-if="historyError" class="vehicle-profile__error" role="alert">{{ historyError }} <button type="button" @click="loadCard">Повторить</button></p>
        <div v-else-if="history.length === 0" class="detailing-empty">Журнал изменений пока пустой.</div>
        <ol v-else class="vehicle-journal" aria-label="История изменений техники">
          <li v-for="event in history" :key="event.id" class="vehicle-journal__event">
            <div class="vehicle-journal__meta">
              <time :datetime="event.createdAt">{{ formatEventDate(event.createdAt) }}</time>
              <span v-if="event.actorDisplayName">{{ event.actorDisplayName }}</span>
            </div>
            <div class="vehicle-journal__body">
              <strong>{{ event.title }}</strong>
              <p v-if="event.description">{{ event.description }}</p>
              <span v-else class="vehicle-journal__missing">Подробности не сохранены</span>
              <a
                v-if="event.relatedEntityType === 'maintenance' && event.relatedEntityId && event.eventType !== 'maintenance_deleted'"
                :href="`#maintenance/${event.relatedEntityId}?from=vehicle`"
              >Открыть обслуживание</a>
            </div>
          </li>
        </ol>
      </div>
      <div v-else-if="vehicle" class="vehicle-profile__content">
        <div class="vehicle-profile__overview">
          <aside class="vehicle-profile__media">
            <div class="vehicle-profile__photo">
              <button v-if="primaryPhotoUrl" type="button" class="vehicle-profile__preview" aria-label="Открыть фото техники" @click="previewPhotoUrl = primaryPhotoUrl"><img :src="primaryPhotoUrl" :alt="vehicleTitle"></button>
              <div v-else class="vehicle-profile__placeholder"><img v-if="brandLogo" :src="brandLogo.src" :alt="brandLogo.name"><span>Нет фото</span></div>
              <button v-if="photos.length > 1" class="entity-photo-nav entity-photo-nav--prev" type="button" @click="showPreviousPhoto" aria-label="Предыдущее фото">‹</button>
              <button v-if="photos.length > 1" class="entity-photo-nav entity-photo-nav--next" type="button" @click="showNextPhoto" aria-label="Следующее фото">›</button>
              <div v-if="photos.length > 1" class="entity-photo-counter">{{ selectedPhotoIndex + 1 }} / {{ photos.length }}</div>
            </div>
            <div class="vehicle-profile__thumbnails">
              <button v-for="(photo, index) in photos.slice(0, 3)" :key="photo.photoId" type="button" :aria-label="`Показать фото ${index + 1}`" :aria-pressed="selectedPhotoIndex === index" @click="selectedPhotoIndex = index"><img :src="photo.url" alt=""></button>
              <a :href="photosUrl">{{ photos.length ? `Все фото · ${photos.length}` : canWrite ? 'Добавить фото' : 'Фотографии' }}</a>
            </div>
          </aside>
          <div class="vehicle-profile__identity">
            <div class="vehicle-profile__name ui-section-header"><img v-if="brandLogo" :src="brandLogo.src" :alt="`Логотип ${brandLogo.name}`"><h2>{{ getVehicleDisplayName(vehicle) }}</h2></div>
            <div class="vehicle-profile__headline">
              <strong v-if="vehicle.registrationPlate && (vehicle.brandName || vehicle.modelName || vehicle.manualBrandName || vehicle.manualModelName)" class="vehicle-profile__plate">{{ vehicle.registrationPlate }}</strong>
              <span>{{ getVehicleKindName(vehicle.kind) }}<template v-if="vehicle.yearIssue"> · {{ vehicle.yearIssue }}</template></span>
              <span v-if="vehicle.isArchived" class="vehicle-profile__archive">В архиве · {{ formatEventDate(vehicle.archiveDate) }}</span>
            </div>
            <dl class="vehicle-profile__facts">
              <div v-if="showMileageStat"><dt>{{ mileageLabel }}</dt><dd><a v-if="canShowCounters" :href="countersUrl">{{ mileageValue }}</a><span v-else>{{ mileageValue }}</span></dd></div>
              <div v-if="vehicle.color"><dt>Цвет</dt><dd>{{ vehicle.color }}</dd></div>
              <div v-if="vehicle.vin"><dt>VIN</dt><dd class="vehicle-profile__identifier">{{ vehicle.vin }}</dd></div>
              <div v-if="vehicle.organizationClientId || vehicle.organizationClientName || context.shell.scenario?.code !== 'fleet'"><dt>{{ clientLabel }}</dt><dd><a v-if="clientCardUrl" :href="clientCardUrl">{{ vehicle.organizationClientName || 'Открыть карточку' }}</a><span v-else>{{ vehicle.organizationClientName || 'Не привязан' }}</span></dd></div>
              <div v-if="vehicle.name"><dt>Внутренний номер</dt><dd>{{ vehicle.name }}</dd></div>
            </dl>
            <p v-if="vehicle.description" class="vehicle-profile__note">{{ vehicle.description }}</p>
            <details class="vehicle-profile__documents">
              <summary>Документы и сведения</summary>
              <dl class="vehicle-profile__facts">
                <div v-if="vehicle.bodyNumber"><dt>Номер кузова</dt><dd>{{ vehicle.bodyNumber }}</dd></div>
                <div v-if="vehicle.engineNumber"><dt>Номер двигателя</dt><dd>{{ vehicle.engineNumber }}</dd></div>
                <div v-if="vehicle.registrationCertificate"><dt>СТС</dt><dd>{{ vehicle.registrationCertificate }}</dd></div>
                <div v-if="vehicle.commissioningDate"><dt>Ввод в эксплуатацию</dt><dd>{{ formatDate(vehicle.commissioningDate) }}</dd></div>
                <div><dt>Добавлено</dt><dd>{{ formatEventDate(vehicle.dateTimeCreation) }}</dd></div>
              </dl>
            </details>
          </div>
        </div>
        <div v-if="canOpenDefects || canShowCounters" class="vehicle-profile__tools">
          <UiButton v-if="canOpenDefects" variant="secondary" :href="defectsUrl">Неисправности</UiButton>
          <UiButton v-if="canShowCounters" variant="secondary" :href="countersUrl">Пробег и наработка</UiButton>
        </div>
        <p v-if="mediaError" class="vehicle-profile__error" role="alert">{{ mediaError }} <button type="button" @click="loadCard">Повторить</button></p>
        <p v-if="actionMessage" class="vehicle-profile__error" role="alert">{{ actionMessage }}</p>
      </div>
    </UiCard>
    <template v-if="vehicle && !isLoading && !isJournalView && !errorMessage">
      <nav class="vehicle-profile__tabs" aria-label="Разделы карточки техники">
        <button type="button" :aria-current="activePanel === 'history' ? 'page' : undefined" @click="activePanel = 'history'">{{ maintenanceHistoryTitle }}</button>
        <button type="button" :aria-current="activePanel === 'albums' ? 'page' : undefined" @click="activePanel = 'albums'">Альбомы <span>{{ isAlbumPreview ? '—' : albums.length }}</span></button>
        <a :href="journalUrl">Журнал изменений</a>
      </nav>
      <div class="vehicle-profile__layout">
        <div class="vehicle-profile__main">
          <UiCard v-show="activePanel === 'history'" :title="maintenanceHistoryTitle">
            <template #actions><label class="vehicle-profile__filter"><span class="vehicle-profile__count">Записей: {{ maintenanceTotalCount }}</span><select v-model="maintenanceStatus" aria-label="Статус записей" @change="loadHistory()"><option value="">Все записи</option><option value="1">Запланировано</option><option value="2">В работе</option><option value="3">Выполнено</option><option value="4">Отменено</option></select></label></template>
            <p v-if="maintenanceError" class="vehicle-profile__error" role="alert">{{ maintenanceError }} <button type="button" @click="loadHistory(maintenanceItems.length > 0)">Повторить</button></p>
            <div v-if="!maintenanceLoading && !maintenanceError && !maintenanceItems.length" class="detailing-empty">{{ maintenanceStatus ? 'Записей с таким статусом пока нет.' : noMaintenanceText }}</div>
            <div v-if="maintenanceItems.length" class="entity-maintenance-list">
              <article v-for="item in maintenanceItems" :key="item.id" class="entity-maintenance-item">
                <button class="entity-maintenance-summary" type="button" @click="toggleMaintenance(item)" :aria-expanded="expandedMaintenanceId === item.id">
                  <time class="vehicle-record-date">{{ formatDate(getMaintenanceDateValue(item)) }}</time><span>
                    <strong>{{ getRecordTitle(item) }}</strong>
                    <small>{{ [item.location, item.performer].filter(Boolean).join(' · ') || getMaintenanceDateLabel(item) }}</small>
                  </span>
                  <span class="entity-badge-row">
                    <b class="entity-status-badge" :data-status="item.status">{{ getRecordStatus(item) }}</b>
                    <b v-if="item.price != null || item.grossAmount != null" class="entity-price-badge">{{ formatMoney(item.status === 4 ? 0 : Math.max(0, (item.grossAmount ?? item.price ?? 0) - (item.discountAmount ?? 0))) }}</b>
                    <span class="entity-maintenance-expand-hint">
                      <span class="entity-maintenance-chevron" :class="{ open: expandedMaintenanceId === item.id }" aria-hidden="true"></span>
                    </span>
                  </span>
                  <span v-if="getRecordProgress(item)" class="vehicle-record-progress"><progress :value="getRecordProgress(item)!.completed" :max="getRecordProgress(item)!.total" :aria-label="`Выполнено ${getRecordProgress(item)!.completed} из ${getRecordProgress(item)!.total} работ`" /><small>{{ getRecordProgress(item)!.completed }} из {{ getRecordProgress(item)!.total }} работ</small></span>
                </button>

                <div v-if="expandedMaintenanceId === item.id" class="entity-maintenance-details">
                  <dl class="entity-info-grid">
                    <div><dt>Время</dt><dd>{{ getMaintenanceTimeValue(item) }}</dd></div>
                    <div><dt>Исполнитель</dt><dd>{{ valueOrEmpty(item.performer) }}</dd></div>
                    <div><dt>Место</dt><dd>{{ valueOrEmpty(item.location) }}</dd></div>
                    <div><dt>{{ mileageLabel }}</dt><dd>{{ item.mileage != null ? `${item.mileage.toLocaleString('ru-RU')} ${vehicle.mileageUnit === 2 ? 'м/ч' : 'км'}` : 'Не указано' }}</dd></div>
                  </dl>

                  <div v-if="item.maintenanceDetails.length" class="entity-service-lines">
                    <strong>Работы и услуги</strong>
                    <div v-for="detail in item.maintenanceDetails" :key="`${item.id}-${detail.name}`">
                      <span>{{ detail.name }}</span>
                      <small>{{ detail.description || formatMoney(detail.price) }}</small>
                    </div>
                  </div>

                  <div v-if="!isDetailingScenario && item.linkedDefects.length" class="entity-service-lines">
                    <strong>Связанные неисправности</strong>
                    <div v-for="defect in item.linkedDefects" :key="defect.id">
                      <span>{{ defect.title }}</span>
                      <small>{{ defect.description || getMaintenanceStatusName(item.status) }}</small>
                    </div>
                  </div>

                  <p v-if="item.comment" class="entity-note">{{ item.comment }}</p>

                  <div class="entity-card-actions">
                    <UiButton :href="`#maintenance/${item.id}?from=vehicle`">{{ isDetailingScenario ? 'Открыть запись' : 'Открыть обслуживание' }}</UiButton>
                  </div>
                </div>
              </article>
            </div>

            <div v-if="maintenanceLoading" class="vehicle-profile__loading" role="status">Загружаем записи…</div>
            <button v-if="maintenanceHasMore && !maintenanceError" type="button" class="vehicle-profile__load-more" :disabled="maintenanceLoading" @click="loadHistory(true)">Показать ещё</button>
          </UiCard>
          <UiCard title="Альбомы">
            <template #actions><UiButton variant="secondary" :href="albumsUrl">Все альбомы →</UiButton></template>
            <WorkspaceProLock :locked="isAlbumPreview" message="Фотоальбомы доступны в PRO" feature="albums">
              <div v-if="isAlbumPreview" class="vehicle-profile__albums-preview" aria-hidden="true">
                <span v-for="index in 2" :key="index"></span>
              </div>
              <template v-else>
                <div v-if="!albums.length" class="detailing-empty">{{ mediaError ? 'Альбомы недоступны.' : 'Альбомов пока нет.' }} <a v-if="canWrite && !isAlbumsFeatureLocked" :href="albumsUrl">Создать альбом</a></div>
                <div v-if="albums.length" class="entity-album-grid">
                  <a v-for="album in (activePanel === 'albums' ? albums : albums.slice(0, 2))" :key="album.albumId" class="entity-album-item" :href="getAlbumUrl(album)">
                    <img v-if="getAlbumCover(album)" :src="getAlbumCover(album)" :alt="album.title">
                    <span v-else class="entity-album-placeholder">Альбом</span>
                    <strong>{{ album.title }}</strong>
                    <small>{{ album.photoCount }} фото</small>
                  </a>
                </div>
              </template>
            </WorkspaceProLock>
            <UiButton v-if="isAlbumsFeatureLocked" variant="secondary" :disabled="isSavedMediaLoading" @click="toggleSavedMediaHistory">
              {{ showSavedMediaHistory ? 'Скрыть сохранённые альбомы' : 'Показать сохранённые альбомы' }}
            </UiButton>
          </UiCard>
        </div>
        <aside class="vehicle-profile__sidebar" :aria-label="`${clientLabel} и текущие работы`">
          <UiCard v-if="vehicle.organizationClientId || vehicle.organizationClientName" :title="clientLabel">
            <div class="vehicle-profile__client"><span class="vehicle-profile__avatar" aria-hidden="true">{{ clientInitials }}</span><div><strong>{{ vehicle.organizationClientName || clientLabel }}</strong><a v-if="clientCardUrl" :href="clientCardUrl">{{ context.shell.scenario?.code === 'fleet' ? 'Открыть карточку' : 'Карточка клиента' }} →</a></div></div>
          </UiCard>
          <UiCard title="В работе">
            <p v-if="activeRecordError" class="vehicle-profile__error" role="alert">{{ activeRecordError }} <button type="button" @click="loadCard">Повторить</button></p>
            <p v-else-if="!activeRecords.length" class="vehicle-profile__muted">Активных работ пока нет.</p>
            <article v-for="item in activeRecords" :key="item.id" class="vehicle-profile__current">
              <span class="entity-status-badge" :data-status="item.status">{{ getRecordStatus(item) }}</span>
              <strong>{{ getRecordTitle(item) }}</strong>
              <small>{{ formatDate(getMaintenanceDateValue(item)) }} · {{ getMaintenanceTimeValue(item) }}</small>
              <span v-if="getRecordProgress(item)" class="vehicle-record-progress"><progress :value="getRecordProgress(item)!.completed" :max="getRecordProgress(item)!.total" :aria-label="`Выполнено ${getRecordProgress(item)!.completed} из ${getRecordProgress(item)!.total} работ`" /><small>{{ getRecordProgress(item)!.completed }} из {{ getRecordProgress(item)!.total }}</small></span>
              <div v-if="item.debtAmount > 0 || item.paidAmount > 0" class="vehicle-profile__debt"><span>{{ item.debtAmount > 0 ? 'Осталось оплатить' : 'Оплачено' }}</span><strong>{{ formatMoney(item.debtAmount > 0 ? item.debtAmount : item.paidAmount) }}</strong></div>
              <UiButton variant="secondary" :href="`#maintenance/${item.id}?from=vehicle`">{{ isDetailingScenario ? 'Открыть запись' : 'Открыть обслуживание' }} →</UiButton>
            </article>
            <button v-if="activeRecordCount > activeRecords.length" type="button" class="vehicle-profile__load-more" @click="showActiveRecords">Все в работе · {{ activeRecordCount }}</button>
          </UiCard>
          <UiCard :title="isDetailingScenario ? 'Отчёт по автомобилю' : 'Отчёт по технике'">
        <details ref="exportMenu" class="entity-export-menu">
          <summary :class="{ disabled: Boolean(exportingOptionKey) }"><span class="entity-export-download-icon">↓</span>{{ exportingOptionKey ? 'Формируем отчёт...' : 'Скачать отчёт' }}</summary>
          <div class="entity-export-menu__list">
            <button
              v-for="option in exportOptions"
              :key="option.key"
              type="button"
              :disabled="Boolean(exportingOptionKey)"
              @click="downloadDocument(option)"
            >
              <span class="entity-export-format-icon" :class="`entity-export-format-icon--${option.tone}`">{{ option.icon }}</span>
              <span>{{ exportingOptionKey === option.key ? 'Формируем...' : option.label }}</span>
              <span v-if="exportingOptionKey === option.key" class="entity-export-spinner" aria-hidden="true"></span>
            </button>
          </div>
        </details>
          </UiCard>
        </aside>
      </div>
    </template>
    <UiImagePreview :src="previewPhotoUrl" :title="vehicleTitle" @close="previewPhotoUrl = ''" />
    <UiConfirmDialog :open="deleteConfirmOpen" title="Переместить в архив?" :message="isDetailingScenario ? 'Авто перейдет в архив и исчезнет из активного списка.' : 'Техника перейдет в архив и исчезнет из активного списка.'" :busy="isSavingVehicle" confirm-text="В архив" tone="danger" @close="closeDeleteConfirmation" @confirm="confirmArchiveVehicle" />
  </section>
</template>

<style scoped>
.vehicle-journal {
  margin: 0;
  padding: 0;
  list-style: none;
}

.vehicle-journal__event {
  display: grid;
  grid-template-columns: 160px minmax(0, 1fr);
  gap: 24px;
  padding: 18px 0;
  border-bottom: 1px solid var(--crm-border);
}

.vehicle-journal__event:first-child { padding-top: 0; }
.vehicle-journal__event:last-child { padding-bottom: 0; border-bottom: 0; }

.vehicle-journal__meta,
.vehicle-journal__body {
  display: grid;
  align-content: start;
  gap: 8px;
  min-width: 0;
  overflow-wrap: anywhere;
}

.vehicle-journal__meta,
.vehicle-journal__missing {
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
}

.vehicle-journal__body p {
  margin: 0;
  white-space: pre-line;
  line-height: 1.65;
}

.vehicle-journal__body a {
  justify-self: start;
  font-size: 12px;
}

@container (max-width: 600px) {
  .vehicle-journal__event { grid-template-columns: minmax(0, 1fr); gap: 10px; }
  .vehicle-journal__meta { display: flex; flex-wrap: wrap; gap: 6px 16px; }
}

.vehicle-profile {
  container-type: inline-size;
}

.vehicle-profile__content {
  display: grid;
  gap: 20px;
}

.vehicle-profile__overview {
  display: grid;
  grid-template-columns: minmax(220px, 320px) minmax(0, 1fr);
  gap: 28px;
  align-items: start;
}

.vehicle-profile__media {
  display: grid;
  gap: 10px;
  min-width: 0;
  text-align: center;
  font-size: 13px;
}

.vehicle-profile__photo {
  position: relative;
  aspect-ratio: 16 / 10;
  border-radius: 8px;
  overflow: hidden;
  background: var(--crm-surface-soft, #f7f8fa);
  border: 1px solid var(--crm-line, #e6e9ee);
}

.vehicle-profile__photo-preview span {
  display: block;
  width: 100%;
  height: 100%;
  background: linear-gradient(135deg, var(--crm-surface-soft, #f7f8fa), var(--crm-line, #e6e9ee));
}

.vehicle-profile__thumbnails-preview span {
  display: block;
  width: 52px;
  height: 40px;
  border-radius: 5px;
  background: var(--crm-line, #e6e9ee);
}

.vehicle-profile__thumbnails-preview .vehicle-profile__thumbnail-link {
  width: 92px;
  height: 11px;
  margin-left: auto;
}

.vehicle-profile__albums-preview {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.vehicle-profile__albums-preview span {
  aspect-ratio: 1.1;
  border: 1px solid var(--crm-line, #e6e9ee);
  border-radius: 8px;
  background: linear-gradient(180deg, var(--crm-surface-soft, #f7f8fa) 70%, var(--crm-line, #e6e9ee) 70%);
}

.vehicle-profile__preview {
  width: 100%;
  height: 100%;
  padding: 0;
  border: 0;
  background: transparent;
  cursor: zoom-in;
}

.vehicle-profile__preview img {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.vehicle-profile__placeholder {
  height: 100%;
  display: grid;
  place-items: center;
  color: var(--crm-text-muted, #667085);
}

.vehicle-profile__identity {
  display: grid;
  gap: 14px;
  min-width: 0;
}

.vehicle-profile__headline {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  font-size: 13px;
  color: var(--crm-text-muted, #667085);
}

.vehicle-profile__plate {
  padding: 6px 10px;
  border: 1px solid var(--crm-line, #dce2e9);
  border-radius: 5px;
  font-size: 17px;
  letter-spacing: .04em;
  color: var(--crm-text, #202833);
}

.vehicle-profile__archive {
  padding: 4px 8px;
  border-radius: 4px;
  background: var(--crm-surface-soft, #f7f8fa);
}

.vehicle-profile__facts {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 18px 24px;
  margin: 0;
}

.vehicle-profile__facts div {
  min-width: 0;
}

.vehicle-profile__facts dt {
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
  margin-bottom: 5px;
}

.vehicle-profile__facts dd {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  overflow-wrap: anywhere;
}

.vehicle-profile__identifier {
  font-variant-numeric: tabular-nums;
  letter-spacing: .02em;
}

.vehicle-profile__note {
  margin: 0;
  font-size: 14px;
  line-height: 1.6;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.vehicle-profile__documents summary {
  width: fit-content;
  cursor: pointer;
  font-size: 13px;
  color: var(--crm-text-muted, #667085);
}

.vehicle-profile__documents .vehicle-profile__facts {
  padding-top: 18px;
}

.vehicle-profile__tools {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  border-top: 1px solid var(--crm-line, #e6e9ee);
  padding-top: 16px;
}

.vehicle-profile__count {
  font-size: 13px;
  color: var(--crm-text-muted, #667085);
}

.vehicle-profile__error {
  margin: 0;
  padding: 12px;
  border: 1px solid #cb6a4b;
  border-radius: 6px;
  color: var(--crm-text, #9f331f);
}

.vehicle-profile .entity-export-menu summary {
  min-height: 36px;
  font-size: 13px;
  padding: 6px 12px;
}

.vehicle-profile .entity-export-menu__list {
  min-width: min(255px, 75vw);
}

.vehicle-profile .entity-maintenance-summary {
  display: grid;
  grid-template-columns: 85px minmax(0, 1fr) auto;
  gap: 8px 16px;
  padding: 16px 0;
  text-align: left;
}

.vehicle-profile .entity-maintenance-summary > span {
  min-width: 0;
}

.vehicle-profile .entity-maintenance-summary strong {
  overflow-wrap: anywhere;
}

.vehicle-profile .entity-price-badge {
  color: var(--crm-text, #202833);
  background: transparent;
  font-size: 13px;
}

.vehicle-profile .entity-status-badge {
  color: var(--crm-text, #202833);
  background: var(--crm-surface-soft, #f7f8fa);
  font-weight: 500;
  font-size: 12px;
}

.vehicle-profile .entity-status-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.vehicle-profile .entity-status-badge::before {
  content: '';
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #8998ac;
}

.vehicle-profile .entity-status-badge[data-status="2"]::before {
  background: #e87532;
}

.vehicle-profile .entity-status-badge[data-status="2"] { background:color-mix(in srgb, #e87532 12%, var(--crm-surface, #fff)); }
.vehicle-profile .entity-status-badge[data-status="3"] { background:color-mix(in srgb, #35a692 12%, var(--crm-surface, #fff)); }
.vehicle-profile .entity-status-badge[data-status="4"] { background:color-mix(in srgb, #c04f50 10%, var(--crm-surface, #fff)); }

.vehicle-profile .entity-status-badge[data-status="3"]::before {
  background: #35a692;
}

.vehicle-profile button:focus-visible, .vehicle-profile summary:focus-visible, .vehicle-profile a:focus-visible {
  outline: 2px solid #e87532;
  outline-offset: 3px;
}

.vehicle-profile__name { display:flex; align-items:center; gap:14px; }
.vehicle-profile__name img { width:44px; height:44px; object-fit:contain; background:white; padding:4px; border-radius:8px; }
.vehicle-profile__name h2 { margin:0; font-size:24px; color:var(--crm-text, #202833); }
.vehicle-profile__note { padding:10px 12px; border:1px solid var(--crm-line, #e6e9ee); border-radius:6px; font-size:13px; color:var(--crm-text-muted, #667085); }
.vehicle-profile__thumbnails { display:flex; gap:6px; align-items:center; flex-wrap:wrap; }
.vehicle-profile__thumbnails button { width:52px; height:40px; padding:0; border:2px solid transparent; border-radius:5px; overflow:hidden; background:var(--crm-surface-soft, #f7f8fa); cursor:pointer; }
.vehicle-profile__thumbnails button[aria-pressed="true"] { border-color:var(--crm-accent, #e87532); }
.vehicle-profile__thumbnails img { width:100%; height:100%; object-fit:cover; }
.vehicle-profile__thumbnails a { margin-left:auto; font-size:12px; }
.vehicle-profile__placeholder { align-content:center; gap:12px; }
.vehicle-profile__placeholder img { width:60px; height:60px; object-fit:contain; background:white; padding:6px; border-radius:8px; }
.vehicle-profile__tabs { display:flex; flex-wrap:wrap; gap:8px; border-bottom:1px solid var(--crm-line, #e6e9ee); }
.vehicle-profile__tabs button, .vehicle-profile__tabs a { display:flex; align-items:center; gap:7px; padding:12px 14px; border:0; border-bottom:2px solid transparent; background:transparent; font:inherit; font-size:13px; color:var(--crm-text-muted, #667085); text-decoration:none; cursor:pointer; }
.vehicle-profile__tabs [aria-current="page"] { border-bottom-color:var(--crm-accent, #e87532); font-weight:650; color:var(--crm-text, #202833); }
.vehicle-profile__tabs span { padding:2px 5px; border-radius:4px; background:var(--crm-surface-soft, #f7f8fa); font-size:11px; }
.vehicle-profile__layout { display:grid; grid-template-columns:minmax(0, 1fr) 300px; align-items:start; gap:18px; }
.vehicle-profile__main, .vehicle-profile__sidebar { display:grid; gap:16px; min-width:0; }
.vehicle-profile__sidebar :deep(.ui-card__header) { padding:16px 16px 0; border-bottom:0; background:transparent; }
.vehicle-profile__sidebar :deep(.ui-card__body) { padding:16px; }
.vehicle-profile__sidebar :deep(.ui-card__title h2) { font-size:14px; }
.vehicle-profile .entity-maintenance-list { gap:0; }
.vehicle-profile .entity-maintenance-item { border:0; border-bottom:1px solid var(--crm-line, #e6e9ee); border-radius:0; }
.vehicle-profile .entity-maintenance-item:last-child { border-bottom:0; }
.vehicle-profile__client { display:flex; gap:12px; align-items:center; }
.vehicle-profile__client > div { display:grid; gap:8px; min-width:0; overflow-wrap:anywhere; }
.vehicle-profile__client strong { font-size:14px; }
.vehicle-profile__client a { font-size:12px; }
.vehicle-profile__avatar { display:grid; place-items:center; flex-shrink:0; width:42px; height:42px; border-radius:50%; background:var(--crm-surface-soft, #edf0f5); color:var(--crm-text-muted, #667085); font-weight:600; }
.vehicle-profile__current { display:grid; gap:12px; min-width:0; padding:16px 0; border-bottom:1px solid var(--crm-line, #e6e9ee); }
.vehicle-profile__current:first-of-type { padding-top:0; }
.vehicle-profile__current:last-of-type { padding-bottom:0; border-bottom:0; }
.vehicle-profile__current > strong { font-size:14px; line-height:1.5; overflow-wrap:anywhere; }
.vehicle-profile__current > .entity-status-badge { justify-self:start; }
.vehicle-profile__current > small { font-size:12px; color:var(--crm-text-muted, #667085); }
.vehicle-profile__debt { display:flex; justify-content:space-between; flex-wrap:wrap; gap:8px; padding:10px; border-radius:6px; background:color-mix(in srgb, #e87532 10%, var(--crm-surface, #fff)); font-size:12px; }
.vehicle-profile__debt strong { font-variant-numeric:tabular-nums; }
.vehicle-profile__filter { display:flex; align-items:center; flex-wrap:wrap; gap:8px; }
.vehicle-profile__filter select { border:1px solid var(--crm-line, #e6e9ee); border-radius:6px; background:var(--crm-surface, #fff); color:var(--crm-text, #202833); padding:7px; font:inherit; font-size:12px; }
.vehicle-profile__load-more { display:block; margin:16px auto 0; padding:6px 10px; border:0; background:transparent; color:var(--crm-text-muted, #667085); font:inherit; font-size:12px; text-decoration:underline; cursor:pointer; }
.vehicle-profile__load-more:disabled { opacity:.5; cursor:wait; }
.vehicle-profile__loading, .vehicle-profile__muted { font-size:13px; color:var(--crm-text-muted, #667085); }
.vehicle-profile__loading { padding:16px 0; text-align:center; }
.vehicle-profile__photo-link { margin-bottom:16px; font-size:13px; }
.vehicle-record-date { font-size:12px; color:var(--crm-text-muted, #667085); font-variant-numeric:tabular-nums; }
.vehicle-profile .entity-maintenance-summary > span:nth-child(2) { display:grid; gap:6px; }
.vehicle-profile .entity-maintenance-summary > span:nth-child(2) strong { display:-webkit-box; -webkit-line-clamp:2; -webkit-box-orient:vertical; overflow:hidden; font-size:13px; line-height:1.5; }
.vehicle-profile .entity-badge-row { display:grid; justify-items:end; gap:8px; position:relative; padding-right:20px; }
.vehicle-profile .entity-maintenance-expand-hint { position:absolute; right:0; top:50%; }
.vehicle-record-progress { display:flex; align-items:center; gap:10px; min-width:0; grid-column:2 / -1; }
.vehicle-record-progress progress { display:block; width:100%; min-width:0; height:6px; appearance:none; border:0; border-radius:9px; overflow:hidden; background:var(--crm-line, #e6e9ee); }
.vehicle-record-progress progress::-webkit-progress-bar { background:var(--crm-line, #e6e9ee); }
.vehicle-record-progress progress::-webkit-progress-value { background:var(--crm-accent, #e87532); border-radius:9px; }
.vehicle-record-progress progress::-moz-progress-bar { background:var(--crm-accent, #e87532); border-radius:9px; }
.vehicle-record-progress small { flex-shrink:0; color:var(--crm-text-muted, #667085); font-size:11px; }
.vehicle-profile .entity-album-grid { grid-template-columns:repeat(2, minmax(0, 1fr)); gap:12px; }
.vehicle-profile .entity-album-item { display:grid; grid-template-columns:90px minmax(0, 1fr); grid-template-rows:auto auto; column-gap:12px; align-content:center; border:0; padding:0; }
.vehicle-profile .entity-album-item img, .vehicle-profile .entity-album-placeholder { grid-row:1 / 3; height:70px; width:90px; border-radius:6px; }
.vehicle-profile .entity-album-item strong { align-self:end; font-size:13px; }
.vehicle-profile .entity-album-item small { align-self:start; font-size:12px; }
.vehicle-profile__more { position:relative; }
.vehicle-profile__more summary { padding:8px 12px; border:1px solid var(--crm-line, #e6e9ee); border-radius:6px; list-style:none; cursor:pointer; }
.vehicle-profile__more summary::-webkit-details-marker { display:none; }
.vehicle-profile__more > div { position:absolute; right:0; top:calc(100% + 6px); z-index:5; padding:6px; border:1px solid var(--crm-line, #e6e9ee); border-radius:6px; background:var(--crm-surface, #fff); box-shadow:0 6px 20px #0001; white-space:nowrap; }

@container (max-width: 900px) {
  .vehicle-profile__layout { grid-template-columns:minmax(0, 1fr); }
  .vehicle-profile__sidebar { grid-template-columns:repeat(2, minmax(0, 1fr)); }
  .vehicle-profile__overview {
    grid-template-columns: 180px minmax(0, 1fr);
    gap: 20px;
  }

  .vehicle-profile__facts {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

}
@container (max-width: 600px) {
  .vehicle-profile :deep(.ui-card__header) {
    flex-direction: column;
    align-items: stretch;
    gap: 12px;
  }

  .vehicle-profile :deep(.ui-card__actions) {
    justify-content: flex-start;
  }

  .vehicle-profile__overview {
    grid-template-columns: minmax(0, 1fr);
  }

  .vehicle-profile__media {
    max-width: 240px;
  }

  .vehicle-profile__facts {
    gap: 16px;
  }

  .vehicle-profile .entity-maintenance-summary {
    grid-template-columns:minmax(0, 1fr) auto;
    gap:10px;
  }
  .vehicle-record-date { grid-column:1 / -1; }
  .vehicle-record-progress { grid-column:1 / -1; }
  .vehicle-profile__sidebar { grid-template-columns:minmax(0, 1fr); }
  .vehicle-profile__tabs { gap:0; }
  .vehicle-profile__tabs button, .vehicle-profile__tabs a { padding:10px 8px; font-size:12px; }
  .vehicle-profile .entity-album-grid { grid-template-columns:minmax(0, 1fr); }

  .vehicle-profile .entity-badge-row {
    justify-content: flex-start;
    flex-wrap: wrap;
  }

}
</style>

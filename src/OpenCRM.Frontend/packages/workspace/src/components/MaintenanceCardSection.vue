<script setup lang="ts">
import { formattingCulture } from '@opencrm/ui/i18n'
import { provideWorkspaceCurrency, useWorkspaceCurrency } from '../workspaceCurrency'

import { getVehicleBrandLogo } from '../utils/vehicleBrandLogo'
import type { WorkProgress } from '../api/work'
import { getWorkdayProgress } from '../utils/workdayCard'
import { formatWorkDuration, hasRecordedWorkTime } from '../formatWorkDuration'
import { getVehicleDisplayName } from '../api/vehicles'
import WorkspaceAccessNotice from './WorkspaceAccessNotice.vue'
import MaintenanceCardActions from './MaintenanceCardActions.vue'
import { isSimpleMaintenanceWorkflow } from '../utils/maintenanceWorkflow'
import EmployeePicker from './EmployeePicker.vue'
import { assignMaintenanceEmployee } from '../api/employees'
import MaintenanceServicesEditor from './MaintenanceServicesEditor.vue'
import MaintenanceWorkProgress from './MaintenanceWorkProgress.vue'
import MaintenancePayments from './MaintenancePayments.vue'
import MaintenanceHistoryDialog from './MaintenanceHistoryDialog.vue'
import ClientConversationPanel from './ClientConversationPanel.vue'
import MaintenancePhotoComparison from './MaintenancePhotoComparison.vue'
import IntakeDamageMap from './IntakeDamageMap.vue'
import WorkspaceProLock from './WorkspaceProLock.vue'
import SchedulerBookingDialog from './SchedulerBookingDialog.vue'
import MaintenanceNotificationHistory from './MaintenanceNotificationHistory.vue'
import MaintenanceVisitReminder from './MaintenanceVisitReminder.vue'
import RepeatVisitRecommendations from './RepeatVisitRecommendations.vue'
import { loadMaxMaintenanceNotifications, type MaxNotificationRecord } from '../api/maxNotifications'
import { loadTelegramMaintenanceNotifications, type TelegramNotificationRecord } from '../api/telegramNotifications'
import { computed, inject, onMounted, provide, ref, watch } from 'vue'
import { pageBreadcrumbsKey, UiButton, UiCard, UiImagePreview, UiConfirmDialog, UiLoader, UiSelect, parseDateOnly, useLocalization } from '@opencrm/ui'
import '../maintenanceCardMessages'
import {
  deleteMaintenancePhoto,
  loadMaintenance,
  saveMaintenanceCommerce,
  loadMaintenancePhotos,
  uploadMaintenancePhotos,
  saveMaintenanceIntake,
  setMaintenancePhotoStage,
  type MaintenancePhotoStage,
  type WorkspaceMaintenanceDetail,
  type WorkspaceMaintenance,
  type WorkspaceMaintenancePhoto
} from '../api/maintenance'
import { loadVehicle, type WorkspaceVehicle } from '../api/vehicles'
import type { WorkspaceContext } from '../types'

const inheritedCurrency = useWorkspaceCurrency()
const { t } = useLocalization()

const props = defineProps<{
  context: WorkspaceContext
  maintenanceId: string
  fromVehicle?: boolean
}>()
const emit = defineEmits<{
  changed: []
}>()

const maintenance = ref<WorkspaceMaintenance | null>(null)
const workspaceCurrency = provideWorkspaceCurrency(computed(() => maintenance.value?.currencyCode ?? inheritedCurrency.value))
const historicalCurrencyReadOnly = computed(() => {
  const orderCurrency = maintenance.value?.currencyCode
  const organizationCurrency = props.context.organization.currencyCode
  const currenciesDiffer = Boolean(orderCurrency && organizationCurrency
    && orderCurrency.toUpperCase() !== organizationCurrency.toUpperCase())

  return maintenance.value?.canChangeFinancialData === false || currenciesDiffer
})
const historicalCurrencyReadOnlyMessage = computed(() => t('maintenanceCard.historicalCurrencyReadOnly', {
  orderCurrency: maintenance.value?.currencyCode ?? inheritedCurrency.value,
  workspaceCurrency: props.context.organization.currencyCode ?? inheritedCurrency.value
}))
const netAmount = computed(() => maintenance.value?.status === 4
  ? 0
  : Math.max(0, (maintenance.value?.grossAmount ?? maintenance.value?.price ?? 0) - (maintenance.value?.discountAmount ?? 0)))
const plannedStartLocal = computed(() => maintenance.value?.plannedStartTime
  ? `${(maintenance.value.plannedDate ?? maintenance.value.date).slice(0, 10)}T${maintenance.value.plannedStartTime.slice(0, 5)}` : '')
const plannedEndLocal = computed(() => maintenance.value?.plannedEndTime
  ? `${(maintenance.value.plannedEndDate ?? maintenance.value.plannedDate ?? maintenance.value.date).slice(0, 10)}T${maintenance.value.plannedEndTime.slice(0, 5)}` : '')
const vehicle = ref<WorkspaceVehicle | null>(null)
const parentBreadcrumbs = inject(pageBreadcrumbsKey, undefined)
const navigationSuffix = computed(() => props.fromVehicle ? '?from=vehicle' : '')
provide(pageBreadcrumbsKey, computed(() => {
  if (!props.fromVehicle) return parentBreadcrumbs?.value ?? []
  const items = [{ title: props.context.shell.menu?.find(item => item.code === 'vehicles')?.title ?? t('maintenanceCard.equipment'), href: '#vehicles' }]
  if (vehicle.value) {
    items.push({ title: getVehicleDisplayName(vehicle.value, true), href: `#vehicles/${vehicle.value.id}` })
  }
  return items
}))
const photos = ref<WorkspaceMaintenancePhoto[]>([])
const intakeDamagePhotoIds = ref<Set<string> | null>(null)
const maxNotifications = ref<MaxNotificationRecord[]>([])
const maxNotificationError = ref('')
const telegramNotifications = ref<TelegramNotificationRecord[]>([])
const telegramNotificationError = ref('')
const notificationsLoading = ref(false)
const notificationsRefreshKey = ref(0)
const showNotificationHistory = ref(false)
let notificationRequestId = 0
const isLoading = ref(false)
const isSavingPhotos = ref(false)
const intakeEditing = ref(false)
const showIntakeHistory = ref(false)
const intakeHistoryLoading = ref(false)
const intakeHistoryError = ref('')
const intakeNotes = ref('')
const intakeMileage = ref('')
const intakeCompleteness = ref('')
const intakeVisibleDamage = ref('')
const intakeBusy = ref(false)
const intakeMessage = ref('')
const intakeSuccessMessage = ref('')
const intakeMileageError = ref('')
const settlementPanel = ref<HTMLElement | null>(null)
function showSettlement() {
  settlementPanel.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  settlementPanel.value?.focus({ preventScroll: true })
}
const activePanel = ref<'work' | 'intake' | 'notifications' | 'messages'>('work')
const workProgress = ref<WorkProgress[] | null>(null)
const simpleWorkflow = computed(() => isSimpleMaintenanceWorkflow(props.context.organization))
const brandLogo = computed(() => getVehicleBrandLogo(vehicle.value?.brandLogoKey))
const workByDetail = computed(() => new Map(workProgress.value?.map(work => [work.detailId, work]) ?? []))
const progress = computed(() => getWorkdayProgress(serviceDetails.value.length,
  serviceDetails.value.filter(detail => detail.detailId && workByDetail.value.get(detail.detailId)?.status === 4).length))
const showWorkProgress = computed(() => !simpleWorkflow.value && serviceDetails.value.some(detail => {
  const work = detail.detailId ? workByDetail.value.get(detail.detailId) : undefined
  return work && (work.hasAssignedEmployee || work.version > 0 || work.status >= 2)
}))
const recordStages = computed(() => [
  { title: t('maintenanceCard.planned'), color: 'planned' },
  { title: t('maintenanceCard.inProgress'), color: 'active' },
  { title: maintenance.value?.status === 3 && !maintenance.value.readyForHandoverAt && !maintenance.value.handedOverAt ? t('maintenanceCard.completed') : t('maintenanceCard.ready'), color: 'ready' },
  { title: t('maintenanceCard.handedOver'), color: 'issued' }
])
const currentStage = computed(() => {
  if (maintenance.value?.status === 4) return -1
  if (maintenance.value?.handedOverAt) return 3
  if (maintenance.value?.status === 3) return 2
  return maintenance.value?.status === 2 ? 1 : 0
})
function workStatusName(detail: WorkspaceMaintenanceDetail) {
  if (workProgress.value === null) return t('maintenanceCard.noData')
  const work = detail.detailId ? workByDetail.value.get(detail.detailId) : undefined
  const status = work?.status
  if (status === 1 && work?.hasAssignedEmployee === false) return t('maintenanceCard.notAssigned')
  return status === 4 ? t('maintenanceCard.completedFeminine') : status === 3 ? t('maintenanceCard.paused') : status === 2 ? t('maintenanceCard.inProgress') : status === 1 ? t('maintenanceCard.assigned') : t('maintenanceCard.notAssigned')
}
const intakeMileageLabel = computed(() => vehicle.value?.mileageUnit === 2 ? t('maintenanceCard.intakeEngineHours') : t('maintenanceCard.intakeMileage'))
const intakeMileageUnit = computed(() => vehicle.value?.mileageUnit === 2 ? t('maintenanceCard.mileageUnitHours') : t('maintenanceCard.mileageUnitKilometers'))
const photoStages: { stage: MaintenancePhotoStage; title: string }[] = [
  { stage: 1, title: t('maintenanceCard.beforeWork') }, { stage: 2, title: t('maintenanceCard.afterWork') }, { stage: 0, title: t('maintenanceCard.uncategorized') }
]
const photoGroups = computed(() => photoStages
  .filter(group => group.stage !== 0 || photos.value.some(photo => photo.stage === 0))
  .map(group => ({ ...group, photos: photos.value.filter(photo => photo.stage === group.stage) })))
const documentActionKey = ref('')
const errorMessage = ref('')
const photoMessage = ref('')
const documentMessage = ref('')
let cardRequestId = 0
let loadedOrganizationId = ''
let loadedMaintenanceId = ''
const photoDeleteConfirm = ref<WorkspaceMaintenancePhoto | null>(null)
const comparisonOpen = ref(false)
const historyOpen = ref(false)
const editingServices = ref(false)
const savingServices = ref(false)
const serviceError = ref('')
const employeeDialog = ref<HTMLDialogElement | null>(null)
const employeeDraft = ref('')
const assigningEmployee = ref(false)
const employeeError = ref('')
let employeeDraftVersion: number | undefined
function editEmployee() {
  employeeDraft.value = maintenance.value?.employeeId ?? ''
  employeeDraftVersion = maintenance.value?.version
  employeeError.value = ''
  employeeDialog.value?.showModal()
}
async function saveEmployeeAssignment() {
  if (!maintenance.value || assigningEmployee.value) return
  const organizationId = props.context.organization.id
  const maintenanceId = props.maintenanceId
  cardRequestId++
  assigningEmployee.value = true
  employeeError.value = ''
  try {
    await assignMaintenanceEmployee(organizationId, maintenanceId, employeeDraft.value, employeeDraftVersion)
    if (props.context.organization.id !== organizationId || props.maintenanceId !== maintenanceId) return
    await loadCard(true)
    if (props.context.organization.id !== organizationId || props.maintenanceId !== maintenanceId) return
    employeeDialog.value?.close()
    emit('changed')
  } catch (e) {
    if (props.context.organization.id === organizationId && props.maintenanceId === maintenanceId) {
      employeeError.value = e instanceof Error ? e.message : t('maintenanceCard.assignEmployeeError')
    }
  }
  finally { assigningEmployee.value = false }
}
const serviceDraft = ref<WorkspaceMaintenanceDetail[]>([])
const servicesDialog = ref<HTMLDialogElement | null>(null)
let serviceDraftVersion: number | undefined
const vatDraft = ref<{ lineIndex: number; name: string; vatRateCode: string }[]>([])
const vatDialog = ref<HTMLDialogElement | null>(null)
const commonVatRate = ref('')
const vatRateOptions = [
  { value: 'without_vat', label: t('maintenanceCard.withoutVat') },
  ...['0', '5', '7', '10', '20', '22'].map(value => ({ value, label: `${value} %` }))
]
let vatDraftVersion: number | undefined
const editingVat = ref(false)
const savingVat = ref(false)
const vatError = ref('')
const serviceEditor = ref<InstanceType<typeof MaintenanceServicesEditor> | null>(null)
const canEditServices = computed(() => canWrite.value && maintenance.value?.canEditServices === true && !maintenance.value.handedOverAt && maintenance.value.status !== 4)
const canManageVat = computed(() => canWrite.value && maintenance.value?.canManagePayments === true)
function editServices() {
  if (!canEditServices.value) return
  vatDialog.value?.close()
  editingVat.value = false
  serviceDraft.value = serviceDetails.value.map(item => ({ ...item }))
  serviceDraftVersion = maintenance.value?.version
  serviceError.value = ''
  editingServices.value = true
  servicesDialog.value?.showModal()
}
function closeServices() {
  if (savingServices.value) return
  servicesDialog.value?.close()
  editingServices.value = false
}
function editVat() {
  if (!canManageVat.value || !maintenance.value) return
  servicesDialog.value?.close()
  editingServices.value = false
  vatDraftVersion = maintenance.value.version
  vatDraft.value = maintenance.value.maintenanceDetails
    .filter(detail => detail.price != null)
    .map((detail, lineIndex) => ({ lineIndex, name: detail.name, vatRateCode: validVatRate(detail.vatRateCode) }))
  const rates = new Set(vatDraft.value.map(line => line.vatRateCode))
  commonVatRate.value = rates.size === 1 ? vatDraft.value[0]?.vatRateCode ?? '' : ''
  vatError.value = ''
  editingVat.value = true
  vatDialog.value?.showModal()
}
function applyCommonVatRate() {
  if (!commonVatRate.value || savingVat.value) return
  vatDraft.value.forEach(line => { line.vatRateCode = commonVatRate.value })
  vatError.value = ''
}
function closeVat() {
  if (savingVat.value) return
  vatDialog.value?.close()
  editingVat.value = false
}
async function saveVat() {
  if (!canManageVat.value || !maintenance.value || savingVat.value) return
  if (vatDraft.value.some(line => !validVatRate(line.vatRateCode))) { vatError.value = t('maintenanceCard.vatRequired') ; return }
  const organizationId = props.context.organization.id
  const maintenanceId = props.maintenanceId
  cardRequestId++
  savingVat.value = true
  vatError.value = ''
  try {
    await saveMaintenanceCommerce(organizationId, maintenanceId, 'vat-rates', {
      version: vatDraftVersion,
      lines: vatDraft.value.map(line => ({ lineIndex: line.lineIndex, vatRateCode: line.vatRateCode }))
    }, 'PUT')
    if (props.context.organization.id !== organizationId || props.maintenanceId !== maintenanceId) return
    await loadCard(true)
    if (props.context.organization.id !== organizationId || props.maintenanceId !== maintenanceId) return
    vatDialog.value?.close()
    editingVat.value = false
    emit('changed')
  } catch (error) {
    if (props.context.organization.id === organizationId && props.maintenanceId === maintenanceId) {
      vatError.value = error instanceof Error ? error.message : t('maintenanceCard.saveVatError')
    }
  } finally {
    savingVat.value = false
  }
}
async function saveServices() {
  if (!canEditServices.value || !maintenance.value || savingServices.value) return
  const serviceValidationError = serviceEditor.value?.validate()
  if (serviceValidationError === undefined) { serviceError.value = t('maintenanceCard.validateServicesError'); return }
  if (serviceValidationError) { serviceError.value = serviceValidationError; return }
  const organizationId = props.context.organization.id
  const maintenanceId = props.maintenanceId
  cardRequestId++
  savingServices.value = true
  serviceError.value = ''
  try {
    await saveMaintenanceCommerce(organizationId, maintenanceId, 'services', {
      version: serviceDraftVersion, details: serviceDraft.value
    }, 'PUT')
    if (props.context.organization.id !== organizationId || props.maintenanceId !== maintenanceId) return
    await loadCard(true)
    if (props.context.organization.id !== organizationId || props.maintenanceId !== maintenanceId) return
    servicesDialog.value?.close()
    editingServices.value = false
    emit('changed')
  } catch (e) {
    if (props.context.organization.id === organizationId && props.maintenanceId === maintenanceId) {
      serviceError.value = e instanceof Error ? e.message : t('maintenanceCard.saveServicesError')
    }
  }
  finally { savingServices.value = false }
}
async function paymentChanged() {
  await loadCard(true)
  emit('changed')
}
const canComparePhotos = computed(() => photos.value.some(photo => photo.stage === 1) && photos.value.some(photo => photo.stage === 2))
const previewPhoto = ref<WorkspaceMaintenancePhoto | null>(null)
const maxMaintenancePhotoFiles = 10
const maxMaintenancePhotoBatchSize = 100 * 1024 * 1024

const isDetailingScenario = computed(() => props.context.shell.scenario?.code === 'detailing')
const canWrite = computed(() => props.context.workspaceAccess?.isReadOnly !== true && !historicalCurrencyReadOnly.value)
const canEditIntake = computed(() => canWrite.value && props.context.shell.scenario?.modules?.intake !== false)
const canEditPhotos = computed(() => canWrite.value && props.context.shell.scenario?.modules?.photos !== false)
const hasLockedIntake = computed(() => props.context.shell.scenario?.modules?.intake === false
  && props.context.shell.scenario?.modules?.photos === false)
const hasLockedNotifications = computed(() => props.context.shell.scenario?.modules?.messaging === false)
watch(hasLockedNotifications, () => {
  notificationRequestId += 1
  showNotificationHistory.value = false
  notificationsLoading.value = false
  maxNotifications.value = []
  telegramNotifications.value = []
  maxNotificationError.value = ''
  telegramNotificationError.value = ''
})
const isIntakePreview = computed(() => hasLockedIntake.value && !showIntakeHistory.value)
watch(hasLockedIntake, locked => {
  if (!locked) return
  showIntakeHistory.value = false
  photos.value = []
  intakeEditing.value = false
  previewPhoto.value = null
  comparisonOpen.value = false
})
const canUseTeam = computed(() => canWrite.value && props.context.shell.scenario?.modules?.members !== false)
const title = computed(() => {
  if (!maintenance.value) {
    return isDetailingScenario.value ? t('maintenanceCard.appointmentCard') : t('maintenanceCard.maintenanceCard')
  }

  const date = formatDate(maintenance.value.plannedDate ?? maintenance.value.date)
  return isDetailingScenario.value
    ? t('maintenanceCard.appointmentOn', { date })
    : t('maintenanceCard.maintenanceOn', { date })
})
const vehicleTitle = computed(() => {
  if (!vehicle.value) {
    return t('maintenanceCard.car')
  }

  return [getVehicleDisplayName(vehicle.value, true), vehicle.value.yearIssue].filter(Boolean).join(' · ')
})
const readOnlyMessage = computed(() =>
  historicalCurrencyReadOnly.value
    ? historicalCurrencyReadOnlyMessage.value
    : props.context.workspaceAccess?.readOnlyMessage ?? t('maintenanceCard.readOnly')
)
const billingHref = computed(() => '#billing')
const documentLinks = computed(() => {
  if (!maintenance.value) {
    return []
  }

  const organizationId = props.context.organization.id
  const maintenanceId = maintenance.value.id
  const links = [
    {
      title: t('maintenanceCard.workOrderPdf'),
      href: `/api/v1/documents/pdf/maintenance/${maintenanceId}/work-order?organizationId=${organizationId}`,
      key: 'work-order-pdf'
    },
    {
      title: t('maintenanceCard.workOrderWord'),
      href: `/api/v1/documents/word/maintenance/${maintenanceId}/work-order?organizationId=${organizationId}`,
      key: 'work-order-word'
    },
    {
      title: t('maintenanceCard.reportPdf'),
      href: `/api/v1/documents/pdf/maintenance/${maintenanceId}?organizationId=${organizationId}`,
      key: 'report-pdf'
    },
    {
      title: t('maintenanceCard.reportWithPhotosPdf'),
      href: `/api/v1/documents/pdf/maintenance/${maintenanceId}?organizationId=${organizationId}&includePhotos=true`,
      key: 'report-pdf-photos'
    }
  ]

  if (maintenance.value.status === 3) {
    links.unshift(
      {
        title: t('maintenanceCard.workActPdf'),
        href: `/api/v1/documents/pdf/maintenance/${maintenanceId}/work-act?organizationId=${organizationId}`,
        key: 'work-act-pdf'
      },
      {
        title: t('maintenanceCard.workActWord'),
        href: `/api/v1/documents/word/maintenance/${maintenanceId}/work-act?organizationId=${organizationId}`,
        key: 'work-act-word'
      }
    )
  }

  return links.map(link => ({ ...link, isAvailable: props.context.workspaceAccess?.exportsEnabled !== false }))
})
const serviceDetails = computed(() => {
  if (!maintenance.value) {
    return []
  }

  const details = maintenance.value.maintenanceDetails
  return details.filter(detail => !hasMaterial(detail))
})
const materialDetails = computed(() => maintenance.value?.maintenanceDetails.filter(detail => hasMaterial(detail)) ?? [])

function validVatRate(code?: string | null) {
  return vatRateOptions.find(option => option.value === code)?.value ?? ''
}

function formatVatRate(code?: string | null) {
  if (code === 'without_vat') return t('maintenanceCard.withoutVat')
  return validVatRate(code) ? t('maintenanceCard.russianVatRate', { rate: code }) : t('maintenanceCard.vatNotSpecified')
}

function formatHandoverTime(value: string) {
  return new Intl.DateTimeFormat(formattingCulture.value, {
    day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
    timeZone: props.context.organization.timeZoneId || 'UTC'
  }).format(new Date(value))
}

function getStatusName(status: number) {
  return status === 1
    ? t('maintenanceCard.planned')
    : status === 2
      ? t('maintenanceCard.inProgress')
      : status === 3
        ? maintenance.value?.handedOverAt ? t('maintenanceCard.issued') : maintenance.value?.readyForHandoverAt ? t('maintenanceCard.readyForHandover') : t('maintenanceCard.completed')
        : status === 4
          ? t('maintenanceCard.cancelled')
          : t('maintenanceCard.statusNotSpecified')
}

function getPaymentStatusName(status?: string | null) {
  switch (status) {
    case 'overpaid':
      return t('maintenanceCard.overpaid')
    case 'no_payment_required':
      return t('maintenanceCard.noPaymentRequired')
    case 'paid':
      return t('maintenanceCard.paid')
    case 'partial':
      return t('maintenanceCard.partiallyPaid')
    case 'unpaid':
      return t('maintenanceCard.balanceDue')
    default:
      return t('maintenanceCard.notSpecified')
  }
}

function formatDate(value?: string | null) {
  if (!value) {
    return t('maintenanceCard.none')
  }

  return parseDateOnly(value.slice(0, 10))?.toLocaleDateString(formattingCulture.value, { year: 'numeric', month: '2-digit', day: '2-digit' }) ?? ''
}

function formatInstantDate(value?: string | null) {
  if (!value) return ''

  return new Intl.DateTimeFormat(formattingCulture.value, {
    dateStyle: 'medium', timeStyle: 'short', timeZone: props.context.organization.timeZoneId || 'UTC'
  }).format(new Date(value))
}

function formatTime(value?: string | null) {
  if (!value) {
    return ''
  }

  return value.slice(0, 5)
}

function formatMoney(value?: number | null) {
  if (value === null || value === undefined) {
    return t('maintenanceCard.amountNotSpecified')
  }

  return new Intl.NumberFormat(formattingCulture.value, { style: 'currency', currency: workspaceCurrency.value, maximumFractionDigits: 2 }).format(value)
}

function hasMaterial(detail: WorkspaceMaintenanceDetail) {
  return Boolean(
    detail.materialCatalogItemId ||
    detail.materialName ||
    detail.materialQuantity !== null && detail.materialQuantity !== undefined
  )
}

function getFileNameFromDisposition(contentDisposition: string | null, fallbackName: string) {
  if (!contentDisposition) {
    return fallbackName
  }

  const encodedMatch = contentDisposition.match(/filename\*=UTF-8''([^;]+)/i)
  if (encodedMatch?.[1]) {
    return decodeURIComponent(encodedMatch[1].replace(/"/g, ''))
  }

  const match = contentDisposition.match(/filename="?([^"]+)"?/i)
  return match?.[1] || fallbackName
}

async function getDocumentErrorMessage(response: Response, fallbackMessage: string) {
  try {
    const text = await response.text()
    if (!text) {
      return fallbackMessage
    }

    const parsed = JSON.parse(text)
    return parsed.message || fallbackMessage
  } catch {
    return fallbackMessage
  }
}

async function downloadDocument(link: { href: string, title: string, key: string, isAvailable: boolean }) {
  if (!link.isAvailable) return
  if (documentActionKey.value) {
    return
  }

  documentActionKey.value = link.key
  documentMessage.value = ''

  try {
    const response = await fetch(link.href, {
      method: 'GET',
      credentials: 'same-origin',
      headers: {
        'X-Requested-With': 'XMLHttpRequest'
      }
    })

    if (!response.ok) {
      throw new Error(await getDocumentErrorMessage(response, t('maintenanceCard.documentDownloadError')))
    }

    const blob = await response.blob()
    const downloadUrl = window.URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = downloadUrl
    anchor.download = getFileNameFromDisposition(response.headers.get('content-disposition'), link.title)
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
    window.setTimeout(() => window.URL.revokeObjectURL(downloadUrl), 1000)
  } catch (error) {
    documentMessage.value = error instanceof Error ? error.message : t('maintenanceCard.documentDownloadError')
  } finally {
    documentActionKey.value = ''
  }
}

function collectFiles(event: Event) {
  const input = event.target as HTMLInputElement
  const files = Array.from(input.files ?? [])
  input.value = ''
  return files
}

function validatePhotoFiles(files: File[]) {
  if (files.length > maxMaintenancePhotoFiles) {
    return t('maintenanceCard.photoCountLimit', { count: maxMaintenancePhotoFiles })
  }

  const totalSize = files.reduce((sum, file) => sum + file.size, 0)
  if (totalSize > maxMaintenancePhotoBatchSize) {
    return t('maintenanceCard.photoSizeLimit', { size: maxMaintenancePhotoBatchSize / 1024 / 1024 })
  }

  return ''
}

async function workflowChanged() {
  await loadCard(true)
  emit('changed')
}

async function reloadPhotos() {
  const organizationId = props.context.organization.id
  const maintenanceId = props.maintenanceId
  const photoData = await loadMaintenancePhotos(organizationId, maintenanceId)
  updateMaintenancePhotos(organizationId, maintenanceId, photoData)
}

async function toggleIntakeHistory() {
  if (intakeHistoryLoading.value) return
  intakeHistoryError.value = ''
  if (showIntakeHistory.value) {
    showIntakeHistory.value = false
    photos.value = []
    return
  }
  const organizationId = props.context.organization.id
  const maintenanceId = props.maintenanceId
  intakeHistoryLoading.value = true
  try {
    await reloadPhotos()
    if (props.context.organization.id !== organizationId || props.maintenanceId !== maintenanceId) return
    showIntakeHistory.value = true
  } catch {
    if (props.context.organization.id === organizationId && props.maintenanceId === maintenanceId) {
      intakeHistoryError.value = t('maintenanceCard.intakeHistoryError')
    }
  } finally {
    intakeHistoryLoading.value = false
  }
}

function updateMaintenancePhotos(
  organizationId: string,
  maintenanceId: string,
  photoData: WorkspaceMaintenancePhoto[]
) {
  if (organizationId !== props.context.organization.id || maintenanceId !== props.maintenanceId) return
  photos.value = photoData
}

function updateIntakeDamagePhotoIds(
  organizationId: string,
  maintenanceId: string,
  photoIds: string[]
) {
  if (organizationId !== props.context.organization.id || maintenanceId !== props.maintenanceId) return
  intakeDamagePhotoIds.value = new Set(photoIds)
}

async function refreshAfterIntakeDamageChange() {
  await loadCard(true)
  emit('changed')
}

async function uploadPhotos(event: Event, stage: MaintenancePhotoStage = 0) {
  const files = collectFiles(event)
  if (!files.length || isSavingPhotos.value || !canEditPhotos.value) {
    return
  }

  const validationError = validatePhotoFiles(files)
  if (validationError) {
    photoMessage.value = validationError
    return
  }

  isSavingPhotos.value = true
  photoMessage.value = ''

  try {
    await uploadMaintenancePhotos(props.context.organization.id, props.maintenanceId, files, stage)
    await reloadPhotos()
  } catch (error) {
    photoMessage.value = error instanceof Error ? error.message : t('maintenanceCard.uploadPhotosError')
  } finally {
    isSavingPhotos.value = false
  }
}

async function removePhoto(photo: WorkspaceMaintenancePhoto) {
  if (isSavingPhotos.value || !canEditPhotos.value) {
    return
  }

  photoDeleteConfirm.value = photo
}

function closePhotoDeleteConfirmation() {
  if (!isSavingPhotos.value) {
    photoDeleteConfirm.value = null
  }
}

async function confirmRemovePhoto() {
  const photo = photoDeleteConfirm.value
  if (!photo || isSavingPhotos.value || !canEditPhotos.value) {
    return
  }

  isSavingPhotos.value = true
  photoMessage.value = ''

  try {
    await deleteMaintenancePhoto(props.context.organization.id, props.maintenanceId, photo.photoId)
    await reloadPhotos()
  } catch (error) {
    photoMessage.value = error instanceof Error ? error.message : t('maintenanceCard.deletePhotoError')
  } finally {
    isSavingPhotos.value = false
    photoDeleteConfirm.value = null
  }
}

function editIntake() {
  intakeMileage.value = maintenance.value?.intakeMileage?.toString() ?? ''
  intakeCompleteness.value = maintenance.value?.intakeCompleteness ?? ''
  intakeVisibleDamage.value = maintenance.value?.intakeVisibleDamage ?? ''
  intakeNotes.value = maintenance.value?.intakeNotes ?? ''
  intakeMessage.value = ''
  intakeMileageError.value = ''
  intakeEditing.value = true
}

function closeIntake() {
  if (intakeBusy.value) return
  intakeEditing.value = false
  intakeMessage.value = ''
  intakeMileageError.value = ''
}

function validateIntakeMileage() {
  const mileageText = intakeMileage.value.trim()
  if (!mileageText) {
    intakeMileageError.value = ''

    return true
  }

  const isEngineHours = vehicle.value?.mileageUnit === 2
  if (!/^\d+$/.test(mileageText)) {
    intakeMileageError.value = isEngineHours
      ? t('maintenanceCard.engineHoursInvalid')
      : t('maintenanceCard.mileageInvalid')

    return false
  }

  const mileage = Number(mileageText)
  if (!Number.isSafeInteger(mileage) || mileage > (isEngineHours ? 2147483647 : 9999999)) {
    intakeMileageError.value = isEngineHours
      ? t('maintenanceCard.engineHoursTooHigh')
      : t('maintenanceCard.mileageTooHigh', { limit: '9 999 999' })

    return false
  }

  intakeMileageError.value = ''

  return true
}

async function saveIntake() {
  if (intakeBusy.value || !canEditIntake.value) return
  intakeMessage.value = ''
  if (!validateIntakeMileage()) return

  const mileageText = intakeMileage.value.trim()
  const mileage = mileageText ? Number(mileageText) : null
  if (mileage === null && !intakeCompleteness.value.trim() && !intakeVisibleDamage.value.trim() && !intakeNotes.value.trim()) {
    intakeMessage.value = t('maintenanceCard.intakeRequired')
    return
  }
  const organizationId = props.context.organization.id
  const maintenanceId = props.maintenanceId
  cardRequestId++
  intakeBusy.value = true
  intakeMessage.value = ''
  try {
    await saveMaintenanceIntake(organizationId, maintenanceId, {
      mileage,
      completeness: intakeCompleteness.value.trim(),
      visibleDamage: intakeVisibleDamage.value.trim(),
      notes: intakeNotes.value.trim()
    })
    if (props.context.organization.id !== organizationId || props.maintenanceId !== maintenanceId) return
    await loadCard(true)
    if (props.context.organization.id !== organizationId || props.maintenanceId !== maintenanceId) return
    intakeEditing.value = false
    intakeSuccessMessage.value = props.context.workspaceAccess?.exportsEnabled
      ? t('maintenanceCard.intakeSavedWithDocument')
      : t('maintenanceCard.intakeSaved')
    emit('changed')
  } catch (error) {
    if (props.context.organization.id === organizationId && props.maintenanceId === maintenanceId) {
      intakeMessage.value = error instanceof Error ? error.message : t('maintenanceCard.saveIntakeError')
    }
  } finally {
    intakeBusy.value = false
  }
}

async function changePhotoStage(photo: WorkspaceMaintenancePhoto, event: Event) {
  const select = event.target as HTMLSelectElement
  const stage = Number(select.value) as MaintenancePhotoStage
  if (isSavingPhotos.value || !canEditPhotos.value) return
  isSavingPhotos.value = true
  photoMessage.value = ''
  try {
    await setMaintenancePhotoStage(props.context.organization.id, props.maintenanceId, photo.photoId, stage)
    photo.stage = stage
  } catch (error) {
    select.value = String(photo.stage)
    photoMessage.value = error instanceof Error ? error.message : t('maintenanceCard.photoStageError')
  } finally {
    isSavingPhotos.value = false
  }
}

async function loadCard(silent = false) {
  const currentRequestId = ++cardRequestId
  const organizationId = props.context.organization.id
  const maintenanceId = props.maintenanceId
  const identityChanged = loadedOrganizationId !== organizationId || loadedMaintenanceId !== maintenanceId
  const preserveDraft = silent && !identityChanged && maintenance.value?.id === maintenanceId
  if (!preserveDraft) {
    if (identityChanged) {
      maintenance.value = null
      vehicle.value = null
      photos.value = []
      intakeDamagePhotoIds.value = null
      workProgress.value = null
      activePanel.value = 'work'
      intakeEditing.value = false
      showIntakeHistory.value = false
      intakeHistoryError.value = ''
      intakeNotes.value = ''
      intakeMileage.value = ''
      intakeCompleteness.value = ''
      intakeVisibleDamage.value = ''
      intakeMessage.value = ''
      intakeSuccessMessage.value = ''
      intakeMileageError.value = ''
      editingServices.value = false
      servicesDialog.value?.close()
      serviceDraft.value = []
      serviceDraftVersion = undefined
      serviceError.value = ''
      editingVat.value = false
      vatDialog.value?.close()
      commonVatRate.value = ''
      vatDraft.value = []
      vatDraftVersion = undefined
      vatError.value = ''
      employeeDialog.value?.close()
      employeeDraft.value = ''
      employeeDraftVersion = undefined
      employeeError.value = ''
      photoDeleteConfirm.value = null
      previewPhoto.value = null
      comparisonOpen.value = false
      documentMessage.value = ''
    }
    comparisonOpen.value = false
    isLoading.value = true
    photoMessage.value = ''
    maxNotifications.value = []
    maxNotificationError.value = ''
    telegramNotifications.value = []
    telegramNotificationError.value = ''
  }
  errorMessage.value = ''

  try {
    const maintenanceData = await loadMaintenance(organizationId, maintenanceId)
    if (currentRequestId !== cardRequestId
      || props.context.organization.id !== organizationId
      || props.maintenanceId !== maintenanceId) return
    maintenance.value = maintenanceData
    loadedOrganizationId = organizationId
    loadedMaintenanceId = maintenanceId
    if (!preserveDraft) {
      intakeMileage.value = maintenanceData.intakeMileage?.toString() ?? ''
      intakeCompleteness.value = maintenanceData.intakeCompleteness ?? ''
      intakeVisibleDamage.value = maintenanceData.intakeVisibleDamage ?? ''
      intakeNotes.value = maintenanceData.intakeNotes ?? ''
      intakeEditing.value = false
      intakeMessage.value = ''
      intakeSuccessMessage.value = ''
      intakeMileageError.value = ''
    }

    const [vehicleData, photoData] = await Promise.all([
      loadVehicle(organizationId, maintenanceData.vehicleId),
      props.context.shell.scenario?.modules?.photos === false && !showIntakeHistory.value
        ? Promise.resolve([]) : loadMaintenancePhotos(organizationId, maintenanceId)
    ])
    if (currentRequestId !== cardRequestId
      || props.context.organization.id !== organizationId
      || props.maintenanceId !== maintenanceId) return

    vehicle.value = vehicleData
    photos.value = photoData
    void loadNotificationHistory()
  } catch (error) {
    if (currentRequestId === cardRequestId
      && props.context.organization.id === organizationId
      && props.maintenanceId === maintenanceId) {
      errorMessage.value = error instanceof Error ? error.message : t('maintenanceCard.loadCardError')
    }
  } finally {
    if (currentRequestId === cardRequestId
      && props.context.organization.id === organizationId
      && props.maintenanceId === maintenanceId
      && !preserveDraft) isLoading.value = false
  }
}

async function loadNotificationHistory() {
  if (hasLockedNotifications.value && !showNotificationHistory.value) return
  notificationsRefreshKey.value += 1
  const current = ++notificationRequestId
  const organizationId = props.context.organization.id
  const maintenanceId = props.maintenanceId
  notificationsLoading.value = true
  const [max, telegram] = await Promise.allSettled([
    loadMaxMaintenanceNotifications(organizationId, maintenanceId),
    loadTelegramMaintenanceNotifications(organizationId, maintenanceId)
  ])
  if (current !== notificationRequestId || props.context.organization.id !== organizationId || props.maintenanceId !== maintenanceId) return
  if (max.status === 'fulfilled') maxNotifications.value = max.value
  if (telegram.status === 'fulfilled') telegramNotifications.value = telegram.value
  maxNotificationError.value = max.status === 'rejected' ? t('maintenanceCard.maxHistoryError') : ''
  telegramNotificationError.value = telegram.status === 'rejected' ? t('maintenanceCard.telegramHistoryError') : ''
  notificationsLoading.value = false
}

async function toggleNotificationHistory() {
  showNotificationHistory.value = !showNotificationHistory.value
  if (showNotificationHistory.value) {
    await loadNotificationHistory()
    return
  }
  notificationRequestId += 1
  notificationsLoading.value = false
  maxNotifications.value = []
  telegramNotifications.value = []
  maxNotificationError.value = ''
  telegramNotificationError.value = ''
}

watch(activePanel, panel => { if (panel === 'notifications') void loadNotificationHistory() })

onMounted(loadCard)
watch([() => props.context.organization.id, () => props.maintenanceId], () => {
  notificationRequestId += 1
  showNotificationHistory.value = false
  notificationsLoading.value = false
  activePanel.value = 'work'
  historyOpen.value = false
  void loadCard()
})
</script>

<template>
  <UiCard page :title="title" class="record-page">
    <UiLoader v-if="isLoading" :text="t('maintenanceCard.loadingCard')" />
    <div v-else-if="errorMessage && !maintenance" class="record-empty" role="alert">
      <p>{{ errorMessage }}</p>
      <UiButton variant="secondary" @click="loadCard()">{{ t('maintenanceCard.retry') }}</UiButton>
    </div>
    <template v-else-if="maintenance">
      <MaintenanceHistoryDialog v-if="historyOpen" :key="context.organization.id + maintenance.id" :organization-id="context.organization.id" :maintenance-id="maintenance.id" :title="title" @close="historyOpen = false" />
      <WorkspaceAccessNotice
        v-if="!canWrite"
        :can-manage-billing="context.organization.canManageBilling === true"
        :message="readOnlyMessage"
        :href="billingHref"
      />
      <div v-if="errorMessage" class="record-error" role="alert">{{ errorMessage }}</div>
      <div class="record-toolbar" role="group" :aria-label="t('maintenanceCard.recordActions')">
        <UiButton variant="secondary" @click="historyOpen = true">{{ t('maintenanceCard.serviceHistory') }}</UiButton>
        <div class="record-toolbar__actions">
          <RepeatVisitRecommendations compact
            v-if="maintenance.handedOverAt && ['detailing', 'service-station', 'autoservice'].includes(context.organization.primaryScenarioCode || context.shell.scenario?.code || '')
              && context.shell.menu?.some(item => item.code === 'clients' && item.isVisible)
              && context.shell.menu?.some(item => item.code === 'vehicles' && item.isVisible)"
            :key="`${context.organization.id}:${maintenance.id}`"
            :organization-id="context.organization.id"
            :source-maintenance-id="maintenance.id"
            :can-edit="canWrite"
          />
          <MaintenanceCardActions v-if="canWrite"
            :key="[context.organization.id, maintenance.id].join(':')"
            :organization-id="context.organization.id" :maintenance="maintenance" :time-zone-id="context.organization.timeZoneId" :simple-workflow="simpleWorkflow" @changed="workflowChanged" />
          <UiButton
            :disabled="!canWrite && !context.organization.canManageBilling"
            variant="secondary"
            :href="canWrite ? `#maintenance/${maintenance.id}/edit${navigationSuffix}` : billingHref"
          >
            {{ t('maintenanceCard.edit') }}
          </UiButton>
        </div>
      </div>
      <div class="record-overview">
        <div class="record-identity">
          <div class="record-vehicle">
            <span class="record-label">{{ isDetailingScenario ? t('maintenanceCard.vehicle') : t('maintenanceCard.equipment') }}</span>
            <a :href="`#vehicles/${maintenance.vehicleId}`"><img v-if="brandLogo" :src="brandLogo.src" :alt="t('maintenanceCard.logo', { name: brandLogo.name })" class="record-brand">{{ vehicleTitle }}</a>
          </div>
          <div v-if="simpleWorkflow || currentStage < 0" class="record-status-row">
            <div class="record-status" :data-status="maintenance.status"><span>{{ t('maintenanceCard.status') }}</span><strong>{{ getStatusName(maintenance.status) }}</strong></div>
          </div>
        </div>
        <ol v-if="!simpleWorkflow && currentStage >= 0" class="record-stages" :aria-label="t('maintenanceCard.stages')">
          <li v-for="(stage, index) in recordStages" :key="stage.color" :data-stage="stage.color"
            :class="{ 'record-stages__current': currentStage === index, 'record-stages__done': currentStage > index }"
            :aria-current="currentStage === index ? 'step' : undefined">
            <span class="record-stage__number" aria-hidden="true">{{ currentStage > index ? '✓' : index + 1 }}</span>
            <span class="record-stage__label">{{ stage.title }}</span>
          </li>
        </ol>
        <div class="record-summary">
          <dl class="record-appointment">
            <div>
              <dt>{{ t('maintenanceCard.time') }}</dt>
              <dd>
                {{ formatTime(maintenance.plannedStartTime) || t('maintenanceCard.unspecified') }}
                <template v-if="maintenance.plannedEndTime"> — <template v-if="maintenance.plannedEndDate && maintenance.plannedEndDate.slice(0, 10) !== (maintenance.plannedDate ?? maintenance.date).slice(0, 10)">{{ formatDate(maintenance.plannedEndDate) + ', ' }}</template>{{ formatTime(maintenance.plannedEndTime) }}</template>
              </dd>
            </div>
            <div>
              <dt>{{ t('maintenanceCard.location') }}</dt>
              <dd>{{ maintenance.location || t('maintenanceCard.notSpecified') }}</dd>
            </div>
            <div>
              <dt>{{ t('maintenanceCard.employee') }}</dt>
              <dd><button v-if="canWrite && maintenance.canEditServices && !editingServices" type="button" class="record-employee" :disabled="!canUseTeam" :title="canUseTeam ? t('maintenanceCard.editEmployee') : t('maintenanceCard.availableInPro')" @click="editEmployee">{{ maintenance.performer || t('maintenanceCard.assignEmployee') }}</button><template v-else>{{ maintenance.performer || t('maintenanceCard.unassigned') }}</template></dd>
            </div>
            <div v-if="!isDetailingScenario">
              <dt>{{ t('maintenanceCard.mileage') }}</dt>
              <dd>{{ maintenance.mileage ?? t('maintenanceCard.notSpecified') }}</dd>
            </div>
          </dl>
        </div>

        <dl v-if="maintenance.completedDate || maintenance.readyForHandoverAt || maintenance.handedOverAt" class="record-milestones">
          <div v-if="maintenance.completedDate"><dt>{{ t('maintenanceCard.completedDate') }}</dt><dd>{{ formatDate(maintenance.completedDate) }}</dd></div>
          <div v-if="maintenance.readyForHandoverAt && !simpleWorkflow"><dt>{{ t('maintenanceCard.readyTime') }}</dt><dd>{{ formatHandoverTime(maintenance.readyForHandoverAt) }}</dd></div>
          <div v-if="maintenance.handedOverAt"><dt>{{ t('maintenanceCard.handedOverTime') }}</dt><dd>{{ formatHandoverTime(maintenance.handedOverAt) }}</dd></div>
        </dl>
        <div v-if="showWorkProgress" class="record-progress">
          <template v-if="workProgress !== null">
            <progress :value="progress.completed" :max="progress.total" :class="{ 'record-progress__complete': progress.isComplete }" :aria-label="t('maintenanceCard.progressLabel', { completed: progress.completed, total: progress.total })" />
            <span>{{ t('maintenanceCard.progressLabel', { completed: progress.completed, total: progress.total }) }}</span>
          </template>
          <span v-else>{{ t('maintenanceCard.progressUnavailable') }}</span>
        </div>
      </div>
      <nav class="record-tabs" :aria-label="t('maintenanceCard.sections')">
        <button id="record-services-tab" type="button" :aria-current="activePanel === 'work' ? 'page' : undefined" @click="activePanel = 'work'">{{ t('maintenanceCard.services') }} <span>{{ serviceDetails.length }}</span></button>
        <button type="button" :aria-current="activePanel === 'intake' ? 'page' : undefined" @click="activePanel = 'intake'">{{ t('maintenanceCard.intakeAndPhotos') }} <span :class="{ 'record-tabs__pending': !maintenance.intakeRecordedAt }">{{ maintenance.intakeRecordedAt ? t('maintenanceCard.actReady') : t('maintenanceCard.actMissing') }}</span></button>
        <button v-if="maintenance.buyerClientId && context.shell.menu?.some(item => item.code === 'messages' && item.isVisible)" type="button" :aria-current="activePanel === 'messages' ? 'page' : undefined" @click="activePanel = 'messages'">{{ t('maintenanceCard.conversations') }}</button>
        <button type="button" :aria-current="activePanel === 'notifications' ? 'page' : undefined" @click="activePanel = 'notifications'">{{ t('maintenanceCard.notifications') }}</button>
        <button type="button" class="record-finance-shortcut" @click="showSettlement">{{ t('maintenanceCard.settlement') }}</button>
      </nav>
      <dialog ref="employeeDialog" class="record-employee-dialog" aria-labelledby="record-employee-title" @cancel="assigningEmployee && $event.preventDefault()">
        <form @submit.prevent="saveEmployeeAssignment">
          <h3 id="record-employee-title" class="ui-form-header">{{ t('maintenanceCard.employeeForAppointment') }}</h3>
          <EmployeePicker v-model="employeeDraft" :current-name="maintenance.performer" :organization-id="context.organization.id" :planned-start-local="plannedStartLocal" :planned-end-local="plannedEndLocal" :disabled="assigningEmployee" />
          <p class="record-employee-hint">{{ t('maintenanceCard.employeeHint') }}</p>
          <p v-if="employeeError" class="record-error" role="alert">{{ employeeError }}</p>
          <footer><UiButton variant="secondary" :disabled="assigningEmployee" @click="employeeDialog?.close()">{{ t('maintenanceCard.cancel') }}</UiButton><UiButton type="submit" :disabled="assigningEmployee">{{ assigningEmployee ? t('maintenanceCard.saving') : t('maintenanceCard.save') }}</UiButton></footer>
        </form>
      </dialog>
      <dialog id="record-vat-dialog" ref="vatDialog" class="record-vat-dialog" aria-labelledby="record-vat-title" @cancel="savingVat && $event.preventDefault()" @close="editingVat = false">
        <form @submit.prevent="saveVat">
          <header class="record-vat-header ui-form-header"><h3 id="record-vat-title">{{ t('maintenanceCard.vatTitle') }}</h3><button type="button" :aria-label="t('maintenanceCard.closeVat')" :disabled="savingVat" @click="closeVat">×</button></header>
          <div class="record-vat-body">
            <p class="record-vat-hint">{{ t('maintenanceCard.vatHint') }}</p>
            <div class="record-vat-common">
              <UiSelect v-model="commonVatRate" :label="t('maintenanceCard.commonRate')" :options="vatRateOptions" :searchable="false" :disabled="savingVat" append-to="#record-vat-dialog" />
              <UiButton variant="secondary" :disabled="savingVat || !commonVatRate" @click="applyCommonVatRate">{{ t('maintenanceCard.applyAll') }}</UiButton>
            </div>
            <details class="record-vat-lines" open>
              <summary>{{ t('maintenanceCard.ratesByLine', { count: vatDraft.length }) }}</summary>
              <div v-for="line in vatDraft" :key="line.lineIndex" class="record-vat-row">
                <span>{{ line.name }}</span>
                <UiSelect v-model="line.vatRateCode" :aria-label="t('maintenanceCard.vatLineLabel', { name: line.name, index: line.lineIndex + 1 })" :placeholder="t('maintenanceCard.vatPlaceholder')" :options="vatRateOptions" :searchable="false" :disabled="savingVat" append-to="#record-vat-dialog" required />
              </div>
            </details>
            <p v-if="vatError" class="record-error" role="alert">{{ vatError }}</p>
          </div>
          <footer class="record-vat-footer"><UiButton variant="secondary" :disabled="savingVat" @click="closeVat">{{ t('maintenanceCard.cancel') }}</UiButton><UiButton type="submit" :disabled="savingVat">{{ savingVat ? t('maintenanceCard.saving') : t('maintenanceCard.saveVat') }}</UiButton></footer>
        </form>
      </dialog>
      <dialog id="record-services-dialog" ref="servicesDialog" class="record-services-dialog" aria-labelledby="record-services-dialog-title" @cancel.prevent="closeServices" @close="editingServices = false">
        <div class="record-services-form">
          <header class="record-services-header ui-form-header"><h3 id="record-services-dialog-title">{{ t('maintenanceCard.servicesTitle') }}</h3><button type="button" :aria-label="t('maintenanceCard.closeServices')" :disabled="savingServices" @click="closeServices">×</button></header>
          <div class="record-services-body">
            <MaintenanceServicesEditor :can-assign-employees="context.shell.scenario?.modules?.members !== false" v-if="editingServices" ref="serviceEditor" v-model="serviceDraft" :can-open-catalog="context.shell.menu?.some(item => item.code === 'catalog' && item.isVisible)" :organization-id="context.organization.id" :planned-start-local="plannedStartLocal" :planned-end-local="plannedEndLocal" append-to="#record-services-dialog" :disabled="savingServices" />
            <p v-if="serviceError" class="record-error" role="alert">{{ serviceError }}</p>
          </div>
          <footer class="record-services-footer">
            <p class="record-service-total">{{ t('maintenanceCard.servicesTotal') }} <strong>{{ formatMoney(serviceDraft.reduce((sum, item) => sum + (item.price ?? 0), 0)) }}</strong></p>
            <div class="record-service-actions"><UiButton variant="secondary" :disabled="savingServices" @click="closeServices">{{ t('maintenanceCard.cancel') }}</UiButton><UiButton :disabled="savingServices" @click="saveServices">{{ savingServices ? t('maintenanceCard.saving') : t('maintenanceCard.saveServices') }}</UiButton></div>
          </footer>
        </div>
      </dialog>
      <div class="record-layout">
        <div class="record-content">
          <ClientConversationPanel v-if="activePanel === 'messages' && maintenance.buyerClientId" :key="context.organization.id + ':' + maintenance.id" :context="context" :client-id="maintenance.buyerClientId" :maintenance-id="maintenance.id" />
          <section v-show="activePanel === 'work'" class="record-section" aria-labelledby="record-services-tab">
            <div v-if="!serviceDetails.length" class="record-empty">{{ t('maintenanceCard.servicesEmpty') }}</div>
            <table v-else class="record-table record-services">
              <caption class="record-sr-only">{{ t('maintenanceCard.servicesByRecord') }}</caption>
              <thead>
                <tr>
                  <th scope="col">{{ t('maintenanceCard.name') }}</th>
                  <th v-if="showWorkProgress" scope="col">{{ t('maintenanceCard.status') }}</th>
                  <th scope="col">{{ t('maintenanceCard.amount') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(detail, index) in serviceDetails" :key="index">
                  <td>
                    <strong>{{ detail.name }}</strong>
                    <p v-if="detail.description">{{ detail.description }}</p>
                    <p v-if="detail.employeeName || maintenance.performer">{{ detail.employeeName || maintenance.performer }}<template v-if="!detail.employeeId && !detail.employeeName"> · {{ t('maintenanceCard.asScheduled') }}</template></p>
                    <p>{{ t('maintenanceCard.quantity') }}: {{ detail.quantity ?? 1 }} · {{ formatVatRate(detail.vatRateCode) }}</p>
                  </td>
                  <td v-if="showWorkProgress"><span class="record-work-status" :data-status="detail.detailId ? workByDetail.get(detail.detailId)?.status : undefined">{{ workStatusName(detail) }}</span><small v-if="detail.detailId && hasRecordedWorkTime(workByDetail.get(detail.detailId)?.activeTime)" class="record-work-duration">{{ t('maintenanceCard.workDuration', { duration: formatWorkDuration(workByDetail.get(detail.detailId)!.activeTime) }) }}</small></td>
                  <td>{{ formatMoney(detail.price) }}</td>
                </tr>
              </tbody>
              <tfoot><tr><td :colspan="showWorkProgress ? 2 : 1">{{ t('maintenanceCard.total') }}</td><td>{{ formatMoney(serviceDetails.reduce((sum, item) => sum + (item.price ?? 0), 0)) }}</td></tr></tfoot>
            </table>
            <div v-if="canEditServices || canManageVat && serviceDetails.length && !editingVat" class="record-services-actions">
              <UiButton v-if="canEditServices" variant="secondary" @click="editServices">{{ t('maintenanceCard.editServices') }}</UiButton>
              <UiButton v-if="canManageVat && !canEditServices && serviceDetails.length && !editingVat" variant="secondary" @click="editVat">{{ t('maintenanceCard.clarifyVat') }}</UiButton>
            </div>
          </section>
          <MaintenanceWorkProgress v-if="serviceDetails.length" :key="[context.organization.id, maintenance.id].join(':')" compact @progress="workProgress = $event" v-show="activePanel === 'work'" :organization-id="context.organization.id" :time-zone-id="context.organization.timeZoneId"
            :maintenance-id="maintenance.id" :maintenance-status="maintenance.status" :simple-workflow="simpleWorkflow"
            :team-available="canUseTeam"
            :can-complete="canEditServices" :can-return="canWrite && maintenance.canEditServices === true" @changed="loadCard(true)" />
          <section v-show="activePanel === 'notifications'" class="record-section">
            <WorkspaceProLock centered :locked="hasLockedNotifications" :message="t('maintenanceCard.notificationsPro')" feature="notifications">
            <div class="record-section__heading ui-section-header">
              <h3>{{ t('maintenanceCard.customerNotifications') }}</h3>
              <div class="record-notifications-actions">
                <UiButton v-if="maintenance.buyerClientId && context.shell.menu?.some(item => item.code === 'messages' && item.isVisible)" variant="secondary" :aria-label="t('maintenanceCard.writeCustomer')" @click="activePanel = 'messages'"><span class="record-notifications-write-full">{{ t('maintenanceCard.writeCustomer') }}</span><span class="record-notifications-write-short">{{ t('maintenanceCard.write') }}</span></UiButton>
                <UiButton variant="secondary" :disabled="notificationsLoading" @click="loadNotificationHistory">{{ notificationsLoading ? t('maintenanceCard.refreshing') : t('maintenanceCard.refresh') }}</UiButton>
              </div>
            </div>
            <MaintenanceVisitReminder v-if="activePanel === 'notifications'" :organization-id="context.organization.id" :maintenance-id="maintenance.id" :refresh-key="notificationsRefreshKey" :preview-only="hasLockedNotifications" @changed="loadCard(true)" />
            <div v-if="hasLockedNotifications" class="record-notifications-history">
              <div class="record-section__heading ui-section-header"><h4>{{ t('maintenanceCard.sentHistory') }}</h4></div>
              <p class="record-notifications-empty">{{ t('maintenanceCard.deliveryInfo') }}</p>
            </div>
            </WorkspaceProLock>
            <div v-if="hasLockedNotifications" class="record-intake-history">
              <button type="button" class="record-text-action" @click="toggleNotificationHistory">{{ showNotificationHistory ? t('maintenanceCard.closeHistory') : t('maintenanceCard.savedHistory') }}</button>
            </div>
            <div v-if="!hasLockedNotifications || showNotificationHistory" class="record-notifications-history">
              <div class="record-section__heading ui-section-header">
                <h4>{{ t('maintenanceCard.sentHistory') }}</h4>
                <UiButton v-if="hasLockedNotifications" variant="secondary" :disabled="notificationsLoading" @click="loadNotificationHistory">{{ t('maintenanceCard.refresh') }}</UiButton>
              </div>
            <p v-if="maxNotificationError" role="alert">{{ maxNotificationError }}</p>
            <p v-if="telegramNotificationError" role="alert">{{ telegramNotificationError }}</p>
            <UiLoader v-if="notificationsLoading && !maxNotifications.length && !telegramNotifications.length" :text="t('maintenanceCard.loadingNotifications')" />
            <MaintenanceNotificationHistory v-if="maxNotifications.length || telegramNotifications.length" :max="maxNotifications" :telegram="telegramNotifications" :time-zone-id="context.organization.timeZoneId" />
            <p v-if="!notificationsLoading && !maxNotifications.length && !telegramNotifications.length && !maxNotificationError && !telegramNotificationError" class="record-notifications-empty">{{ t('maintenanceCard.notificationsEmpty') }}</p>
            </div>
          </section>
          <details v-if="materialDetails.length" v-show="activePanel === 'work'" class="record-section" aria-labelledby="record-materials-title">
            <summary id="record-materials-title">{{ t('maintenanceCard.materialCount', { count: materialDetails.length }) }}</summary>
            <table class="record-table record-materials">
              <caption class="record-sr-only">{{ t('maintenanceCard.materials') }}</caption>
              <thead>
                <tr>
                  <th scope="col">{{ t('maintenanceCard.name') }}</th>
                  <th scope="col">{{ t('maintenanceCard.quantity') }}</th>
                  <th scope="col">{{ t('maintenanceCard.costs') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(detail, index) in materialDetails" :key="index">
                  <td>
                    <strong>{{ detail.materialName || detail.name || t('maintenanceCard.material') }}</strong>
                  </td>
                  <td>{{ detail.materialQuantity ?? '—' }} {{ detail.materialUnit }}</td>
                  <td>{{ formatMoney(detail.materialTotalCost) }}</td>
                </tr>
              </tbody>
            </table>
          </details>
          <section v-if="maintenance.comment" v-show="activePanel === 'work'" class="record-section record-comment">
            <h3 class="ui-section-header">{{ t('maintenanceCard.comment') }}</h3>
            <p>{{ maintenance.comment }}</p>
          </section>
          <section v-if="photos.length" v-show="activePanel === 'work'" class="record-section record-photo-strip">
            <div class="record-section__heading ui-section-header"><h3>{{ t('maintenanceCard.beforeAfter') }}</h3><button type="button" @click="activePanel = 'intake'">{{ t('maintenanceCard.allPhotos', { count: photos.length }) }}</button></div>
            <div><button v-for="photo in photos.slice(0, 4)" :key="photo.photoId" type="button" class="photo-preview-button" :aria-label="photoStages.find(group => group.stage === photo.stage)?.title" @click="previewPhoto = photo"><img :src="photo.url" :alt="photoStages.find(group => group.stage === photo.stage)?.title"><span>{{ photo.stage === 1 ? t('maintenanceCard.before') : photo.stage === 2 ? t('maintenanceCard.after') : t('maintenanceCard.photo') }}</span></button></div>
          </section>
          <div v-if="hasLockedIntake" v-show="activePanel === 'intake'" class="record-intake-history">
            <button type="button" class="record-text-action" :disabled="intakeHistoryLoading" @click="toggleIntakeHistory">
              {{ intakeHistoryLoading ? t('maintenanceCard.opening') : showIntakeHistory ? t('maintenanceCard.closeHistory') : t('maintenanceCard.savedHistory') }}
            </button>
            <span v-if="intakeHistoryError" class="record-error" role="alert">{{ intakeHistoryError }}</span>
          </div>
          <WorkspaceProLock v-show="activePanel === 'intake'" class="record-intake-lock" :locked="isIntakePreview" :message="t('maintenanceCard.intakePro')" feature="intake">
          <section class="record-intake" aria-labelledby="record-intake-title">
            <div class="record-section__heading ui-section-header">
              <h3 id="record-intake-title">{{ t('maintenanceCard.intake') }}</h3>
              <span v-if="maintenance.intakeRecordedAt">{{ formatInstantDate(maintenance.intakeRecordedAt) }}</span>
              <a v-if="maintenance.intakeRecordedAt && context.workspaceAccess?.exportsEnabled" class="record-intake__document"
                :href="'/api/v1/maintenance/' + maintenance.id + '/intake/print?organizationId=' + context.organization.id"
                target="_blank" rel="noopener">{{ t('maintenanceCard.openIntakePdf') }}</a>
              <UiButton v-if="canWrite" variant="secondary" :disabled="!canEditIntake" :title="!canEditIntake ? t('maintenanceCard.availableInPro') : undefined" @click="editIntake">
                {{ maintenance.intakeRecordedAt ? t('maintenanceCard.updateIntake') : t('maintenanceCard.recordIntake') }}
              </UiButton>
            </div>
            <SchedulerBookingDialog :open="intakeEditing" :busy="intakeBusy" :title="t('maintenanceCard.intake')" @close="closeIntake">
              <form class="record-intake__form" novalidate @submit.prevent="saveIntake">
                <p class="record-intake__requirement"><span aria-hidden="true">*</span> {{ t('maintenanceCard.intakeRequirement') }}</p>
                <label for="intake-mileage">{{ intakeMileageLabel }}</label>
                <input id="intake-mileage" v-model="intakeMileage" type="text" inputmode="numeric" pattern="[0-9]*" maxlength="10"
                  :aria-invalid="Boolean(intakeMileageError)" :aria-describedby="intakeMileageError ? 'intake-mileage-error' : undefined"
                  :disabled="intakeBusy" @blur="validateIntakeMileage" />
                <p v-if="intakeMileageError" id="intake-mileage-error" class="record-error" role="alert">{{ intakeMileageError }}</p>
                <label for="intake-completeness">{{ t('maintenanceCard.completeness') }}</label>
                <textarea id="intake-completeness" v-model="intakeCompleteness" rows="2" maxlength="4000"
                  :disabled="intakeBusy" :placeholder="t('maintenanceCard.completenessPlaceholder')" />
                <label for="intake-damage">{{ t('maintenanceCard.visibleDamage') }}</label>
                <textarea id="intake-damage" v-model="intakeVisibleDamage" rows="2" maxlength="4000"
                  :disabled="intakeBusy" :placeholder="t('maintenanceCard.damagePlaceholder')" />
                <label for="intake-notes">{{ t('maintenanceCard.comments') }}</label>
                <textarea id="intake-notes" v-model="intakeNotes" rows="3" maxlength="4000" :disabled="intakeBusy"
                  :placeholder="t('maintenanceCard.notesPlaceholder')" />
                <div v-if="intakeMessage" class="record-error" role="alert">{{ intakeMessage }}</div>
                <div class="record-intake__actions">
                  <UiButton type="submit" :disabled="intakeBusy">{{ intakeBusy ? t('maintenanceCard.saving') : t('maintenanceCard.saveIntake') }}</UiButton>
                  <UiButton type="button" variant="secondary" :disabled="intakeBusy" @click="closeIntake">{{ t('maintenanceCard.cancel') }}</UiButton>
                  <button v-if="!intakeVisibleDamage.trim()" type="button" class="record-text-action" :disabled="intakeBusy"
                    @click="intakeVisibleDamage = t('maintenanceCard.noDamageFull')">{{ t('maintenanceCard.noDamage') }}</button>
                </div>
              </form>
            </SchedulerBookingDialog>
            <p v-if="intakeSuccessMessage" class="record-intake__success" role="status">{{ intakeSuccessMessage }}</p>
            <dl v-if="maintenance.intakeRecordedAt || isIntakePreview" class="record-intake__details">
              <div v-if="isIntakePreview || maintenance.intakeMileage !== null && maintenance.intakeMileage !== undefined">
                <dt>{{ vehicle?.mileageUnit === 2 ? t('maintenanceCard.engineHours') : t('maintenanceCard.mileage') }}</dt><dd>{{ isIntakePreview ? '—' : maintenance.intakeMileage?.toLocaleString(formattingCulture) }} {{ isIntakePreview ? '' : intakeMileageUnit }}</dd>
              </div>
              <div v-if="isIntakePreview || maintenance.intakeCompleteness">
                <dt>{{ t('maintenanceCard.completeness') }}</dt><dd>{{ isIntakePreview ? '—' : maintenance.intakeCompleteness }}</dd>
              </div>
              <div v-if="isIntakePreview || maintenance.intakeVisibleDamage">
                <dt>{{ t('maintenanceCard.visibleDamage') }}</dt><dd>{{ isIntakePreview ? '—' : maintenance.intakeVisibleDamage }}</dd>
              </div>
              <div v-if="isIntakePreview || maintenance.intakeNotes">
                <dt>{{ t('maintenanceCard.comments') }}</dt><dd>{{ isIntakePreview ? '—' : maintenance.intakeNotes }}</dd>
              </div>
            </dl>
            <p v-if="maintenance.intakeRecordedAt && !context.workspaceAccess?.exportsEnabled" class="record-intake__empty">{{ t('maintenanceCard.printUnavailable') }}</p>
            <p v-if="!maintenance.intakeRecordedAt && !isIntakePreview" class="record-intake__empty">{{ t('maintenanceCard.actNotFilled') }}</p>
          </section>
          <div v-show="activePanel === 'intake'">
            <IntakeDamageMap :organization-id="context.organization.id" :maintenance-id="maintenanceId"
              :maintenance-version="maintenance.version" :photos="photos" :read-only="!canEditIntake" :preview-only="isIntakePreview" @photos-changed="updateMaintenancePhotos"
              @linked-photo-ids-changed="updateIntakeDamagePhotoIds" @changed="refreshAfterIntakeDamageChange" />
          </div>
          <section v-show="activePanel === 'intake'" class="record-section" aria-labelledby="record-photos-title">
            <div class="record-section__heading ui-section-header">
              <h3 id="record-photos-title">{{ t('maintenanceCard.beforeAfter') }} <span class="record-photo-count">{{ photos.length }}</span></h3>
              <UiButton v-if="canComparePhotos" variant="secondary" @click="comparisonOpen = true">
                {{ t('maintenanceCard.comparePhotos') }}
              </UiButton>
            </div>
            <div v-if="photoMessage" class="record-error" role="alert">{{ photoMessage }}</div>
            <UiLoader v-if="isSavingPhotos" compact :text="t('maintenanceCard.updatingPhotos')" />
            <div class="record-photo-groups">
              <section v-for="group in photoGroups" :key="group.stage" class="record-photo-group" :aria-label="group.title">
                <div class="record-photo-group__heading">
                  <h4 class="ui-section-header">{{ group.title }} <span>{{ group.photos.length }}</span></h4>
                  <label v-if="canWrite" class="record-upload" :class="{ 'record-upload--busy': isSavingPhotos }" :aria-disabled="!canEditPhotos" :title="!canEditPhotos ? t('maintenanceCard.availableInPro') : undefined">
                    ＋ {{ t('maintenanceCard.photo') }}
                    <input type="file" accept="image/*" multiple :disabled="isSavingPhotos || !canEditPhotos"
                      :aria-label="t('maintenanceCard.addPhoto', { title: group.title })" @change="uploadPhotos($event, group.stage)">
                  </label>
                </div>
                <div v-if="group.photos.length" class="record-photos">
                  <article v-for="(photo, index) in group.photos" :key="photo.photoId" class="record-photo">
                    <button type="button" class="photo-preview-button" :aria-label="t('maintenanceCard.openPhoto', { title: group.title, index: index + 1 })"
                      @click="previewPhoto = photo">
                      <img :src="photo.url" :alt="`${group.title} — ${index + 1}`" loading="lazy">
                    </button>
                    <div v-if="canWrite" class="record-photo__actions">
                      <select :value="photo.stage" :disabled="!canEditPhotos || isSavingPhotos || intakeDamagePhotoIds === null || intakeDamagePhotoIds.has(photo.photoId)"
                        :title="intakeDamagePhotoIds?.has(photo.photoId) ? t('maintenanceCard.removeDamageLinkFirst') : intakeDamagePhotoIds === null ? t('maintenanceCard.loadPhotoLinksFirst') : undefined"
                        :aria-label="t('maintenanceCard.photoStage', { index: index + 1 })"
                        @change="changePhotoStage(photo, $event)">
                        <option v-for="stage in photoStages" :key="stage.stage" :value="stage.stage">{{ stage.title }}</option>
                      </select>
                      <span v-if="intakeDamagePhotoIds?.has(photo.photoId)" class="record-photo__linked-damage">{{ t('maintenanceCard.linkedDamage') }}</span>
                      <button type="button" class="record-text-action" :disabled="!canEditPhotos || isSavingPhotos" :title="!canEditPhotos ? t('maintenanceCard.availableInPro') : undefined"
                        :aria-label="t('maintenanceCard.removePhoto', { index: index + 1 })" @click="removePhoto(photo)">{{ t('maintenanceCard.delete') }}</button>
                    </div>
                  </article>
                </div>
                <div v-else class="record-photo-group__empty">{{ group.stage === 1 ? t('maintenanceCard.intakePhotoEmpty') : group.stage === 2 ? t('maintenanceCard.resultPhotoEmpty') : t('maintenanceCard.photosEmpty') }}</div>
              </section>
            </div>
          </section>
          </WorkspaceProLock>
          <section v-if="!isDetailingScenario && maintenance.linkedDefects.length" v-show="activePanel === 'work'" class="record-section">
            <div class="record-section__heading ui-section-header">
              <h3>{{ t('maintenanceCard.relatedDefects') }}</h3>
            </div>
            <div v-for="defect in maintenance.linkedDefects" :key="defect.id" class="record-defect">
              <strong>{{ defect.title }}</strong>
              <p v-if="defect.description">{{ defect.description }}</p>
            </div>
          </section>
        </div>
        <aside ref="settlementPanel" tabindex="-1" class="record-sidebar" :aria-label="t('maintenanceCard.calculationsAndDocuments')">
          <section v-if="maintenance.buyerClientId || maintenance.buyerName" class="record-section record-buyer">
            <h3 class="ui-section-header">{{ isDetailingScenario ? t('maintenanceCard.customer') : t('maintenanceCard.buyer') }}</h3>
          <span class="record-client">
            <a
              v-if="maintenance.buyerClientId && context.shell.menu?.some(item => item.code === 'clients' && item.isVisible)"
              :href="`#clients/${maintenance.buyerClientId}`"
            >
              {{ maintenance.buyerName }}
            </a>
            <span v-else>{{ maintenance.buyerName || t('maintenanceCard.notSpecified') }}</span>
            <span v-if="maintenance.buyerClientId && maintenance.buyerPartyTypeCode === 'company' && maintenance.buyerInn"> · {{ t('maintenanceCard.buyerTaxId') }} {{ maintenance.buyerInn }}</span>
          </span>
          </section>
          <section class="record-finance" aria-labelledby="record-finance-title">
            <div class="record-section__heading ui-section-header">
              <h3 id="record-finance-title">{{ t('maintenanceCard.finance') }}</h3>
              <span class="record-payment">{{ getPaymentStatusName(maintenance.paymentStatus) }}</span>
            </div>
            <div class="record-total">
              <span>{{ t('maintenanceCard.total') }}</span>
              <strong>{{ formatMoney(netAmount) }}</strong>
            </div>
            <dl class="record-finance__payments">
              <div>
                <dt>{{ t('maintenanceCard.paidAmount') }}</dt>
                <dd>{{ formatMoney(maintenance.paidAmount) }}</dd>
              </div>
              <div :class="{ 'record-debt': maintenance.debtAmount > 0 }">
                <dt>{{ maintenance.paymentStatus === 'overpaid' ? t('maintenanceCard.overpaid') : t('maintenanceCard.overpaidOrRemaining') }}</dt>
                <dd>{{ formatMoney(maintenance.paymentStatus === 'overpaid' ? maintenance.paidAmount - netAmount : maintenance.debtAmount) }}</dd>
              </div>
            </dl>
            <MaintenancePayments
              :key="[context.organization.id, maintenance.id].join(':')"
              :organization-id="context.organization.id" :maintenance="maintenance"
              :time-zone-id="context.organization.timeZoneId"
              :disabled="!canWrite || editingServices" @changed="paymentChanged" />
            <details class="record-costs"><summary>{{ t('maintenanceCard.costsAndProfit') }}</summary>
            <dl class="record-finance__details">
              <div>
                <dt>{{ t('maintenanceCard.discount') }}</dt>
                <dd>{{ formatMoney(maintenance.discountAmount) }}</dd>
              </div>
              <div>
                <dt>{{ t('maintenanceCard.materials') }}</dt>
                <dd>{{ formatMoney(maintenance.materialCostAmount) }}</dd>
              </div>
              <div>
                <dt>{{ t('maintenanceCard.labor') }}</dt>
                <dd>{{ formatMoney(maintenance.laborCostAmount) }}</dd>
              </div>
              <div class="record-profit">
                <dt>{{ t('maintenanceCard.profit') }}</dt>
                <dd>{{ formatMoney(maintenance.profitAmount) }}</dd>
              </div>
            </dl>
            </details>
          </section>
          <section class="record-documents" aria-labelledby="record-documents-title">
            <div class="record-section__heading ui-section-header">
              <h3 id="record-documents-title">{{ t('maintenanceCard.documents') }}</h3>
            </div>
            <div v-if="documentMessage" class="record-error" role="alert">{{ documentMessage }}</div>
            <button
              v-for="link in documentLinks"
              :key="link.key"
              type="button"
              :disabled="Boolean(documentActionKey) || !link.isAvailable"
              :title="!link.isAvailable ? t('maintenanceCard.exportsUnavailable') : undefined"
              @click="downloadDocument(link)"
            >
              <span>{{ documentActionKey === link.key ? t('maintenanceCard.generating') : link.title }}</span>
              <small v-if="!link.isAvailable">{{ t('maintenanceCard.unavailable') }}</small>
            </button>
          </section>
        </aside>
      </div>
    </template>
    <UiConfirmDialog
      :open="Boolean(photoDeleteConfirm)"
      :title="t('maintenanceCard.photoDeleteTitle')"
      :message="t('maintenanceCard.photoDeleteMessage')"
      :confirm-text="t('maintenanceCard.delete')"
      :busy="isSavingPhotos"
      tone="danger"
      @close="closePhotoDeleteConfirmation"
      @confirm="confirmRemovePhoto"
    />
    <MaintenancePhotoComparison v-if="comparisonOpen" :photos="photos" :organization-id="context.organization.id"
      :organization-name="context.organization.name" :maintenance-id="maintenanceId" @close="comparisonOpen = false" />
    <UiImagePreview :src="previewPhoto?.url" :title="photoStages.find(group => group.stage === previewPhoto?.stage)?.title ?? t('maintenanceCard.recordPhotoTitle')" @close="previewPhoto = null" />
  </UiCard>

</template>

<style scoped>
.record-notifications-actions { display:flex; align-items:center; flex-wrap:wrap; gap:8px; }
.record-notifications-write-short { display:none; }
@media (max-width:520px) { .record-notifications-write-full { display:none; } .record-notifications-write-short { display:inline; } }
.record-notifications-history { margin-top:20px; }
.record-notifications-history > .ui-section-header { margin-bottom:14px; }
.record-notifications-history h4 { margin-right:auto; font-size:14px; }
.record-notifications-empty { margin:0; color:var(--crm-text-muted, #667085); font-size:12px; line-height:1.6; }
.record-employee { padding:0; background:transparent; border:0; color:inherit; font:inherit; text-align:left; cursor:pointer; text-decoration:underline; text-decoration-style:dotted; text-underline-offset:4px; }
.record-employee-dialog { width:min(440px, calc(100vw - 32px)); padding:20px; border:1px solid var(--crm-line, #dce2e9); border-radius:12px; background:var(--crm-surface, #fff); color:var(--crm-text, #202631); }
.record-employee-dialog::backdrop { background:rgb(0 0 0 / .55); }
.record-employee-dialog h3 { margin:0 0 20px; font-size:17px; }
.record-employee-dialog footer { display:flex; justify-content:flex-end; gap:8px; margin-top:20px; }
.record-employee-hint { color:var(--crm-text-muted, #687386); font-size:13px; }

.record-service-total { margin:0; font-size:13px; }
.record-service-actions { display:flex; gap:8px; flex-wrap:wrap; }
.record-services-dialog { box-sizing:border-box; width:min(960px, calc(100vw - 24px)); max-height:calc(100dvh - 24px); padding:0; overflow:visible; border:1px solid var(--crm-line, #dce2e9); border-radius:12px; background:var(--crm-surface, #fff); color:var(--crm-text, #202631); }
.record-services-dialog::backdrop { background:var(--crm-backdrop, rgb(0 0 0 / .55)); }
.record-services-form { display:flex; flex-direction:column; max-height:calc(100dvh - 26px); }
.record-services-header { display:flex; align-items:center; justify-content:space-between; gap:12px; flex-shrink:0; margin:0; padding:16px 20px; border-radius:11px 11px 0 0; }
.record-services-header h3 { margin:0; font-size:18px; }
.record-services-header button { border:0; padding:0 5px; background:transparent; color:inherit; font-size:24px; cursor:pointer; }
.record-services-body { min-height:0; padding:20px; overflow-y:auto; }
.record-services-footer { display:flex; flex-shrink:0; align-items:center; justify-content:space-between; flex-wrap:wrap; gap:12px; padding:16px 20px; border-top:1px solid var(--crm-line, #dce2e9); }
@media (max-width:600px) {
  .record-services-header, .record-services-body, .record-services-footer { padding:12px; }
}
.record-vat-dialog { width:min(600px, calc(100vw - 32px)); max-height:calc(100dvh - 32px); padding:0; overflow:visible; border:1px solid var(--crm-line, #dce2e9); border-radius:12px; background:var(--crm-surface, #fff); color:var(--crm-text, #202631); }
.record-vat-dialog::backdrop { background:var(--crm-backdrop, rgb(0 0 0 / .55)); }
.record-vat-dialog form { display:flex; flex-direction:column; max-height:calc(100dvh - 34px); }
.record-vat-header { display:flex; align-items:center; justify-content:space-between; gap:12px; flex-shrink:0; margin:0; padding:16px 20px; border-radius:11px 11px 0 0; }
.record-vat-header h3 { margin:0; font-size:18px; }
.record-vat-header button { border:0; padding:0 5px; background:transparent; color:inherit; font-size:24px; cursor:pointer; }
.record-vat-body { padding:20px; overflow-y:auto; }
.record-vat-hint { margin:0 0 18px; color:var(--crm-text-muted, #667085); font-size:13px; line-height:1.5; }
.record-vat-common { display:grid; grid-template-columns:minmax(0, 1fr) auto; align-items:end; gap:12px; }
.record-vat-lines { margin-top:20px; border-top:1px solid var(--crm-line, #dce2e9); padding-top:16px; }
.record-vat-lines summary { cursor:pointer; font-size:13px; font-weight:600; }
.record-vat-row { display:grid; grid-template-columns:minmax(0, 1fr) 160px; align-items:center; gap:16px; margin-top:12px; padding:12px; border-radius:6px; background:var(--crm-surface-soft, #f3f5f8); font-size:13px; }
.record-vat-row > span { overflow-wrap:anywhere; }
.record-vat-footer { display:flex; flex-shrink:0; justify-content:flex-end; gap:8px; padding:16px 20px; border-top:1px solid var(--crm-line, #dce2e9); }
.record-page {
  min-width: 0;
}

.record-toolbar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 16px;
}

.record-toolbar__actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
  min-width: 0;
  max-width: 100%;
  margin-left: auto;
}

.record-status-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px 20px;
  padding-bottom: 18px;
}

.record-status {
  --record-status-color: #6088b2;
  display: grid;
  gap: 4px;
  min-width: 160px;
  padding: 12px 16px;
  border: 1px solid color-mix(in srgb, var(--record-status-color) 50%, var(--crm-line, #dfe4eb));
  border-radius: 8px;
  background: color-mix(in srgb, var(--record-status-color) 14%, var(--crm-surface, #fff));
  color: var(--crm-text, #203246);
}

.record-status span {
  color: var(--crm-text-muted, #667085);
  font-size: 11px;
}

.record-status strong { font-size:15px; font-weight:650; }
.record-status[data-status="2"] { --record-status-color:#e87532; }
.record-status[data-status="3"] { --record-status-color:#379d80; }
.record-status[data-status="4"] { --record-status-color:#8793a2; }
.record-milestones { display:grid; grid-template-columns:repeat(auto-fit, minmax(150px, 1fr)); gap:12px 20px; margin:0 0 14px; padding:12px; border-radius:6px; background:var(--crm-surface-soft, #f3f5f8); }
.record-milestones dt { color:var(--crm-text-muted, #667085); font-size:11px; }
.record-milestones dd { margin:5px 0 0; font-size:13px; font-variant-numeric:tabular-nums; }

.record-workflow {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.record-page .record-workflow :deep(.ui-button.ui-button--primary) {
  color: #fff;
  background: #c94f16;
  border-color: #c94f16;
}

.record-workflow :deep(.ui-button) {
  min-height: 34px;
  padding: 6px 12px;
  font-size: 12px;
}

.record-summary {
  display: grid;
  grid-template-columns: minmax(240px, 1fr) minmax(0, 1.4fr);
  align-items: start;
  gap: 24px;
  padding: 20px 0;
  border-top: 1px solid var(--crm-line, #e6eaf0);
  border-bottom: 1px solid var(--crm-line, #e6eaf0);
}

.record-vehicle {
  display: grid;
  justify-items: start;
  gap: 8px;
  min-width: 0;
}

.record-label, .record-appointment dt {
  font-size: 12px;
  color: var(--crm-text-muted, #667085);
}

.record-vehicle > a {
  font-size: 17px;
  font-weight: 650;
  text-decoration: none;
  color: var(--crm-text, #202833);
  overflow-wrap: anywhere;
}

.record-vehicle > a span {
  font-size: 13px;
  color: var(--crm-text-muted, #667085);
}

.record-client {
  font-size: 13px;
  color: var(--crm-text-muted, #667085);
  overflow-wrap: anywhere;
}

.record-client a {
  color: var(--crm-text, #202833);
  text-decoration: none;
}

.record-client a:hover { color:var(--crm-accent, #c6622b); text-decoration:underline; }

.record-vehicle a:hover {
  color: #c9571c;
  text-decoration: underline;
}

.record-appointment {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
  gap: 16px;
  margin: 0;
}

.record-appointment dd {
  margin: 7px 0 0;
  font-size: 14px;
  color: var(--crm-text, #202833);
  overflow-wrap: anywhere;
}

.record-layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  align-items: start;
  gap: 20px;
  margin-top: 20px;
}

.record-content {
  display: grid;
  gap: 18px;
  min-width: 0;
}

.record-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 22px;
  padding: 8px;
  border: 1px solid var(--crm-line, #e0e5ec);
  border-radius: 9px;
  background: var(--crm-surface-soft, #f2f5f8);
}

.record-tabs button {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  flex: 0 1 auto;
  flex-wrap: wrap;
  min-width: 0;
  max-width: 100%;
  text-align: left;
  overflow-wrap: anywhere;
  min-height: 44px;
  padding: 10px 16px;
  border: 1px solid var(--crm-line, #dce2e9);
  border-radius: 7px;
  background: var(--crm-surface, #fff);
  color: var(--crm-text, #202631);
  font: inherit;
  font-size: 14px;
  font-weight: 650;
  cursor: pointer;
}

.record-tabs button:hover {
  border-color: var(--crm-accent, #c6622b);
}

.record-tabs button[aria-current="page"] {
  color: #fff;
  border-color: #c94f16;
  background: #c94f16;
}

.record-tabs span {
  padding: 3px 7px;
  border-radius: 4px;
  background: var(--crm-surface-soft, #f2f5f8);
  font-size: 11px;
  font-weight: 600;
}

.record-tabs .record-tabs__pending {
  color: var(--crm-danger-text, #a12622);
  background: var(--crm-danger-surface, #fff0eb);
}

.record-tabs button[aria-current="page"] span {
  color: #fff;
  background: rgb(255 255 255 / .2);
}

.record-content > .record-section,
.record-content > .record-comment {
  border: 1px solid var(--crm-line, #e0e5ec);
  border-radius: 9px;
  padding: 18px;
  background: var(--crm-surface, #fff);
}

.record-section__heading {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 14px;
}

.record-section__heading h3, .record-comment h3 {
  margin: 0;
  font-size: 15px;
}

.record-photo-count {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 22px;
  height: 22px;
  padding: 0 6px;
  box-sizing: border-box;
  border-radius: 999px;
  background: var(--crm-accent, #e87532);
  color: #fff;
  font-size: 12px;
  font-weight: 600;
  line-height: 1;
  vertical-align: middle;
}

.record-section__heading > span {
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
}

.record-services-actions {
  display: flex;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 14px;
}

.record-table {
  width: 100%;
  border-collapse: collapse;
  table-layout: fixed;
  font-size: 13px;
  text-align: left;
}

.record-table th {
  padding: 9px 12px;
  background: var(--crm-surface-soft, #f5f7f9);
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
  font-weight: 500;
}

.record-table td {
  padding: 15px 12px;
  border-bottom: 1px solid var(--crm-line, #e6eaf0);
  vertical-align: top;
  color: var(--crm-text, #202833);
  overflow-wrap: anywhere;
}

.record-services tbody tr:last-child td {
  border-bottom: 0;
}

.record-table th:last-child, .record-table td:last-child {
  text-align: right;
  width: 125px;
  font-variant-numeric: tabular-nums;
}

.record-table strong {
  font-size: 14px;
  font-weight: 600;
}

.record-table p {
  margin: 6px 0 0;
  color: var(--crm-text-muted, #667085);
  line-height: 1.5;
  max-width: 70ch;
}

.record-materials th:nth-child(2) {
  width: 100px;
}

.record-comment {
  padding: 16px;
  border-left: 3px solid var(--crm-line, #dce2e9);
  border-radius: 0 6px 6px 0;
  background: var(--crm-surface, #fff);
}

.record-comment p {
  margin: 10px 0 0;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  font-size: 14px;
  line-height: 1.6;
  color: var(--crm-text-muted, #667085);
  max-width: 85ch;
}

.record-sidebar {
  min-width: 0;
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(260px, .7fr);
  gap: 18px;
}

.record-finance {
  border: 1px solid var(--crm-line, #e0e5ec);
  border-radius: 8px;
  padding: 18px;
  background: var(--crm-surface-soft, #f8f9fb);
}

.record-finance .record-section__heading {
  justify-content: space-between;
}

.record-total {
  display: grid;
  gap: 6px;
  margin: 20px 0;
}

.record-total > span {
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
}

.record-total strong {
  font-size: 28px;
  font-weight: 650;
  letter-spacing: -.5px;
  overflow-wrap: anywhere;
}

.record-finance dl {
  display: grid;
  gap: 12px;
  margin: 0;
}

.record-finance dl > div {
  display: flex;
  justify-content: space-between;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 6px 12px;
  font-size: 13px;
}

.record-finance dt {
  color: var(--crm-text-muted, #667085);
}

.record-finance dd {
  margin: 0;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.record-finance .record-finance__details {
  margin-top: 18px;
  padding-top: 18px;
  border-top: 1px solid var(--crm-line, #e0e5ec);
}

.record-debt dd {
  color: var(--crm-text, #ab451c);
  border-bottom: 2px solid #df7a43;
}

.record-profit {
  padding-top: 4px;
}

.record-documents {
  padding: 0 4px;
}

.record-documents > button {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  width: 100%;
  padding: 12px 0;
  border: 0;
  border-bottom: 1px solid var(--crm-line, #e6eaf0);
  background: transparent;
  color: var(--crm-text, #202833);
  font: inherit;
  font-size: 13px;
  text-align: left;
  cursor: pointer;
}

.record-documents > button:hover {
  color: #ce5b23;
}

.record-documents > button:disabled {
  opacity: .6;
  cursor: wait;
}

.record-upload {
  position: relative;
  padding: 7px 10px;
  border: 1px solid var(--crm-line, #dce2e9);
  border-radius: 6px;
  color: var(--crm-text, #202833);
  background: var(--crm-surface, #fff);
  font-size: 12px;
  cursor: pointer;
}

.record-upload input {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  opacity: 0;
  cursor: pointer;
}

.record-upload--busy {
  opacity: .6;
}

.record-upload:focus-within {
  outline: 2px solid #e87532;
  outline-offset: 3px;
}

.record-photos {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, 160px), 1fr));
  gap: 12px;
}

.record-photo {
  min-width: 0;
  display: grid;
  gap: 7px;
  align-content: start;
}

.record-photo .photo-preview-button {
  display: block;
  border-radius: 6px;
  overflow: hidden;
  border: 1px solid var(--crm-line, #dce2e9);
}

.record-photo img {
  display: block;
  width: 100%;
  aspect-ratio: 4 / 3;
  object-fit: cover;
}

.record-photo > button:not(.photo-preview-button) {
  justify-self: start;
  padding: 2px 0;
  border: 0;
  background: transparent;
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
  text-decoration: underline;
  text-underline-offset: 3px;
  cursor: pointer;
}

.record-defect {
  padding: 12px 0;
  border-bottom: 1px solid var(--crm-line, #e6eaf0);
  font-size: 14px;
}

.record-defect p {
  color: var(--crm-text-muted, #667085);
  margin: 6px 0 0;
}

.record-empty {
  padding: 24px 14px;
  border: 1px dashed var(--crm-line, #dce2e9);
  border-radius: 6px;
  color: var(--crm-text-muted, #667085);
  text-align: center;
  font-size: 13px;
}

.record-error {
  padding: 12px;
  border: 1px solid #cd764f;
  border-radius: 6px;
  margin-bottom: 14px;
  color: var(--crm-text, #9d3619);
  font-size: 13px;
}

.record-page a:focus-visible, .record-page button:focus-visible {
  outline: 2px solid #e87532;
  outline-offset: 3px;
}

.record-sr-only {
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

.record-overview { container-type:inline-size; padding:14px 16px; border:1px solid var(--crm-line, #dce2e9); border-radius:9px; background:var(--crm-surface, #fff); }
.record-identity { display:flex; justify-content:space-between; align-items:center; gap:16px; flex-wrap:wrap; }
.record-status-row { padding:0; gap:8px; }
.record-vehicle > a { display:flex; align-items:center; gap:9px; }
.record-brand { width:32px; height:32px; object-fit:contain; padding:3px; background:#fff; border:1px solid #e5e7eb; border-radius:7px; }
.record-summary { display:block; padding:10px 0; margin-top:12px; border-bottom:0; }
.record-appointment { grid-template-columns:repeat(auto-fit, minmax(130px, 1fr)); }
.record-stages { display:grid; grid-template-columns:repeat(4, minmax(0, 1fr)); gap:8px; padding:0; margin:12px 0 0; list-style:none; }
.record-stages li { --stage-color:#64758a; display:flex; align-items:center; gap:9px; min-width:0; padding:8px 10px; border:1px solid transparent; border-radius:7px; background:color-mix(in srgb, var(--stage-color) 10%, var(--crm-surface, #fff)); color:var(--crm-text-muted, #667085); font-size:12px; }
.record-stages li[data-stage="active"] { --stage-color:#e87532; }
.record-stages li[data-stage="ready"] { --stage-color:#b79b20; }
.record-stages li[data-stage="issued"] { --stage-color:#379d80; }
.record-stages li.record-stages__current { border-color:var(--stage-color); color:var(--crm-text, #202631); font-weight:650; }
.record-stage__number { display:grid; place-items:center; width:22px; height:22px; flex-shrink:0; border:1px solid currentColor; border-radius:50%; font-size:11px; }
.record-stage__label { min-width:0; overflow-wrap:anywhere; }
.record-stages__done .record-stage__number { color:#379d80; }
.record-progress { display:flex; align-items:center; gap:12px; font-size:12px; color:var(--crm-text-muted, #667085); }
.record-progress > span { flex-shrink:0; }
.record-progress progress { --progress-color:#e87532; display:block; flex:1; width:100%; min-width:0; height:7px; appearance:none; border:0; border-radius:10px; overflow:hidden; background:var(--crm-line, #e0e5ec); }
.record-progress progress.record-progress__complete { --progress-color:#379d80; }
.record-progress progress::-webkit-progress-bar { background:var(--crm-line, #e0e5ec); border-radius:10px; }
.record-progress progress::-webkit-progress-value { background:var(--progress-color); border-radius:10px; }
.record-progress progress::-moz-progress-bar { background:var(--progress-color); border-radius:10px; }
.record-layout { grid-template-columns:minmax(0, 1fr) 300px; gap:20px; }
.record-sidebar { grid-template-columns:minmax(0, 1fr); gap:14px; }
.record-section, .record-documents { padding:16px; border:1px solid var(--crm-line, #e0e5ec); border-radius:8px; background:var(--crm-surface, #fff); }
.record-section__heading { flex-wrap:wrap; gap:8px; }
.record-section__heading h3 { margin-right:auto; }
.record-buyer h3 { margin:0 0 10px; font-size:14px; }
.record-buyer .record-client { font-size:14px; font-weight:600; }
.record-finance { background:var(--crm-surface, #fff); padding:16px; }
.record-finance .record-total { display:flex; justify-content:space-between; align-items:baseline; gap:12px; margin:16px 0; }
.record-total > strong { font-size:23px; }
.record-debt { padding:9px; background:color-mix(in srgb, #e87532 12%, var(--crm-surface, #fff)); border-radius:6px; }
.record-debt dd { border:0; }
.record-costs { margin-top:16px; font-size:12px; }
.record-costs summary, .record-section > summary { cursor:pointer; font-weight:600; }
.record-finance .record-finance__details { margin-top:12px; padding-top:12px; }
.record-finance :deep(.record-payments) { display:grid; gap:3px; }
.record-finance :deep(.record-payments .ui-button) { width:100%; }
.record-finance :deep(.record-payments__history) { justify-self:center; }
.record-work-status { display:inline-block; padding:4px 7px; border-radius:6px; background:var(--crm-surface-soft, #f2f3f5); color:var(--crm-text-muted, #667085); font-size:11px; white-space:nowrap; }
.record-work-status[data-status="2"] { background:color-mix(in srgb, #e87532 15%, var(--crm-surface, #fff)); color:var(--crm-text, #8c451e); }
.record-work-status[data-status="3"] { background:color-mix(in srgb, #b79b20 15%, var(--crm-surface, #fff)); color:var(--crm-text, #77641d); }
.record-work-status[data-status="4"] { background:color-mix(in srgb, #379d80 15%, var(--crm-surface, #fff)); color:var(--crm-text, #20664f); }
.record-work-duration { display:block; color:var(--crm-text-muted, #667085); margin-top:7px; font-variant-numeric:tabular-nums; }
.record-services th:nth-child(2) { width:115px; }
.record-services tfoot td { border-top:1px solid var(--crm-line, #e0e5ec); border-bottom:0; font-weight:650; }
.record-photo-strip > div:last-child { display:grid; grid-template-columns:repeat(4, minmax(0, 1fr)); gap:8px; }
.record-photo-strip .photo-preview-button { position:relative; border:1px solid var(--crm-line, #e0e5ec); border-radius:6px; overflow:hidden; }
.record-photo-strip img { display:block; width:100%; aspect-ratio:4 / 3; object-fit:cover; }
.record-photo-strip .photo-preview-button > span { position:absolute; bottom:5px; left:5px; padding:3px 6px; background:#000b; border-radius:4px; color:white; font-size:11px; }
.record-photo-strip .record-section__heading > button { border:0; background:transparent; color:var(--crm-text, #202631); font:inherit; font-size:12px; cursor:pointer; }

.record-appointment dd { margin-top:4px; font-size:13px; }
.record-tabs { padding:0; margin-top:14px; border:0; border-bottom:1px solid var(--crm-line, #e0e5ec); border-radius:0; background:transparent; }
.record-tabs button { min-height:40px; padding:10px 12px; border:0; border-bottom:2px solid transparent; border-radius:0; background:transparent; }
.record-tabs button[aria-current="page"] { background:transparent; border-color:var(--crm-accent, #e87532); color:var(--crm-text, #202631); }
.record-tabs button[aria-current="page"] span { background:var(--crm-surface-soft, #edf0f3); color:inherit; }
.record-table td { padding-top:10px; padding-bottom:10px; }
.record-table p { font-size:11px; margin-top:4px; }
.record-buyer { order:1; }
.record-documents { order:2; }
.record-sidebar { scroll-margin-top:90px; }
.record-tabs .record-finance-shortcut { display:none; }

@container (max-width:700px) {
  .record-stages { grid-template-columns:repeat(2, minmax(0, 1fr)); }
}

@container (max-width:300px) {
  .record-stages { grid-template-columns:minmax(0, 1fr); }
}

@media (min-width:1200px) and (min-height:850px) {
  .record-sidebar { position:sticky; top:calc(var(--crm-header-height, 66px) + 16px); }
  .record-page :deep(.ui-card__header) { position:sticky; top:var(--crm-header-height, 66px); z-index:4; background:var(--crm-surface, #fff); }
}

@media (max-width: 1100px) {
  .record-tabs .record-finance-shortcut { display:block; }
  .record-summary {
    grid-template-columns: 1fr;
    gap: 20px;
  }

  .record-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .record-materials th:last-child {
    width: 90px;
  }

  .record-materials th:nth-child(2) {
    width: 80px;
  }
}

@media (max-width: 900px) {
  .record-layout {
    grid-template-columns: 1fr;
  }

  .record-sidebar {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    align-items: start;
  }
}

@media (max-width: 600px) {
  .record-status-row { width:100%; }
  .record-status { width:100%; min-width:0; }
  .record-vat-common, .record-vat-row { grid-template-columns:minmax(0, 1fr); }
  .record-vat-header, .record-vat-body, .record-vat-footer { padding:14px; }
  .record-vat-common :deep(.ui-button) { justify-self:start; }
  .record-sidebar {
    grid-template-columns: 1fr;
  }

  .record-page :deep(.ui-card__header) {
    flex-direction: column;
    align-items: stretch;
  }

  .record-page :deep(.ui-card__actions) {
    justify-content: flex-start;
  }

  .record-table th, .record-table td { padding-inline:6px; }
  .record-overview { padding:12px; }
  .record-stages { gap:5px; }
  .record-stages li { padding:8px; }
  .record-progress { flex-wrap:wrap; }
  .record-progress > span { flex-shrink:1; }
  .record-tabs { gap:0; flex-wrap:wrap; }
  .record-tabs button { padding-inline:8px; }
  .record-section, .record-documents { padding:12px; }
  .record-services th:nth-child(2) { width:82px; }
  .record-photo-strip > div:last-child { grid-template-columns:repeat(2, minmax(0, 1fr)); }

  .record-services th:last-child {
    width: 95px;
  }

  .record-materials th:nth-child(2), .record-materials th:last-child {
    width: 70px;
  }

  .record-table th {
    font-size: 11px;
  }

  .record-appointment {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
.photo-preview-button {
  width: 100%;
  padding: 0;
  background: var(--crm-surface-soft, #f5f7f9);
  cursor: zoom-in;
}

.record-intake {
  padding: 16px;
  border: 1px solid var(--crm-line, #dce2e9);
  border-radius: 8px;
  background: var(--crm-surface-soft, #f7f8fa);
}
.record-intake .record-section__heading { flex-wrap: wrap; }
.record-intake .record-section__heading > button { margin-left: auto; }
.record-intake__document { color: var(--crm-text, #202631); font-size: 13px; font-weight: 650; text-decoration: underline; text-underline-offset: 3px; }
.record-intake__requirement { margin: 0; color: var(--crm-text-muted, #667085); font-size: 12px; }
.record-intake__requirement span { color: var(--crm-danger-text, #a12622); font-weight: 700; }
.record-intake__success { margin: 12px 0 0; color: var(--crm-text, #202631); font-weight: 600; }
.record-intake__details { display: grid; gap: 10px; margin: 12px 0 0; }
.record-intake-history { display: flex; align-items: center; justify-content: flex-end; gap: 12px; }
.record-intake-lock :deep(.workspace-pro-lock__content) { display: grid; gap: 16px; }
.record-intake__details > div { display: grid; gap: 2px; }
.record-intake__details dt { color: var(--crm-text-muted, #667085); font-size: 12px; }
.record-intake__details dd { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; line-height: 1.5; }
.record-intake__empty { margin: 12px 0 0; color: var(--crm-text-muted, #667085); }
.record-intake__form { display: grid; gap: 10px; margin: 0; padding: 20px 24px; font-size: 14px; }
@media (min-width: 481px) {
  :global(.scheduler-booking-dialog:has(.record-intake__form)) { width: min(680px, calc(100vw - 40px)); }
}
@media (max-width: 480px) {
  .record-intake__form { padding: 16px; }
}
.record-intake__form textarea,
.record-intake__form input {
  width: 100%; box-sizing: border-box;
  border: 1px solid var(--crm-line, #dce2e9); border-radius: 6px; padding: 10px 12px;
  background: var(--crm-surface, #fff); color: var(--crm-text, #202833); font: inherit;
}
.record-intake__form textarea { resize: vertical; min-height: 90px; }
.record-intake__form input { max-width: 240px; }
.record-intake__actions { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.record-photo-groups { display: grid; gap: 16px; grid-template-columns: repeat(2, minmax(0, 1fr)); }
.record-photo-group { min-width: 0; }
.record-photo-groups > .record-photo-group {
  border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; padding: 12px;
}
.record-photo-groups > .record-photo-group:nth-child(3) { grid-column: 1 / -1; }
.record-photo-group__heading { display: flex; align-items: center; justify-content: space-between; gap: 10px; margin-bottom: 12px; }
.record-photo-group__heading h4 { margin: 0; font-size: 14px; }
.record-photo-group__heading h4 span { margin-left: 6px; font-size: 12px; color: var(--crm-text-muted, #667085); }
.record-photo-group__empty { padding: 28px 8px; text-align: center; color: var(--crm-text-muted, #667085); font-size: 13px; }
.record-photo__actions { display: flex; align-items: center; flex-wrap: wrap; gap: 8px; }
.record-photo__actions select {
  min-width: 0; max-width: 100%; flex: 1; padding: 5px 6px;
  border: 1px solid var(--crm-line, #dce2e9); border-radius: 5px;
  background: var(--crm-surface, #fff); color: var(--crm-text, #202833); font: inherit; font-size: 12px;
}
.record-photo__linked-damage { flex-basis: 100%; color: var(--crm-text-muted, #667085); font-size: 11px; }
.record-text-action { padding: 5px 0; border: 0; background: transparent; color: var(--crm-text-muted, #667085); font: inherit; font-size: 12px; cursor: pointer; }
.record-text-action:hover { color: var(--crm-text, #202833); text-decoration: underline; }
@media (max-width: 760px) {
  .record-photo-groups { grid-template-columns: minmax(0, 1fr); }
}
</style>

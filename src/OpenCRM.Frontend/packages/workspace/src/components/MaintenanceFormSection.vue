<script setup lang="ts">
import EmployeePicker from './EmployeePicker.vue'
import WorkspaceProFeatureDialog from './WorkspaceProFeatureDialog.vue'
import { proFeatureBillingAccessKey } from '../proFeatureAccess'
import { isSimpleMaintenanceWorkflow as hasSimpleMaintenanceWorkflow } from '../utils/maintenanceWorkflow'
import MaintenanceServicesEditor from './MaintenanceServicesEditor.vue'
import { getVehicleDisplayName } from '../api/vehicles'
import WorkspaceAccessNotice from './WorkspaceAccessNotice.vue'
import BookingTimeFields from './BookingTimeFields.vue'
import { computed, inject, onMounted, provide, reactive, ref, watch } from 'vue'
import { pageBreadcrumbsKey, UiAlertDialog, UiButton, UiCard, UiImagePreview, UiConfirmDialog, UiDatePicker, UiInput, UiLoader, UiMoneyInput, UiNumberInput, UiSelect, UiTextarea, formatDateOnly } from '@opencrm/ui'
import {
  createMaintenance,
  deleteMaintenancePhoto,
  loadMaintenance,
  loadMaintenancePhotos,
  updateMaintenance,
  uploadMaintenancePhotos,
  type WorkspaceMaintenanceDetail,
  type WorkspaceMaintenancePhoto,
  type MaintenancePhotoStage,
  type WorkspaceMaintenanceSaveRequest
} from '../api/maintenance'
import { loadScenarioCatalog, type WorkspaceScenarioCatalogItem } from '../api/scenarioCatalog'
import { loadVehicle, type WorkspaceVehicle } from '../api/vehicles'
import InquiryVehiclePicker from './InquiryVehiclePicker.vue'
import { loadClientOptions, type WorkspaceClientOption } from '../api/clients'
import { isDiscreteMaterialUnit, materialUnitOptions, normalizeMaterialUnit } from '../materialUnits'
import type { WorkspaceContext } from '../types'
import { trackWorkspaceGoal } from '../analytics'
import WorkspacePageHelpButton from './WorkspacePageHelpButton.vue'
import { getUnknownServiceDurationSaveMessage } from '../serviceDuration'

const teamPreviewOpen = ref(false)
const canManageBilling = inject(proFeatureBillingAccessKey, computed(() => false))
const bookingTimes = ref<InstanceType<typeof BookingTimeFields> | null>(null)
const serviceDuration = ref<number | null | undefined>()
const materialSection = ref<HTMLDetailsElement | null>(null)

const props = defineProps<{
  context: WorkspaceContext
  maintenanceId?: string
  initialVehicleId?: string
  initialDate?: string
  initialStartTime?: string
  initialEndTime?: string
  initialLocation?: string
  fromVehicle?: boolean
}>()
const emit = defineEmits<{
  changed: []
  saved: [id: string, successMessage?: string]
}>()

const form = reactive({
  vehicleId: '',
  buyerClientId: '',
  status: '1',
  type: '1',
  plannedDate: formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC'),
  plannedEndDate: formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC'),
  plannedStartTime: '',
  plannedEndTime: '',
  completedDate: '',
  location: '',
  employeeId: '',
  performer: '',
  mileage: '',
  discountAmount: '',
  paidAmount: '',
  laborCostAmount: '',
  comment: ''
})
const plannedStartLocal = computed(() => form.plannedDate && form.plannedStartTime
  ? `${form.plannedDate}T${form.plannedStartTime.slice(0, 5)}` : '')
const plannedEndLocal = computed(() => form.plannedEndDate && form.plannedEndTime
  ? `${form.plannedEndDate}T${form.plannedEndTime.slice(0, 5)}` : '')

type MaterialLine = {
  id: string
  detailId?: string | null
  materialCatalogItemId: string
  materialName: string
  materialQuantity: string
  materialUnit: string
  materialUnitCost: string
}

const clients = ref<WorkspaceClientOption[]>([])
const services = ref<WorkspaceScenarioCatalogItem[]>([])
const maintenanceVersion = ref<number | undefined>()
const savedBuyerClientId = ref('')
const servicesEditor = ref<InstanceType<typeof MaintenanceServicesEditor> | null>(null)
type ServiceLine = {
  detailId?: string | null
  employeeId?: string | null
  employeeName?: string | null
  id: string
  catalogItemId: string
  savedCatalogItemId: string
  savedName: string
  description: string
  price: string
  vatRateCode: string
  quantity: number
}
const serviceLines = ref<ServiceLine[]>([])
const serviceEditorRows = computed({
  get: () => serviceLines.value.map(line => ({
    detailId: line.detailId,
    name: getServiceName(line), catalogItemId: line.catalogItemId || null, price: toNullableNumber(line.price),
    employeeId: line.employeeId, employeeName: line.employeeName,
    description: line.description, quantity: line.quantity, vatRateCode: line.vatRateCode || null
  })),
  set: (value: WorkspaceMaintenanceDetail[]) => { serviceLines.value = value.map(createServiceLine) }
})
const posts = ref<WorkspaceScenarioCatalogItem[]>([])
const materials = ref<WorkspaceScenarioCatalogItem[]>([])
const materialLines = ref<MaterialLine[]>([])
const photos = ref<WorkspaceMaintenancePhoto[]>([])
const hasLoadedPhotos = ref(false)
const selectedVehicle = ref<WorkspaceVehicle | null>(null)
const parentBreadcrumbs = inject(pageBreadcrumbsKey, undefined)
const navigationSuffix = computed(() => props.fromVehicle ? '?from=vehicle' : '')
provide(pageBreadcrumbsKey, computed(() => {
  if (!props.fromVehicle) return parentBreadcrumbs?.value ?? []
  const items = [{ title: props.context.shell.menu?.find(item => item.code === 'vehicles')?.title ?? 'Техника', href: '#vehicles' }]
  if (selectedVehicle.value) {
    items.push({ title: getVehicleDisplayName(selectedVehicle.value, true), href: `#vehicles/${selectedVehicle.value.id}` })
  }
  if (props.maintenanceId) {
    items.push({ title: 'Запись', href: `#maintenance/${props.maintenanceId}${navigationSuffix.value}` })
  }
  return items
}))
const isLoading = ref(false)
const isFormReady = ref(false)
const isSaving = ref(false)
const isSavingPhotos = ref(false)
const uploadStage = ref('1')
const photoStageOptions = [{ value: '1', label: 'До работы' }, { value: '2', label: 'После работы' }, { value: '0', label: 'Без категории' }]
const errorMessage = ref('')
const errorDialogMessage = ref('')
const photoMessage = ref('')
const photoPromptMaintenanceId = ref('')
const photoDeleteConfirm = ref<WorkspaceMaintenancePhoto | null>(null)
const previewPhoto = ref<WorkspaceMaintenancePhoto | null>(null)
let materialLineSeq = 0
const maxMaintenancePhotoFiles = 10
const maxMaintenancePhotoBatchSize = 100 * 1024 * 1024
const maxMaterialQuantity = 999999999
const organizationId = computed(() => props.context.organization.id)
const isEditMode = computed(() => Boolean(props.maintenanceId))
const isDetailingScenario = computed(() => props.context.shell.scenario?.code === 'detailing')
const isSimpleMaintenanceWorkflow = computed(() =>
  hasSimpleMaintenanceWorkflow(props.context.organization)
)
const isReadOnly = computed(() => props.context.workspaceAccess?.isReadOnly === true)
const canEditPhotos = computed(() => !isReadOnly.value && props.context.shell.scenario?.modules?.photos !== false)
const canUseMaterials = computed(() => props.context.shell.scenario?.modules?.materials !== false)
const canUseTeam = computed(() => props.context.shell.scenario?.modules?.members !== false)
const readOnlyMessage = computed(() =>
  props.context.workspaceAccess?.readOnlyMessage ?? 'Рабочее пространство сейчас доступно только для просмотра.'
)
const billingHref = computed(() => '#billing')
const title = computed(() => isEditMode.value ? 'Редактирование записи' : 'Новая запись')
const cancelHref = computed(() => props.maintenanceId
  ? `#maintenance/${props.maintenanceId}${navigationSuffix.value}`
  : props.initialVehicleId ? `#vehicles/${props.initialVehicleId}` : '#scheduler')
const canOpenCatalog = computed(() => props.context.shell.menu?.some(item => item.code === 'catalog' && item.isVisible))
const payableAmount = computed(() =>
  Math.max(0,
    serviceLines.value.reduce((total, line) => total + (toNullableNumber(line.price) ?? 0), 0)
      - (toNullableNumber(form.discountAmount) ?? 0))
)
const remainingAmount = computed(() => Math.max(0, payableAmount.value - (toNullableNumber(form.paidAmount) ?? 0)))

function formatMoney(value: number) {
  return new Intl.NumberFormat('ru-RU', { style: 'currency', currency: 'RUB', maximumFractionDigits: 2 }).format(value)
}

const buyerOptions = computed(() => [
  { value: '', label: 'Не указан' },
  ...clients.value.map(client => ({ value: client.id, label: [client.name, client.phone || client.details].filter(Boolean).join(' · ') }))
])

const savedStatus = ref<number | null>(null)
const statusOptions = computed(() => {
  const allowed = savedStatus.value === 1 ? [1, 2, 4]
    : savedStatus.value === 2 ? [2, 4]
      : savedStatus.value === 4 ? [4, 1]
        : [savedStatus.value ?? 1]

  return [
    { value: '1', label: savedStatus.value === 4 ? 'Возобновить запись' : 'Запланировано' },
    { value: '2', label: 'В работе' },
    { value: '3', label: 'Выполнено' },
    { value: '4', label: 'Отменено' }
  ].filter(option => allowed.includes(Number(option.value)))
})

const typeOptions = [
  { value: '1', label: 'Уход / мойка' },
  { value: '2', label: 'Ремонт' },
  { value: '100', label: 'Другое' }
]
function createServiceLine(detail?: WorkspaceMaintenanceDetail): ServiceLine {
  const item = services.value.find(service => service.id === detail?.catalogItemId)

  return {
    id: crypto.randomUUID(),
    detailId: detail?.detailId,
    employeeId: detail?.employeeId, employeeName: detail?.employeeName,
    catalogItemId: detail?.catalogItemId ?? item?.id ?? '',
    savedCatalogItemId: detail?.catalogItemId ?? '',
    savedName: detail?.name ?? '',
    description: detail?.description ?? '',
    quantity: detail?.quantity ?? 1,
    price: detail?.price !== null && detail?.price !== undefined ? String(detail.price) : '',
    vatRateCode: detail?.vatRateCode ?? ''
  }
}

function getServiceName(line: ServiceLine) {
  return line.catalogItemId === line.savedCatalogItemId && line.savedName
    ? line.savedName
    : services.value.find(item => item.id === line.catalogItemId)?.name ?? ''
}

const postOptions = computed(() => [...new Set([
  '',
  form.location,
  ...posts.value
    .filter(item => item.isActive !== false && item.name?.trim())
    .map(item => item.name.trim())
])].map(value => ({ value, label: value || 'Без поста' })))
const materialOptions = computed(() => [
  { value: '', label: 'Без материала' },
  ...materials.value
    .filter(item => item.isActive !== false && item.name?.trim())
    .map(item => ({ value: item.id, label: item.name.trim() }))
])

function getVehicleLabel(vehicle: WorkspaceVehicle) {
  return getVehicleDisplayName(vehicle, true)
}

function toNullableString(value: string | number | null | undefined) {
  const trimmed = String(value ?? '').trim()
  return trimmed.length ? trimmed : null
}

function toNullableNumber(value: string | number | null | undefined, max = 999999999) {
  const normalized = String(value ?? '').replace(',', '.').trim()
  if (!normalized) return null
  const parsed = Number(normalized)
  return Number.isFinite(parsed) && parsed >= 0 && parsed <= max ? parsed : null
}

function toTimeValue(value: string) {
  return value ? `${value}:00` : null
}

function createMaterialLine(detail?: WorkspaceMaintenanceDetail): MaterialLine {
  materialLineSeq += 1
  return {
    id: `material-${materialLineSeq}`,
    detailId: detail?.detailId,
    materialCatalogItemId: detail?.materialCatalogItemId ?? '',
    materialName: detail?.materialName ?? '',
    materialQuantity: detail?.materialQuantity !== null && detail?.materialQuantity !== undefined ? String(detail.materialQuantity) : '',
    materialUnit: normalizeMaterialUnit(detail?.materialUnit),
    materialUnitCost: detail?.materialUnitCost !== null && detail?.materialUnitCost !== undefined ? String(detail.materialUnitCost) : ''
  }
}

function hasMaterial(detail?: WorkspaceMaintenanceDetail | null) {
  return Boolean(
    detail?.materialCatalogItemId ||
    detail?.materialName ||
    detail?.materialQuantity !== null && detail?.materialQuantity !== undefined
  )
}

function isMaterialLineEmpty(line: MaterialLine) {
  return !line.materialCatalogItemId &&
    !String(line.materialName ?? '').trim() &&
    !String(line.materialQuantity ?? '').trim()
}

function getMaterialName(materialCatalogItemId: string) {
  return materials.value.find(item => item.id === materialCatalogItemId)?.name ?? ''
}

function getMaterialDisplayName(line: MaterialLine) {
  return line.materialCatalogItemId
    ? getMaterialName(line.materialCatalogItemId) || String(line.materialName ?? '').trim() || 'Материал'
    : String(line.materialName ?? '').trim()
}

function addMaterialLine() {
  materialLines.value.push(createMaterialLine())
}

function removeMaterialLine(lineId: string) {
  materialLines.value = materialLines.value.filter(line => line.id !== lineId)
}

function revealInvalidField(event: Event) {
  let section = (event.target as HTMLElement).closest('details')
  while (section) {
    section.open = true
    section = section.parentElement?.closest('details') ?? null
  }
}

function showFormError(message: string) {
  errorMessage.value = message
  errorDialogMessage.value = message
}

function collectFiles(event: Event) {
  const input = event.target as HTMLInputElement
  const files = Array.from(input.files ?? [])
  input.value = ''
  return files
}

function validatePhotoFiles(files: File[]) {
  if (files.length > maxMaintenancePhotoFiles) {
    return 'За одну загрузку можно добавить не более 10 фото.'
  }

  const totalSize = files.reduce((sum, file) => sum + file.size, 0)
  if (totalSize > maxMaintenancePhotoBatchSize) {
    return 'Фото услуги: максимум 100 МБ за одну загрузку.'
  }

  return ''
}

async function reloadPhotos() {
  if (!props.maintenanceId) {
    photos.value = []
    return
  }

  const maintenanceId = props.maintenanceId
  const currentOrganizationId = organizationId.value
  const loadedPhotos = await loadMaintenancePhotos(currentOrganizationId, maintenanceId)
  if (props.maintenanceId !== maintenanceId || organizationId.value !== currentOrganizationId) return
  photos.value = loadedPhotos
  hasLoadedPhotos.value = true
}

async function loadPhotoHistory(event: Event) {
  if (!(event.target as HTMLDetailsElement).open || hasLoadedPhotos.value || isSavingPhotos.value) return
  isSavingPhotos.value = true
  photoMessage.value = ''
  try {
    await reloadPhotos()
  } catch (error) {
    photoMessage.value = error instanceof Error ? error.message : 'Не удалось загрузить сохранённые фото.'
  } finally {
    isSavingPhotos.value = false
  }
}

async function uploadPhotos(event: Event) {
  const files = collectFiles(event)
  if (!props.maintenanceId || !files.length || isSavingPhotos.value || !canEditPhotos.value) {
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
    await uploadMaintenancePhotos(organizationId.value, props.maintenanceId, files, Number(uploadStage.value) as MaintenancePhotoStage)
    await reloadPhotos()
  } catch (error) {
    photoMessage.value = error instanceof Error ? error.message : 'Не удалось загрузить фото услуги.'
  } finally {
    isSavingPhotos.value = false
  }
}

function removePhoto(photo: WorkspaceMaintenancePhoto) {
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
  if (!props.maintenanceId || !photo || isSavingPhotos.value || !canEditPhotos.value) {
    return
  }

  isSavingPhotos.value = true
  photoMessage.value = ''

  try {
    await deleteMaintenancePhoto(organizationId.value, props.maintenanceId, photo.photoId)
    await reloadPhotos()
  } catch (error) {
    photoMessage.value = error instanceof Error ? error.message : 'Не удалось удалить фото услуги.'
  } finally {
    isSavingPhotos.value = false
    photoDeleteConfirm.value = null
  }
}

function openSavedMaintenance() {
  if (photoPromptMaintenanceId.value) {
    window.location.hash = `#maintenance/${photoPromptMaintenanceId.value}${navigationSuffix.value}`
  }
}

function skipSavedMaintenancePhotos() {
  const vehicleId = form.vehicleId || selectedVehicle.value?.id
  photoPromptMaintenanceId.value = ''
  window.location.hash = vehicleId ? `#vehicles/${vehicleId}` : '#vehicles'
}

function resetForCreate() {
  form.vehicleId = props.initialVehicleId || ''
  form.buyerClientId = ''
  form.status = isSimpleMaintenanceWorkflow.value ? '3' : '1'
  form.type = '1'
  form.plannedDate = props.initialDate || formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')
  form.plannedEndDate = form.plannedDate
  form.plannedStartTime = props.initialStartTime || ''
  form.plannedEndTime = props.initialEndTime || ''
  if (form.plannedStartTime && form.plannedEndTime && form.plannedEndTime <= form.plannedStartTime) {
    const end = new Date(`${form.plannedDate}T00:00:00`)
    end.setDate(end.getDate() + 1)
    form.plannedEndDate = formatDateOnly(end)
  }
  form.completedDate = ''
  savedStatus.value = null
  form.location = props.initialLocation || ''
  form.employeeId = ''
  form.performer = ''
  form.mileage = ''
  serviceLines.value = [createServiceLine()]
  form.discountAmount = ''
  form.paidAmount = ''
  form.laborCostAmount = ''
  form.comment = ''
  materialLines.value = []
}

async function loadForm() {
  isLoading.value = true
  isFormReady.value = false
  errorMessage.value = ''
  photoMessage.value = ''
  photos.value = []
  hasLoadedPhotos.value = false
  selectedVehicle.value = null

  try {
    const [catalog, clientOptions] = await Promise.all([
      loadScenarioCatalog(organizationId.value),
      loadClientOptions(organizationId.value)
    ])
    clients.value = clientOptions
    services.value = catalog.services ?? []
    posts.value = catalog.workPosts ?? []
    materials.value = catalog.materials ?? []

    if (props.maintenanceId) {
      const maintenance = await loadMaintenance(organizationId.value, props.maintenanceId)
      form.vehicleId = maintenance.vehicleId
      form.buyerClientId = maintenance.buyerClientId ?? ''
      savedBuyerClientId.value = form.buyerClientId
      form.status = String(maintenance.status || 1)
      form.type = String(maintenance.type || 1)
      form.plannedDate = maintenance.plannedDate?.slice(0, 10) || maintenance.date?.slice(0, 10) || formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')
      form.plannedStartTime = maintenance.plannedStartTime?.slice(0, 5) || ''
      form.plannedEndDate = maintenance.plannedEndDate?.slice(0, 10) || form.plannedDate
      form.plannedEndTime = maintenance.plannedEndTime?.slice(0, 5) || ''
      form.completedDate = maintenance.completedDate?.slice(0, 10) || ''
      savedStatus.value = maintenance.status
      form.location = maintenance.location || ''
      form.employeeId = maintenance.employeeId || ''
      form.performer = maintenance.performer || ''
      form.mileage = maintenance.mileage ? String(maintenance.mileage) : ''
      form.comment = maintenance.comment || ''

      const serviceDetails = maintenance.maintenanceDetails.filter(detail => !hasMaterial(detail))
      serviceLines.value = serviceDetails.map(createServiceLine)
      form.discountAmount = maintenance.discountAmount ? String(maintenance.discountAmount) : ''
      maintenanceVersion.value = maintenance.version
      form.paidAmount = maintenance.paidAmount ? String(maintenance.paidAmount) : ''
      form.laborCostAmount = maintenance.laborCostAmount ? String(maintenance.laborCostAmount) : ''
      materialLines.value = maintenance.maintenanceDetails
        .filter(detail => hasMaterial(detail))
        .map(detail => createMaterialLine(detail))

      const [vehicleData, photoData] = await Promise.all([
        loadVehicle(organizationId.value, maintenance.vehicleId),
        props.context.shell.scenario?.modules?.photos === false
          ? Promise.resolve([])
          : loadMaintenancePhotos(organizationId.value, props.maintenanceId)
      ])
      selectedVehicle.value = vehicleData
      photos.value = photoData
      hasLoadedPhotos.value = props.context.shell.scenario?.modules?.photos !== false
    } else {
      resetForCreate()
      photos.value = []

      if (form.vehicleId) {
        selectedVehicle.value = await loadVehicle(organizationId.value, form.vehicleId)
        form.buyerClientId = selectedVehicle.value.organizationClientId ?? ''
      }
    }

    isFormReady.value = true
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось открыть форму записи.'
  } finally {
    isLoading.value = false
  }
}

function validateMaterialLines() {
  if (!canUseMaterials.value) return ''
  for (const line of materialLines.value) {
    if (isMaterialLineEmpty(line)) {
      continue
    }

    if (!line.materialCatalogItemId && !String(line.materialName ?? '').trim()) {
      return 'Укажите материал или удалите пустую строку.'
    }

    const quantity = toNullableNumber(line.materialQuantity, maxMaterialQuantity)
    if (String(line.materialQuantity ?? '').trim() && quantity === null) {
      return `Расход материала должен быть от 0 до ${maxMaterialQuantity}.`
    }
    if (quantity !== null && isDiscreteMaterialUnit(line.materialUnit) && !Number.isInteger(quantity)) {
      return `Для единицы «${line.materialUnit}» расход должен быть целым числом.`
    }
  }

  return ''
}

function buildMaterialDetails(): WorkspaceMaintenanceDetail[] {
  return materialLines.value
    .filter(line => !isMaterialLineEmpty(line))
    .map(line => {
      const materialName = getMaterialDisplayName(line)
      const materialQuantity = toNullableNumber(line.materialQuantity, maxMaterialQuantity)
      const materialUnitCost = toNullableNumber(line.materialUnitCost)
      const materialTotalCost = materialQuantity !== null && materialUnitCost !== null
        ? Math.round(materialQuantity * materialUnitCost * 100) / 100
        : null

      return {
        detailId: line.detailId,
        name: materialName,
        description: null,
        price: null,
        catalogItemId: null,
        materialCatalogItemId: toNullableString(line.materialCatalogItemId),
        materialName,
        materialQuantity,
        materialUnit: normalizeMaterialUnit(line.materialUnit),
        materialUnitCost,
        materialTotalCost
      }
    })
}

function buildPayload(): WorkspaceMaintenanceSaveRequest {
  const isPlannedBooking = Boolean(props.initialDate) || form.plannedDate > formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')
  const status = !isEditMode.value && isSimpleMaintenanceWorkflow.value ? (isPlannedBooking ? 1 : 3) : (Number(form.status) || 1)
  const completedDate = status !== 3 ? null
    : !isEditMode.value && isSimpleMaintenanceWorkflow.value
      ? form.plannedDate || formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')
      : form.completedDate || null

  return {
    buyerClientId: form.buyerClientId || null,
    hasBuyerSelection: !isEditMode.value,
    changeBuyer: isEditMode.value && form.buyerClientId !== savedBuyerClientId.value,
    status,
    version: maintenanceVersion.value,
    type: Number(form.type) || 1,
    plannedDate: form.plannedDate || null,
    plannedEndDate: form.plannedEndDate || form.plannedDate || null,
    plannedStartTime: toTimeValue(form.plannedStartTime),
    plannedEndTime: toTimeValue(form.plannedEndTime),
    completedDate,
    location: toNullableString(form.location),
    employeeId: form.employeeId || null,
    performer: null,
    mileage: toNullableNumber(form.mileage),
    comment: toNullableString(form.comment),
    discountAmount: toNullableNumber(form.discountAmount),
    paidAmount: null,
    laborCostAmount: toNullableNumber(form.laborCostAmount),
    details: [
      ...serviceLines.value.map(line => ({
        detailId: line.detailId,
        name: getServiceName(line),
        description: toNullableString(line.description),
        price: toNullableNumber(line.price),
        vatRateCode: line.vatRateCode || null,
        quantity: line.quantity,
        employeeId: line.employeeId || null,
        catalogItemId: line.catalogItemId || null,
        materialCatalogItemId: null,
        materialName: null,
        materialQuantity: null,
        materialUnit: null,
        materialUnitCost: null,
        materialTotalCost: null
      })),
      ...(canUseMaterials.value ? buildMaterialDetails() : [])
    ],
    linkedDefectIds: null
  }
}

async function saveMaintenance() {
  if (isSaving.value || !isFormReady.value) {
    return
  }

  errorMessage.value = ''
  errorDialogMessage.value = ''
  const serviceValidationError = servicesEditor.value?.validate()
  if (serviceValidationError === undefined) { errorMessage.value = 'Не удалось проверить строки работ.'; return }
  if (serviceValidationError) { errorMessage.value = serviceValidationError; return }

  if (isReadOnly.value) {
    showFormError(readOnlyMessage.value)
    return
  }

  if (!form.vehicleId) {
    showFormError('Выберите автомобиль для записи.')
    return
  }

  if (!form.plannedDate) {
    showFormError('Укажите дату записи.')
    return
  }

  if (!serviceLines.value.length || serviceLines.value.some(line => !getServiceName(line).trim())) {
    showFormError('Укажите название работы в каждой строке.')
    return
  }

  const materialError = validateMaterialLines()
  if (materialError) {
    if (materialSection.value) materialSection.value.open = true
    showFormError(materialError)
    return
  }

  isSaving.value = true

  try {
    if (bookingTimes.value && !await bookingTimes.value.validate()) return

    const payload = buildPayload()

    if (props.maintenanceId) {
      await updateMaintenance(organizationId.value, props.maintenanceId, payload)
      emit('saved', props.maintenanceId, serviceDuration.value === null
        ? getUnknownServiceDurationSaveMessage(Boolean(form.plannedEndTime))
        : undefined)
      emit('changed')
      window.location.hash = `#maintenance/${props.maintenanceId}${navigationSuffix.value}`
    } else {
      const maintenanceId = await createMaintenance(organizationId.value, form.vehicleId, payload)
      trackWorkspaceGoal('work_created', {
        method: 'full_form',
        scenario: props.context.organization.primaryScenarioCode ?? 'organization',
        is_demo: props.context.currentSubscription?.subscriptionPlan?.isDemoPlan === true
      })
      emit('saved', maintenanceId, serviceDuration.value === null
        ? getUnknownServiceDurationSaveMessage(Boolean(form.plannedEndTime))
        : undefined)
      emit('changed')
      if (isDetailingScenario.value && props.initialVehicleId && canEditPhotos.value) {
        photoPromptMaintenanceId.value = maintenanceId
      } else {
        window.location.hash = `#maintenance/${maintenanceId}${navigationSuffix.value}`
      }
    }
  } catch (error) {
    showFormError(error instanceof Error ? error.message : 'Не удалось сохранить услугу.')
  } finally {
    isSaving.value = false
  }
}

onMounted(loadForm)
watch(() => props.maintenanceId, loadForm)
watch(() => form.vehicleId, vehicleId => {
  if (isEditMode.value || !isFormReady.value) return
  form.buyerClientId = (selectedVehicle.value?.id === vehicleId ? selectedVehicle.value.organizationClientId : null)
    ?? ''
})
watch(() => form.plannedDate, (date, previous) => {
  if (!form.plannedEndDate || form.plannedEndDate === previous || form.plannedEndDate < date) form.plannedEndDate = date
})
</script>

<template>
  <UiCard page :title="title" class="booking-editor">
    <template #actions>
      <WorkspacePageHelpButton
        section-code="maintenance"
        :entity-id="maintenanceId || 'new'"
        :action="maintenanceId ? 'edit' : ''"
        :title="title"
      />
    </template>
    <UiLoader v-if="isLoading" text="Открываем запись…" />
    <div v-else-if="!isFormReady" class="booking-error" role="alert">
      <p>{{ errorMessage }}</p>
      <UiButton variant="secondary" @click="loadForm">Повторить</UiButton>
    </div>
    <form v-else class="booking-form" @submit.prevent="saveMaintenance" @invalid.capture="revealInvalidField">
      <WorkspaceAccessNotice
        v-if="isReadOnly"
        :can-manage-billing="context.organization.canManageBilling === true"
        :message="readOnlyMessage"
        :href="billingHref"
      />
      <div class="booking-context">
        <div v-if="isEditMode && selectedVehicle" class="booking-context__vehicle">
          <a :href="`#vehicles/${selectedVehicle.id}`">{{ getVehicleLabel(selectedVehicle) }}</a>
          <span v-if="selectedVehicle.organizationClientName">{{ selectedVehicle.organizationClientName }}</span>
          <span v-if="statusOptions.length === 1" class="booking-context__status">Статус: {{ statusOptions[0]?.label }}</span>
        </div>
        <template v-else>
          <InquiryVehiclePicker
            v-model="form.vehicleId"
            class="booking-vehicle"
            :organization-id="organizationId"
            client-id=""
            @selected="selectedVehicle = $event"
            :disabled="isSaving || isEditMode || isReadOnly"
          />
        </template>
        <UiSelect
          v-model="form.buyerClientId"
          label="Покупатель по заказу"
          :options="buyerOptions"
          :disabled="isSaving || isReadOnly"
        />
        <UiSelect
          v-if="isEditMode && !isSimpleMaintenanceWorkflow && statusOptions.length > 1"
          v-model="form.status"
          label="Статус"
          :options="statusOptions"
          :searchable="false"
          :disabled="isSaving || isReadOnly"
        />
      </div>
      <div class="booking-layout">
        <div class="booking-main">
          <section class="booking-section" aria-labelledby="booking-visit-title">
            <div class="booking-heading ui-section-header">
              <h3 id="booking-visit-title">Дата и место</h3>
            </div>
            <div class="booking-fields booking-fields--visit">
              <UiDatePicker v-model="form.plannedDate" label="Дата начала" :disabled="isSaving || isReadOnly" required />
              <UiDatePicker v-model="form.plannedEndDate" @update:model-value="bookingTimes?.useManualEnd()" label="Дата окончания" :disabled="isSaving || isReadOnly" required />
              <div class="booking-linked">
                <UiSelect v-model="form.location" label="Пост / место" :options="postOptions" :disabled="isSaving || isReadOnly" />
                <a v-if="canOpenCatalog && !isSaving" href="#catalog?catalog=work_post" aria-label="Открыть справочник постов">
                  Справочник ↗
                </a>
              </div>
              <BookingTimeFields
                ref="bookingTimes"
                v-model:start="form.plannedStartTime"
                v-model:end="form.plannedEndTime"
                :organization-id="organizationId"
                :date="form.plannedDate"
                v-model:end-date="form.plannedEndDate"
                :duration-minutes="serviceDuration"
                :auto-duration="!isEditMode"
                :location="form.location"
                :disabled="isSaving || isReadOnly"
                :exclude-maintenance-id="maintenanceId"
                :cancelled="Number(form.status) === 4"
                class="booking-times"
              />
            </div>
            <details class="booking-disclosure booking-extra">
              <summary><strong>Дополнительно</strong><span>Сотрудник, тип работ</span></summary>
              <div class="booking-disclosure__body booking-fields booking-fields--visit">
                <EmployeePicker v-if="canUseTeam" v-model="form.employeeId" :current-name="form.performer" :organization-id="organizationId" :planned-start-local="plannedStartLocal" :planned-end-local="plannedEndLocal" :disabled="isSaving || isReadOnly" />
                <div v-else class="ui-field booking-team-field">
                  <span class="ui-field-label">Сотрудник</span>
                  <UiButton variant="secondary" class="booking-team-field__button" aria-haspopup="dialog" :aria-expanded="teamPreviewOpen" @click="teamPreviewOpen = true">
                    <span class="booking-team-field__name" :title="form.performer || undefined">{{ form.performer || (form.employeeId ? 'Назначен' : 'Назначить') }}</span><span class="booking-team-field__badge">PRO</span>
                  </UiButton>
                </div>
                <UiSelect
                  v-model="form.type"
                  label="Тип работ"
                  :options="typeOptions"
                  :searchable="false"
                  :disabled="isSaving || isReadOnly"
                />
                <UiDatePicker
                  v-if="savedStatus === 3 && Number(form.status) === 3"
                  v-model="form.completedDate"
                  label="Дата выполнения"
                  :disabled="isSaving || isReadOnly"
                />
                <UiInput
                  v-if="!isDetailingScenario"
                  v-model="form.mileage"
                  label="Пробег"
                  type="number"
                  :disabled="isSaving || isReadOnly"
                />
              </div>
            </details>
          </section>
          <section class="booking-section" aria-labelledby="booking-service-title">
            <div class="booking-heading ui-section-header">
              <h3 id="booking-service-title">Услуги</h3>
              <a v-if="canOpenCatalog && !isSaving" href="#catalog?catalog=service">Справочник услуг ↗</a>
            </div>
            <MaintenanceServicesEditor :can-assign-employees="context.shell.scenario?.modules?.members !== false" ref="servicesEditor" @duration-change="serviceDuration = $event" v-model="serviceEditorRows" :can-open-catalog="canOpenCatalog" :organization-id="context.organization.id" :default-vat-rate-code="isEditMode ? undefined : 'without_vat'" :planned-start-local="plannedStartLocal" :planned-end-local="plannedEndLocal" :disabled="isSaving || isReadOnly" />
          </section>
          <details v-if="isDetailingScenario" ref="materialSection" class="booking-disclosure booking-section booking-materials-section">
            <summary><strong>Материалы</strong><span>{{ materialLines.length ? materialLines.length + ' добавлено' : 'Не добавлены' }}</span></summary>
            <p v-if="!canUseMaterials" role="status">Изменение материалов — <a href="#billing">доступно в PRO</a>. Сохранённые строки остаются в заказе.</p>
            <div v-if="canUseMaterials" class="booking-disclosure__body">
              <div class="booking-heading ui-section-header">
                <a v-if="canOpenCatalog && !isSaving" href="#catalog?catalog=material">Справочник ↗</a>
                <UiButton v-if="!isReadOnly" variant="secondary" :disabled="isSaving" @click="addMaterialLine">＋ Добавить материал</UiButton>
              </div>
              <div v-if="!materialLines.length" class="booking-empty">Материалы не добавлены</div>
              <div v-else class="booking-materials">
                <div v-for="(line, index) in materialLines" :key="line.id" class="booking-material">
                  <div class="booking-material__heading">
                    <span>Материал {{ index + 1 }}</span>
                    <button
                      v-if="!isReadOnly"
                      type="button"
                      :disabled="isSaving"
                      :aria-label="`Удалить материал ${index + 1}`"
                      @click="removeMaterialLine(line.id)"
                    >
                      Удалить
                    </button>
                  </div>
                  <div class="booking-material__fields">
                    <div class="booking-material__name">
                      <UiSelect
                        v-model="line.materialCatalogItemId"
                      @update:model-value="line.materialUnitCost = ''"
                        label="Материал"
                        :options="materialOptions"
                        :disabled="isSaving || isReadOnly"
                      />
                      <UiInput
                        v-if="!line.materialCatalogItemId"
                        v-model="line.materialName"
                      @update:model-value="line.materialUnitCost = ''"
                        label="Название вручную"
                        :disabled="isSaving || isReadOnly"
                      />
                    </div>
                    <UiNumberInput
                      v-model="line.materialQuantity"
                      @update:model-value="line.materialUnitCost = ''"
                      label="Расход"
                      :min="0"
                      :max="999999999"
                      :step="isDiscreteMaterialUnit(line.materialUnit) ? 1 : 0.001"
                      :integer="isDiscreteMaterialUnit(line.materialUnit)"
                      :disabled="isSaving || isReadOnly"
                    />
                    <UiSelect
                      v-model="line.materialUnit"
                      @update:model-value="line.materialUnitCost = ''"
                      label="Ед. изм."
                      :options="materialUnitOptions"
                      :searchable="false"
                      :disabled="isSaving || isReadOnly"
                    />
                  </div>
                </div>
              </div>
            </div>
          </details>
          <details class="booking-disclosure booking-section" :open="Boolean(form.comment)">
            <summary><strong>Комментарий к записи</strong><span v-if="!form.comment">Добавить</span></summary>
            <div class="booking-disclosure__body">
              <UiTextarea
                v-model="form.comment"
                label="Комментарий к записи"
                placeholder="Что важно учесть при выполнении и выдаче"
                :rows="3"
                :disabled="isSaving || isReadOnly"
              />
            </div>
          </details>
          <details v-if="isEditMode && context.shell.scenario?.modules?.photos !== false" class="booking-disclosure booking-section booking-photos-section" @toggle="loadPhotoHistory">
            <summary><strong>Фото до / после</strong><span>{{ photos.length ? photos.length + ' фото' : hasLoadedPhotos ? 'Фото нет' : 'Показать фото' }}</span></summary>
            <div class="booking-disclosure__body">
              <div v-if="canEditPhotos" class="booking-heading ui-section-header">
                <UiSelect v-model="uploadStage" label="Этап съёмки" :options="photoStageOptions" required :searchable="false" :disabled="isSavingPhotos || isSaving" />
                <label class="booking-upload">
                  ＋ Добавить фото
                  <input
                    type="file"
                    accept="image/*"
                    multiple
                    :disabled="isSavingPhotos || isSaving"
                    aria-label="Добавить фото записи"
                    @change="uploadPhotos"
                  >
                </label>
              </div>
              <UiLoader v-if="isSavingPhotos" compact text="Обновляем фото…" />
              <div v-if="photoMessage" class="booking-error" role="alert">{{ photoMessage }}</div>
              <div v-if="photos.length" class="booking-photos">
                <article v-for="(photo, index) in photos" :key="photo.photoId" class="booking-photo">
                  <button
                    type="button"
                    class="photo-preview-button"
                    :aria-label="`Открыть фото ${index + 1}`"
                    @click="previewPhoto = photo"
                  >
                    <img :src="photo.url" :alt="`Фото записи ${index + 1}`" loading="lazy">
                  </button>
                  <span>{{ photoStageOptions.find(stage => stage.value === String(photo.stage))?.label }}</span>
                  <button
                    v-if="canEditPhotos"
                    type="button"
                    :disabled="isSavingPhotos || isSaving"
                    :aria-label="`Удалить фото ${index + 1}`"
                    @click="removePhoto(photo)"
                  >
                    Удалить
                  </button>
                </article>
              </div>
              <div v-else class="booking-empty">Фото пока нет</div>
            </div>
          </details>
        </div>
        <aside class="booking-finance" aria-labelledby="booking-finance-title">
          <h3 id="booking-finance-title" class="ui-section-header">Расчёт</h3>
          <div class="booking-total">
            <span>К оплате со скидкой</span>
            <strong>{{ formatMoney(payableAmount) }}</strong>
          </div>
          <div class="booking-finance__fields">
            <UiMoneyInput
              v-model="form.discountAmount"
              label="Скидка, ₽"
              :min="0"
              :max="999999999"
              :step="0.01"
              :disabled="isSaving || isReadOnly"
            />

          </div>
          <dl class="booking-balance">
            <dt>Осталось оплатить</dt>
            <dd>{{ formatMoney(remainingAmount) }}</dd>
          </dl>
          <details class="booking-disclosure booking-labor">
            <summary><strong>Себестоимость труда</strong><span>{{ form.laborCostAmount ? formatMoney(Number(form.laborCostAmount)) : 'Не указана' }}</span></summary>
            <div class="booking-disclosure__body">
            <UiMoneyInput
              v-model="form.laborCostAmount"
              label="Себестоимость труда, ₽"
              :min="0"
              :max="999999999"
              :step="0.01"
              :disabled="isSaving || isReadOnly"
            />
            </div>
          </details>
        </aside>
      </div>
      <div class="booking-actions">
        <UiButton v-if="!isReadOnly" type="submit" :disabled="isSaving || isSavingPhotos">
          {{ isSaving ? 'Сохраняем…' : isEditMode ? 'Сохранить изменения' : 'Создать запись' }}
        </UiButton>
        <UiButton variant="secondary" :disabled="isSaving || isSavingPhotos" :href="cancelHref">
          {{ isReadOnly ? 'Назад' : 'Отмена' }}
        </UiButton>
      </div>
    </form>
    <UiAlertDialog
      :open="Boolean(errorDialogMessage)"
      title="Не удалось сохранить"
      :message="errorDialogMessage"
      tone="danger"
      @close="errorDialogMessage = ''"
    />
    <UiConfirmDialog
      :open="canEditPhotos && Boolean(photoPromptMaintenanceId)"
      title="Добавить фото записи"
      message="Запись создана. Можно сразу приложить фото осмотра, результата или пропустить этот шаг."
      confirm-text="Добавить фото"
      cancel-text="Пропустить"
      @close="skipSavedMaintenancePhotos"
      @confirm="openSavedMaintenance"
    />
    <UiConfirmDialog
      :open="Boolean(photoDeleteConfirm)"
      title="Удалить фото"
      message="Фото записи будет удалено без возможности восстановления."
      confirm-text="Удалить"
      :busy="isSavingPhotos"
      tone="danger"
      @close="closePhotoDeleteConfirmation"
      @confirm="confirmRemovePhoto"
    />
    <WorkspaceProFeatureDialog v-if="teamPreviewOpen" :open="teamPreviewOpen" feature="team" :can-manage-billing="canManageBilling" @close="teamPreviewOpen = false" />
    <UiImagePreview :src="previewPhoto?.url" @close="previewPhoto = null" />
  </UiCard>

</template>

<style scoped>
.booking-context {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 210px;
  align-items: center;
  gap: 16px;
  padding-bottom: 20px;
  margin-bottom: 20px;
  border-bottom: 1px solid var(--crm-line, #e3e8ee);
}
.booking-context__vehicle {
  display: grid;
  gap: 5px;
  min-width: 0;
}
.booking-context__vehicle a {
  color: var(--crm-text, #202733);
  font-size: 17px;
  font-weight: 700;
  text-decoration: none;
  overflow-wrap: anywhere;
}
.booking-context__vehicle a:hover { text-decoration: underline; }
.booking-context__vehicle > span:not(.booking-context__status) {
  color: var(--crm-text-muted, #64748b);
  font-size: 13px;
}
.booking-context__status { justify-self:start; padding:4px 9px; border:1px solid var(--crm-line, #d9e0e9); border-radius:6px; background:var(--crm-surface-soft, #f6f7f9); color:var(--crm-text-muted, #64748b); font-size:12px; font-weight:500; }
.booking-disclosure { min-width: 0; }
.booking-disclosure > summary {
  display: flex;
  align-items: center;
  gap: 12px;
  list-style: none;
  cursor: pointer;
  min-height: 32px;
  color: var(--crm-text, #202733);
  font-size: 13px;
}
.booking-disclosure > summary::-webkit-details-marker { display: none; }
.booking-disclosure > summary::before {
  content: '';
  width: 6px;
  height: 6px;
  border-right: 1.5px solid currentColor;
  border-bottom: 1.5px solid currentColor;
  transform: rotate(-45deg);
  flex-shrink: 0;
}
.booking-disclosure[open] > summary::before { transform: rotate(45deg); }
.booking-disclosure > summary strong { font-weight: 600; flex-shrink: 0; }
.booking-disclosure > summary span {
  color: var(--crm-text-muted, #64748b);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  min-width: 0;
  font-size: 12px;
}
.booking-disclosure > summary:focus-visible {
  outline: 2px solid var(--crm-accent, #c45112);
  outline-offset: 3px;
}
.booking-disclosure__body { padding-top: 12px; }
.booking-extra { margin-top: 12px; }
.booking-description { margin-top: -6px; }
.booking-description > summary { font-size: 12px; }
.booking-section.booking-disclosure { padding-top: 12px; }
.booking-labor > summary { flex-wrap: wrap; gap: 6px 10px; }
.booking-labor > summary span { margin-left: 16px; }
@media (max-width: 640px) {
  .booking-context { grid-template-columns: minmax(0, 1fr); }
  .booking-context__status { justify-self: start; }
}

.booking-service-line + .booking-service-line {
  border-top: 1px solid var(--crm-line, #d9dee3);
  padding-top: 16px;
}

.booking-service-line {
  margin-bottom: 16px;
}


.booking-service-warning {
  padding: 10px 12px;
  border: 1px solid var(--crm-danger-border, #e4b2ae);
  border-radius: 6px;
  background: var(--crm-danger-surface, #fff3f2);
  color: var(--crm-danger-text, #a12b24);
  font-size: 13px;
}

.booking-times { grid-column: span 2; }
@media (max-width: 640px) { .booking-times { grid-column: 1 / -1; } }
.booking-editor, .booking-form, .booking-main, .booking-section {
  min-width: 0;
}

.booking-layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 280px;
  align-items: start;
  gap: 24px;
}

.booking-main {
  display: grid;
  gap: 16px;
}

.booking-section + .booking-section {
  padding-top: 16px;
  border-top: 1px solid var(--crm-line, #e3e8ee);
}

.booking-heading {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px 16px;
  margin-bottom: 16px;
}

.booking-heading h3, .booking-finance h3 {
  margin: 0;
  font-size: 15px;
}

.booking-heading a, .booking-linked > a {
  color: var(--crm-text-muted, #64748b);
  font-size: 12px;
  text-decoration: none;
}

.booking-heading a:hover, .booking-linked > a:hover {
  color: #ce5b23;
  text-decoration: underline;
}

.booking-count {
  color: var(--crm-text-muted, #64748b);
  font-size: 12px;
}

.booking-team-field {
  display: grid;
  gap: 6px;
  min-width: 0;
}

.booking-team-field__button {
  width: 100%;
  justify-content: space-between;
}

.booking-team-field__name {
  min-width: 0;
  overflow: hidden;
  text-align: left;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.booking-team-field__badge {
  flex-shrink: 0;
  color: var(--crm-accent, #c64d0b);
  font-size: 10px;
  font-weight: 700;
}

.booking-fields {
  display: grid;
  gap: 16px;
  align-items: start;
}

.booking-fields--visit {
  grid-template-columns: repeat(4, minmax(0, 1fr));
}

.booking-context .booking-vehicle {
  min-width: 0;
}

.booking-fields--service {
  grid-template-columns: minmax(0, 1fr) 160px;
}

.booking-fields--service.booking-fields--removable {
  grid-template-columns: minmax(0, 1fr) 160px auto;
}

.booking-service-remove {
  align-self: end;
}


.booking-wide {
  grid-column: 1 / -1;
}

.booking-linked {
  display: grid;
  gap: 6px;
  min-width: 0;
}

.booking-linked > a {
  justify-self: start;
}

.booking-editor :deep(.ui-field) {
  min-width: 0;
  margin: 0;
}

.booking-editor :deep(.ui-field input), .booking-editor :deep(.ui-field textarea) {
  min-width: 0;
  max-width: 100%;
}

.booking-editor :deep(.multiselect-single-label) {
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
  display: block;
  padding-top: 7px;
}

.booking-editor :deep(.multiselect-option) {
  white-space: normal;
  overflow-wrap: anywhere;
}

.booking-finance {
  min-width: 0;
  padding: 20px;
  border: 1px solid var(--crm-line, #e1e6ed);
  border-radius: 8px;
  background: var(--crm-surface-soft, #f7f9fb);
}

.booking-total {
  display: grid;
  gap: 6px;
  margin: 16px 0;
}

.booking-total span {
  font-size: 12px;
  color: var(--crm-text-muted, #64748b);
}

.booking-total strong {
  font-size: 24px;
  font-weight: 650;
  overflow-wrap: anywhere;
}

.booking-finance__fields {
  display: grid;
  gap: 16px;
}

.booking-balance {
  display: flex;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 8px;
  margin: 20px 0;
  padding: 16px 0;
  border-block: 1px solid var(--crm-line, #e1e6ed);
  font-size: 13px;
}

.booking-balance dt {
  font-weight: 400;
  color: var(--crm-text-muted, #64748b);
}

.booking-balance dd {
  margin: 0;
  font-weight: 600;
}

.booking-empty {
  padding: 20px;
  border: 1px dashed var(--crm-line, #dce2e9);
  border-radius: 6px;
  font-size: 13px;
  color: var(--crm-text-muted, #64748b);
  text-align: center;
}

.booking-materials {
  display: grid;
  gap: 14px;
}

.booking-material {
  min-width: 0;
  padding: 16px;
  border: 1px solid var(--crm-line, #e1e6ed);
  border-radius: 7px;
}

.booking-material__heading {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
  font-size: 12px;
  color: var(--crm-text-muted, #64748b);
}

.booking-material__fields {
  display: grid;
  grid-template-columns: minmax(0, 2fr) repeat(2, minmax(0, 1fr));
  align-items: start;
  gap: 12px;
}

.booking-material__name {
  min-width: 0;
  display: grid;
  gap: 12px;
}

.booking-material__heading button, .booking-photo button:not(.photo-preview-button) {
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--crm-text-muted, #64748b);
  font-size: 12px;
  cursor: pointer;
  text-decoration: underline;
  text-underline-offset: 3px;
}

.booking-actions {
  position: sticky;
  bottom: 0;
  z-index: 2;
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  margin-top: 26px;
  padding: 16px 0 0;
  border-top: 1px solid var(--crm-line, #e1e6ed);
  background: var(--crm-surface, #fff);
}

.booking-actions :deep(.ui-button--primary) {
  background: #c94f16;
  border-color: #c94f16;
  color: #fff;
}

.booking-upload {
  position: relative;
  padding: 7px 10px;
  border: 1px solid var(--crm-line, #dce2e9);
  border-radius: 6px;
  color: var(--crm-text, #202833);
  background: var(--crm-surface, #fff);
  font-size: 12px;
  cursor: pointer;
}

.booking-upload input {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  opacity: 0;
  cursor: pointer;
}

.booking-photos {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, 160px), 1fr));
  gap: 12px;
}

.booking-photo {
  display: grid;
  gap: 8px;
  min-width: 0;
}

.booking-photo .photo-preview-button {
  display: block;
  border: 1px solid var(--crm-line, #e1e6ed);
  border-radius: 6px;
  overflow: hidden;
}

.booking-photo img {
  display: block;
  width: 100%;
  aspect-ratio: 4 / 3;
  object-fit: cover;
}

.booking-photo button:not(.photo-preview-button) {
  justify-self: start;
}

.booking-error {
  padding: 14px;
  margin-bottom: 16px;
  border: 1px solid #cd764f;
  border-radius: 6px;
  font-size: 13px;
  color: var(--crm-text, #9d3619);
}

.booking-upload:focus-within, .booking-editor button:focus-visible, .booking-editor a:focus-visible {
  outline: 2px solid #e87532;
  outline-offset: 3px;
}

@media (min-width: 1900px) {
  .booking-layout {
    grid-template-columns: minmax(0, 1fr) 360px;
    gap: 40px;
  }

  .booking-fields--visit {
    grid-template-columns: repeat(4, minmax(0, 1fr));
  }
}

@media (max-width: 1200px) {
  .booking-layout {
    grid-template-columns: minmax(0, 1fr) 280px;
    gap: 22px;
  }

  .booking-fields--visit {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .booking-material__fields {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .booking-material__name {
    grid-column: 1 / -1;
  }
}

@media (max-width: 950px) {
  .booking-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .booking-finance__fields {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .booking-labor {
    max-width: 360px;
  }
}

@media (max-width: 600px) {
  .booking-fields--visit, .booking-fields--service {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .booking-fields--service > :first-child {
    grid-column: 1 / -1;
  }

  .booking-fields--visit > :first-child {
    grid-column: 1 / -1;
  }

  .booking-material {
    padding: 12px;
  }

  .booking-material__fields {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .booking-finance {
    padding: 16px;
  }

  .booking-actions {
    padding-bottom: 8px;
  }
}
.booking-editor :deep(textarea) {
  font: inherit;
  font-size: 14px;
  font-weight: 400;
  line-height: 1.5;
}
.photo-preview-button {
  width: 100%;
  padding: 0;
  background: var(--crm-surface-soft, #f5f7f9);
  cursor: zoom-in;
}
@media (max-width: 640px) {
  .booking-fields--service.booking-fields--removable {
    grid-template-columns: minmax(0, 1fr) auto;
  }
  .booking-fields--removable > :first-child {
    grid-column: 1 / -1;
  }
}
</style>

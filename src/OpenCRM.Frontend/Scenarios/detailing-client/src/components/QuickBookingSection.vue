<script setup lang="ts">
import {
  BookingTimeFields,
  QuickBookingClientPicker,
  InquiryBookingContext,
  loadInquiry,
  convertInquiryBooking,
  loadRepeatVisit,
  convertRepeatVisitBooking,
  type RepeatVisit,
  type Inquiry,
  formatServiceDuration,
  getUnknownServiceDurationSaveMessage,
  getVehicleBrandLogo,
  getVehicleDisplayName,
  MaintenanceServicesEditor,
  VehicleRegistrationPlateInput,
  WorkspaceAccessNotice,
  getVehicleRegistrationPlateFormat,
  loadVehicleRegistrationPlateFormats,
  normalizeRegistrationPlate,
  validateRegistrationPlate,
  type VehicleRegistrationPlateFormats,
  type WorkspaceMaintenanceDetail
} from '@opencrm/workspace'
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, useId, watch } from 'vue'
import { UiAlertDialog, UiButton, UiCard, UiDatePicker, UiInput, UiLoader, UiSelect, UiTextarea, formatDateOnly } from '@opencrm/ui'
import {
  WorkspacePageHelpButton,
  trackWorkspaceGoal,
  loadVehicle,
  loadVehicleBrandOptions,
  loadVehicleModelOptions,
  loadVehiclesPage,
  type WorkspaceVehicle,
  type WorkspaceVehicleCatalogOption
} from '@opencrm/workspace'
import {
  createDetailingQuickBooking,
  loadScenarioCatalog,
  type DetailingCatalogItem,
  type CreateDetailingQuickBookingRequest,
  type DetailingWorkspaceContext
} from '@/api/detailingBff'

const bookingTimes = ref<InstanceType<typeof BookingTimeFields> | null>(null)
const inlineError = ref<HTMLElement | null>(null)
const serviceDuration = ref<number | null | undefined>()
const selectedServices = ref<WorkspaceMaintenanceDetail[]>([])
const servicesEditor = ref<InstanceType<typeof MaintenanceServicesEditor> | null>(null)
const allowOtherRegistrationPlateFormat = ref(false)
const registrationPlateFormats = ref<VehicleRegistrationPlateFormats>()

const props = defineProps<{
  context: DetailingWorkspaceContext
  inquiryId?: string
  repeatVisitId?: string
  initialVehicleId?: string
  initialDate?: string
  initialStartTime?: string
  initialEndTime?: string
  initialLocation?: string
  plannedBooking?: boolean
  modal?: boolean
}>()
const emit = defineEmits<{
  changed: []
  saved: [id: string, successMessage?: string]
  cancel: []
  'update:busy': [busy: boolean]
  'update:dirty': [dirty: boolean]
}>()

const inquiry = ref<Inquiry | null>(null)
const repeatVisit = ref<RepeatVisit | null>(null)
const inquiryContact = ref<InstanceType<typeof InquiryBookingContext> | null>(null)
const clientPicker = ref<InstanceType<typeof QuickBookingClientPicker> | null>(null)

const form = reactive({
  registrationPlate: '',
  kind: '1',
  manualBrandName: '',
  manualModelName: '',
  organizationClientId: '',
  clientName: '',
  clientPhone: '',
  plannedDate: normalizeDateValue(props.initialDate) ?? formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC'),
  plannedEndDate: resolveInitialEndDate(),
  plannedStartTime: normalizeTimeValue(props.initialStartTime) ?? '',
  plannedEndTime: normalizeTimeValue(props.initialEndTime) ?? resolveDefaultEndTime(props.initialStartTime),
  location: props.initialLocation ?? '',
  comment: ''
})

const foundVehicles = ref<WorkspaceVehicle[]>([])
const services = ref<DetailingCatalogItem[]>([])
const posts = ref<DetailingCatalogItem[]>([])
const brands = ref<WorkspaceVehicleCatalogOption[]>([])
const models = ref<WorkspaceVehicleCatalogOption[]>([])
const selectedVehicleId = ref('')
const selectedBrandId = ref('')
const selectedModelId = ref('')
const isManualIdentity = ref(false)
const isLoading = ref(false)
const isSearching = ref(false)
const isSaving = ref(false)
const formId = useId()
const initialDraft = ref('')
const draftSnapshot = computed(() => JSON.stringify([form, selectedServices.value, selectedVehicleId.value, selectedBrandId.value, selectedModelId.value, isManualIdentity.value, allowOtherRegistrationPlateFormat.value]))
const isLoadingModels = ref(false)
const errorMessage = ref('')
const errorDialogMessage = ref('')
const formErrorSource = ref<'service' | 'plate' | 'date' | 'time' | 'submission' | 'other'>('other')
const registrationPlateError = ref('')
const plannedDateError = ref('')
let searchTimer: number | undefined
let searchRequestId = 0
let brandRequestId = 0
let modelRequestId = 0
const organizationId = computed(() => props.context.organization.id)
const repeatVisitSourceReady = computed(() => !props.repeatVisitId || Boolean(repeatVisit.value && selectedVehicle.value?.id === repeatVisit.value.vehicleId))
const isReadOnly = computed(() => props.context.workspaceAccess?.isReadOnly === true)
const readOnlyMessage = computed(() =>
  props.context.workspaceAccess?.readOnlyMessage ?? 'Рабочее пространство сейчас доступно только для просмотра.'
)
const billingHref = computed(() => '#billing')
const searchText = computed(() => form.registrationPlate.trim())
const selectedVehicle = computed(() => foundVehicles.value.find(vehicle => vehicle.id === selectedVehicleId.value))
const selectedVehicleLogo = computed(() => getVehicleBrandLogo(selectedVehicle.value?.brandLogoKey))
const selectedClientName = computed(() => {
  if (selectedVehicle.value?.organizationClientId === form.organizationClientId) return selectedVehicle.value.organizationClientName || ''
  if (repeatVisit.value?.organizationClientId === form.organizationClientId) return repeatVisit.value.clientName
  if (inquiry.value?.organizationClientId === form.organizationClientId) return inquiry.value.organizationClientName || ''
  return ''
})
const brandOptions = computed(() => [
  { value: '', label: 'Неизвестно' },
  ...brands.value.map(brand => ({ value: String(brand.id), label: brand.name }))
])
const modelOptions = computed(() => [
  { value: '', label: 'Неизвестно' },
  ...models.value.map(model => ({ value: String(model.id), label: model.name }))
])
const postOptions = computed(() => [
  { value: '', label: 'Без поста' },
  ...posts.value
    .filter(item => item.isActive !== false && item.name?.trim())
    .map(item => ({ value: item.name.trim(), label: item.name.trim() }))
])
const kindOptions = [
  { value: '1', label: 'Легковой автомобиль' },
  { value: '2', label: 'Грузовая техника' },
  { value: '3', label: 'Мотоцикл' },
  { value: '4', label: 'Квадроцикл' },
  { value: '5', label: 'Багги / UTV' },
  { value: '9', label: 'Спецтехника' },
  { value: '10', label: 'Прицеп' },
  { value: '99', label: 'Другая техника' }
]

function getVehicleTitle(vehicle: WorkspaceVehicle) {
  return getVehicleDisplayName(vehicle, true)
}

function getVehicleMeta(vehicle: WorkspaceVehicle) {
  return [
    vehicle.organizationClientName,
    vehicle.yearIssue ? `${vehicle.yearIssue}` : '',
    vehicle.vin ? `VIN ${vehicle.vin}` : ''
  ].filter(Boolean).join(' · ')
}

function toNullableString(value: string | number | null | undefined) {
  const trimmed = String(value ?? '').trim()
  return trimmed.length ? trimmed : null
}

function toTimeValue(value: string) {
  return value ? `${value}:00` : null
}

function normalizeDateValue(value?: string) {
  return value && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : null
}

function normalizeTimeValue(value?: string) {
  if (!value) {
    return null
  }

  const match = value.match(/^(\d{2}):(\d{2})/)
  return match ? `${match[1]}:${match[2]}` : null
}

function resolveInitialEndDate() {
  const date = normalizeDateValue(props.initialDate) ?? formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')
  const start = normalizeTimeValue(props.initialStartTime)
  const end = normalizeTimeValue(props.initialEndTime) ?? resolveDefaultEndTime(props.initialStartTime)
  if (!start || !end || end > start) return date
  const nextDay = new Date(`${date}T00:00:00`)
  nextDay.setDate(nextDay.getDate() + 1)
  return formatDateOnly(nextDay)
}

function resolveDefaultEndTime(start?: string) {
  const normalized = normalizeTimeValue(start)
  if (!normalized) {
    return ''
  }

  const [hours, minutes] = normalized.split(':').map(Number)
  const date = new Date()
  date.setHours(hours + 1, minutes, 0, 0)
  return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
}

function toNullableNumber(value: string | number | null | undefined) {
  const normalized = String(value ?? '').replace(',', '.').trim()
  const parsed = Number(normalized)
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null
}

function toNullableId(value: string | number | null | undefined) {
  const normalized = String(value ?? '').trim()
  const parsed = Number(normalized)
  return Number.isInteger(parsed) && parsed > 0 ? parsed : null
}

function showFormError(message: string, focusSummary = true, source: 'service' | 'plate' | 'date' | 'time' | 'submission' | 'other' = 'other') {
  errorMessage.value = message
  errorDialogMessage.value = props.modal ? '' : message
  formErrorSource.value = source
  if (props.modal && focusSummary) void nextTick(() => inlineError.value?.focus())
}

function focusField(id: string) {
  void nextTick(() => {
    const field = document.getElementById(id)
    field?.scrollIntoView({ block: 'center' })
    field?.focus()
  })
}

function clearClientValidationSummary(source: 'service' | 'plate' | 'date' | 'time' | 'submission') {
  if (formErrorSource.value !== source) return
  errorMessage.value = ''
  errorDialogMessage.value = ''
  formErrorSource.value = 'other'
}

function applyVehicle(vehicle: WorkspaceVehicle) {
  if (vehicle.organizationClientId && (
    (form.organizationClientId && form.organizationClientId !== vehicle.organizationClientId) ||
    form.clientName.trim() || form.clientPhone.trim()
  )) {
    showFormError('У автомобиля уже указан другой клиент. Выберите его или измените клиента в карточке автомобиля.', false)
    return
  }
  window.clearTimeout(searchTimer)
  searchRequestId++
  isSearching.value = false
  selectedVehicleId.value = vehicle.id
  registrationPlateError.value = ''
  clearClientValidationSummary('plate')
  allowOtherRegistrationPlateFormat.value = Boolean(vehicle.registrationPlate && registrationPlateFormats.value &&
    getVehicleRegistrationPlateFormat(vehicle.kind || 1, registrationPlateFormats.value) &&
    validateRegistrationPlate(vehicle.registrationPlate, vehicle.kind || 1, registrationPlateFormats.value))
  form.registrationPlate = vehicle.registrationPlate || form.registrationPlate
  form.organizationClientId = vehicle.organizationClientId || form.organizationClientId
  form.kind = String(vehicle.kind || 1)
  form.manualBrandName = vehicle.brandName || vehicle.manualBrandName || ''
  form.manualModelName = vehicle.modelName || vehicle.manualModelName || ''
  selectedBrandId.value = vehicle.brandId ? String(vehicle.brandId) : ''
  selectedModelId.value = vehicle.modelId ? String(vehicle.modelId) : ''
  isManualIdentity.value = vehicle.isManualEntry || !vehicle.modelId
}

function clearVehicleSelection() {
  window.clearTimeout(searchTimer)
  searchRequestId++
  isSearching.value = false
  selectedVehicleId.value = ''
  form.registrationPlate = ''
  allowOtherRegistrationPlateFormat.value = false
  selectedBrandId.value = ''
  selectedModelId.value = ''
  foundVehicles.value = []
  void loadBrandsForKind()
}

async function loadBrandsForKind() {
  const request = ++brandRequestId
  const kind = form.kind
  brands.value = []
  try {
    const result = await loadVehicleBrandOptions(kind)
    if (request !== brandRequestId || kind !== form.kind) return
    brands.value = result
    if (!result.length) isManualIdentity.value = true
  } catch {
    if (request !== brandRequestId || kind !== form.kind) return
    brands.value = []
    isManualIdentity.value = true
  }
}

async function loadModelsForBrand() {
  const request = ++modelRequestId
  const brandId = selectedBrandId.value
  const kind = form.kind
  if (selectedVehicle.value) {
    isLoadingModels.value = false
    return
  }

  models.value = []
  selectedModelId.value = ''
  isLoadingModels.value = false
  if (!brandId || isManualIdentity.value) return

  isLoadingModels.value = true
  try {
    const result = await loadVehicleModelOptions(brandId, kind)
    if (request !== modelRequestId || brandId !== selectedBrandId.value || kind !== form.kind || selectedVehicle.value || isManualIdentity.value) return
    models.value = result
  } catch {
    if (request === modelRequestId && brandId === selectedBrandId.value && kind === form.kind && !selectedVehicle.value && !isManualIdentity.value) models.value = []
  } finally {
    if (request === modelRequestId) isLoadingModels.value = false
  }
}

async function searchVehicles() {
  const requestId = ++searchRequestId
  const query = searchText.value
  const currentVehicle = selectedVehicle.value
  if (currentVehicle && (currentVehicle.registrationPlate ?? '').trim().toLowerCase() === query.toLowerCase()) {
    isSearching.value = false
    return
  }

  selectedVehicleId.value = ''

  if (query.length < 2) {
    foundVehicles.value = []
    isSearching.value = false
    return
  }

  isSearching.value = true

  try {
    const page = await loadVehiclesPage(organizationId.value, 1, 8, {
      isArchived: false,
      kind: form.kind,
      searchQuery: query
    })
    if (requestId !== searchRequestId) return
    foundVehicles.value = page.items
  } catch {
    if (requestId !== searchRequestId) return
    foundVehicles.value = []
  } finally {
    if (requestId === searchRequestId) isSearching.value = false
  }
}

async function saveBooking() {
  if (isSaving.value) return
  if (!repeatVisitSourceReady.value) return
  if (repeatVisit.value?.bookedMaintenanceId) return
  if (isSaving.value) {
    return
  }

  errorMessage.value = ''
  errorDialogMessage.value = ''
  formErrorSource.value = 'other'

  if (isReadOnly.value) {
    showFormError(readOnlyMessage.value)
    return
  }

  if (!form.registrationPlate.trim()) {
    const message = 'Укажите госномер. Без него быстро появятся одинаковые записи.'
    registrationPlateError.value = message
    showFormError(message, false, 'plate')
    focusField(`${formId}-registration-plate`)
    return
  }

  try {
    registrationPlateFormats.value ??= await loadVehicleRegistrationPlateFormats()
  } catch (error) {
    const message = error instanceof Error ? error.message : 'Не удалось загрузить форматы госномеров.'
    registrationPlateError.value = message
    showFormError(message, false, 'plate')
    focusField(`${formId}-registration-plate`)
    return
  }

  if (selectedVehicle.value?.registrationPlate &&
    getVehicleRegistrationPlateFormat(selectedVehicle.value.kind || 1, registrationPlateFormats.value) &&
    validateRegistrationPlate(selectedVehicle.value.registrationPlate, selectedVehicle.value.kind || 1, registrationPlateFormats.value)) {
    allowOtherRegistrationPlateFormat.value = true
  }

  if (!selectedVehicle.value) {
    const validationError = validateRegistrationPlate(
      form.registrationPlate,
      form.kind,
      registrationPlateFormats.value,
      allowOtherRegistrationPlateFormat.value
    )
    if (validationError) {
      registrationPlateError.value = validationError
      showFormError(validationError, false, 'plate')
      focusField(`${formId}-registration-plate`)
      return
    }
  }

  const serviceValidationError = servicesEditor.value?.validate()
  if (serviceValidationError === undefined) {
    showFormError('Не удалось проверить строки работ.')
    return
  }
  if (serviceValidationError) {
    showFormError(serviceValidationError, false, 'service')
    return
  }

  if (!form.plannedDate) {
    const message = 'Укажите дату записи.'
    plannedDateError.value = message
    showFormError(message, false, 'date')
    focusField(`${formId}-planned-date`)
    return
  }


  const clientValidationError = props.inquiryId ? null : clientPicker.value?.validate()
  if (clientValidationError) {
    showFormError(clientValidationError)
    return
  }
  isSaving.value = true

  try {
    if (bookingTimes.value && !await bookingTimes.value.validate()) {
      showFormError(bookingTimes.value.getValidationError(), false, 'time')
      return
    }

    const selectedBrand = brands.value.find(brand => String(brand.id) === selectedBrandId.value)
    const selectedModel = models.value.find(model => String(model.id) === selectedModelId.value)
    const payload: CreateDetailingQuickBookingRequest = {
      vehicleId: selectedVehicle.value?.id ?? null,
      isPlannedBooking: Boolean(props.inquiryId || props.repeatVisitId) || props.plannedBooking === true || Boolean(props.initialDate) || form.plannedDate > formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC'),
      registrationPlate: registrationPlateFormats.value
        ? normalizeRegistrationPlate(form.registrationPlate.trim(), registrationPlateFormats.value,
          allowOtherRegistrationPlateFormat.value || !getVehicleRegistrationPlateFormat(form.kind, registrationPlateFormats.value))
        : form.registrationPlate.trim(),
      allowOtherRegistrationPlateFormat: allowOtherRegistrationPlateFormat.value,
      kind: Number(form.kind) || 1,
      modelId: selectedVehicle.value || isManualIdentity.value ? null : toNullableId(selectedModelId.value),
      manualBrandName: selectedVehicle.value || !isManualIdentity.value ? selectedBrand?.name ?? null : toNullableString(form.manualBrandName),
      manualModelName: selectedVehicle.value || !isManualIdentity.value ? selectedModel?.name ?? null : toNullableString(form.manualModelName),
      organizationClientId: repeatVisit.value?.organizationClientId ?? (form.organizationClientId || null),
      clientName: form.organizationClientId || props.inquiryId || props.repeatVisitId ? null : toNullableString(form.clientName),
      clientPhone: form.organizationClientId || props.inquiryId || props.repeatVisitId ? null : toNullableString(form.clientPhone),
      serviceId: selectedServices.value[0].catalogItemId ?? undefined,
      services: selectedServices.value,
      plannedDate: form.plannedDate,
      plannedEndDate: form.plannedEndDate || form.plannedDate,
      plannedStartTime: toTimeValue(form.plannedStartTime),
      plannedEndTime: toTimeValue(form.plannedEndTime),
      price: null,
      location: toNullableString(form.location),
      comment: toNullableString(form.comment)
    }

    if (props.inquiryId && props.repeatVisitId) throw new Error('Нельзя одновременно создавать запись по обращению и рекомендации.')
    const bookingOrganizationId = organizationId.value
    const result = props.repeatVisitId && repeatVisit.value
      ? await convertRepeatVisitBooking(bookingOrganizationId, props.repeatVisitId, { version: repeatVisit.value.version, detailing: payload })
      : props.inquiryId && inquiry.value
      ? await convertInquiryBooking(bookingOrganizationId, props.inquiryId, { version: inquiry.value.version, detailing: payload, newClient: inquiryContact.value?.getNewClient() })
      : await createDetailingQuickBooking(bookingOrganizationId, payload)
    trackWorkspaceGoal('work_created', {
      method: 'quick_booking',
      scenario: 'detailing',
      is_demo: props.context.currentSubscription?.subscriptionPlan?.isDemoPlan === true
    })
    emit('changed')
    const successMessage = serviceDuration.value === null
      ? getUnknownServiceDurationSaveMessage(Boolean(form.plannedEndTime))
      : undefined
    emit('saved', result.maintenanceId, successMessage)
    if (!props.modal) {
      window.location.hash = `#maintenance/${result.maintenanceId}`
    }
  } catch (error) {
    showFormError(error instanceof Error ? error.message : 'Не удалось создать запись.', true, 'submission')
  } finally {
    isSaving.value = false
  }
}

onMounted(async () => {
  isLoading.value = true
  errorMessage.value = ''

  try {
    if (props.inquiryId && props.repeatVisitId) throw new Error('Выберите один источник записи: обращение или рекомендацию.')
    const [catalog, , plateFormats] = await Promise.all([
      loadScenarioCatalog(organizationId.value),
      loadBrandsForKind(),
      loadVehicleRegistrationPlateFormats().catch(() => undefined)
    ])
    registrationPlateFormats.value = plateFormats
    services.value = catalog.services ?? []
    posts.value = catalog.workPosts ?? []

    if (props.repeatVisitId) {
      repeatVisit.value = await loadRepeatVisit(organizationId.value, props.repeatVisitId)
      if (repeatVisit.value.bookedMaintenanceId) throw new Error(`По рекомендации уже создана запись ${repeatVisit.value.bookedMaintenanceId}.`)
      if (!repeatVisit.value.canBook || !repeatVisit.value.isActive) throw new Error('Создание записи по этой рекомендации недоступно.')
      form.organizationClientId = repeatVisit.value.organizationClientId
      form.plannedDate = repeatVisit.value.suggestedVisitDate
      form.comment = repeatVisit.value.reason ?? ''
      selectedServices.value = [{ name: repeatVisit.value.serviceName, description: repeatVisit.value.reason ?? undefined, catalogItemId: null, quantity: 1 }]
    }

    if (props.inquiryId) {
      inquiry.value = await loadInquiry(organizationId.value, props.inquiryId)
      if (!inquiry.value.canCreateBooking) throw new Error('Создание записи по этому обращению недоступно.')
      form.organizationClientId = inquiry.value.organizationClientId ?? ''
      form.comment = [inquiry.value.interestText, inquiry.value.vehicleDescription].filter(Boolean).join('\n')
    }
    const initialVehicleId = repeatVisit.value?.vehicleId || inquiry.value?.vehicleId || props.initialVehicleId
    if (initialVehicleId) {
      const vehicle = await loadVehicle(organizationId.value, initialVehicleId)
      foundVehicles.value = [vehicle]
      applyVehicle(vehicle)
    }
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось открыть форму записи.'
    foundVehicles.value = []
  } finally {
    isLoading.value = false
    await nextTick()
    initialDraft.value = draftSnapshot.value
  }
})

watch(searchText, () => {
  window.clearTimeout(searchTimer)
  searchTimer = window.setTimeout(searchVehicles, 300)
})

watch(() => form.registrationPlate, () => {
  registrationPlateError.value = ''
  clearClientValidationSummary('plate')
})
watch(selectedServices, () => clearClientValidationSummary('service'))
watch([form, selectedServices, selectedVehicleId, selectedBrandId, selectedModelId, isManualIdentity, allowOtherRegistrationPlateFormat], () => clearClientValidationSummary('submission'), { deep: true })

watch(() => form.organizationClientId, () => {
  if (selectedVehicle.value) {
    if (!selectedVehicle.value.organizationClientId || selectedVehicle.value.organizationClientId === form.organizationClientId) return
    selectedVehicleId.value = ''
  }

  foundVehicles.value = []
  searchRequestId++
  window.clearTimeout(searchTimer)
  searchTimer = window.setTimeout(searchVehicles, 150)
})

watch(() => form.kind, async () => {
  if (selectedVehicle.value) {
    return
  }

  selectedVehicleId.value = ''
  foundVehicles.value = []
  selectedBrandId.value = ''
  selectedModelId.value = ''
  models.value = []

  modelRequestId++
  isLoadingModels.value = false
  const kind = form.kind
  await loadBrandsForKind()
  if (kind !== form.kind || selectedVehicle.value) return

  window.clearTimeout(searchTimer)
  searchTimer = window.setTimeout(searchVehicles, 150)
})

onBeforeUnmount(() => {
  window.clearTimeout(searchTimer)
  searchRequestId++
  brandRequestId++
  modelRequestId++
})

watch(selectedBrandId, loadModelsForBrand)
watch(isManualIdentity, () => {
  modelRequestId++
  isLoadingModels.value = false
  if (!isManualIdentity.value && !selectedVehicle.value) void loadModelsForBrand()
})
watch(isSaving, busy => emit('update:busy', busy))
watch([draftSnapshot, initialDraft], ([draft, initial]) => emit('update:dirty', Boolean(initial) && draft !== initial))
watch(() => form.plannedDate, (date, previous) => {
  plannedDateError.value = ''
  clearClientValidationSummary('date')
  if (!form.plannedEndDate || form.plannedEndDate === previous || form.plannedEndDate < date) form.plannedEndDate = date
})
watch(() => [form.plannedDate, form.plannedEndDate, form.plannedStartTime, form.plannedEndTime, form.location], () => {
  clearClientValidationSummary('time')
})
</script>

<template>
  <section class="entity-card-section">
    <UiCard :page="!modal" class="quick-editor" :title="modal ? undefined : 'Новая запись'">
      <template v-if="!modal" #actions>
        <WorkspacePageHelpButton section-code="maintenance" entity-id="new" title="Новая запись" />
      </template>
      <UiLoader v-if="isLoading" text="Открываем форму записи..." />
      <form v-else class="quick-editor-form" novalidate @submit.prevent="saveBooking">
        <InquiryBookingContext v-if="inquiry" ref="inquiryContact" :inquiry="inquiry" :selected-client-id="form.organizationClientId" />
        <WorkspaceAccessNotice
          :can-manage-billing="context.organization.canManageBilling === true"
          v-if="isReadOnly"
          class="entity-form__wide"
          :message="readOnlyMessage"
          :href="billingHref"
        />
        <div class="quick-editor-layout">
          <section class="quick-editor-customer" aria-labelledby="quick-customer-title">
            <h3 id="quick-customer-title">Автомобиль и клиент</h3>
            <div v-if="selectedVehicle" class="quick-booking-selected-vehicle">
              <img v-if="selectedVehicleLogo" :src="selectedVehicleLogo.src" :alt="`Логотип ${selectedVehicleLogo.name}`">
              <div>
                <strong>{{ getVehicleTitle(selectedVehicle) }}</strong>
                <span>{{ getVehicleMeta(selectedVehicle) || 'Автомобиль из базы' }}</span>
              </div>
              <button type="button" :disabled="isSaving || isReadOnly || Boolean(repeatVisitId)" @click="clearVehicleSelection">Выбрать другой</button>
            </div>
            <div class="quick-editor-fields">
              <QuickBookingClientPicker
                ref="clientPicker"
                class="entity-form__wide"
                v-model="form.organizationClientId"
                v-model:client-name="form.clientName"
                v-model:client-phone="form.clientPhone"
                :organization-id="organizationId"
                :selected-name="selectedClientName"
                :can-create="!inquiryId && !repeatVisitId"
                :disabled="isSaving || isReadOnly || Boolean(selectedVehicle?.organizationClientId) || Boolean(repeatVisitId)"
              />
              <template v-if="!selectedVehicle">
              <VehicleRegistrationPlateInput
                v-model="form.registrationPlate"
                v-model:allow-other-format="allowOtherRegistrationPlateFormat"
                :id="`${formId}-registration-plate`"
                :kind="form.kind"
                :disabled="isSaving || isReadOnly || Boolean(repeatVisitId)"
                :error="registrationPlateError"
                required
              />
              <div class="entity-form__wide quick-booking-search">
                <UiLoader v-if="isSearching" compact text="Ищем авто..." />
                <div v-else-if="foundVehicles.length" class="quick-booking-matches">
                  <button
                    v-for="vehicle in foundVehicles"
                    :key="vehicle.id"
                    type="button"
                    :class="{ active: selectedVehicleId === vehicle.id }"
                    :disabled="isSaving || isReadOnly"
                    @click="applyVehicle(vehicle)"
                  >
                    <strong>{{ getVehicleTitle(vehicle) }}</strong>
                    <span>{{ getVehicleMeta(vehicle) || 'Найдено в базе' }}</span>
                  </button>
                </div>
                <div v-else-if="searchText.length >= 2" class="detailing-empty">
                  Совпадений нет, при сохранении создадим новый автомобиль
                  {{ form.organizationClientId ? ' у выбранного клиента' : '' }}
                  .
                </div>
              </div>
              <UiSelect
                v-model="form.kind"
                label="Тип техники"
                :options="kindOptions"
                :searchable="false"
                :disabled="isSaving || isReadOnly || Boolean(selectedVehicle)"
              />
              <div class="entity-form-identity entity-form__wide">
                <div class="entity-form-toggle" role="group" aria-label="Способ заполнения автомобиля">
                  <button
                    type="button"
                    class="entity-form-toggle__button"
                    :class="{ active: !isManualIdentity }"
                    :disabled="isSaving || isReadOnly || Boolean(selectedVehicle)"
                    @click="isManualIdentity = false"
                  >
                    Из справочника
                  </button>
                  <button
                    type="button"
                    class="entity-form-toggle__button"
                    :class="{ active: isManualIdentity }"
                    :disabled="isSaving || isReadOnly || Boolean(selectedVehicle)"
                    @click="isManualIdentity = true"
                  >
                    Ввести вручную
                  </button>
                </div>
              </div>
              <template v-if="!isManualIdentity">
                <UiSelect
                  v-model="selectedBrandId"
                  label="Марка"
                  :options="brandOptions"
                  :disabled="isSaving || isReadOnly || Boolean(selectedVehicle)"
                />
                <UiSelect
                  v-model="selectedModelId"
                  label="Модель"
                  :options="modelOptions"
                  :disabled="isSaving || isReadOnly || Boolean(selectedVehicle) || isLoadingModels || !selectedBrandId"
                  :placeholder="isLoadingModels ? 'Загружаем модели...' : 'Неизвестно'"
                />
              </template>
              <template v-else>
                <UiInput
                  v-model="form.manualBrandName"
                  label="Марка"
                  label-help="Укажите марку вручную, если автомобиля еще нет в справочнике."
                  :disabled="isSaving || isReadOnly || Boolean(selectedVehicle)"
                />
                <UiInput
                  v-model="form.manualModelName"
                  label="Модель"
                  label-help="Укажите модель вручную, если автомобиля еще нет в справочнике."
                  :disabled="isSaving || isReadOnly || Boolean(selectedVehicle)"
                />
              </template>
              </template>
            </div>
            <div class="quick-services"><h3>Услуги</h3>
              <MaintenanceServicesEditor :can-assign-employees="context.shell.scenario?.modules?.members !== false" ref="servicesEditor" @duration-change="serviceDuration = $event" @validation-cleared="clearClientValidationSummary('service')" v-model="selectedServices" :can-open-catalog="context.shell.menu?.some(item => item.code === 'catalog' && item.isVisible)" :organization-id="organizationId" default-vat-rate-code="without_vat" :disabled="isSaving || isReadOnly" />
            </div>
            <UiTextarea
              class="entity-form__wide quick-booking-comment"
              v-model="form.comment"
              label="Комментарий"
              :rows="3"
              :disabled="isSaving || isReadOnly"
            />
          </section>
          <section class="quick-editor-visit" aria-labelledby="quick-visit-title">
            <h3 id="quick-visit-title">Параметры записи</h3>

            <div class="catalog-linked-field">
              <UiSelect v-model="form.location" label="Пост / место" :options="postOptions" :disabled="isSaving || isReadOnly" />
              <a
                v-if="context.shell.menu?.some(item => item.code === 'catalog' && item.isVisible) && !isSaving"
                href="#catalog?catalog=work_post"
              >
                Справочник постов ↗
              </a>
            </div>
            <UiDatePicker
              v-model="form.plannedDate"
              :id="`${formId}-planned-date`"
              label="Дата начала"
              :disabled="isSaving || isReadOnly"
              :error="plannedDateError"
              required
            />
            <UiDatePicker v-model="form.plannedEndDate" @update:model-value="bookingTimes?.useManualEnd()" label="Дата окончания" :disabled="isSaving || isReadOnly" required />
            <BookingTimeFields
              ref="bookingTimes"
              v-model:start="form.plannedStartTime"
              v-model:end="form.plannedEndTime"
              :organization-id="organizationId"
              :date="form.plannedDate"
              v-model:end-date="form.plannedEndDate"
              :duration-minutes="serviceDuration"
              :auto-duration="true"
              :location="form.location"
              :disabled="isSaving || isReadOnly"
              class="booking-times entity-form__wide"
            />
            <div class="quick-booking-totals">
              <div><span>Длительность</span><strong>{{ serviceDuration == null ? 'Не указана' : formatServiceDuration(serviceDuration) }}</strong></div>
              <div><span>Стоимость работ</span><strong>{{ selectedServices.reduce((sum, item) => sum + (item.price ?? 0), 0).toLocaleString('ru-RU') }} ₽</strong></div>
            </div>
          </section>
        </div>
        <div class="quick-editor-footer">
          <div v-if="errorMessage && !errorDialogMessage" ref="inlineError" class="quick-editor-error" role="alert" tabindex="-1">{{ errorMessage }}</div>
          <a v-if="repeatVisit?.bookedMaintenanceId" :href="`#maintenance/${repeatVisit.bookedMaintenanceId}`">Открыть уже созданную запись</a>
          <div class="quick-editor-actions">
            <UiButton v-if="!isReadOnly" type="submit" :disabled="isSaving || !repeatVisitSourceReady || Boolean(repeatVisit?.bookedMaintenanceId)">{{ isSaving ? 'Создаем...' : 'Создать запись' }}</UiButton>
            <UiButton v-if="modal" variant="secondary" :disabled="isSaving" @click="emit('cancel')">Отмена</UiButton>
            <UiButton v-else variant="secondary" href="#scheduler" :disabled="isSaving">Отмена</UiButton>
          </div>
        </div>
      </form>
    </UiCard>
    <UiAlertDialog
      :open="!modal && Boolean(errorDialogMessage)"
      title="Не удалось создать запись"
      :message="errorDialogMessage"
      tone="danger"
      @close="errorDialogMessage = ''"
    />
  </section>

</template>

<style scoped src="@opencrm/workspace/quick-booking.css"></style>

<script setup lang="ts">
import {
  BookingTimeFields,
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
import { computed, nextTick, onMounted, reactive, ref, useId, watch } from 'vue'
import { UiAlertDialog, UiButton, UiCard, UiDatePicker, UiInput, UiLoader, UiPhoneInput, UiSelect, UiTextarea, formatDateOnly } from '@opencrm/ui'
import {
  WorkspacePageHelpButton,
  trackWorkspaceGoal,
  loadClientOptions,
  loadVehicle,
  loadVehicleBrandOptions,
  loadVehicleModelOptions,
  loadVehiclesPage,
  type WorkspaceClientOption,
  type WorkspaceVehicle,
  type WorkspaceVehicleCatalogOption
} from '@opencrm/workspace'
import {
  createAutoServiceQuickBooking,
  loadScenarioCatalog,
  type AutoServiceCatalogItem,
  type CreateAutoServiceQuickBookingRequest,
  type AutoServiceWorkspaceContext
} from '@/api/autoserviceBff'

const bookingTimes = ref<InstanceType<typeof BookingTimeFields> | null>(null)
const inlineError = ref<HTMLElement | null>(null)
const serviceDuration = ref<number | null | undefined>()
const selectedServices = ref<WorkspaceMaintenanceDetail[]>([])
const servicesEditor = ref<InstanceType<typeof MaintenanceServicesEditor> | null>(null)
const allowOtherRegistrationPlateFormat = ref(false)
const registrationPlateFormats = ref<VehicleRegistrationPlateFormats>()

const props = defineProps<{
  context: AutoServiceWorkspaceContext
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

const form = reactive({
  registrationPlate: '',
  kind: '1',
  manualBrandName: '',
  manualModelName: '',
  organizationClientId: '',
  buyerClientId: '',
  clientPhone: '',
  clientLastName: '',
  clientFirstName: '',
  clientMiddleName: '',
  plannedDate: normalizeDateValue(props.initialDate) ?? formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC'),
  plannedEndDate: resolveInitialEndDate(),
  plannedStartTime: normalizeTimeValue(props.initialStartTime) ?? '',
  plannedEndTime: normalizeTimeValue(props.initialEndTime) ?? resolveDefaultEndTime(props.initialStartTime),
  location: props.initialLocation ?? '',
  comment: ''
})

const foundVehicles = ref<WorkspaceVehicle[]>([])
const services = ref<AutoServiceCatalogItem[]>([])
const posts = ref<AutoServiceCatalogItem[]>([])
const clients = ref<WorkspaceClientOption[]>([])
const brands = ref<WorkspaceVehicleCatalogOption[]>([])
const models = ref<WorkspaceVehicleCatalogOption[]>([])
const selectedVehicleId = ref('')
const selectedVehicle = ref<WorkspaceVehicle | null>(null)
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
const formErrorSource = ref<'service' | 'plate' | 'phone' | 'date' | 'time' | 'submission' | 'other'>('other')
const registrationPlateError = ref('')
const clientPhoneError = ref('')
const plannedDateError = ref('')
let searchTimer: number | undefined
let searchRequestId = 0
const organizationId = computed(() => props.context.organization.id)
const repeatVisitSourceReady = computed(() => !props.repeatVisitId || Boolean(repeatVisit.value && selectedVehicle.value?.id === repeatVisit.value.vehicleId))
const isReadOnly = computed(() => props.context.workspaceAccess?.isReadOnly === true)
const readOnlyMessage = computed(() =>
  props.context.workspaceAccess?.readOnlyMessage ?? 'Рабочее пространство сейчас доступно только для просмотра.'
)
const billingHref = computed(() => '#billing')
const searchText = computed(() => form.registrationPlate.trim())
const selectedVehicleLogo = computed(() => getVehicleBrandLogo(selectedVehicle.value?.brandLogoKey))
const clientOptions = computed(() => [
  { value: '', label: 'Без клиента' },
  ...clients.value.map(client => ({
    value: client.id,
    label: client.details ? `${client.name} · ${client.details}` : client.name
  }))
])
const buyerOptions = computed(() => [
  { value: '', label: 'Как у автомобиля' },
  ...clients.value.map(client => ({
    value: client.id,
    label: client.details ? `${client.name} · ${client.details}` : client.name
  }))
])
const vehicleOptions = computed(() => foundVehicles.value.map(vehicle => ({
  value: vehicle.id,
  label: [getVehicleTitle(vehicle), getVehicleMeta(vehicle)].filter(Boolean).join(' · ')
})))
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

function buildClientName() {
  return [form.clientLastName, form.clientFirstName, form.clientMiddleName]
    .map(value => value.trim())
    .filter(Boolean)
    .join(' ')
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

function showFormError(message: string, focusSummary = true, source: 'service' | 'plate' | 'phone' | 'date' | 'time' | 'submission' | 'other' = 'other') {
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

function clearClientValidationSummary(source: 'service' | 'plate' | 'phone' | 'date' | 'time' | 'submission') {
  if (formErrorSource.value !== source) return
  errorMessage.value = ''
  errorDialogMessage.value = ''
  formErrorSource.value = 'other'
}

function applyVehicle(vehicle: WorkspaceVehicle) {
  window.clearTimeout(searchTimer)
  searchRequestId++
  isSearching.value = false
  selectedVehicleId.value = vehicle.id
  selectedVehicle.value = vehicle
  registrationPlateError.value = ''
  clientPhoneError.value = ''
  clearClientValidationSummary('plate')
  clearClientValidationSummary('phone')
  allowOtherRegistrationPlateFormat.value = Boolean(vehicle.registrationPlate && registrationPlateFormats.value &&
    getVehicleRegistrationPlateFormat(vehicle.kind || 1, registrationPlateFormats.value) &&
    validateRegistrationPlate(vehicle.registrationPlate, vehicle.kind || 1, registrationPlateFormats.value))
  form.registrationPlate = vehicle.registrationPlate || form.registrationPlate
  form.organizationClientId = vehicle.organizationClientId || form.organizationClientId
  form.buyerClientId = ''
  form.kind = String(vehicle.kind || 1)
  form.manualBrandName = vehicle.brandName || vehicle.manualBrandName || ''
  form.manualModelName = vehicle.modelName || vehicle.manualModelName || ''
  selectedBrandId.value = vehicle.brandId ? String(vehicle.brandId) : ''
  selectedModelId.value = vehicle.modelId ? String(vehicle.modelId) : ''
  isManualIdentity.value = vehicle.isManualEntry || !vehicle.modelId
}

function selectFoundVehicle(vehicleId: string) {
  const vehicle = foundVehicles.value.find(item => item.id === vehicleId)
  if (vehicle) {
    applyVehicle(vehicle)
  }
}

function clearVehicleSelection() {
  window.clearTimeout(searchTimer)
  searchRequestId++
  isSearching.value = false
  selectedVehicle.value = null
  selectedVehicleId.value = ''
  clientPhoneError.value = ''
  clearClientValidationSummary('phone')
  form.registrationPlate = ''
  allowOtherRegistrationPlateFormat.value = false
  foundVehicles.value = []
  void searchVehicles()
}

async function loadModelsForBrand() {
  if (selectedVehicle.value) {
    return
  }

  models.value = []
  selectedModelId.value = ''

  if (!selectedBrandId.value) {
    return
  }

  isLoadingModels.value = true
  try {
    models.value = await loadVehicleModelOptions(selectedBrandId.value, form.kind)
  } catch {
    models.value = []
  } finally {
    isLoadingModels.value = false
  }
}

async function searchVehicles() {
  if (selectedVehicle.value) {
    isSearching.value = false
    return
  }
  const requestId = ++searchRequestId
  const query = searchText.value
  const hasClientFilter = Boolean(form.organizationClientId)
  selectedVehicleId.value = ''

  if (query.length < 2 && !hasClientFilter) {
    foundVehicles.value = []
    isSearching.value = false
    return
  }

  isSearching.value = true

  try {
    const page = await loadVehiclesPage(organizationId.value, 1, 8, {
      isArchived: false,
      kind: form.kind,
      organizationClientId: form.organizationClientId || undefined,
      searchQuery: query.length >= 2 ? query : undefined
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

  if (!selectedVehicle.value && !form.registrationPlate.trim()) {
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

  if (selectedVehicle.value && selectedVehicle.value.registrationPlate &&
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

  if (!form.organizationClientId && buildClientName() && !form.clientPhone.trim()) {
    const message = 'Для создания нового клиента укажите телефон. Фамилию, имя и отчество можно оставить пустыми.'
    clientPhoneError.value = message
    showFormError(message, false, 'phone')
    focusField(`${formId}-client-phone`)
    return
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
    const message = 'Укажите дату приемки.'
    plannedDateError.value = message
    showFormError(message, false, 'date')
    focusField(`${formId}-planned-date`)
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
    const payload: CreateAutoServiceQuickBookingRequest = {
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
      buyerClientId: repeatVisit.value?.organizationClientId ?? (form.buyerClientId || null),
      clientPhone: props.inquiryId || form.organizationClientId ? null : toNullableString(form.clientPhone),
      clientName: props.inquiryId || form.organizationClientId ? null : toNullableString(buildClientName()),
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
      ? await convertRepeatVisitBooking(bookingOrganizationId, props.repeatVisitId, { version: repeatVisit.value.version, autoService: payload })
      : props.inquiryId && inquiry.value
      ? await convertInquiryBooking(bookingOrganizationId, props.inquiryId, { version: inquiry.value.version, autoService: payload, newClient: inquiryContact.value?.getNewClient() })
      : await createAutoServiceQuickBooking(bookingOrganizationId, payload)
    trackWorkspaceGoal('work_created', {
      method: 'quick_booking',
      scenario: 'service_station'
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
    showFormError(error instanceof Error ? error.message : 'Не удалось создать приемку.', true, 'submission')
  } finally {
    isSaving.value = false
  }
}

onMounted(async () => {
  isLoading.value = true
  errorMessage.value = ''

  try {
    if (props.inquiryId && props.repeatVisitId) throw new Error('Выберите один источник записи: обращение или рекомендацию.')
    const [catalog, clientOptionsData, brandOptionsData, plateFormats] = await Promise.all([
      loadScenarioCatalog(organizationId.value),
      loadClientOptions(organizationId.value),
      loadVehicleBrandOptions(form.kind),
      loadVehicleRegistrationPlateFormats().catch(() => undefined)
    ])
    registrationPlateFormats.value = plateFormats
    services.value = catalog.services ?? []
    posts.value = catalog.workPosts ?? []
    clients.value = clientOptionsData
    brands.value = brandOptionsData

    if (props.repeatVisitId) {
      repeatVisit.value = await loadRepeatVisit(organizationId.value, props.repeatVisitId)
      if (repeatVisit.value.bookedMaintenanceId) throw new Error(`По рекомендации уже создана запись ${repeatVisit.value.bookedMaintenanceId}.`)
      if (!repeatVisit.value.canBook || !repeatVisit.value.isActive) throw new Error('Создание записи по этой рекомендации недоступно.')
      form.organizationClientId = repeatVisit.value.organizationClientId
      form.buyerClientId = repeatVisit.value.organizationClientId
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
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось открыть новый заказ-наряд.'
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

watch(() => form.clientPhone, () => {
  clientPhoneError.value = ''
  clearClientValidationSummary('phone')
})
watch(() => form.registrationPlate, () => {
  registrationPlateError.value = ''
  clearClientValidationSummary('plate')
})
watch(selectedServices, () => clearClientValidationSummary('service'))
watch([form, selectedServices, selectedVehicleId, selectedBrandId, selectedModelId, isManualIdentity, allowOtherRegistrationPlateFormat], () => clearClientValidationSummary('submission'), { deep: true })

watch(() => form.organizationClientId, () => {
  clientPhoneError.value = ''
  clearClientValidationSummary('phone')
  if (selectedVehicle.value) {
    if (selectedVehicle.value.organizationClientId === form.organizationClientId) return
    selectedVehicleId.value = ''
    selectedVehicle.value = null
  }

  if (selectedVehicle.value) return
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

  try {
    brands.value = await loadVehicleBrandOptions(form.kind)
    if (brands.value.length === 0) {
      isManualIdentity.value = true
    }
  } catch {
    brands.value = []
    isManualIdentity.value = true
  }

  window.clearTimeout(searchTimer)
  searchTimer = window.setTimeout(searchVehicles, 150)
})

watch(selectedBrandId, loadModelsForBrand)
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
    <UiCard :page="!modal" class="quick-editor" :title="modal ? undefined : 'Новый заказ-наряд'">
      <template v-if="!modal" #actions>
        <WorkspacePageHelpButton section-code="maintenance" entity-id="new" title="Новый заказ-наряд" />
      </template>
      <UiLoader v-if="isLoading" text="Открываем новый заказ-наряд..." />
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
                <span v-if="!selectedVehicle.registrationPlate">Номер можно добавить в <a :href="`#vehicles/${selectedVehicle.id}`">карточке автомобиля</a>.</span>
              </div>
              <button type="button" :disabled="isSaving || isReadOnly || Boolean(repeatVisitId)" @click="clearVehicleSelection">Выбрать другой</button>
            </div>
            <div class="quick-editor-fields">
              <UiSelect
                v-model="form.organizationClientId"
                label="Клиент"
                :options="clientOptions"
                :disabled="isSaving || isReadOnly || Boolean(selectedVehicle) || Boolean(repeatVisitId)"
              />
              <UiSelect
                v-model="form.buyerClientId"
                label="Покупатель по заказу"
                :options="buyerOptions"
                :disabled="isSaving || isReadOnly || Boolean(repeatVisitId)"
              />
              <template v-if="!selectedVehicle">
              <VehicleRegistrationPlateInput
                v-model="form.registrationPlate"
                v-model:allow-other-format="allowOtherRegistrationPlateFormat"
                :id="`${formId}-registration-plate`"
                :kind="form.kind"
                :disabled="isSaving || isReadOnly || Boolean(selectedVehicle)"
                :error="registrationPlateError"
                :required="!selectedVehicle"
              />
              <div v-if="!inquiryId && !form.organizationClientId && !selectedVehicle" class="quick-booking-new-client entity-form__wide">
                <div class="quick-booking-new-client__intro">
                  <strong>Новый клиент</strong>
                  <span>Если клиента ещё нет в базе, укажите телефон — создадим его вместе с заказ-нарядом.</span>
                </div>
                <div class="quick-booking-new-client__fields">
                  <UiPhoneInput v-model="form.clientPhone" :id="`${formId}-client-phone`" label="Телефон клиента" :disabled="isSaving || isReadOnly" :error="clientPhoneError" />
                  <UiInput v-model="form.clientLastName" label="Фамилия" :disabled="isSaving || isReadOnly" />
                  <UiInput v-model="form.clientFirstName" label="Имя" :disabled="isSaving || isReadOnly" />
                  <UiInput v-model="form.clientMiddleName" label="Отчество" :disabled="isSaving || isReadOnly" />
                </div>
              </div>
              <div class="entity-form__wide quick-booking-search">
                <UiLoader v-if="isSearching" compact text="Ищем авто..." />
                <UiSelect
                  v-else-if="foundVehicles.length"
                  v-model="selectedVehicleId"
                  :label="form.organizationClientId && searchText.length < 2 ? 'Автомобили клиента' : 'Найденные автомобили'"
                  :options="vehicleOptions"
                  placeholder="Выберите автомобиль"
                  :disabled="isSaving || isReadOnly || Boolean(selectedVehicle)"
                  @update:model-value="selectFoundVehicle"
                />
                <div v-else-if="searchText.length >= 2" class="autoservice-empty">
                  Совпадений нет, при сохранении создадим новый автомобиль
                  {{ form.organizationClientId ? ' у выбранного клиента' : '' }}
                  .
                </div>
                <div v-else-if="form.organizationClientId" class="autoservice-empty">
                  У выбранного клиента пока нет автомобилей. Укажите госномер — автомобиль будет создан вместе с заказ-нарядом.
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
            <UiButton v-if="!isReadOnly" type="submit" :disabled="isSaving || !repeatVisitSourceReady || Boolean(repeatVisit?.bookedMaintenanceId)">{{ isSaving ? 'Создаем...' : 'Создать заказ-наряд' }}</UiButton>
            <UiButton v-if="modal" variant="secondary" :disabled="isSaving" @click="emit('cancel')">Отмена</UiButton>
            <UiButton v-else variant="secondary" href="#scheduler" :disabled="isSaving">Отмена</UiButton>
          </div>
        </div>
      </form>
    </UiCard>
    <UiAlertDialog
      :open="!modal && Boolean(errorDialogMessage)"
      title="Не удалось создать заказ-наряд"
      :message="errorDialogMessage"
      tone="danger"
      @close="errorDialogMessage = ''"
    />
  </section>

</template>

<style scoped src="@opencrm/workspace/quick-booking.css"></style>

<script setup lang="ts">
import '@opencrm/workspace/quickBookingMessages'
import { formatLocaleCurrency, i18n, translate as t } from '@opencrm/ui/i18n'
import { useWorkspaceCurrency, registrationCountryOptions, internationalRegistrationPlateError } from '@opencrm/workspace'

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

const workspaceCurrency = useWorkspaceCurrency()
const countryOptions = computed(() => [{ value: 'ZZ', label: t('quickBooking.unknownCountry') }, ...registrationCountryOptions(i18n.global.locale.value)])
const isInternationalPlate = computed(() => Boolean(form.registrationCountryCode && form.registrationCountryCode !== 'RU'))

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
  registrationCountryCode: 'RU',
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
  props.context.workspaceAccess?.readOnlyMessage ?? t('quickBooking.readOnly')
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
  { value: '', label: t('quickBooking.unknown') },
  ...brands.value.map(brand => ({ value: String(brand.id), label: brand.name }))
])
const modelOptions = computed(() => [
  { value: '', label: t('quickBooking.unknown') },
  ...models.value.map(model => ({ value: String(model.id), label: model.name }))
])
const postOptions = computed(() => [
  { value: '', label: t('quickBooking.noPost') },
  ...posts.value
    .filter(item => item.isActive !== false && item.name?.trim())
    .map(item => ({ value: item.name.trim(), label: item.name.trim() }))
])
const kindOptions = [
  { value: '1', label: t('quickBooking.car') },
  { value: '2', label: t('quickBooking.truck') },
  { value: '3', label: t('quickBooking.motorcycle') },
  { value: '4', label: t('quickBooking.atv') },
  { value: '5', label: t('quickBooking.utv') },
  { value: '9', label: t('quickBooking.special') },
  { value: '10', label: t('quickBooking.trailer') },
  { value: '99', label: t('quickBooking.otherVehicle') }
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
    showFormError(t('quickBooking.clientMismatch'), false)
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
  form.registrationCountryCode = vehicle.registrationCountryCode || 'RU'
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
  form.registrationCountryCode = 'RU'
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
    const message = t('quickBooking.plateRequired')
    registrationPlateError.value = message
    showFormError(message, false, 'plate')
    focusField(`${formId}-registration-plate`)
    return
  }

  try {
    registrationPlateFormats.value ??= await loadVehicleRegistrationPlateFormats()
  } catch (error) {
    const message = error instanceof Error ? error.message : t('quickBooking.plateLoadError')
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
    const internationalError = isInternationalPlate.value ? internationalRegistrationPlateError(form.registrationPlate) : ''
    const validationError = isInternationalPlate.value
      ? internationalError ? t(`quickBooking.plate.${internationalError}`) : ''
      : validateRegistrationPlate(
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
    showFormError(t('quickBooking.validationError'))
    return
  }
  if (serviceValidationError) {
    showFormError(serviceValidationError, false, 'service')
    return
  }

  if (!form.plannedDate) {
    const message = t('quickBooking.dateRequired')
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
      registrationCountryCode: form.registrationCountryCode || null,
      registrationPlate: !isInternationalPlate.value && registrationPlateFormats.value
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

    if (props.inquiryId && props.repeatVisitId) throw new Error(t('quickBooking.oneSource'))
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
    showFormError(error instanceof Error ? error.message : t('quickBooking.bookingError'), true, 'submission')
  } finally {
    isSaving.value = false
  }
}

onMounted(async () => {
  isLoading.value = true
  errorMessage.value = ''

  try {
    if (props.inquiryId && props.repeatVisitId) throw new Error(t('quickBooking.sourceRequired'))
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
      if (repeatVisit.value.bookedMaintenanceId) throw new Error(t('quickBooking.alreadyBooked', { id: repeatVisit.value.bookedMaintenanceId }))
      if (!repeatVisit.value.canBook || !repeatVisit.value.isActive) throw new Error(t('quickBooking.recommendationUnavailable'))
      form.organizationClientId = repeatVisit.value.organizationClientId
      form.plannedDate = repeatVisit.value.suggestedVisitDate
      form.comment = repeatVisit.value.reason ?? ''
      selectedServices.value = [{ name: repeatVisit.value.serviceName, description: repeatVisit.value.reason ?? undefined, catalogItemId: null, quantity: 1 }]
    }

    if (props.inquiryId) {
      inquiry.value = await loadInquiry(organizationId.value, props.inquiryId)
      if (!inquiry.value.canCreateBooking) throw new Error(t('quickBooking.inquiryUnavailable'))
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
    errorMessage.value = error instanceof Error ? error.message : t('quickBooking.formLoadError')
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
    <UiCard :page="!modal" class="quick-editor" :title="modal ? undefined : t('quickBooking.newBooking')">
      <template v-if="!modal" #actions>
        <WorkspacePageHelpButton section-code="maintenance" entity-id="new" :title="t('quickBooking.newBooking')" />
      </template>
      <UiLoader v-if="isLoading" :text="t('quickBooking.loadingBooking')" />
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
            <h3 id="quick-customer-title">{{ t('quickBooking.vehicleClient') }}</h3>
            <div v-if="selectedVehicle" class="quick-booking-selected-vehicle">
              <img v-if="selectedVehicleLogo" :src="selectedVehicleLogo.src" :alt="t('quickBooking.logo', { name: selectedVehicleLogo.name })">
              <div>
                <strong>{{ getVehicleTitle(selectedVehicle) }}</strong>
                <span>{{ getVehicleMeta(selectedVehicle) || t('quickBooking.existingVehicle') }}</span>
              </div>
              <button type="button" :disabled="isSaving || isReadOnly || Boolean(repeatVisitId)" @click="clearVehicleSelection">{{ t('quickBooking.other') }}</button>
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
              <UiSelect v-model="form.registrationCountryCode" :label="t('quickBooking.registrationCountry')" :options="countryOptions" :disabled="isSaving || isReadOnly" />
              <VehicleRegistrationPlateInput
                v-model="form.registrationPlate"
                v-model:allow-other-format="allowOtherRegistrationPlateFormat"
                :id="`${formId}-registration-plate`"
                :country-code="form.registrationCountryCode"
                :kind="form.kind"
                :disabled="isSaving || isReadOnly || Boolean(repeatVisitId)"
                :error="registrationPlateError"
                required
              />
              <div class="entity-form__wide quick-booking-search">
                <UiLoader v-if="isSearching" compact :text="t('quickBooking.searching')" />
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
                    <span>{{ getVehicleMeta(vehicle) || t('quickBooking.foundVehicle') }}</span>
                  </button>
                </div>
                <div v-else-if="searchText.length >= 2" class="detailing-empty">
                  {{ form.organizationClientId ? t('quickBooking.newClientVehicle') : t('quickBooking.newVehicle') }}
                </div>
              </div>
              <UiSelect
                v-model="form.kind"
                :label="t('quickBooking.vehicleType')"
                :options="kindOptions"
                :searchable="false"
                :disabled="isSaving || isReadOnly || Boolean(selectedVehicle)"
              />
              <div class="entity-form-identity entity-form__wide">
                <div class="entity-form-toggle" role="group" :aria-label="t('quickBooking.identityMethod')">
                  <button
                    type="button"
                    class="entity-form-toggle__button"
                    :class="{ active: !isManualIdentity }"
                    :disabled="isSaving || isReadOnly || Boolean(selectedVehicle)"
                    @click="isManualIdentity = false"
                  >
                    {{ t('quickBooking.fromCatalog') }}
                  </button>
                  <button
                    type="button"
                    class="entity-form-toggle__button"
                    :class="{ active: isManualIdentity }"
                    :disabled="isSaving || isReadOnly || Boolean(selectedVehicle)"
                    @click="isManualIdentity = true"
                  >
                    {{ t('quickBooking.manual') }}
                  </button>
                </div>
              </div>
              <template v-if="!isManualIdentity">
                <UiSelect
                  v-model="selectedBrandId"
                  :label="t('quickBooking.brand')"
                  :options="brandOptions"
                  :disabled="isSaving || isReadOnly || Boolean(selectedVehicle)"
                />
                <UiSelect
                  v-model="selectedModelId"
                  :label="t('quickBooking.model')"
                  :options="modelOptions"
                  :disabled="isSaving || isReadOnly || Boolean(selectedVehicle) || isLoadingModels || !selectedBrandId"
                  :placeholder="isLoadingModels ? t('quickBooking.loadingModels') : t('quickBooking.unknown')"
                />
              </template>
              <template v-else>
                <UiInput
                  v-model="form.manualBrandName"
                  :label="t('quickBooking.brand')"
                  :label-help="t('quickBooking.manualBrandHelp')"
                  :disabled="isSaving || isReadOnly || Boolean(selectedVehicle)"
                />
                <UiInput
                  v-model="form.manualModelName"
                  :label="t('quickBooking.model')"
                  :label-help="t('quickBooking.manualModelHelp')"
                  :disabled="isSaving || isReadOnly || Boolean(selectedVehicle)"
                />
              </template>
              </template>
            </div>
            <div class="quick-services"><h3>{{ t('quickBooking.services') }}</h3>
              <MaintenanceServicesEditor :can-assign-employees="context.shell.scenario?.modules?.members !== false" ref="servicesEditor" @duration-change="serviceDuration = $event" @validation-cleared="clearClientValidationSummary('service')" v-model="selectedServices" :can-open-catalog="context.shell.menu?.some(item => item.code === 'catalog' && item.isVisible)" :organization-id="organizationId" default-vat-rate-code="without_vat" :disabled="isSaving || isReadOnly" />
            </div>
            <UiTextarea
              class="entity-form__wide quick-booking-comment"
              v-model="form.comment"
              :label="t('quickBooking.comment')"
              :rows="3"
              :disabled="isSaving || isReadOnly"
            />
          </section>
          <section class="quick-editor-visit" aria-labelledby="quick-visit-title">
            <h3 id="quick-visit-title">{{ t('quickBooking.bookingOptions') }}</h3>

            <div class="catalog-linked-field">
              <UiSelect v-model="form.location" :label="t('quickBooking.post')" :options="postOptions" :disabled="isSaving || isReadOnly" />
              <a
                v-if="context.shell.menu?.some(item => item.code === 'catalog' && item.isVisible) && !isSaving"
                href="#catalog?catalog=work_post"
              >
                {{ t('quickBooking.postCatalog') }}
              </a>
            </div>
            <UiDatePicker
              v-model="form.plannedDate"
              :id="`${formId}-planned-date`"
              :label="t('quickBooking.startDate')"
              :disabled="isSaving || isReadOnly"
              :error="plannedDateError"
              required
            />
            <UiDatePicker v-model="form.plannedEndDate" @update:model-value="bookingTimes?.useManualEnd()" :label="t('quickBooking.endDate')" :disabled="isSaving || isReadOnly" required />
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
              <div><span>{{ t('quickBooking.duration') }}</span><strong>{{ serviceDuration == null ? t('quickBooking.unspecified') : formatServiceDuration(serviceDuration) }}</strong></div>
              <div><span>{{ t('quickBooking.price') }}</span><strong>{{ formatLocaleCurrency(selectedServices.reduce((sum, item) => sum + (item.price ?? 0), 0), workspaceCurrency) }}</strong></div>
            </div>
          </section>
        </div>
        <div class="quick-editor-footer">
          <div v-if="errorMessage && !errorDialogMessage" ref="inlineError" class="quick-editor-error" role="alert" tabindex="-1">{{ errorMessage }}</div>
          <a v-if="repeatVisit?.bookedMaintenanceId" :href="`#maintenance/${repeatVisit.bookedMaintenanceId}`">{{ t('quickBooking.openBooking') }}</a>
          <div class="quick-editor-actions">
            <UiButton v-if="!isReadOnly" type="submit" :disabled="isSaving || !repeatVisitSourceReady || Boolean(repeatVisit?.bookedMaintenanceId)">{{ isSaving ? t('quickBooking.creating') : t('quickBooking.createBooking') }}</UiButton>
            <UiButton v-if="modal" variant="secondary" :disabled="isSaving" @click="emit('cancel')">{{ t('quickBooking.cancel') }}</UiButton>
            <UiButton v-else variant="secondary" href="#scheduler" :disabled="isSaving">{{ t('quickBooking.cancel') }}</UiButton>
          </div>
        </div>
      </form>
    </UiCard>
    <UiAlertDialog
      :open="!modal && Boolean(errorDialogMessage)"
      :title="t('quickBooking.bookingFailed')"
      :message="errorDialogMessage"
      tone="danger"
      @close="errorDialogMessage = ''"
    />
  </section>

</template>

<style scoped src="@opencrm/workspace/quick-booking.css"></style>

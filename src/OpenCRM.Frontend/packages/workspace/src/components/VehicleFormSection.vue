<script setup lang="ts">
import WorkspaceAccessNotice from './WorkspaceAccessNotice.vue'
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { UiAlertDialog, UiButton, UiCard, UiDatePicker, UiImagePreview, UiInput, UiLoader, UiNumberInput, UiSelect, UiTextarea } from '@opencrm/ui'
import VehicleRegistrationPlateInput from './VehicleRegistrationPlateInput.vue'
import { getVehicleRegistrationPlateFormat, loadVehicleRegistrationPlateFormats, normalizeRegistrationPlate, validateRegistrationPlate } from '../vehicleRegistrationPlate'
import type { VehicleRegistrationPlateFormats } from '../vehicleRegistrationPlate'
import { loadClientOptions, type WorkspaceClientOption } from '../api/clients'
import {
  createVehicle,
  loadVehicle,
  loadVehicleBrandOptions,
  loadVehicleModelOptions,
  loadVehiclePhotoList,
  updateVehicle,
  type WorkspaceVehicleCatalogOption,
  type WorkspaceVehicleSaveRequest
} from '../api/vehicles'
import type { WorkspaceVehicle, WorkspaceVehiclePhoto } from '../api/vehicles'
import { getVehicleBrandLogo } from '../utils/vehicleBrandLogo'
import type { WorkspaceContext } from '../types'
import { trackWorkspaceGoal } from '../analytics'
import WorkspacePageHelpButton from './WorkspacePageHelpButton.vue'
import { useLocalization } from '@opencrm/ui'
import '../localization'
import '../vehicleMessages'
import { registrationCountryOptions } from '../utils/registrationCountries'
import { internationalRegistrationPlateError } from '../internationalRegistrationPlate'

const props = defineProps<{
  context: WorkspaceContext
  vehicleId?: string
  initialClientId?: string
}>()

const form = reactive({
  organizationClientId: '',
  kind: '1',
  manualBrandName: '',
  manualModelName: '',
  registrationPlate: '',
  registrationCountryCode: 'RU',
  allowOtherRegistrationPlateFormat: false,
  yearIssue: '',
  color: '',
  vin: '',
  bodyNumber: '',
  engineNumber: '',
  registrationCertificate: '',
  commissioningDate: '',
  name: '',
  description: ''
})

const clientOptions = ref<WorkspaceClientOption[]>([])
const brandOptions = ref<WorkspaceVehicleCatalogOption[]>([])
const modelOptions = ref<WorkspaceVehicleCatalogOption[]>([])
const selectedBrandId = ref('')
const selectedModelId = ref('')
const isManualIdentity = ref(false)
const isLoading = ref(true)
const isSaving = ref(false)
const isLoadingBrands = ref(false)
const isLoadingModels = ref(false)
const errorMessage = ref('')
const errorDialogMessage = ref('')
const loadedVehicle = ref<WorkspaceVehicle | null>(null)
const photos = ref<WorkspaceVehiclePhoto[]>([])
const registrationPlateFormats = ref<VehicleRegistrationPlateFormats>()
const selectedPhotoId = ref('')
const failedPhotos = ref(new Set<string>())
const previewPhotoUrl = ref('')
const photoError = ref('')
let latestRequest = 0
let latestBrandRequest = 0
let latestModelRequest = 0
let isApplyingLoadedVehicle = false
let isInitializationComplete = false
let isComponentMounted = true
const vehicleYearMin = 1900
const vehicleYearMax = new Date().getFullYear()

const organizationId = computed(() => props.context.organization.id)
const { t, locale } = useLocalization()
const countryOptions = computed(() => [
  { value: 'ZZ', label: t('regional.unknownCountry') },
  ...registrationCountryOptions(locale.value)
])
const isInternationalPlate = computed(() => Boolean(form.registrationCountryCode && form.registrationCountryCode !== 'RU'))
const isEditMode = computed(() => Boolean(props.vehicleId))
const isRegistrationPlateUnchanged = computed(() => isEditMode.value && loadedVehicle.value !== null &&
  Number(form.kind) === loadedVehicle.value.kind &&
  form.registrationCountryCode === (loadedVehicle.value.registrationCountryCode || 'RU') &&
  form.registrationPlate === (loadedVehicle.value.registrationPlate || ''))
const canEditVehicles = computed(() => props.context.workspaceAccess?.canEditVehicles === true)
const canCreateVehicle = computed(() => props.context.workspaceAccess?.isReadOnly !== true
  && props.context.shell.quickActions?.some(action => action.code === 'create-vehicle' && action.isVisible) === true)
const isReadOnly = computed(() => props.context.workspaceAccess?.isReadOnly === true
  || (isEditMode.value ? !canEditVehicles.value : !canCreateVehicle.value))
const readOnlyMessage = computed(() =>
  props.context.workspaceAccess?.readOnlyMessage ?? t('vehicle.readOnly')
)
const billingHref = computed(() => '#billing')
const isFleetScenario = computed(() => props.context.shell.scenario?.code === 'fleet')
const title = computed(() => isEditMode.value
  ? (isFleetScenario.value ? t('vehicle.editEquipment') : t('vehicle.editVehicle'))
  : (isFleetScenario.value ? t('vehicle.newEquipment') : t('vehicle.newVehicle')))
const isClientLocked = computed(() => !isEditMode.value && Boolean(props.initialClientId))

const clientSelectOptions = computed(() => [
  { value: '', label: t('vehicle.noClient') },
  ...clientOptions.value.map(client => ({
    value: client.id,
    label: client.details ? `${client.name} · ${client.details}` : client.name
  }))
])
const selectedClientLabel = computed(() => {
  const client = clientOptions.value.find(item => item.id === form.organizationClientId)
  return client ? (client.details ? `${client.name} · ${client.details}` : client.name) : t('vehicle.selectedClient')
})
const cancelHref = computed(() => {
  if (props.vehicleId) {
    return `#vehicles/${props.vehicleId}`
  }

  return isClientLocked.value && props.initialClientId ? `#clients/${props.initialClientId}` : '#vehicles'
})
const brandSelectOptions = computed(() => brandOptions.value.map(brand => ({
  value: String(brand.id),
  label: brand.name
})))
const modelSelectOptions = computed(() => modelOptions.value.map(model => ({
  value: String(model.id),
  label: model.name
})))
const catalogModeAvailable = computed(() => brandOptions.value.length > 0)
const vehicleName = computed(() => {
  const brand = isManualIdentity.value ? form.manualBrandName : brandOptions.value.find(item => String(item.id) === selectedBrandId.value)?.name
  const model = isManualIdentity.value ? form.manualModelName : modelOptions.value.find(item => String(item.id) === selectedModelId.value)?.name
  return [brand, model].filter(Boolean).join(' ') || form.name || (isFleetScenario.value ? t('vehicle.equipment') : t('vehicle.vehicle'))
})
const brandLogo = computed(() => !isManualIdentity.value && selectedBrandId.value === String(loadedVehicle.value?.brandId)
  ? getVehicleBrandLogo(loadedVehicle.value?.brandLogoKey) : null)
const availablePhotos = computed(() => photos.value.filter(photo => !failedPhotos.value.has(photo.url)))
const currentPhotoUrl = computed(() => availablePhotos.value.find(photo => photo.photoId === selectedPhotoId.value)?.url
  || availablePhotos.value[0]?.url
  || (loadedVehicle.value?.photoUrl && !failedPhotos.value.has(loadedVehicle.value.photoUrl) ? loadedVehicle.value.photoUrl : ''))
const clientHref = computed(() => form.organizationClientId && props.context.shell.menu?.some(item => item.code === 'clients' && item.isVisible)
  ? `#clients/${form.organizationClientId}` : '')
const photosHref = computed(() => props.vehicleId ? `#vehicles/${props.vehicleId}/photos` : '')

async function loadPhotos(request: number, vehicleId: string) {
  photoError.value = ''
  try {
    const result = await loadVehiclePhotoList(organizationId.value, vehicleId)
    if (request === latestRequest) photos.value = [...result].sort((left, right) => Number(right.isPrimary) - Number(left.isPrimary))
  } catch {
    if (request === latestRequest) photoError.value = t('vehicle.photosLoadError')
  }
}

function retryPhotos() {
  if (!props.vehicleId) return
  failedPhotos.value.clear()
  void loadPhotos(latestRequest, props.vehicleId)
}

const kindOptions = [
  { value: '1', label: t('vehicle.car') },
  { value: '2', label: t('vehicle.truck') },
  { value: '3', label: t('vehicle.motorcycle') },
  { value: '4', label: t('vehicle.quad') },
  { value: '5', label: t('vehicle.utv') },
  { value: '6', label: t('vehicle.snowmobile') },
  { value: '7', label: t('vehicle.jetSki') },
  { value: '8', label: t('vehicle.boat') },
  { value: '9', label: t('vehicle.special') },
  { value: '10', label: t('vehicle.trailer') },
  { value: '99', label: t('vehicle.otherEquipment') }
]

function toNullableString(value: string | number | null | undefined) {
  const trimmed = String(value ?? '').trim()
  return trimmed.length ? trimmed : null
}

function toNullableNumber(value: string | number | null | undefined) {
  const parsed = Number(String(value ?? '').trim())
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null
}

function getVehicleYearValidationMessage() {
  return t('vehicle.yearRange', { min: vehicleYearMin, max: vehicleYearMax })
}

function validateYearIssue() {
  const value = String(form.yearIssue ?? '').trim()
  if (!value) {
    return true
  }

  const year = Number(value)
  return Number.isInteger(year) && year >= vehicleYearMin && year <= vehicleYearMax
}

function showFormError(message: string) {
  errorDialogMessage.value = message
}

function hasVehicleIdentifier() {
  return [
    form.registrationPlate,
    form.vin,
    form.bodyNumber,
    form.engineNumber,
    form.registrationCertificate,
    form.name
  ].some(value => String(value ?? '').trim().length > 0)
}

function applyInitialClient() {
  if (!isEditMode.value && props.initialClientId) {
    form.organizationClientId = props.initialClientId
  }
}

function clearForm() {
  form.organizationClientId = ''
  form.kind = '1'
  selectedBrandId.value = ''
  selectedModelId.value = ''
  isManualIdentity.value = false
  form.manualBrandName = ''
  form.manualModelName = ''
  form.registrationPlate = ''
  form.registrationCountryCode = 'RU'
  form.allowOtherRegistrationPlateFormat = false
  form.yearIssue = ''
  form.color = ''
  form.vin = ''
  form.bodyNumber = ''
  form.engineNumber = ''
  form.registrationCertificate = ''
  form.commissioningDate = ''
  form.name = ''
  form.description = ''
  applyInitialClient()
}

async function loadBrandsForKind() {
  const request = ++latestBrandRequest
  const kind = form.kind
  isLoadingBrands.value = true

  try {
    const result = await loadVehicleBrandOptions(kind)
    if (request !== latestBrandRequest) return
    brandOptions.value = result

    if (brandOptions.value.length === 0) {
      isManualIdentity.value = true
      selectedBrandId.value = ''
      selectedModelId.value = ''
      modelOptions.value = []
    }
  } catch (error) {
    if (request !== latestBrandRequest) return
    brandOptions.value = []
    modelOptions.value = []
    selectedBrandId.value = ''
    selectedModelId.value = ''
    isManualIdentity.value = true
    errorMessage.value = error instanceof Error ? error.message : t('vehicle.brandsLoadError')
  } finally {
    if (request === latestBrandRequest) isLoadingBrands.value = false
  }
}

async function loadModelsForBrand(modelId = '') {
  const request = ++latestModelRequest
  const brandId = selectedBrandId.value
  modelOptions.value = []

  if (!selectedBrandId.value) {
    selectedModelId.value = ''
    return
  }

  isLoadingModels.value = true

  try {
    const result = await loadVehicleModelOptions(brandId, form.kind)
    if (request !== latestModelRequest) return
    modelOptions.value = result
    selectedModelId.value = modelId
  } catch (error) {
    if (request !== latestModelRequest) return
    modelOptions.value = []
    selectedModelId.value = ''
    errorMessage.value = error instanceof Error ? error.message : t('vehicle.modelsLoadError')
  } finally {
    if (request === latestModelRequest) isLoadingModels.value = false
  }
}

function setManualIdentity(value: boolean) {
  if (!value && !catalogModeAvailable.value) {
    return
  }

  isManualIdentity.value = value
}

async function loadForm() {
  const request = ++latestRequest
  isSaving.value = false
  latestBrandRequest++
  latestModelRequest++
  isLoading.value = true
  errorMessage.value = ''
  errorDialogMessage.value = ''
  loadedVehicle.value = null
  photos.value = []
  selectedPhotoId.value = ''
  failedPhotos.value.clear()
  previewPhotoUrl.value = ''
  photoError.value = ''

  try {
    const clientsTask = loadClientOptions(organizationId.value)
    const vehicleTask = props.vehicleId ? loadVehicle(organizationId.value, props.vehicleId) : Promise.resolve(null)
    const [clients, vehicle] = await Promise.all([clientsTask, vehicleTask])
    if (request !== latestRequest) return

    clientOptions.value = clients
    loadedVehicle.value = vehicle

    if (vehicle) {
      void loadPhotos(request, vehicle.id)
      isApplyingLoadedVehicle = true
      form.organizationClientId = vehicle.organizationClientId ?? ''
      form.kind = String(vehicle.kind || 1)
      selectedBrandId.value = vehicle.brandId ? String(vehicle.brandId) : ''
      selectedModelId.value = vehicle.modelId ? String(vehicle.modelId) : ''
      isManualIdentity.value = vehicle.isManualEntry || !vehicle.modelId
      form.manualBrandName = vehicle.manualBrandName || vehicle.brandName || ''
      form.manualModelName = vehicle.manualModelName || vehicle.modelName || ''
      form.registrationPlate = vehicle.registrationPlate || ''
      form.registrationCountryCode = vehicle.registrationCountryCode || 'RU'
      form.allowOtherRegistrationPlateFormat = Boolean(vehicle.registrationPlate && registrationPlateFormats.value &&
        validateRegistrationPlate(vehicle.registrationPlate, vehicle.kind || 1, registrationPlateFormats.value))
      form.yearIssue = vehicle.yearIssue ? String(vehicle.yearIssue) : ''
      form.color = vehicle.color || ''
      form.vin = vehicle.vin || ''
      form.bodyNumber = vehicle.bodyNumber || ''
      form.engineNumber = vehicle.engineNumber || ''
      form.registrationCertificate = vehicle.registrationCertificate || ''
      form.commissioningDate = vehicle.commissioningDate?.slice(0, 10) || ''
      form.name = vehicle.name || ''
      form.description = vehicle.description || ''
      await loadBrandsForKind()
      if (request !== latestRequest) return

      if (!isManualIdentity.value && selectedBrandId.value) {
        await loadModelsForBrand(selectedModelId.value)
      }
      if (request !== latestRequest) return

      isApplyingLoadedVehicle = false
    } else {
      clearForm()
      await loadBrandsForKind()
    }
  } catch (error) {
    if (request === latestRequest) errorMessage.value = error instanceof Error ? error.message : t('vehicle.formLoadError')
  } finally {
    if (request === latestRequest) {
      isApplyingLoadedVehicle = false
      isLoading.value = false
    }
  }
}

function buildPayload(): WorkspaceVehicleSaveRequest {
  return {
    kind: Number(form.kind) || 1,
    mileageUnit: 1,
    modelId: isManualIdentity.value ? null : toNullableNumber(selectedModelId.value),
    yearIssue: toNullableNumber(form.yearIssue),
    manualBrandName: isManualIdentity.value
      ? toNullableString(form.manualBrandName)
      : !selectedModelId.value ? brandOptions.value.find(brand => String(brand.id) === selectedBrandId.value)?.name ?? null : null,
    manualModelName: isManualIdentity.value ? toNullableString(form.manualModelName) : null,
    name: toNullableString(form.name),
    vin: toNullableString(form.vin),
    registrationPlate: isRegistrationPlateUnchanged.value ? loadedVehicle.value?.registrationPlate ?? null : toNullableString(registrationPlateFormats.value
      ? normalizeRegistrationPlate(form.registrationPlate, registrationPlateFormats.value,
        isInternationalPlate.value || form.allowOtherRegistrationPlateFormat || !getVehicleRegistrationPlateFormat(form.kind, registrationPlateFormats.value))
      : form.registrationPlate),
    allowOtherRegistrationPlateFormat: form.allowOtherRegistrationPlateFormat,
    registrationCountryCode: isEditMode.value && loadedVehicle.value?.registrationCountryCode == null && form.registrationCountryCode === 'RU'
      ? null : form.registrationCountryCode,
    bodyNumber: toNullableString(form.bodyNumber),
    engineNumber: toNullableString(form.engineNumber),
    color: toNullableString(form.color),
    registrationCertificate: toNullableString(form.registrationCertificate),
    commissioningDate: form.commissioningDate || null,
    description: toNullableString(form.description),
    organizationId: organizationId.value,
    organizationClientId: form.organizationClientId || null
  }
}

async function saveVehicle() {
  if (isSaving.value) {
    return
  }

  errorMessage.value = ''
  errorDialogMessage.value = ''

  if (isReadOnly.value) {
    showFormError(readOnlyMessage.value)
    return
  }

  if (!hasVehicleIdentifier()) {
    showFormError(t('vehicle.identifierRequired'))
    return
  }

  if (!validateYearIssue()) {
    showFormError(getVehicleYearValidationMessage())
    return
  }

  const request = latestRequest
  const requestOrganizationId = organizationId.value
  const requestVehicleId = props.vehicleId
  isSaving.value = true

  try {
    const plateFormats = registrationPlateFormats.value ?? await loadVehicleRegistrationPlateFormats()
    if (request !== latestRequest || requestOrganizationId !== organizationId.value || requestVehicleId !== props.vehicleId) return
    registrationPlateFormats.value = plateFormats
    const internationalError = isInternationalPlate.value ? internationalRegistrationPlateError(form.registrationPlate) : ''
    const internationalErrorMessages = { length: 'plateLength', characters: 'plateCharacters', short: 'plateShort' }
    const registrationPlateError = isRegistrationPlateUnchanged.value ? ''
      : isInternationalPlate.value
        ? internationalError ? t(`regional.${internationalErrorMessages[internationalError]}`) : ''
        : validateRegistrationPlate(form.registrationPlate, form.kind, registrationPlateFormats.value, form.allowOtherRegistrationPlateFormat)
    if (registrationPlateError) {
      showFormError(registrationPlateError)
      return
    }

    const payload = buildPayload()

    if (requestVehicleId) {
      await updateVehicle(requestOrganizationId, requestVehicleId, payload)
      if (request !== latestRequest || requestOrganizationId !== organizationId.value || requestVehicleId !== props.vehicleId) return
      window.location.hash = `#vehicles/${requestVehicleId}`
    } else {
      const vehicleId = await createVehicle(payload)
      if (request !== latestRequest || requestOrganizationId !== organizationId.value || requestVehicleId !== props.vehicleId) return
      trackWorkspaceGoal('vehicle_created', { scope: 'organization' })
      window.location.hash = `#vehicles/${vehicleId}`
    }
  } catch (error) {
    if (request === latestRequest && requestOrganizationId === organizationId.value && requestVehicleId === props.vehicleId) {
      showFormError(error instanceof Error ? error.message : t('vehicle.saveError'))
    }
  } finally {
    if (request === latestRequest && requestOrganizationId === organizationId.value && requestVehicleId === props.vehicleId) {
      isSaving.value = false
    }
  }
}

onMounted(async () => {
  let formatsError = ''

  try {
    registrationPlateFormats.value = await loadVehicleRegistrationPlateFormats()
  } catch (error) {
    formatsError = error instanceof Error ? error.message : t('vehicle.formatsLoadError')
  }

  if (!isComponentMounted) return

  isInitializationComplete = true
  const request = latestRequest + 1
  await loadForm()

  if (formatsError && isComponentMounted && request === latestRequest && !errorMessage.value) {
    errorMessage.value = formatsError
  }
})
watch(() => [props.vehicleId, organizationId.value], () => {
  if (!isInitializationComplete) return

  return loadForm()
})
onBeforeUnmount(() => {
  isComponentMounted = false
  latestRequest++
  latestBrandRequest++
  latestModelRequest++
})
watch(() => form.kind, async () => {
  if (isApplyingLoadedVehicle) {
    return
  }

  selectedBrandId.value = ''
  selectedModelId.value = ''
  modelOptions.value = []
  await loadBrandsForKind()
})
watch(selectedBrandId, async () => {
  if (isApplyingLoadedVehicle) {
    return
  }

  selectedModelId.value = ''
  await loadModelsForBrand()
})
</script>

<template>
  <section class="entity-card-section">
    <UiCard page :title="title">
      <template #actions>
        <WorkspacePageHelpButton section-code="vehicles" :entity-id="vehicleId ? vehicleId : 'new'" :action="vehicleId ? 'edit' : ''" :title="title" />
      </template>
      <UiLoader v-if="isLoading" :text="t('vehicle.opening')" />
      <div v-else-if="errorMessage" class="detailing-state detailing-state-error">{{ errorMessage }}</div>

      <form v-if="!isLoading && (!isEditMode || loadedVehicle)" class="vehicle-editor" @submit.prevent="saveVehicle">
        <WorkspaceAccessNotice v-if="isReadOnly" :can-manage-billing="context.organization.canManageBilling === true" :message="readOnlyMessage" :href="billingHref" />
        <div class="vehicle-editor__layout">
          <div class="vehicle-editor__main">
            <section class="vehicle-editor__section">
              <h3 class="ui-section-header">{{ t('vehicle.mainDetails') }}</h3>
              <div class="vehicle-editor__fields">
                <UiSelect v-model="form.kind" :label="t('vehicle.kind')" :options="kindOptions" :searchable="false" :disabled="isSaving || isReadOnly" />
                <UiSelect v-model="form.registrationCountryCode" :label="t('regional.registrationCountry')" :options="countryOptions" :disabled="isSaving || isReadOnly" />
                <VehicleRegistrationPlateInput
                  v-model="form.registrationPlate"
                  v-model:allow-other-format="form.allowOtherRegistrationPlateFormat"
                  :kind="form.kind"
                  :country-code="form.registrationCountryCode"
                  :infer-other-format="isEditMode"
                  :disabled="isSaving || isReadOnly"
                />
                <div class="vehicle-editor__identity">
                  <div class="entity-form-toggle" role="group" :aria-label="t('vehicle.identityMethod')">
                    <button
                      type="button"
                      class="entity-form-toggle__button"
                      :class="{ active: !isManualIdentity }"
                      :aria-pressed="!isManualIdentity"
                      :disabled="isSaving || isReadOnly || !catalogModeAvailable || isLoadingBrands"
                      @click="setManualIdentity(false)"
                    >
                      {{ t('vehicle.fromCatalog') }}
                    </button>
                    <button
                      type="button"
                      class="entity-form-toggle__button"
                      :class="{ active: isManualIdentity }"
                      :aria-pressed="isManualIdentity"
                      :disabled="isSaving || isReadOnly"
                      @click="setManualIdentity(true)"
                    >
                      {{ t('vehicle.manual') }}
                    </button>
                  </div>
                </div>
                <template v-if="!isManualIdentity">
                  <UiSelect
                    v-model="selectedBrandId"
                    :label="t('vehicle.make')"
                    :options="brandSelectOptions"
                    :disabled="isSaving || isReadOnly || isLoadingBrands"
                    :placeholder="isLoadingBrands ? t('vehicle.loadingMakes') : t('vehicle.chooseMake')"
                  />
                  <UiSelect
                    v-model="selectedModelId"
                    :label="t('vehicle.model')"
                    :options="modelSelectOptions"
                    :disabled="isSaving || isReadOnly || isLoadingModels || !selectedBrandId"
                    :placeholder="isLoadingModels ? t('vehicle.loadingModels') : t('vehicle.chooseModel')"
                  />
                </template>
                <template v-else>
                  <UiInput v-model="form.manualBrandName" :label="t('vehicle.make')" :disabled="isSaving || isReadOnly" />
                  <UiInput v-model="form.manualModelName" :label="t('vehicle.model')" :disabled="isSaving || isReadOnly" />
                </template>
                <UiNumberInput v-model="form.yearIssue" :label="t('vehicle.year')" :min="vehicleYearMin" :max="vehicleYearMax" :step="1" integer :disabled="isSaving || isReadOnly" />
                <UiInput v-model="form.color" :label="t('vehicle.color')" :disabled="isSaving || isReadOnly" />
              </div>
            </section>
            <section class="vehicle-editor__section">
              <h3 class="ui-section-header">{{ t('vehicle.identification') }}</h3>
              <div class="vehicle-editor__fields">
                <UiInput v-model="form.vin" label="VIN" :disabled="isSaving || isReadOnly" />
                <UiInput v-model="form.name" :label="t('vehicle.internalName')" :disabled="isSaving || isReadOnly" />
              </div>
            </section>
            <details class="vehicle-editor__documents" :open="Boolean(form.bodyNumber || form.engineNumber || form.registrationCertificate || form.commissioningDate)">
              <summary>{{ t('vehicle.documents') }}<span v-if="form.bodyNumber || form.engineNumber || form.registrationCertificate || form.commissioningDate">{{ t('vehicle.hasDetails') }}</span></summary>
              <div class="vehicle-editor__fields">
                <UiInput v-model="form.bodyNumber" :label="t('vehicle.bodyNumber')" :disabled="isSaving || isReadOnly" />
                <UiInput v-model="form.engineNumber" :label="t('vehicle.engineNumber')" :disabled="isSaving || isReadOnly" />
                <UiInput v-model="form.registrationCertificate" :label="t('vehicle.certificate')" :disabled="isSaving || isReadOnly" />
                <UiDatePicker v-model="form.commissioningDate" :label="t('vehicle.commissioning')" :disabled="isSaving || isReadOnly" />
              </div>
            </details>
          </div>
          <aside class="vehicle-editor__aside">
            <section v-if="isEditMode || selectedBrandId || form.manualBrandName || form.registrationPlate || form.name" class="vehicle-editor__preview">
              <header class="ui-section-header">
                <img v-if="brandLogo" :src="brandLogo.src" :alt="brandLogo.name" />
                <div><strong>{{ vehicleName }}</strong><span>{{ [form.yearIssue, form.color].filter(Boolean).join(' · ') }}</span></div>
                <span v-if="form.registrationPlate" class="vehicle-editor__plate">{{ form.registrationPlate }}</span>
              </header>
              <template v-if="currentPhotoUrl">
                <button class="vehicle-editor__photo" type="button" :aria-label="t('vehicle.zoomPhoto')" @click="previewPhotoUrl = currentPhotoUrl"><img :src="currentPhotoUrl" :alt="vehicleName" @error="failedPhotos.add(currentPhotoUrl)" /></button>
                <div v-if="availablePhotos.length > 1" class="vehicle-editor__thumbnails" role="group" :aria-label="t('vehicle.photos')">
                  <button v-for="(photo, index) in availablePhotos.slice(0, 4)" :key="photo.photoId" type="button" :aria-label="t('vehicle.photoNumber', { number: index + 1 })" :aria-pressed="currentPhotoUrl === photo.url" @click="selectedPhotoId = photo.photoId"><img :src="photo.url" alt="" @error="failedPhotos.add(photo.url)" /></button>
                </div>
              </template>
              <div v-if="photoError" class="vehicle-editor__photo-error" role="status">{{ photoError }} <button type="button" @click="retryPhotos">{{ t('vehicle.retry') }}</button></div>
              <a v-if="photosHref" class="vehicle-editor__link" :href="photosHref" target="_blank" rel="noopener">{{ t('vehicle.photosLink') }}<span class="vehicle-editor__sr">{{ t('vehicle.newTab') }}</span></a>
            </section>
            <section class="vehicle-editor__section">
              <h3 class="ui-section-header">{{ t('vehicle.owner') }}</h3>
              <UiSelect v-model="form.organizationClientId" :label="t('vehicle.customer')" :options="clientSelectOptions" :disabled="isSaving || isReadOnly || isClientLocked" />
              <p v-if="isClientLocked" class="vehicle-editor__note">{{ t('vehicle.fromCustomer') }}<strong>{{ selectedClientLabel }}</strong></p>
              <a v-if="clientHref" class="vehicle-editor__link" :href="clientHref" target="_blank" rel="noopener">{{ t('vehicle.customerLink') }}<span class="vehicle-editor__sr">{{ t('vehicle.newTab') }}</span></a>
            </section>
            <section class="vehicle-editor__section">
              <h3 class="ui-section-header">{{ t('vehicle.description') }}</h3>
              <UiTextarea v-model="form.description" :label="t('vehicle.vehicleDescription')" :rows="3" :disabled="isSaving || isReadOnly" />
            </section>
          </aside>
        </div>
        <div class="vehicle-editor__actions">
          <UiButton v-if="!isReadOnly" type="submit" :disabled="isSaving">{{ isSaving ? t('vehicle.saving') : isEditMode ? t('vehicle.saveChanges') : isFleetScenario ? t('vehicle.addEquipment') : t('vehicle.addVehicle') }}</UiButton>
          <UiButton variant="secondary" type="button" :disabled="isSaving" :href="cancelHref">{{ t('vehicle.cancel') }}</UiButton>
        </div>
      </form>
    </UiCard>
    <UiImagePreview :src="previewPhotoUrl" :title="vehicleName" @close="previewPhotoUrl = ''" />
    <UiAlertDialog
      :open="Boolean(errorDialogMessage)"
      :title="t('vehicle.saveErrorTitle')"
      :message="errorDialogMessage"
      tone="danger"
      @close="errorDialogMessage = ''"
    />
  </section>
</template>

<style scoped>
.vehicle-editor {
  display: grid;
  gap: 20px;
  container-type: inline-size;
}

.vehicle-editor__layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(260px, 360px);
  gap: 20px;
  align-items: start;
}

.vehicle-editor__main, .vehicle-editor__aside {
  display: grid;
  gap: 16px;
  min-width: 0;
}

.vehicle-editor__section {
  display: grid;
  gap: 14px;
  min-width: 0;
  padding: 16px;
  border: 1px solid var(--crm-line, #e6e9ee);
  border-radius: 8px;
  background: var(--crm-surface, #fff);
}

.vehicle-editor h3 {
  margin: -16px -16px 0;
  padding: 14px 16px;
  border-bottom: 1px solid var(--crm-line, #e6e9ee);
  font-size: 15px;
}

.vehicle-editor__fields {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.vehicle-editor__fields > * {
  min-width: 0;
}

.vehicle-editor__identity {
  grid-column: 1 / -1;
}

.vehicle-editor__aside {
  align-content: start;
}

.vehicle-editor__preview {


  min-width: 0;


  overflow: hidden;


  border: 1px solid var(--crm-line, #e6e9ee);


  border-radius: 8px;


  background: var(--crm-surface, #fff);


}
.vehicle-editor__preview header {

  display: flex;

  align-items: center;

  flex-wrap: wrap;

  gap: 10px;

  padding: 14px;

}
.vehicle-editor__preview header > img {

  width: 36px;

  height: 36px;

  padding: 3px;

  flex-shrink: 0;

  object-fit: contain;

  background: white;

  border-radius: 50%;

}
.vehicle-editor__preview header > div {

  display: grid;

  gap: 5px;

  flex: 1;

  min-width: 100px;

  overflow-wrap: anywhere;

}
.vehicle-editor__preview header strong {

  font-size: 15px;

}
.vehicle-editor__preview header div > span {

  color: var(--crm-text-muted, #667085);

  font-size: 12px;

}
.vehicle-editor__plate {

  max-width: 100%;

  padding: 4px 6px;

  border: 1px solid var(--crm-line, #e6e9ee);

  border-radius: 4px;

  background: var(--crm-surface-soft, #f7f8fa);

  font-size: 12px;

  overflow-wrap: anywhere;

}
.vehicle-editor__photo {

  display: block;

  width: 100%;

  height: 190px;

  padding: 0;

  border: 0;

  background: var(--crm-surface-soft, #f7f8fa);

  cursor: zoom-in;

}
.vehicle-editor__photo img {

  width: 100%;

  height: 100%;

  object-fit: contain;

}
.vehicle-editor__thumbnails {

  display: grid;

  grid-template-columns: repeat(auto-fill, minmax(80px, 1fr));

  gap: 8px;

  padding: 10px 12px 0;

}
.vehicle-editor__thumbnails button {

  min-width: 0;

  aspect-ratio: 4 / 3;

  padding: 2px;

  border: 1px solid var(--crm-line, #e6e9ee);

  border-radius: 5px;

  background: var(--crm-surface-soft, #f7f8fa);

  cursor: pointer;

}
.vehicle-editor__thumbnails button[aria-pressed="true"] {

  border-color: #e87532;

}
.vehicle-editor__thumbnails img {

  display: block;

  width: 100%;

  height: 100%;

  object-fit: contain;

  border-radius: 3px;

}
.vehicle-editor__link {

  font-size: 13px;

  color: #e87532;

  text-decoration: none;

}
.vehicle-editor__preview > .vehicle-editor__link {

  display: block;

  padding: 12px 14px;

}
.vehicle-editor__photo-error {

  padding: 12px 14px 0;

  font-size: 12px;

  color: var(--crm-text-muted, #667085);

}
.vehicle-editor__photo-error button {

  font: inherit;

  background: transparent;

  border: 0;

  color: #e87532;

  cursor: pointer;

}
.vehicle-editor__sr {

  position: absolute;

  width: 1px;

  height: 1px;

  padding: 0;

  overflow: hidden;

  clip-path: inset(50%);

  white-space: nowrap;

}

.vehicle-editor__note {
  margin: 0;
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
  overflow-wrap: anywhere;
}

.vehicle-editor__documents {
  border: 1px solid var(--crm-line, #e6e9ee);
  border-radius: 8px;
  background: var(--crm-surface, #fff);
}

.vehicle-editor__documents summary {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  list-style: none;
}

.vehicle-editor__documents summary::-webkit-details-marker {
  display: none;
}

.vehicle-editor__documents summary::after {
  content: '';
  flex-shrink: 0;
  width: 6px;
  height: 6px;
  margin-left: auto;
  border-right: 1.5px solid currentColor;
  border-bottom: 1.5px solid currentColor;
  transform: rotate(45deg);
}

.vehicle-editor__documents[open] summary::after {
  transform: rotate(225deg);
}

.vehicle-editor__documents summary span {
  font-size: 12px;
  font-weight: 400;
  color: var(--crm-text-muted, #667085);
}

.vehicle-editor__documents .vehicle-editor__fields {
  padding: 0 16px 16px;
}

.vehicle-editor__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  border-top: 1px solid var(--crm-line, #e6e9ee);
  padding-top: 18px;
}

.vehicle-editor :deep(input), .vehicle-editor :deep(textarea) {
  min-width: 0;
}

.vehicle-editor :deep(textarea) {
  font: inherit;
  font-size: 14px;
  line-height: 1.5;
}

.vehicle-editor summary:focus-visible {
  outline: 2px solid #e87532;
  outline-offset: 3px;
}

.vehicle-editor button:focus-visible, .vehicle-editor a:focus-visible {


  outline: 2px solid #e87532;


  outline-offset: 2px;


}
.vehicle-editor__actions :deep(.ui-button--primary) {

  background: #e87532;

  border-color: #e87532;

  color: white;

}

@container (max-width: 850px) {
  .vehicle-editor__layout {
    grid-template-columns: minmax(0, 1fr);
    gap: 24px;
  }

  .vehicle-editor__aside {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .vehicle-editor__preview {


    grid-column: 1 / -1;


  }

}
@container (max-width: 540px) {
  .vehicle-editor__fields, .vehicle-editor__aside {
    grid-template-columns: minmax(0, 1fr);
  }

  .vehicle-editor__documents summary {
    flex-wrap: wrap;
  }

}
</style>

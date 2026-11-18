<script setup lang="ts">
import WorkspaceAccessNotice from './WorkspaceAccessNotice.vue'
import ClientBotConnection from './ClientBotConnection.vue'
import ClientConversationPanel from './ClientConversationPanel.vue'
import SchedulerBookingDialog from './SchedulerBookingDialog.vue'
import { clientDisplayName, clientInnMask, clientKppMask, normalizeTaxCode, validateClientIdentity } from '../utils/clientForm'
import ClientVehicles from './ClientVehicles.vue'
import RepeatVisitRecommendations from './RepeatVisitRecommendations.vue'
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useId, watch } from 'vue'
import { UiButton, UiCard, UiInput, UiLoader, UiPagination, UiPhoneInput, UiSelect, UiTextarea, useLocalization } from '@opencrm/ui'
import { formatLocaleCurrency, formatLocaleDate } from '@opencrm/ui/i18n'
import '../clientMessages'
import {
  createClient,
  loadClientDetails,
  loadClientMaintenances,
  updateClient,
  updateClientComment,
  type WorkspaceClientDefectItem,
  type WorkspaceClientDetails,
  type WorkspaceClientMaintenancesPage,
  type WorkspaceClientMaintenanceItem
} from '../api/clients'
import type { WorkspaceContext } from '../types'
import { createMemberSelectOptions } from '../utils/memberSelectOptions'
import { trackWorkspaceGoal } from '../analytics'

const props = defineProps<{
  context: WorkspaceContext
  clientId: string
}>()
const emit = defineEmits<{
  changed: []
}>()
const { t } = useLocalization()

const details = ref<WorkspaceClientDetails | null>(null)
const isLoading = ref(false)
const isSaving = ref(false)
const isEditing = ref(false)
const errorMessage = ref('')
const isCommentOpen = ref(false)
const isCommentSaving = ref(false)
const commentDraft = ref('')
const commentError = ref('')
const isFormSubmitted = ref(false)
const clientFormElement = ref<HTMLFormElement>()
const phoneValidity = ref<boolean | null>(null)
const commentLimitMessage = computed(() => t('clients.commentLimit'))
const activePanel = ref<'maintenance' | 'defects' | 'messages'>('maintenance')
const historySearch = ref('')
const historyStatus = ref('')
const historyPage = ref(1)
const historyPageSize = 10
const historyResult = ref<WorkspaceClientMaintenancesPage | null>(null)
const isHistoryLoading = ref(false)
const historyError = ref('')
const copyMessage = ref('')
let copyMessageTimer: ReturnType<typeof setTimeout> | undefined
let historySearchTimer: ReturnType<typeof setTimeout> | undefined
let historyRequest = 0
const tagDraft = ref('')
const tagInputId = useId()
const vehiclePicker = ref<InstanceType<typeof ClientVehicles> | null>(null)
let latestRequest = 0
const form = ref({
  name: '',
  lastName: '',
  firstName: '',
  middleName: '',
  companyTypeCode: 'legal_entity',
  phone: '',
  partyTypeCode: 'person',
  inn: '',
  kpp: '',
  legalAddress: '',
  statusCode: 'active',
  responsibleUserId: '',
  tagNamesText: '',
  comment: ''
})

const organizationId = computed(() => props.context.organization.id)
const isCreateMode = computed(() => props.clientId === 'new')
const isReadOnly = computed(() => props.context.workspaceAccess?.isReadOnly === true)
const canWrite = computed(() => !isReadOnly.value)
const canManageBotConnections = computed(() => canWrite.value && details.value?.canEdit === true)
const readOnlyMessage = computed(() =>
  props.context.workspaceAccess?.readOnlyMessage ?? t('clients.readOnly')
)
const billingHref = computed(() => '#billing')
const client = computed(() => details.value?.client ?? null)
const vehiclesTitle = computed(() => props.context.shell.scenario?.code === 'detailing' ? t('clients.vehicles') : (props.context.shell.scenario?.terms?.vehicles ?? t('clients.vehicles')))
const maintenanceTitle = computed(() => props.context.shell.scenario?.terms?.maintenances ?? t('clients.historyTitle'))
const defectsTitle = computed(() => props.context.shell.scenario?.terms?.defects ?? t('clients.defectsTitle'))
const isDetailingScenario = computed(() => props.context.shell.scenario?.code === 'detailing')
const canOpenVehicles = computed(() => props.context.shell.menu?.some(item => item.code === 'vehicles' && item.isVisible) === true)
const canOpenMaintenance = computed(() => details.value?.canViewMaintenance === true && props.context.shell.menu?.some(item => item.isVisible && (item.code === 'maintenance' || item.code === 'scheduler' || item.sectionCodes?.includes('maintenance'))) === true)
const canLoadMaintenanceHistory = computed(() => canOpenMaintenance.value && !isCreateMode.value && !isEditing.value)
const canOpenMessages = computed(() => props.context.shell.menu?.some(item => item.code === 'messages' && item.isVisible) === true)
const canOpenDefects = computed(() => props.context.shell.menu?.some(item => item.code === 'defects' && item.isVisible) === true)
const canCreateMaintenance = computed(() => canWrite.value && canOpenMaintenance.value && !client.value?.isArchived && props.context.shell.quickActions?.some(action => action.code === 'create-maintenance' && action.isVisible) === true)
const canCreateVehicle = computed(() => canWrite.value && canOpenVehicles.value && !client.value?.isArchived && props.context.shell.quickActions?.some(action => action.code === 'create-vehicle' && action.isVisible) === true)
const vehicles = computed(() => client.value?.vehicles ?? [])
const upcoming = computed(() => details.value?.upcomingMaintenance)
const createLabel = computed(() => isDetailingScenario.value ? t('clients.createAppointment') : t('clients.createWork'))
const historyPaginationInfo = computed(() => {
  const totalCount = historyResult.value?.totalCount ?? 0
  if (totalCount === 0) {
    return t('clients.zeroRecords')
  }

  const from = (historyPage.value - 1) * historyPageSize + 1
  const to = Math.min(historyPage.value * historyPageSize, totalCount)
  return t('clients.rangeOf', { from, to, total: totalCount })
})
const hasHistoryFilters = computed(() => Boolean(historySearch.value.trim() || historyStatus.value))

function formatMoney(value?: number | null, currencyCode = details.value?.currencyCode ?? props.context.organization.currencyCode ?? 'RUB') {
  return value === undefined || value === null
    ? '—'
    : formatLocaleCurrency(value, currencyCode, { maximumFractionDigits: 2 })
}

async function copyPhone() {
  try {
    await navigator.clipboard.writeText(client.value?.phone || '')
    copyMessage.value = t('clients.phoneCopied')
  } catch {
    copyMessage.value = t('clients.copyPhoneError')
  }
  clearTimeout(copyMessageTimer)
  copyMessageTimer = setTimeout(() => { copyMessage.value = '' }, 2000)
}

function createMaintenance() {
  if (!canCreateMaintenance.value || !vehicles.value.length) return
  if (vehicles.value.length === 1) {
    window.location.hash = `#maintenance/new/${vehicles.value[0]!.id}`
    return
  }
  vehiclePicker.value?.open(true)
}

function getStatusName(item: WorkspaceClientMaintenanceItem) {
  if (item.status === 4) return t('clients.cancelled')
  if (item.handedOverAt) return t('clients.handedOver')
  if (item.readyForHandoverAt) return t('clients.readyForHandover')
  return getMaintenanceStatusName(item.status)
}
const statusOptions = [
  { value: 'active', label: t('clients.active') },
  { value: 'lead', label: t('clients.lead') },
  { value: 'attention', label: t('clients.attention') },
  { value: 'paused', label: t('clients.paused') },
  { value: 'lost', label: t('clients.lost') }
]
const partyTypeOptions = [
  { value: 'person', label: t('clients.person') },
  { value: 'company', label: t('clients.company') }
]
const companyTypeOptions = computed(() => [
  ...(!isCreateMode.value && client.value?.partyTypeCode === 'company' && !client.value.companyTypeCode ? [{ value: '', label: t('clients.notSpecified') }] : []),
  { value: 'legal_entity', label: t('clients.legalEntity') }, { value: 'entrepreneur', label: t('clients.entrepreneur') }
])
const hasLegacyName = computed(() => Boolean(!isCreateMode.value && client.value?.partyTypeCode !== 'company' && !client.value?.firstName && client.value?.name))
const identityErrors = computed(() => {
  const identity = {
    ...form.value,
    name: form.value.partyTypeCode === 'person' && hasLegacyName.value ? client.value!.name : form.value.name
  }
  return validateClientIdentity(identity, isFormSubmitted.value, hasLegacyName.value, (key, params) => t(key, params ?? {}))
})
function lengthHint(value: string, limit: number) { return t('clients.lengthHint', { current: value.length, limit }) }

const formTags = computed(() => form.value.tagNamesText.split(',').map(tag => tag.trim()).filter(Boolean))

function commitTagDraft() {
  const tags = [...formTags.value]
  for (const tag of tagDraft.value.split(',').map(value => value.trim()).filter(Boolean)) {
    if (!tags.some(value => value.toLocaleLowerCase('ru') === tag.toLocaleLowerCase('ru'))) tags.push(tag)
  }
  form.value.tagNamesText = tags.join(', ')
  tagDraft.value = ''
}

function removeTag(index: number) {
  form.value.tagNamesText = formTags.value.filter((_, tagIndex) => tagIndex !== index).join(', ')
}

function onTagKeydown(event: KeyboardEvent) {
  if (event.isComposing || (event.key !== 'Enter' && event.key !== ',')) return
  event.preventDefault()
  commitTagDraft()
}
const responsibleOptions = computed(() => [
  { value: '', label: t('clients.noResponsible') },
  ...createMemberSelectOptions((props.context.organization.members ?? []).map(member => ({
    ...member,
    value: member.userId
  })))
])

function formatDate(value?: string | null, fallback = t('clients.none')) {
  if (!value) {
    return fallback
  }

  const dateOnlyMatch = value.match(/^(\d{4})-(\d{2})-(\d{2})/)
  if (dateOnlyMatch) {
    return `${dateOnlyMatch[3]}.${dateOnlyMatch[2]}.${dateOnlyMatch[1]}`
  }

  return formatLocaleDate(value, { timeZone: props.context.organization.timeZoneId || 'UTC' })
}

function formatEventDate(value?: string | null) {
  return value ? formatLocaleDate(value, { timeZone: props.context.organization.timeZoneId || 'UTC' }) : t('clients.none')
}

function formatTime(value?: string | null) {
  return value ? value.slice(0, 5) : ''
}

function getMaintenanceTypeName(type: number) {
  const names: Record<number, string> = {
    1: t('clients.maintenance'),
    2: t('clients.repair'),
    3: t('clients.refueling'),
    100: t('clients.other')
  }

  return names[type] ?? t('clients.service')
}

function getMaintenanceStatusName(status: number) {
  const names: Record<number, string> = {
    1: t('clients.planned'),
    2: t('clients.inProgress'),
    3: t('clients.completed'),
    4: t('clients.cancelled')
  }

  return names[status] ?? t('clients.unknown')
}

function getMaintenanceDate(item: WorkspaceClientMaintenanceItem) {
  return item.status === 3 ? (item.completedDate ?? item.date) : (item.plannedDate ?? item.date)
}

function getMaintenanceTime(item: WorkspaceClientMaintenanceItem) {
  const start = formatTime(item.plannedStartTime)
  const end = formatTime(item.plannedEndTime)
  if (item.plannedEndDate && item.plannedEndDate.slice(0, 10) !== (item.plannedDate ?? item.date).slice(0, 10)) {
    return `${formatDate(item.plannedDate ?? item.date)} ${start} — ${formatDate(item.plannedEndDate)} ${end}`
  }

  return start && end ? `${start}-${end}` : start || ''
}

function getMaintenanceUrl(item: WorkspaceClientMaintenanceItem) {
  return item.id ? `#maintenance/${item.id}` : normalizeWorkspaceUrl(item.detailUrl)
}

function openCreateVehicleForClient() {
  window.location.hash = canWrite.value && client.value?.id
    ? `#vehicles/new/${client.value.id}`
    : billingHref.value
}

function getDefectUrl(item: WorkspaceClientDefectItem) {
  return item.id ? `#defects/${item.id}` : normalizeWorkspaceUrl(item.detailUrl) || '#defects'
}

function normalizeWorkspaceUrl(url?: string | null) {
  if (!url) {
    return ''
  }

  const maintenanceMatch = url.match(/\/maintenance\/([0-9a-f-]+)/i)
  if (maintenanceMatch) {
    return `#maintenance/${maintenanceMatch[1]}`
  }

  const vehicleMatch = url.match(/\/vehicle\/([0-9a-f-]+)/i)
  if (vehicleMatch) {
    return `#vehicles/${vehicleMatch[1]}`
  }

  const defectMatch = url.match(/\/defects\/([0-9a-f-]+)/i)
  if (defectMatch) {
    return `#defects/${defectMatch[1]}`
  }

  return url
}

async function loadMaintenanceHistory(page: number) {
  if (!canLoadMaintenanceHistory.value) {
    historyRequest++
    historyResult.value = null
    isHistoryLoading.value = false
    return
  }

  const request = ++historyRequest
  const requestOrganizationId = organizationId.value
  const requestClientId = props.clientId
  const requestedPage = Math.max(1, page)
  historyPage.value = requestedPage
  historyResult.value = null
  isHistoryLoading.value = true
  historyError.value = ''

  try {
    const result = await loadClientMaintenances(requestOrganizationId, requestClientId, {
      page: requestedPage,
      pageSize: historyPageSize,
      searchQuery: historySearch.value.trim() || undefined,
      status: historyStatus.value === 'upcoming' || historyStatus.value === 'completed' ? historyStatus.value : undefined
    })

    if (request !== historyRequest || requestOrganizationId !== organizationId.value || requestClientId !== props.clientId || !canLoadMaintenanceHistory.value) {
      return
    }

    const lastPage = Math.max(1, Math.ceil(result.totalCount / Math.max(1, result.pageSize)))
    if (result.totalCount > 0 && requestedPage > lastPage) {
      void loadMaintenanceHistory(lastPage)
      return
    }

    historyResult.value = result
    historyPage.value = result.page
  } catch (error) {
    if (request === historyRequest && requestOrganizationId === organizationId.value && requestClientId === props.clientId) {
      historyError.value = error instanceof Error ? error.message : t('clients.historyLoadError')
    }
  } finally {
    if (request === historyRequest) {
      isHistoryLoading.value = false
    }
  }
}

function scheduleHistorySearch() {
  historyPage.value = 1
  clearTimeout(historySearchTimer)
  historyRequest++
  historyResult.value = null
  historyError.value = ''
  isHistoryLoading.value = canLoadMaintenanceHistory.value

  if (!canLoadMaintenanceHistory.value) {
    return
  }

  historySearchTimer = setTimeout(() => {
    historySearchTimer = undefined
    void loadMaintenanceHistory(1)
  }, 275)
}

function setHistoryStatus(status: string) {
  historyStatus.value = status
  historyPage.value = 1
  clearTimeout(historySearchTimer)
  historySearchTimer = undefined
  void loadMaintenanceHistory(1)
}

async function loadCard() {
  isCommentOpen.value = false
  commentError.value = ''
  const request = ++latestRequest
  isSaving.value = false
  historyRequest++
  clearTimeout(historySearchTimer)
  historySearchTimer = undefined
  details.value = null
  isEditing.value = false
  isLoading.value = false
  historyResult.value = null
  isHistoryLoading.value = false
  historyError.value = ''
  historyPage.value = 1
  activePanel.value = 'maintenance'
  historySearch.value = ''
  historyStatus.value = ''

  if (isCreateMode.value) {
    isEditing.value = canWrite.value
    errorMessage.value = ''
    resetForm()
    return
  }

  isLoading.value = true
  errorMessage.value = ''
  const requestOrganizationId = organizationId.value
  const requestClientId = props.clientId

  try {
    const data = await loadClientDetails(requestOrganizationId, requestClientId)
    if (request !== latestRequest || requestOrganizationId !== organizationId.value || requestClientId !== props.clientId) {
      return
    }

    details.value = data
    applyFormFromDetails()
    if (canLoadMaintenanceHistory.value) {
      await loadMaintenanceHistory(1)
    }
  } catch (error) {
    if (request === latestRequest) {
      errorMessage.value = error instanceof Error ? error.message : t('clients.cardLoadError')
    }
  } finally {
    if (request === latestRequest) {
      isLoading.value = false
    }
  }
}

function resetForm() {
  tagDraft.value = ''
  isFormSubmitted.value = false
  phoneValidity.value = null
  form.value = {
    name: '',
    lastName: '',
    firstName: '',
    middleName: '',
    companyTypeCode: 'legal_entity',
    phone: '',
    partyTypeCode: 'person',
    inn: '',
    kpp: '',
    legalAddress: '',
    statusCode: 'active',
    responsibleUserId: '',
    tagNamesText: '',
    comment: ''
  }
}

function applyFormFromDetails() {
  tagDraft.value = ''
  if (!details.value?.client) {
    resetForm()
    return
  }

  const current = details.value.client
  form.value = {
    name: current.name,
    lastName: current.lastName ?? '',
    firstName: current.firstName ?? '',
    middleName: current.middleName ?? '',
    companyTypeCode: current.companyTypeCode ?? (current.partyTypeCode === 'company' ? '' : 'legal_entity'),
    phone: current.phone ?? '',
    partyTypeCode: current.partyTypeCode ?? 'person',
    inn: current.inn ?? '',
    kpp: current.kpp ?? '',
    legalAddress: current.legalAddress ?? '',
    statusCode: current.statusCode || 'active',
    responsibleUserId: current.responsibleUserId ?? '',
    tagNamesText: (current.tags ?? []).map(tag => tag.name).join(', '),
    comment: current.comment ?? ''
  }
}

function startEdit() {
  if (isReadOnly.value) {
    errorMessage.value = readOnlyMessage.value
    return
  }

  applyFormFromDetails()
  isFormSubmitted.value = false
  historyRequest++
  clearTimeout(historySearchTimer)
  historySearchTimer = undefined
  isHistoryLoading.value = false
  errorMessage.value = ''
  isEditing.value = true
}

function cancelEdit() {
  errorMessage.value = ''
  if (isCreateMode.value) {
    window.location.hash = '#clients'
    return
  }

  isEditing.value = false
  applyFormFromDetails()
  if (canLoadMaintenanceHistory.value && !historyResult.value) {
    void loadMaintenanceHistory(historyPage.value)
  }
}

function openComment() {
  if (!canWrite.value || !client.value) return
  commentDraft.value = client.value.comment ?? ''
  commentError.value = ''
  isCommentOpen.value = true
}

function closeComment() {
  if (isCommentSaving.value) return
  isCommentOpen.value = false
  commentError.value = ''
}

async function saveComment() {
  if (!canWrite.value || !client.value || isCommentSaving.value) return
  if (commentDraft.value.length > 4000) {
    commentError.value = commentLimitMessage.value
    return
  }

  const requestOrganizationId = organizationId.value
  const requestClientId = props.clientId
  isCommentSaving.value = true
  commentError.value = ''
  try {
    const result = await updateClientComment(requestOrganizationId, requestClientId, commentDraft.value.trim() || null)
    if (requestOrganizationId !== organizationId.value || requestClientId !== props.clientId) return
    details.value = result
    applyFormFromDetails()
    isCommentOpen.value = false
    emit('changed')
  } catch (error) {
    if (requestOrganizationId === organizationId.value && requestClientId === props.clientId) {
      commentError.value = error instanceof Error ? error.message : t('clients.commentSaveError')
    }
  } finally {
    isCommentSaving.value = false
  }
}

function changeHistoryPage(page: number) {
  void loadMaintenanceHistory(page)
}

async function focusInvalidField() {
  await nextTick()
  const field = clientFormElement.value?.querySelector<HTMLElement>(
    '[aria-invalid="true"], [data-invalid="true"] input, [data-invalid="true"] textarea'
  )
  field?.focus({ preventScroll: true })
  field?.scrollIntoView({ block: 'center', behavior: 'smooth' })
}

async function saveClient() {
  if (isSaving.value) return
  if (isReadOnly.value) {
    errorMessage.value = readOnlyMessage.value
    return
  }

  isFormSubmitted.value = true
  errorMessage.value = ''
  if ((form.value.phone && phoneValidity.value === false) || Object.keys(identityErrors.value).length) {
    await focusInvalidField()
    return
  }

  if (!form.value.partyTypeCode) {
    errorMessage.value = t('clients.clientTypeRequired')
    return
  }

  if (form.value.comment.length > 4000) {
    errorMessage.value = commentLimitMessage.value
    await focusInvalidField()
    return
  }

  commitTagDraft()
  const request = latestRequest
  const requestOrganizationId = organizationId.value
  const requestClientId = props.clientId
  isSaving.value = true
  errorMessage.value = ''

  try {
    const payload = {
      name: clientDisplayName(form.value, hasLegacyName.value ? client.value?.name : ''),
      lastName: form.value.partyTypeCode === 'person' ? form.value.lastName.trim() || null : null,
      firstName: form.value.partyTypeCode === 'person' ? form.value.firstName.trim() || null : null,
      middleName: form.value.partyTypeCode === 'person' ? form.value.middleName.trim() || null : null,
      companyTypeCode: form.value.partyTypeCode === 'company' ? form.value.companyTypeCode || null : null,
      phone: form.value.phone || null,
      partyTypeCode: form.value.partyTypeCode || null,
      inn: form.value.partyTypeCode === 'company' ? (form.value.companyTypeCode ? normalizeTaxCode(form.value.inn) : form.value.inn.trim()) || null : null,
      kpp: form.value.partyTypeCode === 'company' && form.value.companyTypeCode !== 'entrepreneur' ? (form.value.companyTypeCode ? normalizeTaxCode(form.value.kpp) : form.value.kpp.trim()) || null : null,
      legalAddress: form.value.partyTypeCode === 'company' ? form.value.legalAddress || null : null,
      statusCode: form.value.statusCode || 'active',
      responsibleUserId: form.value.responsibleUserId || null,
      tagNamesText: form.value.tagNamesText || null,
      comment: form.value.comment || null
    }

    const result = isCreateMode.value
      ? await createClient(requestOrganizationId, payload)
      : await updateClient(requestOrganizationId, requestClientId, payload)
    if (request !== latestRequest || requestOrganizationId !== organizationId.value || requestClientId !== props.clientId) return
    details.value = result
    emit('changed')

    isEditing.value = false
    applyFormFromDetails()
    if (canLoadMaintenanceHistory.value && !historyResult.value) {
      void loadMaintenanceHistory(historyPage.value)
    }

    if (isCreateMode.value && details.value?.client.id) {
      trackWorkspaceGoal('client_created', { scope: 'organization' })
      window.location.hash = `#clients/${details.value.client.id}`
    }
  } catch (error) {
    if (request === latestRequest && requestOrganizationId === organizationId.value && requestClientId === props.clientId) {
      errorMessage.value = error instanceof Error ? error.message : t('clients.clientSaveError')
    }
  } finally {
    if (request === latestRequest && requestOrganizationId === organizationId.value && requestClientId === props.clientId) {
      isSaving.value = false
    }
  }
}

onMounted(loadCard)
watch(() => [props.clientId, organizationId.value], loadCard)
watch(historySearch, scheduleHistorySearch)
onBeforeUnmount(() => {
  latestRequest++
  historyRequest++
  clearTimeout(historySearchTimer)
  clearTimeout(copyMessageTimer)
})
</script>

<template>
  <UiCard page :title="isCreateMode ? t('clients.newClient') : (isEditing ? t('clients.editClient') : (client?.name || t('clients.cardTitle')))" class="client-profile">
    <template #actions>
      <UiButton
        v-if="client && !isEditing && !isLoading"
        :disabled="isReadOnly && !context.organization.canManageBilling"
        variant="secondary"
        :href="canWrite ? undefined : billingHref"
        @click="canWrite ? startEdit() : undefined"
      >{{ t('clients.edit') }}</UiButton>
      <UiButton v-if="client && !isEditing && !isLoading && canCreateMaintenance && vehicles.length" class="profile-create" @click="createMaintenance">+ {{ isDetailingScenario ? t('clients.newAppointment') : t('clients.newWork') }}</UiButton>
    </template>

    <WorkspaceAccessNotice v-if="isReadOnly" :can-manage-billing="context.organization.canManageBilling === true" :message="readOnlyMessage" :href="billingHref" />
    <UiLoader v-if="isLoading" :text="t('clients.loadingCard')" />
    <div v-else-if="errorMessage && !isEditing" class="profile-empty" role="alert">
      <p>{{ errorMessage }}</p>
      <UiButton v-if="!isCreateMode" variant="secondary" @click="loadCard">{{ t('clients.retry') }}</UiButton>
    </div>

    <form v-else-if="isEditing" ref="clientFormElement" class="profile-form" @submit.prevent="saveClient">
      <div v-if="errorMessage && errorMessage !== commentLimitMessage" class="profile-form__error" role="alert">{{ errorMessage }}</div>
      <div class="profile-form__layout">
        <div class="profile-form__main">
          <section class="profile-form__panel" :aria-label="t('clients.contactsAndDetails')">
            <h3 class="ui-section-header">{{ t('clients.contactsAndDetails') }}</h3>
            <div class="profile-form__type" role="group" :aria-label="t('clients.clientType')">
              <button v-for="option in partyTypeOptions" :key="option.value" type="button" :aria-pressed="form.partyTypeCode === option.value" :disabled="isSaving || isReadOnly" @click="form.partyTypeCode = option.value">{{ option.label }}</button>
            </div>
            <template v-if="form.partyTypeCode === 'person'">
              <p v-if="hasLegacyName" class="profile-form__legacy">{{ t('clients.client') }}: <strong>{{ client?.name }}</strong></p>
              <div class="profile-form__person-name" :data-invalid="!!identityErrors.fullName">
                <UiInput v-model="form.lastName" :label="t('clients.lastName')" :label-help="t('clients.maxCharacters', { count: 60 })" :hint="lengthHint(form.lastName, 60)" :error="identityErrors.lastName" :disabled="isSaving || isReadOnly" />
                <UiInput v-model="form.firstName" :label="t('clients.firstName')" :label-help="t('clients.maxCharacters', { count: 60 })" :hint="lengthHint(form.firstName, 60)" :error="identityErrors.firstName" :required="!hasLegacyName" :disabled="isSaving || isReadOnly" />
                <UiInput v-model="form.middleName" :label="t('clients.middleName')" :label-help="t('clients.maxCharacters', { count: 60 })" :hint="lengthHint(form.middleName, 60)" :error="identityErrors.middleName" :disabled="isSaving || isReadOnly" />
              </div>
              <p v-if="identityErrors.fullName" class="ui-field__error" role="alert">{{ identityErrors.fullName }}</p>
            </template>
            <template v-else>
              <UiSelect v-model="form.companyTypeCode" :label="t('clients.organizationType')" :options="companyTypeOptions" :searchable="false" :disabled="isSaving || isReadOnly" />
              <UiInput v-model="form.name" :label="form.companyTypeCode === 'entrepreneur' ? t('clients.businessName') : t('clients.organizationName')" :error="identityErrors.name" required :disabled="isSaving || isReadOnly" />
            </template>
            <div :data-invalid="!!identityErrors.phone || !!(form.phone && phoneValidity === false)">
              <UiPhoneInput v-model="form.phone" :label="t('clients.phone')" :disabled="isSaving || isReadOnly" @validity-change="phoneValidity = $event" />
              <p v-if="identityErrors.phone || (form.phone && phoneValidity === false)" class="ui-field__error" role="alert">{{ identityErrors.phone || t('clients.invalidPhone') }}</p>
            </div>
            <div v-if="form.partyTypeCode === 'company'" class="profile-form__requisites">
              <h4>{{ t('clients.companyDetails') }}</h4>
              <div class="profile-form__requisites-row">
                <UiInput v-model="form.inn" :label="t('clients.inn')" inputmode="numeric" :mask="clientInnMask(form.companyTypeCode)" :placeholder="!form.companyTypeCode ? t('clients.taxId10Or12') : form.companyTypeCode === 'entrepreneur' ? t('clients.digits12') : t('clients.digits10')" :label-help="!form.companyTypeCode ? t('clients.taxIdHelp') : form.companyTypeCode === 'entrepreneur' ? t('clients.taxIdEntrepreneurHelp') : t('clients.taxIdLegalHelp')" :error="identityErrors.inn" :disabled="isSaving || isReadOnly" />
                <UiInput v-if="form.companyTypeCode !== 'entrepreneur'" v-model="form.kpp" :label="t('clients.kpp')" :placeholder="t('clients.characters9')" :label-help="t('clients.registrationCodeHelp')" :mask="form.companyTypeCode ? clientKppMask : undefined" :error="identityErrors.kpp" :disabled="isSaving || isReadOnly" />
              </div>
              <UiInput v-model="form.legalAddress" :label="t('clients.legalAddress')" :label-help="t('clients.maxCharacters', { count: 500 })" :hint="lengthHint(form.legalAddress, 500)" :error="identityErrors.legalAddress" :disabled="isSaving || isReadOnly" />
            </div>
          </section>
          <section class="profile-form__panel" :aria-label="t('clients.comment')" :data-invalid="form.comment.length > 4000">
            <h3 :id="`${tagInputId}-comment-label`" class="ui-section-header">{{ t('clients.comment') }}</h3>
            <UiTextarea v-model="form.comment" :labelled-by="`${tagInputId}-comment-label`" :described-by="form.comment.length > 4000 ? `${tagInputId}-comment-error` : undefined" :rows="3" :disabled="isSaving || isReadOnly" />
            <p v-if="form.comment.length > 4000" :id="`${tagInputId}-comment-error`" class="ui-field__error" role="alert">{{ commentLimitMessage }}</p>
          </section>
        </div>
        <section class="profile-form__panel" :aria-label="t('clients.clientManagement')">
          <h3 class="ui-section-header">{{ t('clients.clientManagement') }}</h3>
          <div class="profile-form__status" :data-status="form.statusCode">
            <UiSelect v-model="form.statusCode" :label="t('clients.status')" :options="statusOptions" :searchable="false" :disabled="isSaving || isReadOnly" />
          </div>
          <UiSelect v-model="form.responsibleUserId" :label="t('clients.responsible')" :options="responsibleOptions" :disabled="isSaving || isReadOnly" />
          <div class="profile-form__tag-field">
            <label :for="tagInputId">{{ t('clients.tags') }}</label>
            <div class="profile-form__tag-editor">
              <span v-for="(tag, index) in formTags" :key="index" class="profile-form__tag">
                {{ tag }}<button type="button" :aria-label="t('clients.removeTag', { tag })" :disabled="isSaving || isReadOnly" @click="removeTag(index)">×</button>
              </span>
              <input :id="tagInputId" v-model="tagDraft" :placeholder="t('clients.addTag')" :disabled="isSaving || isReadOnly" :aria-describedby="`${tagInputId}-help`" @keydown="onTagKeydown" @blur="commitTagDraft" />
            </div>
            <p :id="`${tagInputId}-help`">{{ t('clients.tagHelp') }}</p>
          </div>
        </section>
      </div>
      <div class="profile-form__actions">
        <UiButton type="submit" :disabled="isSaving || isReadOnly">{{ isSaving ? t('clients.saving') : (isCreateMode ? t('clients.createClient') : t('clients.saveChanges')) }}</UiButton>
        <UiButton variant="secondary" type="button" :disabled="isSaving" @click="cancelEdit">{{ t('clients.cancel') }}</UiButton>
      </div>
    </form>

    <div v-else-if="client">
      <div class="profile-summary" :aria-label="t('clients.summary')">
        <div><span>{{ isDetailingScenario ? t('clients.appointments') : t('clients.workOrders') }}</span><strong>{{ client.maintenanceCount }}</strong></div>
        <div v-if="details?.totalPaidAmount != null"><span>{{ t('clients.paid') }}</span><strong>{{ formatMoney(details.totalPaidAmount) }}</strong></div>
        <div v-if="details?.totalDebtAmount != null"><span>{{ t('clients.debt') }}</span><strong>{{ formatMoney(details.totalDebtAmount) }}</strong></div>
        <template v-for="total in details?.otherCurrencyTotals ?? []" :key="total.currencyCode">
          <div><span>{{ t('clients.paid') }} · {{ total.currencyCode }}</span><strong>{{ formatMoney(total.totalPaidAmount, total.currencyCode) }}</strong></div>
          <div><span>{{ t('clients.debt') }} · {{ total.currencyCode }}</span><strong>{{ formatMoney(total.totalDebtAmount, total.currencyCode) }}</strong></div>
        </template>
        <div><span>{{ t('clients.lastWork') }}</span><strong>{{ formatDate(client.lastMaintenanceDate, t('clients.never')) }}</strong></div>
      </div>
      <div class="profile-layout">
      <aside class="profile-sidebar" :aria-label="t('clients.clientInformation')">
        <section class="profile-panel">
        <div class="profile-contact">
          <h3 class="ui-section-header">{{ t('clients.contacts') }}</h3>
          <div class="profile-contact-row">
            <span>{{ t('clients.phone') }}</span>
            <a v-if="client.phone" class="profile-phone" :href="`tel:${client.phone.replace(/[^+\d]/g, '')}`">{{ client.phone }}</a>
            <span v-else class="profile-muted">{{ t('clients.notSpecified') }}</span>
            <button v-if="client.phone" type="button" :aria-label="t('clients.copyPhone')" @click="copyPhone">⧉</button>
            <span v-if="copyMessage" class="profile-copy-message ui-tooltip" role="status">{{ copyMessage }}</span>
          </div>
        </div>
        <dl class="profile-facts">
          <div><dt>{{ t('clients.responsible') }}</dt><dd>{{ client.responsibleDisplayName || t('clients.unassigned') }}</dd></div>
          <div><dt>{{ t('clients.clientSince') }}</dt><dd>{{ formatEventDate(client.createdAt) }}</dd></div>
          <div v-if="client.partyTypeCode === 'company' && client.inn"><dt>{{ t('clients.inn') }}</dt><dd>{{ client.inn }}</dd></div>
          <div v-if="client.partyTypeCode === 'company' && client.kpp"><dt>{{ t('clients.kpp') }}</dt><dd>{{ client.kpp }}</dd></div>
          <div v-if="client.partyTypeCode === 'company' && client.legalAddress"><dt>{{ t('clients.legalAddress') }}</dt><dd>{{ client.legalAddress }}</dd></div>
          <div v-if="client.isArchived"><dt>{{ t('clients.archivedSince') }}</dt><dd>{{ formatEventDate(client.archivedAt) }}</dd></div>
        </dl>
        <div class="profile-tags"><span class="profile-status">{{ client.isArchived ? t('clients.archive') : (client.statusName || t('clients.active')) }}</span><span v-for="tag in client.tags" :key="tag.id">{{ tag.name }}</span></div>
        </section>
        <section class="profile-note profile-panel"><h3 class="ui-section-header">{{ t('clients.comment') }}</h3><p v-if="client.comment" class="profile-note__entry">{{ client.comment }}</p><p v-else class="profile-note__empty">{{ t('clients.commentEmpty') }}</p><UiButton v-if="canWrite" variant="ghost" @click="openComment">{{ client.comment ? t('clients.editComment') : t('clients.addComment') }}</UiButton></section>
        <section v-if="upcoming && canOpenMaintenance" class="profile-note profile-upcoming profile-panel">
          <h3 class="ui-section-header">{{ t('clients.nextAppointment') }}</h3>
          <div class="profile-upcoming__heading"><strong>{{ formatDate(getMaintenanceDate(upcoming)) }} <span v-if="upcoming.plannedStartTime">· {{ formatTime(upcoming.plannedStartTime) }}</span></strong><span class="profile-work-status" :data-status="upcoming.status">{{ getStatusName(upcoming) }}</span></div>
          <p>{{ upcoming.vehicleDisplayName }}</p>
          <p>{{ upcoming.serviceNames?.join(', ') || getMaintenanceTypeName(upcoming.type) }}</p>
          <a :href="getMaintenanceUrl(upcoming)">{{ t('clients.openAppointment') }} →</a>
        </section>
        <ClientBotConnection :time-zone-id="context.organization.timeZoneId"
          :is-locked="context.shell.scenario?.modules?.messaging === false"
          v-if="!client.isArchived"
          :key="organizationId + ':' + client.id"
          :organization-id="organizationId"
          :client-id="client.id"
          :client-name="client.name"
          :can-write="canManageBotConnections"
        />
      </aside>

      <div class="profile-content">
        <ClientVehicles
          ref="vehiclePicker"
          :vehicles="vehicles"
          :client-name="client.name"
          :title="vehiclesTitle"
          :can-open="canOpenVehicles"
          :can-create="canCreateMaintenance"
          :can-add="canCreateVehicle"
          :create-label="createLabel"
          @add="openCreateVehicleForClient"
        />

        <div class="profile-tabs ui-section-header" role="group" :aria-label="t('clients.clientCardSections')">
          <button v-if="canOpenMessages" type="button" :aria-pressed="activePanel === 'messages'" @click="activePanel = 'messages'">{{ t('clients.conversations') }}</button>
          <button v-if="canOpenMaintenance" type="button" :aria-pressed="activePanel === 'maintenance'" @click="activePanel = 'maintenance'">{{ isDetailingScenario ? t('clients.visitHistory') : maintenanceTitle }} <span>{{ client.maintenanceCount }}</span></button>
          <button v-if="!isDetailingScenario && canOpenDefects && details?.defects.length" type="button" :aria-pressed="activePanel === 'defects'" @click="activePanel = 'defects'">{{ defectsTitle }} <span>{{ details.defects.length }}</span></button>
        </div>

        <section v-if="activePanel === 'maintenance' && canOpenMaintenance" :aria-label="maintenanceTitle">
          <div class="profile-history-tools">
            <div class="profile-history-filters" role="group" :aria-label="t('clients.workFilter')">
              <button v-for="filter in [{ value: '', label: t('clients.all') }, { value: 'upcoming', label: t('clients.upcomingAndCurrent') }, { value: 'completed', label: t('clients.completed') }]" :key="filter.value" type="button" :aria-pressed="historyStatus === filter.value" @click="setHistoryStatus(filter.value)">{{ filter.label }}</button>
            </div>
            <UiInput v-model="historySearch" type="search" :label="t('clients.searchWorkHistory')" :placeholder="t('clients.vehicleOrWork')" />
          </div>
          <div v-if="isHistoryLoading && !historyResult" class="profile-empty"><UiLoader :text="t('clients.loadingWorkHistory')" /></div>
          <div v-else-if="historyError" class="profile-empty" role="alert"><p>{{ historyError }}</p><UiButton variant="secondary" @click="loadMaintenanceHistory(historyPage)">{{ t('clients.retry') }}</UiButton></div>
          <div v-else-if="!historyResult?.totalCount" class="profile-empty" role="status"><p>{{ hasHistoryFilters ? t('clients.noWorkMatches') : t('clients.emptyWorkHistory') }}</p></div>
          <div v-else class="profile-history profile-history--maintenance" :aria-busy="isHistoryLoading">
            <div class="profile-history__columns" aria-hidden="true"><span>{{ t('clients.dateVehicle') }}</span><span>{{ t('clients.work') }}</span><span>{{ t('clients.status') }}</span><span>{{ t('clients.amount') }}</span><span></span></div>
            <article v-for="item in historyResult.items" :key="item.id" class="profile-history__item profile-history__item--maintenance">
              <div class="profile-history__date"><strong>{{ formatDate(getMaintenanceDate(item)) }} <span v-if="getMaintenanceTime(item)">· {{ getMaintenanceTime(item) }}</span></strong><span>{{ item.vehicleDisplayName }}</span></div>
              <div class="profile-history__description"><span>{{ item.serviceNames?.join(', ') || getMaintenanceTypeName(item.type) }}</span><p v-if="item.comment">{{ item.comment }}</p></div>
              <span class="profile-work-status" :data-status="item.status">{{ getStatusName(item) }}</span>
              <strong class="profile-history__amount">{{ formatMoney(item.netAmount ?? item.grossAmount, item.currencyCode) }}</strong>
              <UiButton variant="secondary" :href="getMaintenanceUrl(item)">{{ t('clients.open') }}</UiButton>
            </article>
          </div>
          <div v-if="historyResult?.totalCount" class="profile-history-pagination">
            <p class="profile-history-caption">{{ historyPaginationInfo }} {{ t('clients.work') }}</p>
            <UiPagination :model-value="historyPage" :total-items="historyResult.totalCount" :items-per-page="historyPageSize" :disabled="isHistoryLoading" :label="t('clients.workHistoryPages')" @update:model-value="changeHistoryPage" />
          </div>
          <RepeatVisitRecommendations
            v-if="client?.id && !isEditing && canOpenVehicles && canOpenMaintenance
              && ['detailing', 'service-station', 'autoservice'].includes(context.organization.primaryScenarioCode || context.shell.scenario?.code || '')"
            :key="`${organizationId}:${client.id}`"
            :organization-id="organizationId"
            :client-id="client.id"
            :can-edit="canWrite && details?.canEdit === true"
          />
        </section>

        <ClientConversationPanel v-else-if="activePanel === 'messages' && canOpenMessages" :key="organizationId + ':' + client.id" :context="context" :client-id="client.id" />

        <section v-else-if="activePanel === 'defects'" :aria-label="defectsTitle">
          <div class="profile-section-heading ui-section-header"><h3>{{ defectsTitle }}</h3></div>
          <div class="profile-history">
            <a v-for="item in details?.defects" :key="item.id" class="profile-history__item" :href="getDefectUrl(item)">
              <div class="profile-history__date"><strong>{{ formatEventDate(item.createdAt) }}</strong><span>{{ item.severityName }}</span></div>
              <div class="profile-history__description"><strong>{{ item.title }}</strong><span>{{ item.vehicleDisplayName }}</span></div>
              <span class="profile-work-status">{{ item.statusName }}</span><span class="profile-history__arrow" aria-hidden="true">↗</span>
            </a>
          </div>
        </section>
      </div>
    </div>
    </div>
  </UiCard>
  <SchedulerBookingDialog :open="isCommentOpen" :busy="isCommentSaving" :dismiss-on-backdrop="false" :title="t('clients.comment')" @close="closeComment">
    <form class="client-comment-dialog" @submit.prevent="saveComment">
      <UiTextarea v-model="commentDraft" labelled-by="scheduler-booking-title" :described-by="commentDraft.length > 4000 || commentError ? `${tagInputId}-modal-comment-error` : `${tagInputId}-modal-comment-hint`" :rows="7" :disabled="isCommentSaving" />
      <p v-if="commentDraft.length > 4000 || commentError" :id="`${tagInputId}-modal-comment-error`" class="ui-field__error" role="alert">{{ commentDraft.length > 4000 ? commentLimitMessage : commentError }}</p>
      <p v-else :id="`${tagInputId}-modal-comment-hint`" class="client-comment-dialog__hint">{{ lengthHint(commentDraft, 4000) }}</p>
      <footer class="client-comment-dialog__actions">
        <UiButton type="submit" :disabled="isCommentSaving || !canWrite || commentDraft.length > 4000">{{ isCommentSaving ? t('clients.saving') : t('clients.saveComment') }}</UiButton>
        <UiButton type="button" variant="secondary" :disabled="isCommentSaving" @click="closeComment">{{ t('clients.cancel') }}</UiButton>
      </footer>
    </form>
  </SchedulerBookingDialog>
</template>

<style scoped>
.profile-form__requisites-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 200px), 1fr)); gap: 12px; }
.profile-form__person-name { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
.profile-form__legacy { margin: 0; font-size: 12px; color: var(--crm-text-muted); }
:global(.scheduler-booking-dialog:has(.client-comment-dialog)) { width: min(620px, calc(100vw - 32px)); height: fit-content; max-height: calc(100dvh - 32px); border: 1px solid var(--crm-line); border-radius: 12px; }
.client-comment-dialog { display: grid; gap: 10px; padding: 20px 24px; }
.client-comment-dialog :deep(textarea) { font: inherit; font-size: 14px; line-height: 1.5; }
.client-comment-dialog__hint { margin: 0; text-align: right; color: var(--crm-text-muted); font-size: 12px; }
.client-comment-dialog__actions { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 6px; padding-top: 14px; border-top: 1px solid var(--crm-line); }
@media (max-width: 600px) { .profile-form__person-name { grid-template-columns: 1fr; } .client-comment-dialog { padding: 16px; } }

.client-profile {
  min-width: 0;
}

.profile-summary { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 140px), 1fr)); margin-bottom: 24px; padding: 16px 0; border: 1px solid var(--crm-line, #e6e9ee); border-radius: 8px; background: var(--crm-surface-soft, #f8f9fb); }
.profile-summary > div { display: grid; gap: 6px; padding: 0 20px; border-right: 1px solid var(--crm-line, #e6e9ee); }
.profile-summary > div:last-child { border: 0; }
.profile-summary span { color: var(--crm-text-muted, #667085); font-size: 12px; }
.profile-summary strong { font-size: 18px; overflow-wrap: anywhere; }
.profile-contact h3 { margin: 0; font-size: 16px; }
.profile-note :deep(.ui-button) { margin-top: 4px; min-height: 30px; padding: 5px 9px; font-size: 12px; }
.profile-upcoming { display: grid; gap: 10px; }
.profile-upcoming h3 { margin-bottom: 0; }
.profile-upcoming > strong { font-size: 14px; }
.profile-upcoming a { font-size: 13px; color: var(--crm-text, #202833); }
.profile-history-tools { display: flex; justify-content: space-between; flex-wrap: wrap; align-items: center; gap: 10px; padding: 10px 0 12px; }
.profile-history-tools :deep(.ui-field) { width: min(100%, 290px); }
.profile-history-tools :deep(.ui-field-label) { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0, 0, 0, 0); clip-path: inset(50%); white-space: nowrap; }
.profile-history-filters { display: flex; flex-wrap: wrap; gap: 6px; }
.profile-history-filters button { padding: 7px 9px; border: 1px solid transparent; border-radius: 6px; background: transparent; color: var(--crm-text-muted, #667085); font: inherit; font-size: 12px; cursor: pointer; }
.profile-history-filters button[aria-pressed="true"] { color: var(--crm-text, #202833); background: var(--crm-surface-soft, #f1f3f6); border-color: var(--crm-line, #e6e9ee); }
.profile-content > .profile-tabs { margin: 20px 0 0; }
.profile-history__columns, .profile-history .profile-history__item--maintenance { grid-template-columns: minmax(150px, 1fr) minmax(140px, 1fr) minmax(110px, auto) 85px minmax(80px, auto); }
.profile-history__columns { display: grid; gap: 16px; padding: 12px 13px; border-radius: 6px; color: var(--crm-text-muted, #667085); background: var(--crm-surface-soft, #f1f3f6); font-size: 12px; }
.profile-history__item--maintenance :deep(.ui-button) { justify-self: end; min-height: 32px; padding: 6px 10px; font-size: 12px; white-space: nowrap; }
.profile-history__amount { text-align: right; white-space: nowrap; font-size: 13px; }
.profile-history-pagination { display: flex; justify-content: space-between; align-items: center; gap: 12px; padding-top: 14px; }
.profile-history-caption { margin: 0; color: var(--crm-text-muted, #667085); font-size: 12px; line-height: 1.5; }
.client-profile :deep(.profile-create) { background: #e87532; border-color: #e87532; color: #fff; }

.profile-layout {
  display: grid;
  grid-template-columns: minmax(250px, 310px) minmax(0, 1fr);
  gap: 28px;
  align-items: start;
}

.profile-sidebar {
  min-width: 0;
  display: grid;
  gap: 16px;
}

.profile-panel { min-width: 0; padding: 16px; border: 1px solid var(--crm-line, #e6e9ee); border-radius: 8px; background: var(--crm-surface, #fff); }
.profile-panel h3 { display: flex; align-items: center; min-height: 54px; margin: -16px -16px 14px; padding: 14px 16px; border-bottom: 1px solid var(--crm-section-header-border, #e6e9ee); font-size: 14px; }
.profile-panel.profile-note h3 { margin: -16px -16px 4px; padding: 14px 16px; font-size: 14px; }
.profile-contact h3 { justify-self: stretch; }
.profile-panel .profile-contact { gap: 10px; padding-bottom: 12px; }
.profile-panel .profile-contact h3 { margin-bottom: 0; }
.profile-panel.profile-note :deep(.ui-button--ghost) { color: #e87532; padding-inline: 0; }
.profile-contact-row { position: relative; display: flex; align-items: center; gap: 10px; width: 100%; font-size: 12px; color: var(--crm-text-muted, #667085); }
.profile-copy-message { position: absolute; right: 0; bottom: calc(100% + 8px); width: max-content; max-width: 100%; }
.profile-contact-row a { margin-left: auto; }
.profile-contact-row button { flex-shrink: 0; padding: 4px; border: 0; color: var(--crm-text-muted, #667085); background: transparent; font-size: 20px; cursor: pointer; }
.profile-panel .profile-phone { font-size: 13px; font-weight: 500; }
.profile-panel .profile-facts { gap: 10px; margin-bottom: 14px; }
.profile-panel .profile-facts > div { display: flex; justify-content: space-between; gap: 10px; }
.profile-panel .profile-facts dt, .profile-panel .profile-facts dd { font-size: 12px; }
.profile-panel .profile-facts dd { text-align: right; }
.profile-panel.profile-note { margin-top: 0; padding: 16px; border: 1px solid var(--crm-line, #e6e9ee); }
.profile-panel .profile-note__entry { margin: 0; padding: 0; border: 0; color: var(--crm-text, #202833); white-space: pre-wrap; overflow-wrap: anywhere; }
.profile-panel .profile-note__empty { margin: 0 0 10px; color: var(--crm-text-muted, #667085); }
.profile-upcoming__heading { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 8px; font-size: 13px; }
.profile-upcoming a { color: #e87532; text-decoration: none; padding-top: 10px; border-top: 1px solid var(--crm-line, #e6e9ee); }

.profile-contact {
  display: grid;
  justify-items: start;
  gap: 14px;
  padding-bottom: 20px;
}

.profile-status {
  display: inline-flex;
  padding: 4px 9px;
  background: var(--crm-surface-soft, #f1f3f6);
  border: 1px solid var(--crm-line, #e6e9ee);
  border-radius: 5px;
  color: var(--crm-text, #202833);
  font-size: 12px;
}

.profile-phone {
  color: var(--crm-text, #202833);
  font-size: 20px;
  font-weight: 650;
  text-decoration: none;
  overflow-wrap: anywhere;
}

.profile-phone:hover {
  color: #cf591f;
}

.profile-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.profile-tags span {
  max-width: 100%;
  padding: 3px 7px;
  border-radius: 4px;
  color: var(--crm-text-muted, #667085);
  background: var(--crm-surface-soft, #f1f3f6);
  overflow-wrap: anywhere;
  font-size: 12px;
}

.profile-facts {
  margin: 0;
  display: grid;
  gap: 16px;
}

.profile-facts dt {
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
  margin-bottom: 4px;
}

.profile-facts dd {
  margin: 0;
  font-size: 14px;
  color: var(--crm-text, #202833);
  overflow-wrap: anywhere;
}

.profile-note {
  margin-top: 22px;
  padding-top: 18px;
  border-top: 1px solid var(--crm-line, #e6e9ee);
}

.profile-note h3 {
  font-size: 13px;
  margin: 0 0 8px;
}

.profile-note p {
  margin: 0;
  font-size: 13px;
  line-height: 1.65;
  color: var(--crm-text-muted, #667085);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.profile-content {
  min-width: 0;
  container-type: inline-size;
}

.profile-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 16px;
  border-bottom: 1px solid var(--crm-line, #e6e9ee);
}

.profile-tabs button {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 0 2px 13px;
  border: 0;
  border-bottom: 2px solid transparent;
  background: transparent;
  color: var(--crm-text-muted, #667085);
  font: inherit;
  font-size: 14px;
  cursor: pointer;
}

.profile-tabs button[aria-pressed="true"] {
  color: var(--crm-text, #202833);
  border-bottom-color: #e87532;
  font-weight: 650;
}

.profile-tabs button span {
  padding: 2px 6px;
  border-radius: 5px;
  background: var(--crm-surface-soft, #f1f3f6);
  font-size: 11px;
  font-variant-numeric: tabular-nums;
}

.profile-section-heading {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 16px;
  min-height: 70px;
  padding-block: 14px;
}

.profile-section-heading h3 {
  margin: 0;
  font-size: 15px;
}

.profile-section-heading :deep(.ui-button) {
  min-height: 32px;
  padding: 6px 10px;
  font-size: 12px;
}

.profile-history {
  display: grid;
}

.profile-history__item {
  display: grid;
  grid-template-columns: 112px minmax(0, 1fr) auto 16px;
  align-items: start;
  gap: 16px;
  padding: 18px 8px;
  border-bottom: 1px solid var(--crm-line, #e6e9ee);
  text-decoration: none;
  color: var(--crm-text, #202833);
  font-size: 13px;
}

.profile-history--maintenance {
  gap: 8px;
}

.profile-history .profile-history__item--maintenance {
  padding: 14px 12px;
  border: 1px solid var(--crm-line, #dce1e8);
  border-radius: 8px;
  background: var(--crm-surface, #fff);
}

.profile-history__item:hover {
  background: var(--crm-surface-soft, #f8f9fb);
}

.profile-history__date, .profile-history__description {
  display: grid;
  gap: 6px;
  overflow-wrap: anywhere;
}

.profile-history__date > span, .profile-history__description > span {
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
}

.profile-history__description p {
  margin: 2px 0 0;
  max-width: 65ch;
  line-height: 1.5;
  color: var(--crm-text-muted, #667085);
}

.profile-work-status {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
  padding: 4px 7px;
  border: 1px solid var(--crm-line, #dce1e8);
  border-radius: 999px;
  width: fit-content;
}

.profile-work-status::before {
  content: '';
  flex: 0 0 6px;
  height: 6px;
  border-radius: 50%;
  background: #8793a2;
}

.profile-work-status[data-status="2"]::before {
  background: #e87a33;
}

.profile-work-status[data-status="1"] { border-color: #4f83b8; background: #4f83b814; color: var(--crm-text, #202833); }
.profile-work-status[data-status="1"]::before { background: #4f83b8; }

.profile-work-status[data-status="3"]::before {
  background: #309f86;
}

.profile-history__arrow, .profile-muted {
  color: var(--crm-text-muted, #667085);
}

.profile-empty {
  padding: 30px 16px;
  border: 1px dashed var(--crm-line, #dce1e8);
  border-radius: 8px;
  text-align: center;
  color: var(--crm-text-muted, #667085);
  font-size: 14px;
}

.profile-empty p {
  margin: 0 0 12px;
}

.profile-empty p:last-child {
  margin: 0;
}

.profile-form {
  display: grid;
  gap: 20px;
  min-width: 0;
  container-type: inline-size;
}

.profile-form__layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(260px, 360px);
  align-items: start;
  gap: 20px;
}

.profile-form__main, .profile-form__panel, .profile-form__requisites {
  display: grid;
  gap: 16px;
  min-width: 0;
}

.profile-form__panel {
  align-content: start;
  padding: 16px;
  border: 1px solid var(--crm-line, #e6e9ee);
  border-radius: 8px;
  background: var(--crm-surface, #fff);
}

.profile-form__panel h3 {
  margin: -16px -16px 0;
  padding: 14px 16px;
  border-bottom: 1px solid var(--crm-line, #e6e9ee);
  font-size: 15px;
}

.profile-form__requisites {
  padding-top: 16px;
  border-top: 1px solid var(--crm-line, #e6e9ee);
}

.profile-form__requisites h4 {
  margin: 0;
  font-size: 14px;
}

.profile-form__type {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  justify-self: start;
  max-width: 100%;
  padding: 4px;
  border: 1px solid var(--crm-line, #e6e9ee);
  border-radius: 8px;
  background: var(--crm-surface-soft, #f7f8fa);
}

.profile-form__type button {
  padding: 9px 16px;
  border: 0;
  border-radius: 5px;
  font: inherit;
  font-size: 13px;
  font-weight: 600;
  color: var(--crm-text-muted, #667085);
  background: transparent;
  cursor: pointer;
}

.profile-form__type button[aria-pressed="true"] {
  background: #e87532;
  color: #fff;
}

.profile-form button:disabled {
  cursor: default;
  opacity: 0.6;
}

.profile-form__tag-field {
  display: grid;
  gap: 8px;
  min-width: 0;
}

.profile-form__tag-field > label {
  font-size: 14px;
  font-weight: 600;
}

.profile-form__tag-editor {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  padding: 8px;
  border: 1px solid var(--crm-line, #dce1e8);
  border-radius: 6px;
  background: var(--crm-surface, #fff);
}

.profile-form__tag-editor:focus-within {
  outline: 2px solid #e87532;
  outline-offset: 1px;
}

.profile-form__tag {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  max-width: 100%;
  padding: 4px 8px;
  border: 1px solid var(--crm-line, #dce1e8);
  border-radius: 999px;
  background: var(--crm-surface-soft, #f7f8fa);
  font-size: 12px;
  overflow-wrap: anywhere;
}

.profile-form__tag button {
  flex-shrink: 0;
  padding: 0 3px;
  border: 0;
  font: inherit;
  font-size: 17px;
  color: var(--crm-text-muted, #667085);
  background: transparent;
  cursor: pointer;
}

.profile-form__tag-editor input {
  flex: 1 1 120px;
  width: 0;
  min-width: 0;
  max-width: 100%;
  min-height: 26px;
  padding: 0 2px;
  border: 0;
  outline: none;
  box-shadow: none;
  font: inherit;
  font-size: 13px;
  color: var(--crm-text, #202833);
  background: transparent;
}

.profile-form__tag-field p {
  margin: 0;
  font-size: 12px;
  color: var(--crm-text-muted, #667085);
}

.profile-form__status :deep(.multiselect-single-label)::before {
  content: '';
  flex: 0 0 7px;
  height: 7px;
  margin-right: 8px;
  border-radius: 50%;
  background: var(--client-status-color, #8793a2);
}

.profile-form__status[data-status="active"] { --client-status-color: #309f86; }
.profile-form__status[data-status="lead"] { --client-status-color: #4f83b8; }
.profile-form__status[data-status="attention"] { --client-status-color: #e87532; }

.profile-form__actions :deep(.ui-button--primary),
.client-comment-dialog__actions :deep(.ui-button--primary) {
  background: #e87532;
  border-color: #e87532;
  color: #fff;
}

.profile-form :deep(textarea) {
  font: inherit;
  font-size: 14px;
  line-height: 1.5;
}

.profile-form__pair {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.profile-form__pair > *, .profile-form :deep(.ui-field) {
  min-width: 0;
}

.profile-form__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  border-top: 1px solid var(--crm-line, #e6e9ee);
  padding-top: 18px;
}

.profile-form__panel > .ui-field__error { margin: 0; }

.profile-form__error {
  padding: 12px;
  border: 1px solid #cb6a4b;
  border-radius: 6px;
  color: var(--crm-text, #9f331f);
  font-size: 14px;
}

.client-profile button:focus-visible, .client-profile a:focus-visible {
  outline: 2px solid #e87532;
  outline-offset: 3px;
}

@container (max-width: 800px) {
  .profile-form__layout {
    grid-template-columns: minmax(0, 1fr);
  }
}

@container (max-width: 480px) {
  .profile-form__pair {
    grid-template-columns: minmax(0, 1fr);
  }

  .profile-form__type button {
    padding-inline: 12px;
  }
}

@container (max-width: 650px) {
  .profile-history-pagination { flex-direction: column; align-items: flex-start; }
  .profile-history__columns { display: none; }
  .profile-history .profile-history__item--maintenance { grid-template-columns: minmax(0, 1fr) auto; }
  .profile-history__item--maintenance .profile-history__date { grid-column: 1 / -1; display: grid; }
  .profile-history__item--maintenance .profile-history__description { grid-column: 1 / -1; }
  .profile-history__item--maintenance .profile-history__amount { grid-column: 2; }
  .profile-history__item--maintenance :deep(.ui-button) { grid-column: 1 / -1; justify-self: start; }
}

@media (max-width: 1100px) {
  .profile-history-pagination { flex-direction: column; align-items: flex-start; }
  .profile-history__columns { display: none; }
  .profile-history .profile-history__item--maintenance { grid-template-columns: minmax(0, 1fr) auto; }
  .profile-history__item--maintenance .profile-history__date { grid-column: 1 / -1; display: grid; }
  .profile-history__item--maintenance .profile-history__description { grid-column: 1 / -1; }
  .profile-history__amount { grid-column: 2; }
  .profile-history__item--maintenance :deep(.ui-button) { grid-column: 1 / -1; justify-self: start; }
  .profile-layout {
    grid-template-columns: minmax(220px, 260px) minmax(0, 1fr);
    gap: 20px;
  }
  .profile-sidebar {
    padding-right: 0;
  }
  .profile-history__item {
    grid-template-columns: 1fr auto;
    gap: 10px;
  }
  .profile-history__date {
    grid-column: 1 / -1;
    display: flex;
    flex-wrap: wrap;
    gap: 10px;
  }
  .profile-history__arrow {
    display: none;
  }
}

@media (max-width: 760px) {
  .profile-summary > div { padding-inline: 10px; }
  .profile-summary strong { font-size: 14px; }
  .profile-layout {
    grid-template-columns: 1fr;
  }
  .profile-sidebar {
    padding: 0;
  }
  .profile-facts {
    grid-template-columns: minmax(0, 1fr);
  }
  .profile-contact {
    gap: 10px;
  }
}
@media (max-width: 600px) {
  .client-profile :deep(.ui-card__header) {
    flex-direction: column;
    align-items: stretch;
  }

  .client-profile :deep(.ui-card__actions) {
    justify-content: flex-start;
  }
}
</style>

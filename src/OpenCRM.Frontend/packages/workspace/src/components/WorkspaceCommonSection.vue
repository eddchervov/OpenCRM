<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { UiAlertDialog, UiCard, UiConfirmDialog, UiLoader } from '@opencrm/ui'
import WorkspaceWorkSection from './WorkspaceWorkSection.vue'
import WorkspaceShiftsSection from './WorkspaceShiftsSection.vue'
import {
  buildWorkspaceCommonSections,
  type WorkspaceCommonSectionCode
} from '../commonSections'
import {
  createScenarioCatalogItem,
  deleteScenarioCatalogItem,
  loadScenarioCatalog,
  loadWorkPostAffectedMaintenances,
  type WorkPostAffectedMaintenances,
  updateScenarioCatalogItem,
  type WorkspaceScenarioCatalogItem,
  type WorkspaceScenarioCatalogKindCode
} from '../api/scenarioCatalog'
import {
  loadWorkspaceBilling,
  loadWorkspaceMembers,
  loadWorkspaceOverview,
  loadWorkspaceSettings,
  loadWorkspaceStatistics,
  loadWorkspaceWiki,
  loadWorkspaceWikiArticle,
  updateWorkspaceProfile,
  updateWorkspaceRequisites,
  WorkspaceActionError,
  archiveWorkspaceOrganization,
  updateWorkspaceRolePermissions,
  activateWorkspaceDemoPlan,
  purchaseWorkspaceBusinessPlan,
  extendWorkspaceBusinessPlan,
  loadPaymentPurchase,
  type WorkspaceBillingData,
  type WorkspaceMembersData,
  type WorkspaceOverviewData,
  type WorkspaceSettingsData,
  type WorkspaceStatisticsData,
  type WorkspacePlan,
  type WorkspacePaymentBuyer,
  type WorkspaceWikiArticle,
  type WorkspaceWikiData
} from '../api/workspaceAdmin'
import { submitPaymentOperation, type PaymentOperationScope } from '../paymentOperation'
import type { WorkspaceContext } from '../types'
import { trackWorkspaceGoal } from '../analytics'
import WorkspaceBillingSection from './WorkspaceBillingSection.vue'
import WorkspaceCatalogSection from './WorkspaceCatalogSection.vue'
import MessageTemplateCatalog from './MessageTemplateCatalog.vue'
import WorkspaceProLock from './WorkspaceProLock.vue'
import { loadMessageTemplateAccess, type MessageTemplateAccess } from '../api/messageTemplates'
import WorkspaceEmployeesSection from './WorkspaceEmployeesSection.vue'
import WorkspaceMembersSection from './WorkspaceMembersSection.vue'
import WorkspaceOverviewSection from './WorkspaceOverviewSection.vue'
import WorkspacePageHelpButton from './WorkspacePageHelpButton.vue'
import WorkspaceSettingsSection from './WorkspaceSettingsSection.vue'
import OrganizationWorkingHoursSettings from './OrganizationWorkingHoursSettings.vue'
import WorkspaceStatisticsSection from './WorkspaceStatisticsSection.vue'
import WorkspaceWikiSection from './WorkspaceWikiSection.vue'
import WorkspaceImportSection from './WorkspaceImportSection.vue'

const props = defineProps<{
  context: WorkspaceContext
  sectionCode: WorkspaceCommonSectionCode
}>()
const emit = defineEmits<{
  changed: []
}>()

const isLoading = ref(false)
const postActivityDialog = ref<HTMLDialogElement | null>(null)
const selectedWorkPost = ref<WorkspaceScenarioCatalogItem | null>(null)
const affectedMaintenances = ref<WorkPostAffectedMaintenances | null>(null)
const postActivityLoading = ref(false)
const postActivityError = ref('')
const buyerDialog = ref<HTMLDialogElement | null>(null)
const paymentBuyerType = ref<'' | 'individual' | 'business'>('')
const paymentBuyerName = ref('')
const paymentBuyerInn = ref('')
const paymentBuyerError = ref('')
const paymentBuyerStatus = ref('')
let paymentPollController: AbortController | undefined
const paymentIntent = ref<{ kind: 'purchase', plan: WorkspacePlan } | { kind: 'extend' } | null>(null)
const errorMessage = ref('')
const actionErrorMessage = ref('')
const overview = ref<WorkspaceOverviewData | null>(null)
const members = ref<WorkspaceMembersData | null>(null)
const employeeCount = ref<number | null>(null)
const wiki = ref<WorkspaceWikiData | null>(null)
const wikiArticle = ref<WorkspaceWikiArticle | null>(null)
const settings = ref<WorkspaceSettingsData | null>(null)
const billing = ref<WorkspaceBillingData | null>(null)
const statistics = ref<WorkspaceStatisticsData | null>(null)
const wikiSearch = ref('')
const activeWikiSlug = ref('')
const successMessage = ref('')
let successMessageTimer: number | undefined

function clearSuccessMessage() {
  if (successMessageTimer !== undefined) {
    window.clearTimeout(successMessageTimer)
    successMessageTimer = undefined
  }
  successMessage.value = ''
}

function showSuccessMessage(message: string) {
  clearSuccessMessage()
  successMessage.value = message
  successMessageTimer = window.setTimeout(clearSuccessMessage, 5000)
}
const isSaving = ref(false)
const isWikiArticleLoading = ref(false)
const wikiArticleErrorMessage = ref('')
const settingsSection = ref(new URLSearchParams(window.location.hash.split('?')[1] ?? '').get('tab') ?? 'profile')
const isEditingProfile = ref(false)
const isPromoHistory = ref(false)
const isEditingRequisites = ref(false)
const requisitesErrors = ref<Record<string, string[]>>({})
const isEditingPermissions = ref(false)
const selectedCatalogKind = ref<WorkspaceScenarioCatalogKindCode>('service')
const catalogMode = ref<'workspace' | 'templates'>('workspace')
const templateAccess = ref<MessageTemplateAccess | null>(null)
const templatesLocked = computed(() => props.context.shell.scenario?.modules?.messaging === false)
const isEditingCatalog = ref(false)
const isCatalogDialogOpen = ref(false)
const catalogRefreshErrorMessage = ref('')
const confirmDialog = ref({
  open: false,
  title: '',
  message: '',
  confirmText: 'Подтвердить',
  onConfirm: null as null | (() => void)
})
const profileForm = reactive({
  name: '',
  description: '',
  primaryScenarioCode: '',
  documentStyleCode: 'auto',
  maintenanceWorkflowMode: 'staged',
  allowWorkSelfAssignment: false
})
const requisitesForm = reactive({
  legalName: '',
  inn: '',
  kpp: '',
  ogrn: '',
  legalAddress: '',
  actualAddress: '',
  phone: '',
  email: '',
  signatoryPosition: '',
  signatoryName: '',
  signatoryBasis: ''
})
const permissionRoleCode = ref('')
const permissionDrafts = ref<Record<string, string[]>>({})
const catalogDrafts = ref<Record<string, {
  name: string
  description: string
  unitPrice: string
  durationMinutes: string
  serviceIds: string[]
  sortOrder: string
  isActive: boolean
}>>({})
const catalogCreateForms = reactive<Record<string, { name: string, description: string, unitPrice: string, durationMinutes: string, serviceIds: string[] }>>({
  service: { name: '', description: '', unitPrice: '', durationMinutes: '', serviceIds: [] },
  service_bundle: { name: '', description: '', unitPrice: '', durationMinutes: '', serviceIds: [] },
  work_post: { name: '', description: '', unitPrice: '', durationMinutes: '', serviceIds: [] },
  material: { name: '', description: '', unitPrice: '', durationMinutes: '', serviceIds: [] }
})

const section = computed(() =>
  buildWorkspaceCommonSections(props.context).find(item => item.code === props.sectionCode)
)
const organizationName = computed(() =>
  overview.value?.organization.name
  ?? members.value?.organization.name
  ?? settings.value?.organization.name
  ?? statistics.value?.organization.name
  ?? billing.value?.organization.name
  ?? props.context.organization.name
)
const scenarioTitle = computed(() =>
  props.context.shell.scenario?.title || overview.value?.organization.primaryScenarioName || 'Рабочее пространство'
)
const selectedWikiArticle = computed(() => wikiArticle.value ?? wiki.value?.selectedArticle ?? null)
const isDetailingScenario = computed(() =>
  (props.context.organization.primaryScenarioCode ?? props.context.shell.scenario?.code ?? '').toLowerCase() === 'detailing'
)
const settingsTitle = computed(() => isDetailingScenario.value ? 'Настройки детейлинг-центра' : 'Настройки организации')
const canManageCatalog = computed(() => settings.value?.canManageProfile === true)
const catalogGroups = computed<Array<{
  kind: WorkspaceScenarioCatalogKindCode
  title: string
  emptyText: string
  nameHelp: string
  descriptionHelp: string
}>>(() => [
  {
    kind: 'service',
    title: 'Услуги',
    emptyText: 'Услуги еще не настроены.',
    nameHelp: 'Название услуги, которое будет видно в записи обслуживания и расписании. Например: полировка кузова.',
    descriptionHelp: 'Коротко опишите состав работ, длительность или то, что важно администратору.'
  },
  {
    kind: 'service_bundle',
    title: 'Наборы услуг',
    emptyText: 'Наборы ещё не созданы.',
    nameHelp: 'Название набора услуг.',
    descriptionHelp: 'Состав и назначение набора.'
  },
  {
    kind: 'work_post',
    title: 'Посты / боксы',
    emptyText: 'Посты еще не настроены.',
    nameHelp: 'Название поста или бокса, где выполняются работы. Например: Бокс 1.',
    descriptionHelp: 'Укажите назначение зоны: мокрая зона, полировка, выдача или другое рабочее место.'
  },
  {
    kind: 'material',
    title: 'Материалы',
    emptyText: 'Материалы еще не настроены.',
    nameHelp: 'Название материала для учета остатков и движений. Например: керамический состав.',
    descriptionHelp: 'Укажите единицу, зону применения или внутреннее примечание по материалу.'
  }
])
const selectedCatalogGroup = computed(() =>
  catalogGroups.value.find(group => group.kind === selectedCatalogKind.value) ?? catalogGroups.value[0]
)
const roleOptions = computed(() => (members.value?.roleOptions ?? []).map(role => ({
  value: role.code,
  label: role.name
})))

function getCatalogKindFromHash(): WorkspaceScenarioCatalogKindCode | null {
  const query = window.location.hash.split('?')[1] ?? ''
  const kind = new URLSearchParams(query).get('catalog')
  return kind && catalogGroups.value.some(group => group.kind === kind)
    ? kind as WorkspaceScenarioCatalogKindCode
    : null
}

function getWikiSlugFromHash() {
  const query = window.location.hash.split('?')[1] ?? ''
  return new URLSearchParams(query).get('slug')?.trim() ?? ''
}

async function refreshBillingAfterPromo() {
  const organizationId = props.context.organization.id
  actionErrorMessage.value = ''

  try {
    const refreshedBilling = await loadWorkspaceBilling(organizationId)
    if (props.context.organization.id === organizationId) {
      billing.value = refreshedBilling
      emit('changed')
    }
  } catch {
    actionErrorMessage.value = 'Промокод применён, но тарифы не удалось обновить. Обновите страницу.'
  }
}

async function loadSection() {
  isLoading.value = true
  errorMessage.value = ''
  actionErrorMessage.value = ''
  successMessage.value = ''

  try {
    switch (props.sectionCode) {
      case 'organization':
        overview.value = await loadWorkspaceOverview(props.context.organization.id)
        break
      case 'members':
        members.value = await loadWorkspaceMembers(props.context.organization.id)
        permissionDrafts.value = Object.fromEntries(members.value.rolePermissionMatrix.map(role => [
          role.roleCode,
          role.permissions.filter(permission => permission.isEnabled).map(permission => permission.code)
        ]))
        isEditingPermissions.value = false
        break
      case 'statistics':
        statistics.value = await loadWorkspaceStatistics(props.context.organization.id)
        break
      case 'wiki':
        activeWikiSlug.value = getWikiSlugFromHash() || activeWikiSlug.value
        wiki.value = await loadWorkspaceWiki(props.context.organization.id, {
          slug: activeWikiSlug.value,
          searchQuery: wikiSearch.value
        })
        wikiArticle.value = wiki.value.selectedArticle ?? null
        activeWikiSlug.value = selectedWikiArticle.value?.slug ?? activeWikiSlug.value
        wikiArticleErrorMessage.value = ''
        break
      case 'settings':
        settings.value = await loadWorkspaceSettings(props.context.organization.id)
        syncSettingsForm()
        isEditingProfile.value = false
        isEditingRequisites.value = false
        break
      case 'catalog':
        templateAccess.value = await loadMessageTemplateAccess(props.context.organization.id)
        catalogMode.value = !templateAccess.value.canViewWorkspaceCatalog || new URLSearchParams(window.location.hash.split('?')[1] ?? '').get('catalog') === 'message-templates'
          ? 'templates' : 'workspace'
        if (catalogMode.value === 'templates' && templatesLocked.value) break
        if (!templateAccess.value.canViewWorkspaceCatalog) break
        selectedCatalogKind.value = getCatalogKindFromHash() ?? selectedCatalogKind.value
        try {
          settings.value = await loadWorkspaceSettings(props.context.organization.id)
        } catch {
          const [overviewData, scenarioCatalog] = await Promise.all([
            loadWorkspaceOverview(props.context.organization.id),
            loadScenarioCatalog(props.context.organization.id)
          ])
          settings.value = {
            organization: overviewData.organization,
            workspaceFeatures: overviewData.workspaceFeatures,
            canManageProfile: false,
            canManageRequisites: false,
            canArchive: false,
            scenarioCatalog
          }
        }
        syncCatalogDrafts()
        isEditingCatalog.value = false
        break
      case 'billing':
        if (props.context.workspaceAccess?.commercialEnabled !== true) break
        billing.value = await loadWorkspaceBilling(props.context.organization.id)
        break
    }
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось загрузить раздел.'
  } finally {
    isLoading.value = false
  }
}

function syncSettingsForm() {
  const organization = settings.value?.organization
  if (!organization) {
    return
  }

  profileForm.name = organization.name ?? ''
  profileForm.description = organization.description ?? ''
  profileForm.primaryScenarioCode = organization.primaryScenarioCode || props.context.organization.primaryScenarioCode || 'detailing'
  profileForm.documentStyleCode = organization.documentStyleCode || 'auto'
  profileForm.maintenanceWorkflowMode = organization.maintenanceWorkflowMode || 'staged'
  profileForm.allowWorkSelfAssignment = organization.allowWorkSelfAssignment === true
  requisitesForm.legalName = organization.legalName ?? ''
  requisitesForm.inn = organization.inn ?? ''
  requisitesForm.kpp = organization.kpp ?? ''
  requisitesForm.ogrn = organization.ogrn ?? ''
  requisitesForm.legalAddress = organization.legalAddress ?? ''
  requisitesForm.actualAddress = organization.actualAddress ?? ''
  requisitesForm.phone = organization.phone ?? ''
  requisitesForm.email = organization.email ?? ''
  requisitesForm.signatoryPosition = organization.signatoryPosition ?? ''
  requisitesForm.signatoryName = organization.signatoryName ?? ''
  requisitesForm.signatoryBasis = organization.signatoryBasis ?? ''
  syncCatalogDrafts()
}

function syncCatalogDrafts() {
  const items = [
    ...(settings.value?.scenarioCatalog?.services ?? []),
    ...(settings.value?.scenarioCatalog?.serviceBundles ?? []),
    ...(settings.value?.scenarioCatalog?.workPosts ?? []),
    ...(settings.value?.scenarioCatalog?.materials ?? [])
  ]

  catalogDrafts.value = Object.fromEntries(items.map(item => [item.id, {
    name: item.name,
    description: item.description ?? '',
    unitPrice: item.unitPrice == null ? '' : String(item.unitPrice),
    durationMinutes: item.durationMinutes == null ? '' : String(item.durationMinutes),
    serviceIds: [...(item.serviceIds ?? [])],
    sortOrder: String(item.sortOrder || 10),
    isActive: item.isActive
  }]))
}

async function runAction(
  action: () => Promise<{ message?: string | null, paymentUrl?: string | null, code?: string | null, isPaid?: boolean }>,
  options: {
    reloadPageOnSuccess?: boolean
    redirectToOnSuccess?: string
    errorInBuyerDialog?: boolean
    refreshCatalogOnSuccess?: boolean
    onError?: (message: string) => void
    onValidationError?: (errors: Record<string, string[]>) => void
  } = {}
) {
  if (isSaving.value) return false

  isSaving.value = true
  actionErrorMessage.value = ''
  catalogRefreshErrorMessage.value = ''
  successMessage.value = ''

  try {
    const result = await action()
    if (result.paymentUrl) {
      window.location.href = result.paymentUrl
      return
    }

    if (result.code === 'InProgress' || result.code === 'ManualReview') {
      paymentBuyerStatus.value = result.isPaid
        ? 'Оплата получена, активируем подписку.'
        : result.code === 'ManualReview'
          ? 'Оплата требует проверки. Обратитесь в поддержку и не оплачивайте повторно.'
          : result.message || 'Готовим оплату.'
      return false
    }

    if (['Canceled', 'Rejected', 'Refunded', 'Conflict', 'Invalid'].includes(result.code ?? '')) {
      paymentBuyerError.value = result.message || 'Не удалось завершить оплату.'
      return false
    }

    if (options.refreshCatalogOnSuccess && settings.value) {
      try {
        settings.value.scenarioCatalog = await loadScenarioCatalog(props.context.organization.id)
        syncCatalogDrafts()
      } catch {
        catalogRefreshErrorMessage.value = 'Изменения сохранены, но список справочника не удалось обновить. Обновите страницу, чтобы увидеть актуальные данные.'
      }

      isEditingCatalog.value = false

      return true
    }

    successMessage.value = result.message || 'Готово.'
    if (options.redirectToOnSuccess) {
      window.location.href = options.redirectToOnSuccess
      return true
    }

    if (options.reloadPageOnSuccess) {
      window.location.reload()
      return true
    }

    await loadSection()
    return true
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      return false
    }

    const message = error instanceof Error ? error.message : 'Действие не выполнено.'
    if (options.onValidationError && error instanceof WorkspaceActionError && error.errors) {
      options.onValidationError(error.errors)
    } else if (options.onError) {
      options.onError(message)
    } else if (options.errorInBuyerDialog) {
      paymentBuyerError.value = message
    } else {
      actionErrorMessage.value = message
    }
    return false
  } finally {
    isSaving.value = false
  }
}

function askConfirmation(options: {
  title: string
  message: string
  confirmText?: string
  onConfirm: () => void
}) {
  confirmDialog.value = {
    open: true,
    title: options.title,
    message: options.message,
    confirmText: options.confirmText ?? 'Подтвердить',
    onConfirm: options.onConfirm
  }
}

function closeConfirmation() {
  if (!isSaving.value) {
    confirmDialog.value.open = false
  }
}

function confirmAction() {
  const action = confirmDialog.value.onConfirm
  if (!action || isSaving.value) {
    return
  }

  confirmDialog.value.open = false
  action()
}

function saveProfile() {
  if (isSaving.value || !settings.value?.canManageProfile || props.context.workspaceAccess?.isReadOnly !== false) return
  void runAction(() => updateWorkspaceProfile(props.context.organization.id, {
      name: profileForm.name,
      description: profileForm.description || null,
      primaryScenarioCode: profileForm.primaryScenarioCode,
      documentStyleCode: profileForm.documentStyleCode || 'auto',
      maintenanceWorkflowMode: profileForm.maintenanceWorkflowMode || 'staged',
      allowWorkSelfAssignment: profileForm.allowWorkSelfAssignment
    }))
    .then((success) => {
      if (success) {
        isEditingProfile.value = false
        emit('changed')
      }
    })
}

function saveRequisites() {
  if (isSaving.value || !settings.value?.canManageRequisites || props.context.workspaceAccess?.isReadOnly !== false) return
  requisitesErrors.value = {}
  void runAction(() => updateWorkspaceRequisites(props.context.organization.id, {
      legalName: requisitesForm.legalName || null,
      inn: requisitesForm.inn || null,
      kpp: requisitesForm.kpp || null,
      ogrn: requisitesForm.ogrn || null,
      legalAddress: requisitesForm.legalAddress || null,
      actualAddress: requisitesForm.actualAddress || null,
      phone: requisitesForm.phone || null,
      email: requisitesForm.email || null,
      signatoryPosition: requisitesForm.signatoryPosition || null,
      signatoryName: requisitesForm.signatoryName || null,
      signatoryBasis: requisitesForm.signatoryBasis || null
    }), {
      onValidationError: errors => {
        requisitesErrors.value = Object.fromEntries(Object.entries(errors).map(([key, messages]) => [
          key.split('.').at(-1)!.replace(/^./, letter => letter.toLowerCase()), messages
        ]))
      }
    })
    .then((success) => {
      if (success) {
        isEditingRequisites.value = false
      }
    })
}

function cancelProfileEdit() {
  syncSettingsForm()
  isEditingProfile.value = false
}

function cancelRequisitesEdit() {
  requisitesErrors.value = {}
  syncSettingsForm()
  isEditingRequisites.value = false
}

function startPermissionsEdit() {
  if (!members.value?.canManageRoles || props.context.workspaceAccess?.isReadOnly !== false || isSaving.value) return
  permissionDrafts.value = Object.fromEntries(members.value.rolePermissionMatrix.map(role => [
    role.roleCode, role.permissions.filter(permission => permission.isEnabled).map(permission => permission.code)
  ]))
  isEditingPermissions.value = true
}

function selectSettingsSection(section: string) {
  settingsSection.value = section
  const query = new URLSearchParams(window.location.hash.split('?')[1] ?? '')
  query.set('tab', section)
  window.history.replaceState(window.history.state, '', '#settings?' + query.toString())
}

function startProfileEdit() {
  isEditingRequisites.value = false
  isEditingProfile.value = true
}

function startRequisitesEdit() {
  requisitesErrors.value = {}
  isEditingProfile.value = false
  isEditingRequisites.value = true
}

function archiveOrganization() {
  if (isSaving.value || !settings.value?.canArchive) return
  askConfirmation({
    title: 'Архивировать организацию',
    message: 'Организация исчезнет из рабочих разделов. Ее можно будет восстановить из списка организаций в течение срока хранения архива.',
    confirmText: 'Архивировать',
    onConfirm: () => void runAction(
      () => archiveWorkspaceOrganization(props.context.organization.id),
      { redirectToOnSuccess: '/organizations' }
    )
  })
}

function togglePermission(roleCode: string, permissionCode: string, checked: boolean) {
  const current = new Set(permissionDrafts.value[roleCode] ?? [])
  if (checked) {
    current.add(permissionCode)
  } else if (permissionCode !== 'workspace.view') {
    current.delete(permissionCode)
  }
  if (permissionCode === 'shifts.manage' && checked) current.add('shifts.view')
  if (permissionCode === 'shifts.view' && !checked) current.delete('shifts.manage')
  permissionDrafts.value = {
    ...permissionDrafts.value,
    [roleCode]: Array.from(current)
  }
}

function savePermissions(roleCode: string) {
  if (!members.value?.canManageRoles || props.context.workspaceAccess?.isReadOnly !== false || isSaving.value) return
  if (!members.value.rolePermissionMatrix.some(role => role.roleCode === roleCode && role.canEdit)) return
  permissionRoleCode.value = roleCode
  void runAction(() => updateWorkspaceRolePermissions(props.context.organization.id, roleCode, {
    enabledPermissionCodes: permissionDrafts.value[roleCode] ?? []
  }))
}

function purchasePlan(plan: WorkspacePlan) {
  if (!plan.id || billing.value?.canUseTestSubscriptions === true) {
    return
  }

  if (!plan.isDemoPlan) {
    openBuyerDialog({ kind: 'purchase', plan })
    return
  }

  void runAction(async () => {
    const result = await activateWorkspaceDemoPlan(props.context.organization.id, plan.id!)
    trackWorkspaceGoal('demo_activated', { scope: 'organization', plan_id: plan.id! })

    return result
  }, {
    reloadPageOnSuccess: true
  })
}

function extendPlan() {
  if (billing.value?.canUseTestSubscriptions === true) {
    return
  }

  openBuyerDialog({ kind: 'extend' })
}

function openBuyerDialog(intent: { kind: 'purchase', plan: WorkspacePlan } | { kind: 'extend' }) {
  paymentPollController?.abort()
  paymentPollController = undefined
  paymentIntent.value = intent
  paymentBuyerType.value = ''
  paymentBuyerName.value = ''
  paymentBuyerInn.value = ''
  paymentBuyerError.value = ''
  paymentBuyerStatus.value = ''
  void nextTick(() => buyerDialog.value?.showModal())
}

function closeBuyerDialog() {
  if (!isSaving.value) {
    buyerDialog.value?.close()
    paymentIntent.value = null
  }
}

function updatePaymentBuyerInn(event: Event) {
  paymentBuyerInn.value = (event.target as HTMLInputElement).value.replace(/\D/g, '').slice(0, 12)
}

async function submitPaymentBuyer() {
  const intent = paymentIntent.value
  if (!intent || isSaving.value || billing.value?.canUseTestSubscriptions === true) {
    return
  }

  const name = paymentBuyerName.value.trim()
  const inn = paymentBuyerInn.value.trim()
  const buyerType = paymentBuyerType.value
  if (buyerType !== 'individual' && buyerType !== 'business') {
    paymentBuyerError.value = 'Выберите, кто покупает подписку.'
    return
  }

  if (buyerType === 'business' && !name) {
    paymentBuyerError.value = 'Укажите наименование организации или ФИО ИП.'
    return
  }

  if (buyerType === 'business' && ![10, 12].includes(inn.length)) {
    paymentBuyerError.value = 'ИНН организации или ИП должен содержать 10 или 12 цифр.'
    return
  }

  if (name.length > 200) {
    paymentBuyerError.value = 'Наименование или ФИО должно содержать не более 200 символов.'
    return
  }

  const buyer: WorkspacePaymentBuyer = {
    typeCode: buyerType,
    name: buyerType === 'business' ? name : null,
    inn: buyerType === 'business' ? inn : null
  }
  paymentBuyerError.value = ''
  const scope: PaymentOperationScope = {
    userId: props.context.user.id,
    organizationId: props.context.organization.id,
    kind: intent.kind,
    planId: intent.kind === 'purchase' ? intent.plan.id! : undefined
  }
  paymentPollController?.abort()
  const pollController = new AbortController()
  paymentPollController = pollController
  const organizationId = props.context.organization.id
  const succeeded = await runAction(async () => {
    const result = await submitPaymentOperation(scope, operationId => intent.kind === 'purchase'
      ? purchaseWorkspaceBusinessPlan(organizationId, intent.plan.id!, buyer, operationId)
      : extendWorkspaceBusinessPlan(organizationId, buyer, operationId), {
      loadPurchase: loadPaymentPurchase,
      signal: pollController.signal
    })
    if (props.context.organization.id !== organizationId || pollController.signal.aborted) {
      throw new DOMException('Проверка оплаты отменена.', 'AbortError')
    }

    if (result.paymentUrl) {
      trackWorkspaceGoal('checkout_started', {
        scope: 'organization',
        ...(intent.kind === 'purchase' ? { plan_id: intent.plan.id! } : {})
      })
    } else if (result.code === 'Completed') {
      trackWorkspaceGoal('plan_activated', {
        scope: 'organization',
        ...(intent.kind === 'purchase' ? { plan_id: intent.plan.id! } : {})
      })
    }
    return result
  }, { reloadPageOnSuccess: true, errorInBuyerDialog: true })

  if (succeeded) {
    buyerDialog.value?.close()
    paymentIntent.value = null
  }
}

function getCatalogItems(kind: WorkspaceScenarioCatalogKindCode): WorkspaceScenarioCatalogItem[] {
  const catalog = settings.value?.scenarioCatalog
  if (!catalog) {
    return []
  }

  if (kind === 'service') {
    return catalog.services ?? []
  }

  if (kind === 'service_bundle') return catalog.serviceBundles ?? []

  if (kind === 'work_post') {
    return catalog.workPosts ?? []
  }

  if (kind === 'material') {
    return catalog.materials ?? []
  }

  return []
}

function getActiveCatalogCount(kind: WorkspaceScenarioCatalogKindCode) {
  return getCatalogItems(kind).filter(item => item.isActive).length
}

function selectCatalogKind(kind: WorkspaceScenarioCatalogKindCode) {
  selectedCatalogKind.value = kind
  isEditingCatalog.value = false
  syncCatalogDrafts()

  if (props.sectionCode === 'catalog') {
    window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}#catalog?catalog=${kind}`)
  }
}

function selectCatalogMode(mode: 'workspace' | 'templates') {
  catalogMode.value = mode
  const kind = mode === 'templates' ? 'message-templates' : selectedCatalogKind.value
  window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}#catalog?catalog=${kind}`)
  if (mode === 'workspace' && !settings.value) void loadSection()
}

function addCatalogItem(kind: WorkspaceScenarioCatalogKindCode) {
  const form = catalogCreateForms[kind]
  if (!form?.name.trim()) {
    actionErrorMessage.value = 'Укажите название элемента справочника.'
    return
  }

  void runAction(async () => {
    await createScenarioCatalogItem(props.context.organization.id, {
      kindCode: kind,
      name: form.name,
      description: form.description || null,
      unitPrice: form.unitPrice === '' ? null : Number(form.unitPrice),
      durationMinutes: form.durationMinutes === '' ? null : Number(form.durationMinutes),
      serviceIds: form.serviceIds
    })
    form.name = ''
    form.description = ''
    form.unitPrice = ''
    form.durationMinutes = ''
    form.serviceIds = []
    return { message: 'Справочник обновлен.' }
  }, { refreshCatalogOnSuccess: true })
}

function saveCatalogItem(item: WorkspaceScenarioCatalogItem) {
  const draft = catalogDrafts.value[item.id]
  if (!draft?.name.trim()) {
    actionErrorMessage.value = 'Название элемента справочника не должно быть пустым.'
    return
  }

  void runAction(async () => {
    await updateScenarioCatalogItem(props.context.organization.id, item.id, {
      name: draft.name,
      description: draft.description || null,
      unitPrice: draft.unitPrice === '' ? null : Number(draft.unitPrice),
      durationMinutes: draft.durationMinutes === '' ? null : Number(draft.durationMinutes),
      serviceIds: draft.serviceIds,
      sortOrder: Number(draft.sortOrder) || item.sortOrder || 10,
      isActive: draft.isActive
    })
    return { message: 'Справочник обновлен.' }
  }, { refreshCatalogOnSuccess: true })
}

async function changePostActivity(item: WorkspaceScenarioCatalogItem) {
  if (isSaving.value || item.kindCode !== 'work_post' || !settings.value?.canManageProfile) return
  selectedWorkPost.value = item
  affectedMaintenances.value = null
  postActivityError.value = ''
  postActivityLoading.value = item.isActive
  postActivityDialog.value?.showModal()
  if (!item.isActive) return
  try {
    const result = await loadWorkPostAffectedMaintenances(props.context.organization.id, item.id)
    if (selectedWorkPost.value?.id === item.id) affectedMaintenances.value = result
  } catch (error) {
    if (selectedWorkPost.value?.id === item.id) {
      postActivityError.value = error instanceof Error ? error.message : 'Не удалось проверить назначения.'
    }
  } finally {
    postActivityLoading.value = false
  }
}

async function confirmPostActivity() {
  const item = selectedWorkPost.value
  if (!item || isSaving.value || postActivityLoading.value || (item.isActive && !affectedMaintenances.value)) return
  const succeeded = await runAction(async () => {
    await updateScenarioCatalogItem(props.context.organization.id, item.id, {
      name: item.name, description: item.description, unitPrice: item.unitPrice,
      durationMinutes: item.durationMinutes, serviceIds: item.serviceIds ?? [],
      sortOrder: item.sortOrder, isActive: !item.isActive
    })
    return { message: item.isActive ? 'Пост закрыт.' : 'Пост возвращён в работу.' }
  }, { refreshCatalogOnSuccess: true, onError: message => { postActivityError.value = message } })
  if (succeeded) {
    postActivityDialog.value?.close()
    selectedWorkPost.value = null
  }
}

function removeCatalogItem(item: WorkspaceScenarioCatalogItem) {
  const shouldDisable = item.kindCode === 'service' || item.isSystem || item.isUsed
  askConfirmation({
    title: item.kindCode === 'service' ? 'Перенести услугу в архив' : shouldDisable ? 'Отключить элемент' : 'Удалить элемент',
    message: item.kindCode === 'service'
      ? 'Услуга останется в существующих записях, но будет недоступна для новых. Её можно будет восстановить из архива.'
      : item.isUsed
      ? 'Элемент уже используется в записях или складских движениях. Удалить его нельзя: он будет скрыт из новых форм, а история останется корректной.'
      : item.isSystem
        ? 'Системный элемент будет отключен в этом справочнике.'
        : 'Элемент будет удален из справочника.',
    confirmText: item.kindCode === 'service' ? 'В архив' : shouldDisable ? 'Отключить' : 'Удалить',
    onConfirm: () => void runAction(async () => {
      await deleteScenarioCatalogItem(props.context.organization.id, item.id)
      return { message: 'Справочник обновлен.' }
    }, { refreshCatalogOnSuccess: true })
  })
}

function openWikiArticle(slug: string) {
  if (slug === selectedWikiArticle.value?.slug || isWikiArticleLoading.value) {
    return
  }

  activeWikiSlug.value = slug
  isWikiArticleLoading.value = true
  wikiArticleErrorMessage.value = ''

  void loadWorkspaceWikiArticle(props.context.organization.id, slug)
    .then((article) => {
      wikiArticle.value = article
      activeWikiSlug.value = article.slug
    })
    .catch((error) => {
      wikiArticleErrorMessage.value = error instanceof Error ? error.message : 'Не удалось загрузить статью.'
    })
    .finally(() => {
      isWikiArticleLoading.value = false
    })
}

function searchWiki() {
  activeWikiSlug.value = ''
  wikiArticle.value = null
  wikiArticleErrorMessage.value = ''
  void loadSection()
}

function syncSettingsSection() {
  isPromoHistory.value = props.sectionCode === 'billing' && new URLSearchParams(window.location.hash.split('?')[1] ?? '').get('tab') === 'promocodes'
  if (props.sectionCode === 'settings') {
    settingsSection.value = new URLSearchParams(window.location.hash.split('?')[1] ?? '').get('tab') ?? 'profile'
  }
}

onMounted(() => {
  syncSettingsSection()
  window.addEventListener('hashchange', syncSettingsSection)
  void loadSection()
})
onUnmounted(() => window.removeEventListener('hashchange', syncSettingsSection))
onUnmounted(clearSuccessMessage)
onUnmounted(() => paymentPollController?.abort())
watch(() => props.context.organization.id, () => {
  paymentPollController?.abort()
  paymentPollController = undefined
  paymentBuyerStatus.value = ''
  paymentBuyerError.value = ''
  paymentIntent.value = null
  buyerDialog.value?.close()
})
watch(() => props.sectionCode, () => {
  paymentPollController?.abort()
  paymentPollController = undefined
  paymentBuyerStatus.value = ''
  buyerDialog.value?.close()
  paymentIntent.value = null
  syncSettingsSection()
  isCatalogDialogOpen.value = false
  catalogRefreshErrorMessage.value = ''
  clearSuccessMessage()
  void loadSection()
})
</script>

<template>
  <section class="workspace-common-section">
    <UiCard page :title="isPromoHistory ? 'История промокодов' : sectionCode === 'employees' && employeeCount !== null ? `${section?.title} (${employeeCount})` : section?.title">
      <template #actions>
        <WorkspacePageHelpButton v-if="sectionCode !== 'shifts'" :section-code="sectionCode" :title="section?.title" />
      </template>
      <div v-if="isLoading" class="workspace-common-loading">
        <UiLoader text="Загружаем раздел..." />
        <div class="workspace-common-skeleton">
          <span v-for="index in 6" :key="index"></span>
        </div>
      </div>

      <div v-else-if="errorMessage" class="entity-note entity-note--error">
        <strong>Ошибка</strong>
        <p>{{ errorMessage }}</p>
      </div>

      <template v-else>
        <div v-if="successMessage" class="entity-note entity-note--success">
          <strong>Готово</strong>
          <p>{{ successMessage }}</p>
        </div>

        <WorkspaceOverviewSection
          v-if="sectionCode === 'organization' && overview"
          :overview="overview"
          :organization-name="organizationName"
          :scenario-title="scenarioTitle"
          :is-detailing-scenario="isDetailingScenario"
        />

        <WorkspaceEmployeesSection v-else-if="sectionCode === 'employees'" v-model:count="employeeCount" :context="context" />
        <WorkspaceShiftsSection v-else-if="sectionCode === 'shifts'" :context="context" />
        <WorkspaceWorkSection v-else-if="sectionCode === 'my-work'" :context="context" />

        <WorkspaceMembersSection
          v-else-if="sectionCode === 'members' && members"
          :members="members"
          :is-read-only="context.workspaceAccess?.isReadOnly !== false"
          :preferred-role-code="permissionRoleCode"
          :is-saving="isSaving"
          :is-editing-permissions="isEditingPermissions"
          :permission-drafts="permissionDrafts"
          @start-permissions-edit="startPermissionsEdit"
          @cancel-permissions-edit="isEditingPermissions = false"
          @toggle-permission="togglePermission"
          @save-permissions="savePermissions"
        />

        <WorkspaceStatisticsSection
          v-else-if="sectionCode === 'statistics' && statistics"
          :statistics="statistics"
        />

        <WorkspaceWikiSection
          v-else-if="sectionCode === 'wiki' && wiki"
          v-model:wiki-search="wikiSearch"
          :wiki="wiki"
          :selected-article="selectedWikiArticle"
          :active-wiki-slug="activeWikiSlug"
          :is-article-loading="isWikiArticleLoading"
          :article-error-message="wikiArticleErrorMessage"
          @search="searchWiki"
          @open-article="openWikiArticle"
        />

        <WorkspaceSettingsSection
          :is-free-plan="context.workspaceAccess?.commercialEnabled === true && context.workspaceAccess?.isFreePlan === true"
          :messaging-available="context.shell.scenario?.modules?.messaging !== false"
          v-else-if="sectionCode === 'settings' && settings"
          :settings="settings"
          :selected-section="settingsSection"
          :is-read-only="context.workspaceAccess?.isReadOnly !== false"
          :can-view-wiki="context.shell.menu?.some(item => item.code === 'wiki' && item.isVisible) === true"
          @select-section="selectSettingsSection"
          :settings-title="settingsTitle"
          :scenario-title="scenarioTitle"
          :is-saving="isSaving"
          :is-editing-profile="isEditingProfile"
          :is-editing-requisites="isEditingRequisites"
          :profile-form="profileForm"
          :requisites-form="requisitesForm"
          :requisites-errors="requisitesErrors"
          @start-profile-edit="startProfileEdit"
          @start-requisites-edit="startRequisitesEdit"
          @save-profile="saveProfile"
          @save-requisites="saveRequisites"
          @cancel-profile-edit="cancelProfileEdit"
          @cancel-requisites-edit="cancelRequisitesEdit"
          @archive-organization="archiveOrganization"
          @time-zone-saved="emit('changed')"
        >
          <template v-if="settings.canManageProfile && context.workspaceAccess?.isReadOnly === false" #workingHours="{ onClose, onBusyChange }">
            <OrganizationWorkingHoursSettings :key="context.organization.id" :organization-id="context.organization.id" :disabled="isSaving" embedded @saved="onClose" @closed="onClose" @busy-changed="onBusyChange" />
          </template>
          <template #imports="{ onAccessChange }">
            <WorkspaceImportSection :time-zone-id="context.organization.timeZoneId" :organization-id="context.organization.id" :user-id="context.user.id" @access-changed="onAccessChange" />
          </template>
        </WorkspaceSettingsSection>

        <div v-else-if="sectionCode === 'catalog'" class="workspace-catalog-container">
          <nav v-if="templateAccess?.canViewWorkspaceCatalog && templateAccess.canUseTemplates" class="workspace-catalog-modes" aria-label="Разделы справочников">
            <button type="button" :aria-pressed="catalogMode === 'workspace'" @click="selectCatalogMode('workspace')">Услуги и ресурсы</button>
            <button type="button" :aria-pressed="catalogMode === 'templates'" @click="selectCatalogMode('templates')">Шаблоны сообщений</button>
          </nav>
          <WorkspaceProLock v-if="catalogMode === 'templates' && templateAccess?.canUseTemplates" :locked="templatesLocked" message="Шаблоны сообщений доступны в PRO" feature="messageTemplates">
            <MessageTemplateCatalog :key="context.organization.id" :organization-id="context.organization.id" :read-only="context.workspaceAccess?.isReadOnly" :preview-only="templatesLocked" />
          </WorkspaceProLock>
          <WorkspaceCatalogSection
          v-else-if="settings && templateAccess?.canViewWorkspaceCatalog"
          :catalog-groups="catalogGroups"
          :selected-catalog-kind="selectedCatalogKind"
          :selected-catalog-group="selectedCatalogGroup"
          :can-manage-catalog="canManageCatalog"
          :is-editing-catalog="isEditingCatalog"
          :is-saving="isSaving"
          :catalog-create-forms="catalogCreateForms"
          :catalog-drafts="catalogDrafts"
          :action-error-message="actionErrorMessage"
          :get-catalog-items="getCatalogItems"
          :get-active-catalog-count="getActiveCatalogCount"
          @dialog-open-changed="isCatalogDialogOpen = $event"
          @clear-catalog-error="actionErrorMessage = ''"
          @select-catalog-kind="selectCatalogKind"
          @start-catalog-edit="isEditingCatalog = true"
          @cancel-catalog-edit="isEditingCatalog = false"
          @add-catalog-item="addCatalogItem"
          @save-catalog-item="saveCatalogItem"
          @remove-catalog-item="removeCatalogItem"
          @change-post-activity="changePostActivity"
        />
        </div>

        <WorkspaceBillingSection
          v-else-if="sectionCode === 'billing' && billing"
          :billing="billing"
          :history-only="isPromoHistory"
          :is-saving="isSaving"
          @extend-plan="extendPlan"
          @purchase-plan="purchasePlan"
          @promo-applied="refreshBillingAfterPromo"
        />
      </template>
    </UiCard>
    <dialog
      ref="buyerDialog"
      class="workspace-buyer-dialog"
      aria-labelledby="workspace-buyer-title"
      @cancel="isSaving && $event.preventDefault()"
    >
      <header class="ui-form-header">
        <h3 id="workspace-buyer-title">Данные покупателя</h3>
        <button type="button" aria-label="Закрыть" :disabled="isSaving" @click="closeBuyerDialog">×</button>
      </header>
      <form @submit.prevent="submitPaymentBuyer">
        <p>Укажите, кто оплачивает подписку. Эти данные сохранятся вместе с платежом, чтобы оформить чек НПД с правильными данными покупателя.</p>
        <fieldset :disabled="isSaving">
          <legend>Покупатель</legend>
          <label>
            <input v-model="paymentBuyerType" type="radio" name="payment-buyer-type" value="individual">
            Физическое лицо
          </label>
          <label>
            <input v-model="paymentBuyerType" type="radio" name="payment-buyer-type" value="business">
            Организация или ИП
          </label>
        </fieldset>
        <template v-if="paymentBuyerType === 'business'">
          <label class="workspace-buyer-dialog__field">
            Наименование организации или ФИО ИП
            <input v-model="paymentBuyerName" type="text" maxlength="200" autocomplete="organization" required>
          </label>
          <label class="workspace-buyer-dialog__field">
            ИНН
            <input
              :value="paymentBuyerInn"
              type="text"
              inputmode="numeric"
              autocomplete="off"
              maxlength="12"
              pattern="[0-9]{10}|[0-9]{12}"
              aria-describedby="workspace-buyer-inn-help"
              required
              @input="updatePaymentBuyerInn"
            >
            <small id="workspace-buyer-inn-help">10 цифр для организации или 12 для ИП.</small>
          </label>
        </template>
        <p v-if="paymentBuyerError" class="workspace-buyer-dialog__error" role="alert">{{ paymentBuyerError }}</p>
        <p v-if="paymentBuyerStatus" class="workspace-buyer-dialog__status" role="status">{{ paymentBuyerStatus }}</p>
        <footer>
          <button type="button" :disabled="isSaving" @click="closeBuyerDialog">Отмена</button>
          <button type="submit" :disabled="isSaving">{{ isSaving ? 'Готовим оплату…' : paymentBuyerStatus ? 'Проверить статус' : 'Продолжить к оплате' }}</button>
        </footer>
      </form>
    </dialog>
    <dialog ref="postActivityDialog" class="workspace-buyer-dialog" aria-labelledby="post-activity-title" @cancel="isSaving && $event.preventDefault()">
      <form v-if="selectedWorkPost" @submit.prevent="confirmPostActivity">
        <header class="ui-form-header"><h3 id="post-activity-title">{{ selectedWorkPost.isActive ? 'Закрыть пост' : 'Вернуть пост в работу' }} «{{ selectedWorkPost.name }}»?</h3></header>
        <p v-if="selectedWorkPost.isActive">Пост станет недоступен для новых назначений. История и существующие записи сохранятся. Перенос записей выполняется вручную. Вернуть пост в работу можно здесь же.</p>
        <p v-else>Пост снова станет доступен для новых назначений.</p>
        <UiLoader v-if="postActivityLoading" compact text="Проверяем назначения…" />
        <p v-else-if="affectedMaintenances?.canViewMaintenances === false">У вас нет права просмотра работ. Проверьте текущие и будущие назначения с ответственным сотрудником: при закрытии поста они сохранятся без автоматического переноса.</p>
        <template v-else-if="affectedMaintenances">
          <p>Незавершённых записей на посту: {{ affectedMaintenances.totalCount }}.</p>
          <ul v-if="affectedMaintenances.items.length">
            <li v-for="record in affectedMaintenances.items" :key="record.maintenanceId">
              <a :href="`#maintenance/${record.maintenanceId}`" @click="postActivityDialog?.close()">{{ new Date(record.date).toLocaleDateString('ru-RU') }} · {{ record.vehicleName || 'Автомобиль' }} {{ record.registrationPlate }}</a>
            </li>
          </ul>
          <p v-if="affectedMaintenances.totalCount > affectedMaintenances.items.length">Показаны ближайшие {{ affectedMaintenances.items.length }}. Остальные записи доступны в календаре.</p>
        </template>
        <p v-if="postActivityError" class="ui-field__error" role="alert">{{ postActivityError }}</p>
        <footer>
          <button type="button" :disabled="isSaving" @click="postActivityDialog?.close(); selectedWorkPost = null">Отмена</button>
          <button type="submit" :disabled="isSaving || postActivityLoading || (selectedWorkPost.isActive && !affectedMaintenances)">{{ isSaving ? 'Сохраняем…' : selectedWorkPost.isActive ? 'Закрыть пост' : 'Вернуть в работу' }}</button>
        </footer>
      </form>
    </dialog>
    <UiConfirmDialog
      :open="confirmDialog.open"
      :title="confirmDialog.title"
      :message="confirmDialog.message"
      :confirm-text="confirmDialog.confirmText"
      :busy="isSaving"
      tone="danger"
      @close="closeConfirmation"
      @confirm="confirmAction"
    />
    <UiAlertDialog
      :open="Boolean(actionErrorMessage || catalogRefreshErrorMessage) && !isCatalogDialogOpen"
      :title="catalogRefreshErrorMessage ? 'Изменения сохранены' : 'Не удалось выполнить действие'"
      :message="actionErrorMessage || catalogRefreshErrorMessage"
      tone="danger"
      @close="actionErrorMessage = ''; catalogRefreshErrorMessage = ''"
    />
  </section>
</template>

<style scoped>
.workspace-catalog-modes { display:flex; flex-wrap:wrap; gap:8px; margin-bottom:20px; padding-bottom:16px; border-bottom:1px solid var(--crm-line, #e0e5ec); }
.workspace-catalog-modes button { border:1px solid var(--crm-line, #d4dce6); border-radius:8px; padding:10px 16px; color:inherit; background:var(--crm-surface, #fff); font:inherit; cursor:pointer; }
.workspace-catalog-modes button[aria-pressed="true"] { color:var(--crm-accent, #ad511e); border-color:var(--crm-accent, #ad511e); background:var(--crm-accent-soft, #fff3e9); }
.workspace-buyer-dialog {
  box-sizing: border-box;
  width: min(480px, calc(100vw - 24px));
  max-height: calc(100dvh - 32px);
  padding: 0;
  border: 1px solid var(--crm-line, #d9e0e9);
  border-radius: 12px;
  background: var(--crm-surface, #fff);
  color: var(--crm-text, #172033);
}

.workspace-buyer-dialog::backdrop {
  background: #0009;
}

.workspace-buyer-dialog header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 14px 18px;
  border-bottom: 1px solid var(--crm-form-header-border, #d9e0e9);
}

.workspace-buyer-dialog h3 {
  margin: 0;
  font-size: 17px;
}

.workspace-buyer-dialog header button {
  border: 0;
  background: transparent;
  color: inherit;
  font-size: 24px;
  cursor: pointer;
}

.workspace-buyer-dialog form {
  display: grid;
  gap: 14px;
  padding: 18px;
}

.workspace-buyer-dialog p,
.workspace-buyer-dialog fieldset {
  margin: 0;
}

.workspace-buyer-dialog p {
  font-size: 13px;
  line-height: 1.5;
}

.workspace-buyer-dialog fieldset {
  display: flex;
  flex-wrap: wrap;
  gap: 10px 18px;
  padding: 0;
  border: 0;
}

.workspace-buyer-dialog legend {
  margin-bottom: 8px;
  font-weight: 600;
}

.workspace-buyer-dialog fieldset label {
  display: flex;
  align-items: center;
  gap: 7px;
  font-size: 13px;
}

.workspace-buyer-dialog__field {
  display: grid;
  gap: 6px;
  font-size: 13px;
  font-weight: 600;
}

.workspace-buyer-dialog__field input {
  box-sizing: border-box;
  width: 100%;
  min-height: 38px;
  padding: 8px 10px;
  border: 1px solid var(--crm-line, #d9e0e9);
  border-radius: 7px;
  background: var(--crm-surface, #fff);
  color: inherit;
  font: inherit;
  font-weight: 400;
}

.workspace-buyer-dialog__field small {
  color: var(--crm-text-muted, #64748b);
  font-weight: 400;
}

.workspace-buyer-dialog__error {
  color: var(--crm-danger-text, #b42318);
}

.workspace-buyer-dialog__status {
  color: var(--crm-text-muted, #64748b);
}

.workspace-buyer-dialog footer {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 2px;
}

.workspace-buyer-dialog footer button {
  min-height: 36px;
  padding: 7px 12px;
  border: 1px solid var(--crm-line, #d9e0e9);
  border-radius: 7px;
  background: var(--crm-surface, #fff);
  color: inherit;
  font: inherit;
  cursor: pointer;
}

.workspace-buyer-dialog footer button[type='submit'] {
  border-color: var(--crm-accent, #e77924);
  background: var(--crm-accent, #e77924);
  color: #fff;
}

.workspace-buyer-dialog button:disabled {
  cursor: wait;
  opacity: .6;
}
</style>

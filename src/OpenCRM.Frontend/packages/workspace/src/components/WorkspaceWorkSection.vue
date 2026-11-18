<script setup lang="ts">
import { formattingCulture, translate as t } from '@opencrm/ui/i18n'
import { useWorkspaceCurrency } from '../workspaceCurrency'
import '../workMessages'

import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { UiButton, UiLoader, formatDateOnly, parseDateOnly } from '@opencrm/ui'
import {
  addWorkReport, claimAvailableWork, issueOperationMaterial, loadAssignedWork,
  loadAvailableOperationMaterials, loadAvailableWork,
  loadIssuedOperationMaterials, loadWorkPhotos, transitionAssignedWork, uploadWorkPhotos,
  loadWorkReports, returnOperationMaterial, type AssignedWork, type OperationMaterialIssue,
  type OperationMaterialStock, type WorkPhoto, type WorkReport
} from '../api/work'
import type { WorkspaceContext } from '../types'
import { loadOwnShifts, type Shift } from '../api/shifts'
import { formatWorkDuration } from '../formatWorkDuration'

const workspaceCurrency = useWorkspaceCurrency()

const props = withDefaults(defineProps<{ context: WorkspaceContext, previewOnly?: boolean }>(), { previewOnly: false })
const works = ref<AssignedWork[]>([])
const ownShifts = ref<Shift[]>([])
const availableWorks = ref<AssignedWork[]>([])
const loading = ref(true)
const savingId = ref('')
const error = ref('')
const actionError = ref('')
const tab = ref<'today' | 'active' | 'assigned' | 'available' | 'completed'>('today')
const tabs = [
  { code: 'today' },
  { code: 'active' },
  { code: 'assigned' },
  { code: 'available' },
  { code: 'completed' }
] as const
const pauseReasons = ref<Record<string, string>>({})
const materialPanelId = ref('')
const materialStocks = ref<OperationMaterialStock[]>([])
const materialIssues = ref<OperationMaterialIssue[]>([])
const selectedStockId = ref('')
const materialQuantity = ref(1)
const materialUnit = ref('')
const materialReturnId = ref('')
const materialReturnReason = ref('')
const materialLoading = ref(false)
const materialError = ref('')
const reportPanelId = ref('')
const reports = ref<WorkReport[]>([])
const reportKind = ref<WorkReport['kind']>('comment')
const reportText = ref('')
const reportLoading = ref(false)
const photoPanelId = ref('')
const photos = ref<WorkPhoto[]>([])
const photoStage = ref(2)
const photoLoading = ref(false)
let refreshTimer: ReturnType<typeof setInterval> | undefined
let requestId = 0
let materialRequestId = 0
let reportRequestId = 0
let pendingReportRequestId = 0

const today = ref(formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC'))
const timeZoneId = computed(() => props.context.organization.timeZoneId || 'UTC')
const formatInstant = (value: string) => new Intl.DateTimeFormat(formattingCulture.value, { dateStyle: 'short', timeStyle: 'short', timeZone: timeZoneId.value }).format(new Date(value))
const visibleWorks = computed(() => (tab.value === 'available' ? availableWorks.value : works.value).filter(work => {
  if (tab.value === 'available') return true
  if (tab.value === 'active') return work.status === 2 || work.status === 3
  if (tab.value === 'assigned') return work.status === 1
  if (tab.value === 'completed') return work.status === 4
    && !!work.completedAt && formatDateOnly(new Date(work.completedAt), timeZoneId.value) === today.value
  return work.plannedDate?.slice(0, 10) === today.value || work.status === 2 || work.status === 3
    || work.status === 4 && !!work.completedAt && formatDateOnly(new Date(work.completedAt), timeZoneId.value) === today.value
}))
const completedTodayCount = computed(() => works.value.filter(work => work.status === 4
  && !!work.completedAt && formatDateOnly(new Date(work.completedAt), timeZoneId.value) === today.value).length)
const activeCount = computed(() => works.value.filter(work => work.status === 2 || work.status === 3).length)
const canWrite = computed(() => props.context.workspaceAccess?.isReadOnly === false)
const canSelfAssign = computed(() => props.context.organization.allowWorkSelfAssignment === true)
const emptyState = computed(() => {
  const emptyStateKey = tab.value === 'active' ? 'active'
    : tab.value === 'assigned' ? 'assigned'
      : tab.value === 'available' ? 'available'
        : tab.value === 'completed' ? 'completed' : 'today'

  return {
    title: t(`work.empty.${emptyStateKey}Title`),
    description: t(`work.empty.${emptyStateKey}Description`)
  }
})

async function load(silent = false) {
  if (props.previewOnly) return

  if (!silent) loading.value = true
  today.value = formatDateOnly(undefined, timeZoneId.value)
  const currentRequestId = ++requestId
  if (!silent) ownShifts.value = []
  try {
    const [assignedWorks, available] = await Promise.all([
      loadAssignedWork(props.context.organization.id),
      canSelfAssign.value ? loadAvailableWork(props.context.organization.id) : Promise.resolve([])
    ])
    if (currentRequestId !== requestId) return
    const previousPanelWork = works.value.find(work => work.detailId === materialPanelId.value)
    const refreshedPanelWork = assignedWorks.find(work => work.detailId === materialPanelId.value)
    void loadOwnShifts(props.context.organization.id).then(shifts => {
      if (currentRequestId === requestId) ownShifts.value = shifts
    }).catch(() => {
      if (currentRequestId === requestId) ownShifts.value = []
    })
    works.value = assignedWorks
    availableWorks.value = available
    error.value = ''
    if (previousPanelWork && refreshedPanelWork && previousPanelWork.status !== refreshedPanelWork.status) {
      void refreshMaterials(refreshedPanelWork, true)
    }
    const reportWork = assignedWorks.find(work => work.detailId === reportPanelId.value)
    if (reportWork && !savingId.value && !pendingReportRequestId) void refreshReports(reportWork)
  } catch (cause) {
    if (currentRequestId !== requestId) return
    error.value = cause instanceof Error ? cause.message : t('work.errors.load')
  } finally {
    if (currentRequestId === requestId) loading.value = false
  }
}

async function claim(work: AssignedWork) {
  if (props.previewOnly || savingId.value) return
  savingId.value = work.detailId
  actionError.value = ''
  try {
    const assigned = await claimAvailableWork(props.context.organization.id, work)
    availableWorks.value = availableWorks.value.filter(item => item.detailId !== work.detailId)
    works.value = [...works.value, assigned]
    tab.value = 'assigned'
  } catch (cause) {
    actionError.value = cause instanceof Error ? cause.message : t('work.errors.claim')
    await load(true)
  } finally {
    savingId.value = ''
  }
}

async function act(work: AssignedWork, action: 'start' | 'pause' | 'resume' | 'complete') {
  if (props.previewOnly || savingId.value) return
  const reason = pauseReasons.value[work.detailId]?.trim()
  if (action === 'pause' && !reason) {
    actionError.value = t('work.validation.pauseReason')
    return
  }

  savingId.value = work.detailId
  actionError.value = ''
  try {
    const updated = await transitionAssignedWork(props.context.organization.id, work, action, reason)
    requestId++
    works.value = works.value.map(item => item.detailId === work.detailId
      ? { ...updated, reworkReason: action === 'complete' ? null : item.reworkReason }
      : item)
    if (materialPanelId.value === work.detailId && work.status !== updated.status) {
      await refreshMaterials(updated, true)
    }
    pauseReasons.value[work.detailId] = ''
  } catch (cause) {
    actionError.value = cause instanceof Error ? cause.message : t('work.errors.transition')
    await load(true)
  } finally {
    savingId.value = ''
  }
}

async function toggleMaterials(work: AssignedWork) {
  if (props.previewOnly) return

  if (materialPanelId.value === work.detailId) {
    materialPanelId.value = ''
    materialRequestId++
    return
  }

  materialPanelId.value = work.detailId
  materialStocks.value = []
  materialIssues.value = []
  selectedStockId.value = ''
  materialUnit.value = ''
  await refreshMaterials(work, true)
}

async function refreshMaterials(work: AssignedWork, showLoading = false) {
  if (props.previewOnly) return

  const currentRequestId = ++materialRequestId
  materialLoading.value = showLoading
  materialError.value = ''
  actionError.value = ''
  const [issues, stocks] = await Promise.allSettled([
    loadIssuedOperationMaterials(props.context.organization.id, work),
    work.status === 2 ? loadAvailableOperationMaterials(props.context.organization.id, work) : Promise.resolve([])
  ])
  if (currentRequestId !== materialRequestId || materialPanelId.value !== work.detailId) return
  if (issues.status === 'fulfilled') materialIssues.value = issues.value
  materialStocks.value = stocks.status === 'fulfilled' ? stocks.value : []
  const failure = stocks.status === 'rejected' ? stocks.reason : issues.status === 'rejected' ? issues.reason : null
  materialError.value = failure instanceof Error ? failure.message : failure ? t('work.errors.loadMaterials') : ''
  materialLoading.value = false
}

function retryMaterials(work: AssignedWork) {
  materialPanelId.value = ''
  void toggleMaterials(work)
}

async function issueMaterial(work: AssignedWork) {
  if (props.previewOnly || !canManageWorkMaterials(work)) return

  const stock = materialStocks.value.find(item => item.id === selectedStockId.value)
  if (!stock || !Number.isFinite(materialQuantity.value) || materialQuantity.value <= 0) {
    actionError.value = t('work.validation.materialRequired')
    return
  }

  savingId.value = work.detailId
  actionError.value = ''
  try {
    const issue = await issueOperationMaterial(
      props.context.organization.id, work, stock, materialQuantity.value, materialUnit.value || stock.unit
    )
    materialIssues.value = [issue, ...materialIssues.value]
    materialStocks.value = await loadAvailableOperationMaterials(props.context.organization.id, work)
    selectedStockId.value = ''
    materialUnit.value = ''
    materialQuantity.value = 1
  } catch (cause) {
    actionError.value = cause instanceof Error ? cause.message : t('work.errors.issueMaterial')
  } finally {
    savingId.value = ''
  }
}

async function returnMaterial(work: AssignedWork, issue: OperationMaterialIssue) {
  if (props.previewOnly || !canManageWorkMaterials(work)) return

  if (!materialReturnReason.value.trim()) {
    actionError.value = t('work.validation.returnReason')
    return
  }

  savingId.value = work.detailId
  actionError.value = ''
  try {
    const movement = await returnOperationMaterial(
      props.context.organization.id, work, issue, materialReturnReason.value.trim()
    )
    materialIssues.value = [movement, ...materialIssues.value]
    materialStocks.value = await loadAvailableOperationMaterials(props.context.organization.id, work)
    materialReturnId.value = ''
    materialReturnReason.value = ''
  } catch (cause) {
    actionError.value = cause instanceof Error ? cause.message : t('work.errors.returnMaterial')
  } finally {
    savingId.value = ''
  }
}

function chooseMaterialUnit() {
  materialUnit.value = selectedMaterialStock.value?.unit ?? ''
}

const selectedMaterialStock = computed(() => materialStocks.value.find(item => item.id === selectedStockId.value))
const materialUnitOptions = computed(() => {
  const stock = selectedMaterialStock.value
  if (!stock) return []
  const units = stock.unit === 'г' || stock.unit === 'кг' ? ['г', 'кг']
    : stock.unit === 'мл' || stock.unit === 'л' ? ['мл', 'л']
    : [stock.unit]
  return stock.packageQuantity ? [...units, 'уп'] : units
})

function formatUnit(unit: string) {
  const unitKeys: Record<string, string> = { г: 'g', кг: 'kg', мл: 'ml', л: 'l', уп: 'package' }
  const unitKey = unitKeys[unit]

  return unitKey ? t(`work.units.${unitKey}`) : unit
}

async function toggleReports(work: AssignedWork) {
  if (props.previewOnly) return

  if (reportPanelId.value === work.detailId) {
    reportPanelId.value = ''
    reportRequestId++
    return
  }

  reportPanelId.value = work.detailId
  reports.value = []
  actionError.value = ''
  await refreshReports(work, true)
}

async function refreshReports(work: AssignedWork, showLoading = false) {
  if (props.previewOnly) return

  const currentRequestId = ++reportRequestId
  pendingReportRequestId = currentRequestId
  const organizationId = props.context.organization.id
  reportLoading.value = showLoading
  try {
    const nextReports = await loadWorkReports(organizationId, work.maintenanceId, work.detailId)
    if (currentRequestId !== reportRequestId || reportPanelId.value !== work.detailId) return
    reports.value = nextReports
  } catch (cause) {
    if (currentRequestId !== reportRequestId || reportPanelId.value !== work.detailId) return
    if (showLoading) actionError.value = cause instanceof Error ? cause.message : t('work.errors.loadReports')
  } finally {
    if (currentRequestId === reportRequestId) reportLoading.value = false
    if (currentRequestId === pendingReportRequestId) pendingReportRequestId = 0
  }
}

async function sendReport(work: AssignedWork) {
  if (props.previewOnly) return

  if (!reportText.value.trim()) {
    actionError.value = t('work.validation.reportText')
    return
  }

  savingId.value = work.detailId
  actionError.value = ''
  try {
    reportRequestId++
    const report = await addWorkReport(props.context.organization.id, work, reportKind.value, reportText.value.trim())
    reports.value = [report, ...reports.value]
    reportText.value = ''
  } catch (cause) {
    actionError.value = cause instanceof Error ? cause.message : t('work.errors.saveReport')
  } finally {
    savingId.value = ''
  }
}

function reportKindName(kind: WorkReport['kind']) {
  return t(`work.reportKind.${kind}`)
}

async function togglePhotos(work: AssignedWork) {
  if (props.previewOnly) return

  if (photoPanelId.value === work.detailId) {
    photoPanelId.value = ''
    return
  }

  photoPanelId.value = work.detailId
  photoLoading.value = true
  photos.value = []
  actionError.value = ''
  try {
    const nextPhotos = await loadWorkPhotos(props.context.organization.id, work)
    if (photoPanelId.value === work.detailId) photos.value = nextPhotos
  } catch (cause) {
    actionError.value = cause instanceof Error ? cause.message : t('work.errors.loadPhotos')
  } finally {
    photoLoading.value = false
  }
}

async function sendPhotos(work: AssignedWork, event: Event) {
  if (props.previewOnly) return

  const input = event.target as HTMLInputElement
  const files = Array.from(input.files ?? [])
  if (!files.length) return
  if (files.length > 10 || files.reduce((total, file) => total + file.size, 0) > 100 * 1024 * 1024) {
    actionError.value = t('work.validation.photoLimit')
    input.value = ''
    return
  }

  savingId.value = work.detailId
  actionError.value = ''
  try {
    await uploadWorkPhotos(props.context.organization.id, work, files, photoStage.value)
    photos.value = await loadWorkPhotos(props.context.organization.id, work)
  } catch (cause) {
    actionError.value = cause instanceof Error ? cause.message : t('work.errors.loadPhotos')
  } finally {
    input.value = ''
    savingId.value = ''
  }
}

function statusName(status: number) {
  const statusKey = status === 2 ? 'active' : status === 3 ? 'paused' : status === 4 ? 'completed' : 'assigned'

  return t(`work.status.${statusKey}`)
}

function canManageWorkMaterials(work: AssignedWork) {
  return canWrite.value && (!work.currencyCode || work.currencyCode === (props.context.organization.currencyCode ?? workspaceCurrency.value))
}

function formatPrice(price?: number | null, currencyCode = workspaceCurrency.value) {
  return price == null ? t('work.notSpecified') : new Intl.NumberFormat(formattingCulture.value, {
    style: 'currency', currency: currencyCode, maximumFractionDigits: 2
  }).format(price)
}

function formatPlan(work: AssignedWork) {
  if (!work.plannedDate) return t('work.noScheduledTime')
  const date = parseDateOnly(work.plannedDate.slice(0, 10))?.toLocaleDateString(formattingCulture.value, { day: 'numeric', month: 'short' }) ?? ''
  const start = work.plannedStartTime?.slice(0, 5)
  const end = work.plannedEndTime?.slice(0, 5)
  const endDate = work.plannedEndDate?.slice(0, 10)
  const endLabel = endDate && endDate !== work.plannedDate.slice(0, 10)
    ? `${parseDateOnly(endDate)?.toLocaleDateString(formattingCulture.value, { day: 'numeric', month: 'short' }) ?? ''} ${end ?? ''}`
    : end

  return [date, start && endLabel ? `${start}–${endLabel}` : start ?? endLabel].filter(Boolean).join(' · ')
}

function formatShiftStart(value: string) {
  const date = parseDateOnly(value.slice(0, 10))?.toLocaleDateString(formattingCulture.value, { day: 'numeric', month: 'short' }) ?? ''

  return `${date} ${value.slice(11, 16)}`
}

onMounted(() => {
  if (props.previewOnly) return

  void load()
  refreshTimer = setInterval(() => { if (!savingId.value) void load(true) }, 15000)
})
onUnmounted(() => { requestId++; reportRequestId++; if (refreshTimer) clearInterval(refreshTimer) })
watch(() => [props.context.organization.id, props.context.organization.timeZoneId], () => {
  if (props.previewOnly) return

  works.value = []
  materialPanelId.value = ''
  materialRequestId++
  reportPanelId.value = ''
  reportRequestId++
  photoPanelId.value = ''
  void load()
})
</script>

<template>
  <div v-if="previewOnly" class="my-work my-work--preview" aria-hidden="true">
    <div class="my-work__summary"><span>{{ t('work.activeCount') }} <strong>—</strong></span><span>{{ t('work.completedToday') }} <strong>—</strong></span></div>
    <nav class="my-work__tabs" :aria-label="t('work.summary')"><button v-for="item in tabs" :key="item.code" type="button" disabled>{{ t(`work.tabs.${item.code}`) }}</button></nav>
    <div class="my-work__preview-card">
      <header><i></i><span></span></header>
      <p><i></i></p>
      <div class="my-work__preview-facts"><span v-for="index in 4" :key="index"><i></i><b></b></span></div>
      <div class="my-work__preview-actions"><button v-for="index in 3" :key="index" type="button" disabled></button></div>
    </div>
    <div class="my-work__preview-card"><header><i></i><span></span></header><p><i></i><i></i></p></div>
  </div>
  <div v-else class="my-work">
    <dl v-if="!loading && !error" class="my-work__summary" :class="{ 'my-work__summary--with-shift': ownShifts.length }" :aria-label="t('work.summary')">
      <div v-if="ownShifts.length" class="my-work__shift">
        <dt>{{ t('work.nextShift') }}</dt>
        <dd>{{ formatShiftStart(ownShifts[0].startLocal) }} <span>— {{ ownShifts[0].endLocal.slice(11, 16) }}</span></dd>
      </div>
      <div>
        <dt>{{ t('work.activeCount') }}</dt>
        <dd>{{ activeCount }}</dd>
      </div>
      <div>
        <dt>{{ t('work.completedToday') }}</dt>
        <dd>{{ completedTodayCount }}</dd>
      </div>
    </dl>
    <nav class="my-work__tabs" :aria-label="t('work.summary')">
      <button v-for="item in tabs" v-show="item.code !== 'available' || canSelfAssign" :key="item.code" type="button" :aria-current="tab === item.code ? 'page' : undefined" @click="tab = item.code">{{ t(`work.tabs.${item.code}`) }}</button>
    </nav>
    <UiLoader v-if="loading" :text="t('work.load')" />
    <p v-else-if="error" role="alert">{{ error }} <UiButton variant="secondary" @click="load()">{{ t('work.retry') }}</UiButton></p>
    <div v-else-if="!visibleWorks.length" class="my-work__empty" role="status">
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9 5H6a2 2 0 0 0-2 2v13h16V7a2 2 0 0 0-2-2h-3M9 3h6v4H9zM8 12h8M8 16h5" /></svg>
      <strong>{{ emptyState.title }}</strong>
      <p>{{ emptyState.description }}</p>
    </div>
    <div v-else class="my-work__list">
      <article v-for="work in visibleWorks" :key="work.detailId" class="my-work__item">
        <header><strong>{{ work.name }}</strong><span class="my-work__status" :data-status="work.status">{{ statusName(work.status) }}</span></header>
        <p class="my-work__vehicle">{{ work.vehicleName || work.registrationPlate || t('work.workFallback') }}<span v-if="work.registrationPlate && work.vehicleName"> · {{ work.registrationPlate }}</span></p>
        <div class="my-work__facts">
          <div><span>{{ t('work.plan') }}</span><strong>{{ formatPlan(work) }}</strong></div>
          <div><span>{{ t('work.location') }}</span><strong>{{ work.location || t('work.unnamedLocation') }}</strong></div>
          <div><span>{{ t('work.price') }}</span><strong>{{ formatPrice(work.price, work.currencyCode) }}</strong></div>
          <div><span>{{ t('work.quantity') }}</span><strong>{{ work.quantity || 1 }}</strong></div>
        </div>
        <p v-if="work.description" class="my-work__description">{{ work.description }}</p>
        <div v-if="work.intakeNotes || work.intakeCompleteness || work.intakeVisibleDamage || work.workComment || work.reworkReason" class="my-work__context">
          <strong>{{ t('work.consider') }}</strong>
          <p v-if="work.reworkReason">{{ t('work.rework') }}: {{ work.reworkReason }}</p>
          <p v-if="work.intakeNotes">{{ t('work.intake') }}: {{ work.intakeNotes }}</p>
          <p v-if="work.intakeCompleteness">{{ t('work.completeness') }}: {{ work.intakeCompleteness }}</p>
          <p v-if="work.intakeVisibleDamage">{{ t('work.damage') }}: {{ work.intakeVisibleDamage }}</p>
          <p v-if="work.workComment">{{ t('work.workComment') }}: {{ work.workComment }}</p>
        </div>
        <p v-if="work.startedAt" class="my-work__time">{{ t('work.started') }}: {{ formatInstant(work.startedAt) }} · {{ t('work.activeTime') }}: {{ formatWorkDuration(work.activeTime) }}</p>
        <div v-if="canWrite" class="my-work__actions">
          <UiButton v-if="tab === 'available'" :disabled="!!savingId" @click="claim(work)">{{ t('work.takeWork') }}</UiButton>
          <UiButton v-else-if="work.status === 1" :disabled="!!savingId" @click="act(work, 'start')">{{ t('work.start') }}</UiButton>
          <template v-else-if="work.status === 2">
            <input v-model="pauseReasons[work.detailId]" maxlength="1000" :placeholder="t('work.pauseReason')" :aria-label="t('work.pauseReason')" :disabled="!!savingId">
            <UiButton variant="secondary" :disabled="!!savingId" @click="act(work, 'pause')">{{ t('work.pause') }}</UiButton>
            <UiButton :disabled="!!savingId" @click="act(work, 'complete')">{{ t('work.complete') }}</UiButton>
          </template>
          <UiButton v-else-if="work.status === 3" :disabled="!!savingId" @click="act(work, 'resume')">{{ t('work.resume') }}</UiButton>
        </div>
        <div v-if="tab !== 'available'" class="my-work__materials">
          <UiButton variant="secondary" @click="toggleMaterials(work)">{{ t(materialPanelId === work.detailId ? 'work.hideMaterials' : 'work.materials') }}</UiButton>
          <div v-if="materialPanelId === work.detailId" class="my-work__material-panel">
            <UiLoader v-if="materialLoading" :text="t('work.loadMaterials')" />
            <template v-else>
              <p v-if="materialError" class="my-work__error" role="alert">{{ materialError }} <UiButton variant="secondary" @click="retryMaterials(work)">{{ t('work.retry') }}</UiButton></p>
              <strong>{{ t('work.issuedMaterials') }}</strong>
              <p v-if="!materialIssues.length">{{ t('work.noIssuedMaterials') }}</p>
              <ul v-else>
                <li v-for="issue in materialIssues" :key="issue.id">
                  {{ t(issue.isReturn ? 'work.returned' : 'work.issued') }}: {{ issue.materialName }} · {{ issue.quantity }} {{ formatUnit(issue.unit) }} · {{ formatInstant(issue.createdAt) }}
                  <UiButton v-if="canManageWorkMaterials(work) && work.status === 2 && !issue.isReturn && !materialIssues.some(item => item.sourceMovementId === issue.id)" variant="secondary" :disabled="!!savingId" @click="materialReturnId = issue.id; materialReturnReason = ''">{{ t('work.return') }}</UiButton>
                  <div v-if="canManageWorkMaterials(work) && work.status === 2 && materialReturnId === issue.id" class="my-work__material-return">
                    <input v-model="materialReturnReason" maxlength="1000" :placeholder="t('work.returnReason')" :aria-label="t('work.materialReturnLabel')" :disabled="!!savingId">
                    <UiButton :disabled="!!savingId" @click="returnMaterial(work, issue)">{{ t('work.confirm') }}</UiButton>
                    <UiButton variant="secondary" :disabled="!!savingId" @click="materialReturnId = ''">{{ t('work.cancel') }}</UiButton>
                  </div>
                </li>
              </ul>
              <p v-if="canManageWorkMaterials(work) && work.status === 3">{{ t('work.continueBeforeIssue') }}</p>
              <p v-if="canWrite && !canManageWorkMaterials(work)" role="note">{{ t('work.materialCurrencyMismatch', { orderCurrency: work.currencyCode ?? workspaceCurrency, workspaceCurrency: context.organization.currencyCode ?? workspaceCurrency }) }}</p>
              <div v-if="canManageWorkMaterials(work) && work.status === 2" class="my-work__material-form">
                <p v-if="!materialStocks.length && !materialError">{{ t('work.noStock') }}</p>
                <select v-model="selectedStockId" :aria-label="t('work.material')" :disabled="!!savingId" @change="chooseMaterialUnit">
                  <option value="">{{ t('work.chooseMaterial') }}</option>
                  <option v-for="stock in materialStocks" :key="stock.id" :value="stock.id">{{ stock.name }} · {{ stock.availableQuantity }} {{ formatUnit(stock.unit) }}</option>
                </select>
                <input v-model.number="materialQuantity" type="number" min="0.001" step="0.001" :aria-label="t('work.materialQuantity')" :disabled="!!savingId">
                <select v-model="materialUnit" :aria-label="t('work.unit')" :disabled="!!savingId || !selectedStockId"><option v-for="unit in materialUnitOptions" :key="unit" :value="unit">{{ formatUnit(unit) }}</option></select>
                <small v-if="selectedMaterialStock?.packageQuantity">{{ t('work.packageEquals', { quantity: selectedMaterialStock.packageQuantity, unit: formatUnit(selectedMaterialStock.unit) }) }}</small>
                <UiButton :disabled="!!savingId || !selectedStockId" @click="issueMaterial(work)">{{ t('work.issue') }}</UiButton>
              </div>
            </template>
          </div>
        </div>
        <div v-if="tab !== 'available'" class="my-work__reports">
          <UiButton variant="secondary" @click="toggleReports(work)">{{ t(reportPanelId === work.detailId ? 'work.hideReports' : 'work.reports') }}</UiButton>
          <div v-if="reportPanelId === work.detailId" class="my-work__report-panel">
            <UiLoader v-if="reportLoading" :text="t('work.loadReports')" />
            <template v-else>
              <p v-if="!reports.length">{{ t('work.noReports') }}</p>
              <ul v-else>
                <li v-for="report in reports" :key="report.id">
                  <strong>{{ reportKindName(report.kind) }}</strong> · {{ formatInstant(report.createdAt) }}
                  <p>{{ report.text }}</p>
                  <small v-if="report.kind === 'extra'">{{ report.decision === 'approved' ? t('work.decision.approved', { price: formatPrice(report.approvedPrice, work.currencyCode) }) : report.decision === 'rejected' ? t('work.decision.rejected', { reason: report.decisionReason || '' }) : t('work.decision.pending') }}</small>
                </li>
              </ul>
              <div v-if="canWrite && work.status !== 4" class="my-work__report-form">
                <select v-model="reportKind" :aria-label="t('work.reportType')" :disabled="!!savingId">
                  <option value="comment">{{ t('work.comment') }}</option>
                  <option value="defect">{{ t('work.defect') }}</option>
                  <option value="extra">{{ t('work.extraWorkRequest') }}</option>
                </select>
                <textarea v-model="reportText" rows="3" maxlength="2000" :aria-label="t('work.reportText')" :disabled="!!savingId" />
                <UiButton :disabled="!!savingId || !reportText.trim()" @click="sendReport(work)">{{ t('work.send') }}</UiButton>
              </div>
            </template>
          </div>
        </div>
        <div v-if="tab !== 'available'" class="my-work__photos">
          <UiButton variant="secondary" @click="togglePhotos(work)">{{ t(photoPanelId === work.detailId ? 'work.hidePhotos' : 'work.photos') }}</UiButton>
          <div v-if="photoPanelId === work.detailId" class="my-work__photo-panel">
            <UiLoader v-if="photoLoading" :text="t('work.loadPhotos')" />
            <template v-else>
              <p v-if="!photos.length">{{ t('work.noPhotos') }}</p>
              <div v-else class="my-work__photo-grid">
                <a v-for="photo in photos" :key="photo.photoId" :href="photo.url" target="_blank" rel="noopener noreferrer">
                  <img :src="photo.url" :alt="photo.stage === 1 ? t('work.beforeWork') : photo.stage === 2 ? t('work.afterWork') : t('work.workPhoto')" loading="lazy">
                  <span>{{ photo.stage === 1 ? t('work.before') : photo.stage === 2 ? t('work.after') : t('work.photo') }}</span>
                </a>
              </div>
              <div v-if="canWrite && work.status !== 4" class="my-work__photo-form">
                <select v-model.number="photoStage" :aria-label="t('work.photoStage')" :disabled="!!savingId"><option :value="1">{{ t('work.beforeWork') }}</option><option :value="2">{{ t('work.afterWork') }}</option></select>
                <input type="file" accept="image/*" multiple :aria-label="t('work.uploadPhotos')" :disabled="!!savingId" @change="sendPhotos(work, $event)">
              </div>
            </template>
          </div>
        </div>
      </article>
    </div>
    <p v-if="actionError" class="my-work__error" role="alert">{{ actionError }}</p>
  </div>
</template>

<style scoped>
.my-work--preview { opacity: .62; }
.my-work__preview-card { display: grid; gap: 14px; margin-top: 14px; padding: 16px; border: 1px solid var(--crm-border, #dce2e9); border-radius: 10px; }
.my-work__preview-card header { display: flex; justify-content: space-between; align-items: center; }
.my-work__preview-card i, .my-work__preview-card header span, .my-work__preview-facts b, .my-work__preview-actions button { display: block; border: 0; border-radius: 5px; background: var(--crm-border, #dce2e9); }
.my-work__preview-card header i { width: 44%; height: 15px; }
.my-work__preview-card header span { width: 18%; height: 12px; }
.my-work__preview-card p { display: grid; gap: 8px; margin: 0; }
.my-work__preview-card p i { width: 100%; height: 9px; }
.my-work__preview-card p i:last-child { width: 64%; }
.my-work__preview-facts { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; }
.my-work__preview-facts span { display: grid; gap: 8px; }
.my-work__preview-facts i { width: 48%; height: 9px; }
.my-work__preview-facts b { width: 82%; height: 12px; }
.my-work__preview-actions { display: flex; gap: 8px; }
.my-work__preview-actions button { width: 92px; height: 32px; }
.my-work { display:grid; gap:14px; }
.my-work__summary { display:grid; grid-template-columns:repeat(2, minmax(0, 1fr)); gap:0; margin:0; border:1px solid var(--crm-line, #dce2e9); border-radius:9px; background:var(--crm-surface-soft, #f5f6f8); }
.my-work__summary--with-shift { grid-template-columns:minmax(220px, 1.5fr) repeat(2, minmax(0, 1fr)); }
.my-work__summary > div { display:grid; align-content:center; gap:6px; min-width:0; padding:12px 16px; }
.my-work__summary > div + div { border-left:1px solid var(--crm-line, #dce2e9); }
.my-work__summary dt { color:var(--crm-text-muted, #687386); font-size:12px; line-height:1.4; }
.my-work__summary dd { margin:0; color:var(--crm-text, #202631); font-size:22px; font-weight:650; line-height:1.25; font-variant-numeric:tabular-nums; }
.my-work__shift dd { display:flex; flex-wrap:wrap; gap:4px; font-size:14px; }
.my-work__shift dd span { white-space:nowrap; }
.my-work__tabs { display:flex; gap:4px; flex-wrap:wrap; padding:4px; border:1px solid var(--crm-line, #dce2e9); border-radius:9px; background:var(--crm-surface-soft, #f5f6f8); }
.my-work__tabs button { flex:1 1 110px; min-width:0; min-height:36px; border:1px solid transparent; border-radius:6px; padding:7px 10px; background:transparent; color:var(--crm-text-muted, #687386); cursor:pointer; font:inherit; font-size:13px; font-weight:550; }
.my-work__tabs button:hover { color:var(--crm-text, #202631); background:var(--crm-surface, #fff); }
.my-work__tabs button[aria-current="page"] { border-color:var(--crm-line, #dce2e9); background:var(--crm-surface, #fff); color:var(--crm-action-accent, #b94a20); font-weight:650; }
.my-work__tabs button:focus-visible { outline:2px solid var(--crm-header-accent, #e67337); outline-offset:1px; }
.my-work__empty { display:grid; justify-items:center; gap:8px; padding:28px 16px; text-align:center; }
.my-work__empty svg { width:28px; height:28px; margin-bottom:4px; fill:none; stroke:var(--crm-text-muted, #687386); stroke-width:1.5; stroke-linecap:round; stroke-linejoin:round; }
.my-work__empty strong { color:var(--crm-text, #202631); font-size:14px; font-weight:600; }
.my-work__empty p { max-width:360px; margin:0; color:var(--crm-text-muted, #687386); font-size:13px; line-height:1.5; }
.my-work__list { display:grid; gap:10px; }
.my-work__item { border:1px solid var(--crm-line, #dce2e9); border-radius:9px; padding:14px; background:var(--crm-surface, #fff); }
.my-work__item header { display:flex; justify-content:space-between; gap:12px; align-items:flex-start; }
.my-work__item header span,.my-work__item p { color:var(--crm-text-muted, #687386); }
.my-work__item p { margin:8px 0 0; }
.my-work__status { border:1px solid var(--crm-line, #dce2e9); border-radius:999px; padding:3px 8px; font-size:12px; white-space:nowrap; }
.my-work__status[data-status="2"] { color:var(--crm-accent, #dc7135); border-color:currentColor; }
.my-work__status[data-status="4"] { color:var(--crm-success, #247564); border-color:currentColor; }
.my-work__vehicle { font-weight:650; }
.my-work__facts { display:grid; grid-template-columns:repeat(2, minmax(0, 1fr)); gap:8px 16px; margin-top:12px; }
.my-work__facts>div { display:grid; gap:2px; min-width:0; }
.my-work__facts span { color:var(--crm-text-muted, #687386); font-size:11px; }
.my-work__facts strong { font-size:13px; overflow-wrap:anywhere; }
.my-work__context { border-top:1px solid var(--crm-line, #dce2e9); margin-top:12px; padding-top:10px; }
.my-work__context>strong { font-size:12px; }
.my-work__context p { overflow-wrap:anywhere; }
.my-work__description { overflow-wrap:anywhere; }
.my-work__actions { display:flex; gap:8px; flex-wrap:wrap; margin-top:12px; }
.my-work__actions input { min-width:180px; flex:1; border:1px solid var(--crm-line, #dce2e9); border-radius:6px; padding:8px; background:var(--crm-surface, #fff); color:var(--crm-text, #202631); font:inherit; }
.my-work__error { color:var(--crm-danger-text, #a12622); }
.my-work__materials { margin-top:12px; }
.my-work__material-panel { display:grid; gap:8px; border-top:1px solid var(--crm-line, #dce2e9); margin-top:12px; padding-top:12px; }
.my-work__material-panel p { margin:0; }
.my-work__material-panel ul { margin:0; padding-left:20px; }
.my-work__material-form { display:flex; gap:8px; flex-wrap:wrap; }
.my-work__material-form select,.my-work__material-form input { border:1px solid var(--crm-line, #dce2e9); border-radius:6px; padding:8px; background:var(--crm-surface, #fff); color:var(--crm-text, #202631); font:inherit; }
.my-work__material-form select { flex:1; min-width:200px; }
.my-work__material-form input { width:110px; }
.my-work__material-return { display:flex; gap:8px; flex-wrap:wrap; margin-top:6px; }
.my-work__material-return input { flex:1; min-width:180px; border:1px solid var(--crm-line, #dce2e9); border-radius:6px; padding:8px; background:var(--crm-surface, #fff); color:var(--crm-text, #202631); font:inherit; }
.my-work__reports { margin-top:8px; }
.my-work__report-panel { display:grid; gap:8px; border-top:1px solid var(--crm-line, #dce2e9); margin-top:12px; padding-top:12px; }
.my-work__report-panel p { margin:0; overflow-wrap:anywhere; }
.my-work__report-panel ul { display:grid; gap:8px; list-style:none; margin:0; padding:0; }
.my-work__report-panel li { border-bottom:1px solid var(--crm-line, #dce2e9); padding-bottom:8px; }
.my-work__report-panel small { color:var(--crm-text-muted, #687386); }
.my-work__report-form { display:grid; gap:8px; }
.my-work__report-form select,.my-work__report-form textarea { width:100%; border:1px solid var(--crm-line, #dce2e9); border-radius:6px; padding:8px; background:var(--crm-surface, #fff); color:var(--crm-text, #202631); font:inherit; }
.my-work__photos { margin-top:8px; }
.my-work__photo-panel { display:grid; gap:8px; border-top:1px solid var(--crm-line, #dce2e9); margin-top:12px; padding-top:12px; }
.my-work__photo-panel p { margin:0; }
.my-work__photo-grid { display:grid; grid-template-columns:repeat(auto-fill, minmax(100px, 1fr)); gap:8px; }
.my-work__photo-grid a { display:grid; color:var(--crm-text, #202631); text-decoration:none; }
.my-work__photo-grid img { width:100%; height:90px; object-fit:cover; border-radius:6px; }
.my-work__photo-form { display:flex; gap:8px; flex-wrap:wrap; }
.my-work__photo-form select,.my-work__photo-form input { border:1px solid var(--crm-line, #dce2e9); border-radius:6px; padding:8px; background:var(--crm-surface, #fff); color:var(--crm-text, #202631); font:inherit; }
@media(max-width:600px) {
  .my-work__summary--with-shift { grid-template-columns:repeat(2, minmax(0, 1fr)); }
  .my-work__summary > div { padding:12px; }
  .my-work__summary > div:not(.my-work__shift) dt { min-height:2.8em; }
  .my-work__shift { grid-column:1 / -1; border-bottom:1px solid var(--crm-line, #dce2e9); }
  .my-work__summary .my-work__shift + div { border-left:0; }
}
@media(max-width:600px) { .my-work__facts { grid-template-columns:1fr; } .my-work__actions :deep(.ui-button) { flex:1; } }
</style>

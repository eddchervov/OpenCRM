<script setup lang="ts">
import { formattingCulture, formatLocaleCurrency } from '@opencrm/ui/i18n'
import { useWorkspaceCurrency } from '../workspaceCurrency'

import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { UiButton, UiTooltip, useLocalization } from '@opencrm/ui'
import '../maintenanceWorkMessages'
import {
  adjustWorkTime, completeWorkForEmployee, decideWorkReport, loadProductionReport, loadWorkProgress,
  loadWorkReports, returnWorkForRework, type ProductionReport, type WorkProgress, type WorkReport
} from '../api/work'
import { formatWorkDuration, getWorkDurationSeconds, hasRecordedWorkTime } from '../formatWorkDuration'

const workspaceCurrency = useWorkspaceCurrency()
const { t } = useLocalization()

const props = defineProps<{
  organizationId: string
  timeZoneId?: string | null
  maintenanceId: string
  canReturn: boolean
  canComplete?: boolean
  maintenanceStatus: number
  compact?: boolean
  simpleWorkflow?: boolean
  teamAvailable?: boolean
}>()
const emit = defineEmits<{ changed: []; progress: [works: WorkProgress[] | null] }>()
const works = ref<WorkProgress[]>([])
const reports = ref<WorkReport[]>([])
const productionReport = ref<ProductionReport | null>(null)
const error = ref('')
const managementError = ref('')
const reportError = ref('')
const reason = ref('')
const selected = ref<WorkProgress | null>(null)
const busy = ref(false)
const decisionReport = ref<WorkReport | null>(null)
const decisionPrice = ref<number | null>(null)
const decisionVatRateCode = ref('')
const decisionReason = ref('')
const managementDialog = ref<HTMLDialogElement | null>(null)
const reportsDialog = ref<HTMLDialogElement | null>(null)
const auditDialog = ref<HTMLDialogElement | null>(null)
const auditWork = ref<WorkProgress | null>(null)
const timeWork = ref<WorkProgress | null>(null)
const timeMinutes = ref(0)
const timeReason = ref('')
const timeDialog = ref<HTMLDialogElement | null>(null)
const timeError = ref('')
const completionDialog = ref<HTMLDialogElement | null>(null)
const completionWork = ref<WorkProgress | null>(null)
const completionReason = ref('')
const completionError = ref('')
let completionCommand: { reason: string; id: string } | null = null
let refreshTimer: ReturnType<typeof setInterval> | undefined
let lastSnapshot = ''
let requestId = 0

const completedCount = computed(() => works.value.filter(work => work.status === 4).length)
const canOpenManagement = computed(() => props.teamAvailable !== false
  || works.value.some(work => work.hasAssignedEmployee || work.version > 0 || work.status >= 2)
  || reports.value.length > 0)
const managementTitle = computed(() => props.teamAvailable === false ? t('maintenanceWork.historyTitle') : t('maintenanceWork.performersAndAdjustments'))
const completionPercent = computed(() => works.value.length
  ? Math.round(completedCount.value / works.value.length * 100)
  : 0)
const pendingReportCount = computed(() => reports.value.filter(report => report.kind === 'extra' && !report.decision).length)
const awaitsReview = computed(() => props.maintenanceStatus === 2
  && works.value.length > 0 && completedCount.value === works.value.length
  && !reports.value.some(report => report.kind === 'extra' && !report.decision))
const currentWorkSeconds = computed(() => {
  const work = works.value.find(work => work.detailId === timeWork.value?.detailId) ?? timeWork.value
  return getWorkDurationSeconds(work?.activeTime)
})
const timeExplanation = computed(() => [
  t('maintenanceWork.accountedTimeExplanation'),
  currentWorkSeconds.value === 0 ? t('maintenanceWork.timeNotRecordedExplanation') : '',
  timeWork.value?.status === 2 ? t('maintenanceWork.timerRunningExplanation') : ''
].filter(Boolean).join(' '))
const minimumTimeMinutes = computed(() => Math.max(-10080, -Math.floor((currentWorkSeconds.value ?? 0) / 60)))
const adjustedWorkSeconds = computed(() => currentWorkSeconds.value !== null && Number.isInteger(timeMinutes.value)
  ? currentWorkSeconds.value + timeMinutes.value * 60
  : null)
const timeValidationError = computed(() => {
  if (!timeWork.value) return ''
  if (currentWorkSeconds.value === null) return t('maintenanceWork.currentTimeUnknown')
  if (!Number.isInteger(timeMinutes.value) || timeMinutes.value < -10080 || timeMinutes.value > 10080) {
    return t('maintenanceWork.minutesRange')
  }
  if (adjustedWorkSeconds.value !== null && adjustedWorkSeconds.value < 0) {
    return minimumTimeMinutes.value === 0
      ? t('maintenanceWork.cannotReduce')
      : t('maintenanceWork.canRemoveMinutes', { minutes: -minimumTimeMinutes.value })
  }
  return ''
})

function formatAdjustmentTime(seconds: number | null) {
  if (seconds === null) return '—'
  const absoluteSeconds = Math.floor(Math.abs(seconds))
  const hours = Math.floor(absoluteSeconds / 3600)
  const minutes = Math.floor(absoluteSeconds % 3600 / 60)
  const remainder = absoluteSeconds % 60
  const parts = []
  if (hours) parts.push(`${hours} ${t('maintenanceWork.hoursShort')}`)
  if (minutes) parts.push(`${minutes} ${t('maintenanceWork.minutesShort')}`)
  if (remainder) parts.push(`${remainder} ${t('maintenanceWork.secondsShort')}`)

  return `${seconds < 0 ? '−' : ''}${parts.join(' ') || t('maintenanceWork.zeroMinutes')}`
}

async function load() {
  const currentRequestId = ++requestId
  const organizationId = props.organizationId
  const maintenanceId = props.maintenanceId
  const [progressResult, reportsResult, productionResult] = await Promise.allSettled([
    loadWorkProgress(organizationId, maintenanceId),
    loadWorkReports(organizationId, maintenanceId),
    loadProductionReport(organizationId, maintenanceId)
  ])
  if (currentRequestId !== requestId
    || props.organizationId !== organizationId
    || props.maintenanceId !== maintenanceId) return

  const failures: unknown[] = []
  if (progressResult.status === 'fulfilled') {
    works.value = progressResult.value
    emit('progress', progressResult.value)
  } else {
    failures.push(progressResult.reason)
    emit('progress', null)
  }
  if (reportsResult.status === 'fulfilled') {
    reports.value = reportsResult.value
  } else {
    failures.push(reportsResult.reason)
  }
  if (productionResult.status === 'fulfilled') {
    productionReport.value = productionResult.value
  } else {
    failures.push(productionResult.reason)
  }

  const snapshot = JSON.stringify([
    works.value.map(work => [work.detailId, work.status, work.version]),
    reports.value.map(report => [report.id, report.decision]),
    productionReport.value ? { ...productionReport.value, activeTime: undefined } : null
  ])
  if (lastSnapshot && snapshot !== lastSnapshot) emit('changed')
  lastSnapshot = snapshot
  error.value = failures.length
    ? failures[0] instanceof Error ? failures[0].message : t('maintenanceWork.workLoadError')
    : ''
}

async function decide(decision: 'approved' | 'rejected') {
  if (!props.canReturn || !decisionReport.value || busy.value) return
  if (decision === 'approved' && props.teamAvailable === false) return
  if (decision === 'approved' && (decisionPrice.value == null || decisionPrice.value < 0)) {
    reportError.value = t('maintenanceWork.agreedPriceRequired')
    return
  }
  if (decision === 'approved' && !decisionVatRateCode.value) {
    reportError.value = t('maintenanceWork.vatRateRequired')
    return
  }
  if (decision === 'rejected' && !decisionReason.value.trim()) {
    reportError.value = t('maintenanceWork.reasonRequired')
    return
  }

  busy.value = true
  reportError.value = ''
  const organizationId = props.organizationId
  const maintenanceId = props.maintenanceId
  try {
    await decideWorkReport(
      organizationId, decisionReport.value, decision,
      decisionReason.value.trim(), decisionPrice.value ?? undefined, decisionVatRateCode.value
    )
    if (props.organizationId !== organizationId || props.maintenanceId !== maintenanceId) return
    decisionReport.value = null
    decisionReason.value = ''
    decisionPrice.value = null
    decisionVatRateCode.value = ''
    await load()
    emit('changed')
  } catch (cause) {
    if (props.organizationId !== organizationId || props.maintenanceId !== maintenanceId) return
    reportError.value = cause instanceof Error ? cause.message : t('maintenanceWork.decisionSaveError')
    await load()
  } finally {
    busy.value = false
  }
}

function reportKindName(kind: WorkReport['kind']) {
  return kind === 'defect' ? t('maintenanceWork.reportDefect') : kind === 'extra' ? t('maintenanceWork.reportExtra')
    : kind === 'time' ? t('maintenanceWork.reportTime') : t('maintenanceWork.reportComment')
}

function formatMoney(value: number | null | undefined) {
  return value == null ? '—' : formatLocaleCurrency(value, workspaceCurrency.value, { maximumFractionDigits: 2 })
}

function formatInstant(value: string) {
  return new Intl.DateTimeFormat(formattingCulture.value, {
    dateStyle: 'short', timeStyle: 'short', timeZone: props.timeZoneId || 'UTC'
  }).format(new Date(value))
}

function closeManagement() {
  if (busy.value) return
  managementDialog.value?.close()
  selected.value = null
  reason.value = ''
  managementError.value = ''
}

function closeReports() {
  if (busy.value) return
  reportsDialog.value?.close()
  decisionReport.value = null
  decisionPrice.value = null
  decisionVatRateCode.value = ''
  decisionReason.value = ''
  reportError.value = ''
}

async function openCompletionDetails(work: WorkProgress) {
  auditWork.value = work
  await nextTick()
  if (auditWork.value?.detailId === work.detailId) auditDialog.value?.showModal()
}

function closeCompletionDetails() {
  auditDialog.value?.close()
  auditWork.value = null
}

async function openTimeAdjustment(work: WorkProgress) {
  if (!props.canReturn || work.status === 1 || busy.value) return
  timeWork.value = work
  timeMinutes.value = 0
  timeReason.value = ''
  timeError.value = ''
  await nextTick()
  if (timeWork.value?.detailId === work.detailId && props.canReturn) timeDialog.value?.showModal()
}

function resetTimeAdjustment() {
  timeDialog.value?.close()
  timeWork.value = null
  timeMinutes.value = 0
  timeReason.value = ''
  timeError.value = ''
}

function closeTimeAdjustment() {
  if (!busy.value) resetTimeAdjustment()
}

async function adjustTime() {
  if (!props.canReturn || !timeWork.value || busy.value) return
  if (timeValidationError.value) return
  if (!Number.isInteger(timeMinutes.value) || !timeMinutes.value || !timeReason.value.trim()) {
    timeError.value = t('maintenanceWork.timeChangeRequired')
    return
  }

  busy.value = true
  timeError.value = ''
  const organizationId = props.organizationId
  const maintenanceId = props.maintenanceId
  try {
    await adjustWorkTime(
      organizationId, maintenanceId, timeWork.value.detailId,
      timeMinutes.value, timeReason.value.trim()
    )
    if (props.organizationId !== organizationId || props.maintenanceId !== maintenanceId) return
    resetTimeAdjustment()
    await load()
  } catch (cause) {
    if (props.organizationId === organizationId && props.maintenanceId === maintenanceId) {
      timeError.value = cause instanceof Error ? cause.message : t('maintenanceWork.timeChangeError')
    }
  } finally {
    busy.value = false
  }
}

async function returnForRework() {
  if (!props.canReturn || !selected.value || busy.value) return
  if (!reason.value.trim()) {
    managementError.value = t('maintenanceWork.returnReasonRequired')
    return
  }

  busy.value = true
  managementError.value = ''
  const organizationId = props.organizationId
  const maintenanceId = props.maintenanceId
  try {
    await returnWorkForRework(organizationId, maintenanceId, selected.value, reason.value.trim())
    if (props.organizationId !== organizationId || props.maintenanceId !== maintenanceId) return
    selected.value = null
    reason.value = ''
    await load()
  } catch (cause) {
    if (props.organizationId !== organizationId || props.maintenanceId !== maintenanceId) return
    managementError.value = cause instanceof Error ? cause.message : t('maintenanceWork.returnError')
    await load()
  } finally {
    busy.value = false
  }
}

function canCompleteWork(work: WorkProgress) {
  return props.canComplete === true && [2, 3].includes(props.maintenanceStatus)
    && [1, 2, 3].includes(work.status) && work.hasAssignedEmployee === true
}

async function openCompletion(work: WorkProgress) {
  if (!canCompleteWork(work) || busy.value) return
  completionWork.value = work
  completionReason.value = ''
  completionError.value = ''
  completionCommand = null
  await nextTick()
  if (completionWork.value?.detailId === work.detailId && canCompleteWork(work)) {
    completionDialog.value?.showModal()
  }
}

function resetCompletion() {
  completionDialog.value?.close()
  completionWork.value = null
  completionReason.value = ''
  completionError.value = ''
  completionCommand = null
}

function closeCompletion() {
  if (!busy.value) resetCompletion()
}

async function completeForEmployee() {
  const work = completionWork.value
  if (!work || !canCompleteWork(work) || busy.value) return
  const reason = completionReason.value.trim()
  if (!reason || reason.length > 1000) {
    completionError.value = t('maintenanceWork.completionReasonRequired')
    return
  }
  if (!completionCommand || completionCommand.reason !== reason) {
    completionCommand = { reason, id: crypto.randomUUID() }
  }

  const organizationId = props.organizationId
  const maintenanceId = props.maintenanceId
  busy.value = true
  completionError.value = ''
  try {
    await completeWorkForEmployee(organizationId, maintenanceId, work, reason, completionCommand.id)
    if (props.organizationId !== organizationId || props.maintenanceId !== maintenanceId) return
    resetCompletion()
    await load()
    emit('changed')
  } catch (cause) {
    if (props.organizationId === organizationId && props.maintenanceId === maintenanceId) {
      completionError.value = cause instanceof Error ? cause.message : t('maintenanceWork.completionError')
    }
  } finally {
    busy.value = false
  }
}

function statusName(status: number) {
  return status === 2 ? t('maintenanceWork.statusInProgress') : status === 3 ? t('maintenanceWork.statusPaused') : status === 4 ? t('maintenanceWork.statusCompleted') : t('maintenanceWork.statusAssigned')
}

onMounted(() => {
  void load()
  refreshTimer = setInterval(() => {
    if (!busy.value && document.visibilityState === 'visible') void load()
  }, 15000)
})
onUnmounted(() => { requestId++; if (refreshTimer) clearInterval(refreshTimer) })
watch(() => [props.organizationId, props.maintenanceId], () => {
  requestId++
  closeCompletionDetails()
  managementDialog.value?.close()
  reportsDialog.value?.close()
  resetCompletion()
  emit('progress', null)
  works.value = []
  reports.value = []
  productionReport.value = null
  lastSnapshot = ''
  reason.value = ''
  selected.value = null
  decisionReport.value = null
  decisionPrice.value = null
  decisionVatRateCode.value = ''
  decisionReason.value = ''
  resetTimeAdjustment()
  error.value = ''
  managementError.value = ''
  reportError.value = ''
  void load()
})
watch(() => props.canComplete, canComplete => {
  if (!canComplete) resetCompletion()
})
watch(() => props.canReturn, canReturn => {
  if (canReturn) return
  selected.value = null
  reason.value = ''
  decisionReport.value = null
  decisionPrice.value = null
  decisionVatRateCode.value = ''
  decisionReason.value = ''
  resetTimeAdjustment()
})
</script>

<template>
  <section v-if="!simpleWorkflow || error || reports.length || works.some(work => work.version > 0 || work.status >= 2)" class="work-progress" :aria-label="t('maintenanceWork.workProgress')">
    <header class="ui-section-header"><strong>{{ compact ? t('maintenanceWork.workControl') : t('maintenanceWork.workProgress') }}</strong><span v-if="!compact">{{ completionPercent }}% · {{ t('maintenanceWork.completedOf', { completed: completedCount, total: works.length }) }}</span></header>
    <div v-if="!compact" class="work-progress__track" role="progressbar" :aria-valuenow="completionPercent" aria-valuemin="0" aria-valuemax="100" :aria-label="t('maintenanceWork.completedWork')">
      <span :style="{ width: `${completionPercent}%` }" />
    </div>
    <p v-if="awaitsReview && !simpleWorkflow" class="work-progress__review">{{ t('maintenanceWork.reviewReady') }}</p>
    <p v-if="error" class="work-progress__error" role="alert">{{ error }} <button type="button" @click="load">{{ t('maintenanceWork.retry') }}</button></p>
    <div class="work-progress__navigation">
      <UiButton v-if="canOpenManagement" variant="secondary" @click="managementDialog?.showModal()">{{ managementTitle }}</UiButton>
      <UiButton v-if="reports.length" variant="secondary" @click="reportsDialog?.showModal()">{{ t('maintenanceWork.reports') }} <span class="work-progress__count">{{ reports.length }}</span></UiButton>
      <span v-if="pendingReportCount" class="work-progress__pending">{{ t('maintenanceWork.pendingDecisions', { count: pendingReportCount }) }}</span>
    </div>
    <div v-if="productionReport" class="work-progress__summary">
      <strong>{{ t('maintenanceWork.preliminaryReport') }}</strong>
      <dl class="work-progress__metrics">
        <div><dt>{{ t('maintenanceWork.accountedWorkTime') }}</dt><dd>{{ hasRecordedWorkTime(productionReport.activeTime) ? formatWorkDuration(productionReport.activeTime) : t('maintenanceWork.notRecorded') }}</dd></div>
        <div><dt>{{ t('maintenanceWork.adjustments') }}</dt><dd>{{ productionReport.timeAdjustmentsCount }}</dd></div>
        <div v-if="productionReport.materialCostAmount != null"><dt>{{ t('maintenanceWork.materials') }}</dt><dd>{{ formatMoney(productionReport.materialCostAmount) }}</dd></div>
        <div v-if="productionReport.laborCostAmount != null"><dt>{{ t('maintenanceWork.labor') }}</dt><dd>{{ formatMoney(productionReport.laborCostAmount) }}</dd></div>
        <div v-if="productionReport.revenueAmount != null"><dt>{{ t('maintenanceWork.revenue') }}</dt><dd>{{ formatMoney(productionReport.revenueAmount) }}</dd></div>
        <div v-if="productionReport.grossMarginAmount != null"><dt>{{ t('maintenanceWork.grossMargin') }}</dt><dd>{{ formatMoney(productionReport.grossMarginAmount) }}</dd></div>
      </dl>
      <details v-if="productionReport.materials.length" class="work-progress__materials">
        <summary>{{ t('maintenanceWork.usedMaterials', { count: productionReport.materials.length }) }}</summary>
        <ul><li v-for="(material, index) in productionReport.materials" :key="index">{{ material.name }} <span>{{ material.quantity }} {{ material.unit }}</span></li></ul>
      </details>
    </div>
    <dialog ref="managementDialog" class="work-completion-dialog work-progress-dialog" aria-labelledby="work-management-title" @cancel.prevent="closeManagement">
      <header class="work-completion-dialog__header ui-form-header">
        <h3 id="work-management-title">{{ managementTitle }}</h3>
        <button type="button" :aria-label="t('maintenanceWork.closeDialog', { title: managementTitle })" :disabled="busy" @click="closeManagement">×</button>
      </header>
      <div class="work-completion-dialog__body work-progress__management">
        <p v-if="error" class="work-progress__error" role="alert">{{ error }} <button type="button" @click="load">{{ t('maintenanceWork.retry') }}</button></p>
        <p v-if="!works.length">{{ t('maintenanceWork.noWorkData') }}</p>
        <ul class="work-progress__items">
          <li v-for="work in works" :key="work.detailId" class="work-progress__item">
            <div class="work-progress__identity">
              <strong>{{ work.name }}</strong>
              <small>{{ work.employeeName || t('maintenanceWork.employeeUnassigned') }}</small>
              <div class="work-progress__time"><span>{{ t('maintenanceWork.accountedWorkTime') }}</span><strong>{{ hasRecordedWorkTime(work.activeTime) ? formatWorkDuration(work.activeTime) : t('maintenanceWork.notRecorded') }}</strong></div>
              <small v-if="work.pauseReason">{{ t('maintenanceWork.pauseReason', { reason: work.pauseReason }) }}</small>
            </div>
            <div class="work-progress__tools">
              <span class="work-progress__status" :data-status="work.status">{{ statusName(work.status) }}</span>
              <div v-if="canCompleteWork(work) || canReturn && work.status !== 1" class="work-progress__actions">
                <UiButton v-if="canCompleteWork(work)" variant="secondary" :disabled="busy" @click="openCompletion(work)">{{ t('maintenanceWork.markCompleted') }}</UiButton>
                <UiButton v-if="canReturn && maintenanceStatus === 2 && work.status === 4" variant="secondary" :disabled="busy" @click="selected = work; reason = ''; managementError = ''">{{ t('maintenanceWork.returnToWork') }}</UiButton>
                <UiButton v-if="canReturn && work.status !== 1" variant="secondary" :disabled="busy" @click="openTimeAdjustment(work)">{{ t('maintenanceWork.adjustTime') }}</UiButton>
              </div>
            </div>
            <button v-if="work.completionReason" type="button" class="work-progress__completion-link" @click="openCompletionDetails(work)">{{ t('maintenanceWork.completedByAdmin') }}</button>
          </li>
        </ul>
        <p v-if="managementError" class="work-progress__error" role="alert">{{ managementError }}</p>
        <div v-if="selected" class="work-progress__return">
          <label for="work-return-reason">{{ t('maintenanceWork.returnReason') }}</label>
          <textarea id="work-return-reason" v-model="reason" maxlength="1000" :disabled="busy" rows="2" />
          <div><UiButton :disabled="busy" @click="returnForRework">{{ t('maintenanceWork.confirmReturn') }}</UiButton><UiButton variant="secondary" :disabled="busy" @click="selected = null">{{ t('maintenanceWork.cancel') }}</UiButton></div>
        </div>
      </div>
      <footer class="work-completion-dialog__footer"><UiButton variant="secondary" :disabled="busy" @click="closeManagement">{{ t('maintenanceWork.close') }}</UiButton></footer>
    </dialog>
    <dialog ref="reportsDialog" class="work-completion-dialog work-progress-dialog" aria-labelledby="work-reports-title" @cancel.prevent="closeReports">
      <header class="work-completion-dialog__header ui-form-header">
        <h3 id="work-reports-title">{{ t('maintenanceWork.reportsDialog') }}</h3>
        <button type="button" :aria-label="t('maintenanceWork.closeReports')" :disabled="busy" @click="closeReports">×</button>
      </header>
      <div class="work-completion-dialog__body work-progress__reports">
        <p v-if="error" class="work-progress__error" role="alert">{{ error }} <button type="button" @click="load">{{ t('maintenanceWork.retry') }}</button></p>
        <p v-if="!reports.length">{{ t('maintenanceWork.noReports') }}</p>
        <ul>
          <li v-for="report in reports" :key="report.id">
            <div><strong>{{ reportKindName(report.kind) }}</strong><span>{{ report.employeeName }}</span></div>
            <small>{{ formatInstant(report.createdAt) }}</small>
            <p>{{ report.text }}</p>
            <small v-if="report.kind === 'time'">{{ t('maintenanceWork.minutes', { minutes: `${(report.timeAdjustmentMinutes ?? 0) > 0 ? '+' : ''}${report.timeAdjustmentMinutes}` }) }}</small>
            <small v-if="report.kind === 'extra'">{{ report.decision === 'approved' ? t('maintenanceWork.approved', { amount: formatLocaleCurrency(report.approvedPrice ?? 0, workspaceCurrency) }) : report.decision === 'rejected' ? t('maintenanceWork.rejected', { reason: report.decisionReason || '' }) : t('maintenanceWork.awaitingApproval') }}</small>
            <UiButton v-if="canReturn && report.kind === 'extra' && !report.decision" variant="secondary" :disabled="busy" @click="decisionReport = report; decisionPrice = null; decisionVatRateCode = ''; decisionReason = ''; reportError = ''">{{ t('maintenanceWork.review') }}</UiButton>
          </li>
        </ul>
        <p v-if="reportError" class="work-progress__error" role="alert">{{ reportError }}</p>
        <div v-if="decisionReport" class="work-progress__return">
          <strong>{{ t('maintenanceWork.extraWorkDecision') }}</strong>
          <template v-if="teamAvailable !== false">
            <label for="work-extra-price">{{ t('maintenanceWork.agreedAmount') }}</label>
            <input id="work-extra-price" v-model.number="decisionPrice" type="number" min="0" step="0.01" :disabled="busy">
            <label for="work-extra-vat">{{ t('maintenanceWork.vat') }}</label>
            <select id="work-extra-vat" v-model="decisionVatRateCode" :disabled="busy"><option value="">{{ t('maintenanceWork.select') }}</option><option value="without_vat">{{ t('maintenanceWork.withoutVat') }}</option><option value="0">0 %</option><option value="5">5 %</option><option value="7">7 %</option><option value="10">10 %</option><option value="20">20 %</option><option value="22">22 %</option></select>
          </template>
          <label for="work-extra-reason">{{ t('maintenanceWork.reasonOrComment') }}</label>
          <textarea id="work-extra-reason" v-model="decisionReason" maxlength="1000" rows="2" :disabled="busy" />
          <div><UiButton v-if="teamAvailable !== false" :disabled="busy" @click="decide('approved')">{{ t('maintenanceWork.approve') }}</UiButton><UiButton variant="secondary" :disabled="busy" @click="decide('rejected')">{{ t('maintenanceWork.reject') }}</UiButton><UiButton variant="secondary" :disabled="busy" @click="decisionReport = null">{{ t('maintenanceWork.cancel') }}</UiButton></div>
        </div>
      </div>
      <footer class="work-completion-dialog__footer"><UiButton variant="secondary" :disabled="busy" @click="closeReports">{{ t('maintenanceWork.close') }}</UiButton></footer>
    </dialog>
    <dialog ref="auditDialog" class="work-completion-dialog" aria-labelledby="work-audit-title" @cancel.prevent="closeCompletionDetails">
      <header class="work-completion-dialog__header ui-form-header">
        <h3 id="work-audit-title">{{ t('maintenanceWork.completionDetails') }}</h3>
        <button type="button" :aria-label="t('maintenanceWork.closeCompletionDetails')" @click="closeCompletionDetails">×</button>
      </header>
      <div class="work-completion-dialog__body">
        <strong>{{ auditWork?.name }}</strong>
        <dl class="work-progress__audit">
          <div><dt>{{ t('maintenanceWork.completedBy') }}</dt><dd>{{ auditWork?.completedBy || t('maintenanceWork.administrator') }}</dd></div>
          <div><dt>{{ t('maintenanceWork.reason') }}</dt><dd>{{ auditWork?.completionReason }}</dd></div>
        </dl>
      </div>
      <footer class="work-completion-dialog__footer"><UiButton variant="secondary" @click="closeCompletionDetails">{{ t('maintenanceWork.close') }}</UiButton></footer>
    </dialog>
    <dialog ref="timeDialog" class="work-completion-dialog" aria-labelledby="work-time-title" @cancel.prevent="closeTimeAdjustment">
      <form @submit.prevent="adjustTime">
        <header class="work-completion-dialog__header ui-form-header">
          <h3 id="work-time-title">{{ t('maintenanceWork.adjustWorkTime') }}</h3>
          <button type="button" :aria-label="t('maintenanceWork.closeTimeAdjustment')" :disabled="busy" @click="closeTimeAdjustment">×</button>
        </header>
        <div class="work-completion-dialog__body">
          <strong>{{ timeWork?.name }}</strong>
          <dl class="work-time-summary">
            <div>
              <dt>{{ t('maintenanceWork.accountedNow') }} <UiTooltip :text="timeExplanation"><button type="button" class="work-time-help" :aria-label="t('maintenanceWork.howTimeIsCalculated')">?</button></UiTooltip></dt>
              <dd>{{ formatAdjustmentTime(currentWorkSeconds) }}</dd>
            </div>
            <div><dt>{{ t('maintenanceWork.afterSave') }}</dt><dd :class="{ 'work-progress__error': adjustedWorkSeconds !== null && adjustedWorkSeconds < 0 }" aria-live="polite">{{ formatAdjustmentTime(adjustedWorkSeconds) }}</dd></div>
          </dl>
          <label for="work-time-minutes">{{ t('maintenanceWork.timeChangeMinutes') }}</label>
          <input id="work-time-minutes" v-model.number="timeMinutes" type="number" step="1" :min="minimumTimeMinutes" max="10080" required :disabled="busy" autofocus :aria-invalid="Boolean(timeValidationError)" aria-describedby="work-time-hint work-time-validation" @input="timeError = ''">
          <small id="work-time-hint">{{ t('maintenanceWork.positiveAddsNegativeRemoves') }}</small>
          <label for="work-time-reason">{{ t('maintenanceWork.adjustmentReason') }} <span aria-hidden="true">*</span></label>
          <textarea id="work-time-reason" v-model="timeReason" required maxlength="1000" rows="3" :disabled="busy" />
          <p v-if="timeValidationError || timeError" id="work-time-validation" class="work-progress__error" role="alert">{{ timeValidationError || timeError }}</p>
        </div>
        <footer class="work-completion-dialog__footer">
          <UiButton type="button" variant="secondary" :disabled="busy" @click="closeTimeAdjustment">{{ t('maintenanceWork.cancel') }}</UiButton>
          <UiButton type="submit" :disabled="busy || Boolean(timeValidationError) || !timeMinutes || !timeReason.trim()">{{ busy ? t('maintenanceWork.saving') : t('maintenanceWork.save') }}</UiButton>
        </footer>
      </form>
    </dialog>
    <dialog ref="completionDialog" class="work-completion-dialog" aria-labelledby="work-completion-title" @cancel.prevent="closeCompletion">
      <form @submit.prevent="completeForEmployee">
        <header class="work-completion-dialog__header ui-form-header">
          <h3 id="work-completion-title">{{ t('maintenanceWork.completeForEmployee') }}</h3>
          <button type="button" :aria-label="t('maintenanceWork.closeCompletion')" :disabled="busy" @click="closeCompletion">×</button>
        </header>
        <div class="work-completion-dialog__body">
          <strong>{{ completionWork?.name }}</strong>
          <p>{{ t('maintenanceWork.performer', { name: completionWork?.employeeName ?? '' }) }}</p>
          <label for="work-completion-reason">{{ t('maintenanceWork.completionReason') }} <span aria-hidden="true">*</span></label>
          <textarea id="work-completion-reason" v-model="completionReason" required maxlength="1000" rows="3" :disabled="busy" autofocus />
          <small>{{ t('maintenanceWork.completionAuditHint') }}</small>
          <p v-if="completionError" class="work-progress__error" role="alert">{{ completionError }}</p>
        </div>
        <footer class="work-completion-dialog__footer">
          <UiButton type="button" variant="secondary" :disabled="busy" @click="closeCompletion">{{ t('maintenanceWork.cancel') }}</UiButton>
          <UiButton type="submit" :disabled="busy || !completionReason.trim()">{{ busy ? t('maintenanceWork.saving') : t('maintenanceWork.confirmCompletion') }}</UiButton>
        </footer>
      </form>
    </dialog>
  </section>
</template>

<style scoped>
.work-completion-dialog { box-sizing:border-box; width:min(560px, calc(100vw - 24px)); max-height:calc(100dvh - 24px); padding:0; overflow:auto; border:1px solid var(--crm-line, #dce2e9); border-radius:12px; background:var(--crm-surface, #fff); color:var(--crm-text, #202631); }
.work-completion-dialog::backdrop { background:var(--crm-backdrop, rgb(0 0 0 / .55)); }
.work-completion-dialog .work-completion-dialog__header { display:flex; align-items:center; justify-content:space-between; gap:16px; padding:16px 20px; }
.work-completion-dialog h3 { margin:0; font-size:18px; }
.work-completion-dialog__header button { flex-shrink:0; width:32px; height:32px; border:0; border-radius:8px; background:var(--crm-surface-soft, #f2f3f5); color:inherit; font-size:24px; cursor:pointer; }
.work-completion-dialog__body { display:grid; gap:10px; padding:20px; }
.work-completion-dialog__body p { margin:0; overflow-wrap:anywhere; }
.work-completion-dialog__body label { margin-top:6px; font-weight:600; }
.work-completion-dialog__body input, .work-completion-dialog__body textarea, .work-completion-dialog__body select { box-sizing:border-box; width:100%; padding:10px; border:1px solid var(--crm-line, #dce2e9); border-radius:6px; background:var(--crm-surface, #fff); color:inherit; font:inherit; }
.work-completion-dialog__body textarea { resize:vertical; min-height:90px; }
.work-completion-dialog__body small { color:var(--crm-text-muted, #667085); }
.work-time-summary { display:grid; grid-template-columns:repeat(2, minmax(0, 1fr)); gap:12px; margin:4px 0; padding:14px; border-radius:6px; background:var(--crm-surface-soft, #f2f3f5); }
.work-time-summary dt { display:flex; align-items:center; gap:6px; color:var(--crm-text-muted, #667085); font-size:12px; }
.work-time-summary dd { margin:6px 0 0; font-weight:650; font-variant-numeric:tabular-nums; overflow-wrap:anywhere; }
.work-time-help { display:grid; place-items:center; width:18px; height:18px; padding:0; border:1px solid var(--crm-line, #dce2e9); border-radius:50%; background:var(--crm-surface, #fff); color:inherit; font:inherit; font-weight:650; cursor:help; }
.work-time-help:focus-visible { outline:2px solid var(--crm-accent, #c6622b); outline-offset:2px; }
.work-completion-dialog__footer { display:flex; justify-content:flex-end; flex-wrap:wrap; gap:10px; padding:16px 20px; border-top:1px solid var(--crm-line, #dce2e9); }
.work-progress { padding:18px; border:1px solid var(--crm-line, #e3e6eb); border-radius:9px; background:var(--crm-surface, #fff); }
.work-progress header { display:flex; align-items:center; justify-content:space-between; gap:12px; }
.work-progress header { font-size:15px; }
.work-progress header span { color:var(--crm-text-muted, #667085); font-size:12px; font-variant-numeric:tabular-nums; }
.work-progress summary { display:flex; align-items:center; gap:10px; min-height:36px; box-sizing:border-box; padding:6px 2px; border-radius:4px; list-style:none; cursor:pointer; }
.work-progress summary::-webkit-details-marker { display:none; }
.work-progress summary::marker { content:''; }
.work-progress summary::after { content:''; flex-shrink:0; width:6px; height:6px; margin:0 4px 3px auto; border-right:1.5px solid currentColor; border-bottom:1.5px solid currentColor; transform:rotate(45deg); }
.work-progress details[open]>summary::after { margin-bottom:0; transform:rotate(225deg); }
.work-progress summary:hover { color:var(--crm-accent, #c6622b); }
.work-progress summary:focus-visible { outline:2px solid var(--crm-accent, #c6622b); outline-offset:3px; }
.work-progress__navigation { display:flex; align-items:center; flex-wrap:wrap; gap:10px; margin-top:14px; }
.work-progress__navigation :deep(.ui-button) { font-size:13px; }
.work-progress__count { padding:2px 6px; border-radius:4px; background:var(--crm-surface-soft, #f2f3f5); font-size:11px; }
.work-progress__pending { color:var(--crm-warning-text, #806000); font-size:12px; }
.work-progress-dialog { width:min(840px, calc(100vw - 24px)); }
.work-progress__management > ul, .work-progress__reports > ul { margin:0; }
.work-progress__management > ul > li:first-child, .work-progress__reports > ul > li:first-child { border-top:0; padding-top:0; }
.work-progress__track { height:9px; margin-top:14px; overflow:hidden; border-radius:999px; background:var(--crm-surface-soft, #edf0f3); }
.work-progress__track span { display:block; height:100%; border-radius:inherit; background:var(--crm-accent, #c6622b); transition:width .25s ease; }
.work-progress ul { list-style:none; padding:0; margin:12px 0 0; }
.work-progress li { display:grid; gap:7px; padding:12px 0; border-top:1px solid var(--crm-line, #e3e6eb); }
.work-progress li small { color:var(--crm-text-muted, #667085); }
.work-progress li :deep(.ui-button) { justify-self:start; }
.work-progress .work-progress__item { display:grid; grid-template-columns:minmax(0, 1fr) auto; align-items:start; gap:10px 20px; padding:16px 0; }
.work-progress .work-progress__identity { display:grid; gap:6px; min-width:0; }
.work-progress__identity > strong { font-size:14px; font-weight:600; }
.work-progress__identity > small { font-size:12px; }
.work-progress__identity strong, .work-progress__identity small { overflow-wrap:anywhere; }
.work-progress__time { display:flex; align-items:baseline; flex-wrap:wrap; gap:4px 8px; margin-top:2px; font-size:12px; }
.work-progress__time > span { color:var(--crm-text-muted, #667085); }
.work-progress__time > strong { font-weight:500; }
.work-progress__completion-link { grid-column:1 / -1; justify-self:start; padding:2px 0; border:0; background:none; color:var(--crm-text-muted, #667085); font:inherit; font-size:12px; text-decoration:underline dotted; text-underline-offset:4px; cursor:pointer; }
.work-progress__completion-link:hover { color:var(--crm-accent, #c6622b); }
.work-progress__completion-link:focus-visible { outline:2px solid var(--crm-accent, #c6622b); outline-offset:3px; }
.work-progress__audit { display:grid; gap:16px; margin:8px 0; }
.work-progress__audit dt { margin-bottom:6px; color:var(--crm-text-muted, #667085); font-size:12px; }
.work-progress__audit dd { margin:0; overflow-wrap:anywhere; white-space:pre-wrap; }
.work-progress .work-progress__tools { display:flex; flex-direction:column; align-items:flex-end; gap:10px; max-width:100%; }
.work-progress__actions { display:flex; flex-wrap:wrap; justify-content:flex-end; gap:8px; }
.work-progress__actions :deep(.ui-button) { min-height:32px; padding:6px 10px; font-size:12px; line-height:18px; }
.work-progress__status { flex:0 0 auto; padding:5px 8px; border-radius:6px; background:var(--crm-surface-soft, #f2f3f5); color:var(--crm-text-muted, #667085); font-size:11px; font-weight:500; }
.work-progress__status[data-status="2"] { color:var(--crm-accent, #c6622b); background:color-mix(in srgb, var(--crm-accent, #c6622b) 10%, var(--crm-surface, #fff)); }
.work-progress__status[data-status="3"] { color:var(--crm-warning-text, #806000); background:color-mix(in srgb, var(--crm-warning, #c79520) 14%, var(--crm-surface, #fff)); }
.work-progress__status[data-status="4"] { color:var(--crm-success-text, #247564); background:color-mix(in srgb, var(--crm-success, #247564) 11%, var(--crm-surface, #fff)); }
.work-progress__review { color:var(--crm-text, #202733); font-weight:600; }
.work-progress__error { color:var(--crm-danger-text, #a12622); }
.work-progress__return { display:grid; gap:8px; margin-top:12px; }
.work-progress__return>div { display:flex; gap:8px; flex-wrap:wrap; }
.work-progress__reports p { margin:0; overflow-wrap:anywhere; }
.work-progress__reports li>div { display:flex; align-items:baseline; justify-content:space-between; flex-wrap:wrap; gap:6px 12px; }
.work-progress__reports li { font-size:12px; }
.work-progress__reports li strong { font-size:13px; font-weight:600; }
.work-progress__summary { display:grid; gap:14px; margin-top:18px; padding-top:18px; border-top:1px solid var(--crm-line, #e3e6eb); }
.work-progress__summary > strong { font-size:14px; font-weight:600; }
.work-progress__summary p { margin:0; }
.work-progress__metrics { display:grid; grid-template-columns:repeat(3, minmax(0, 1fr)); gap:16px 24px; margin:0; }
.work-progress__metrics > div { min-width:0; }
.work-progress__metrics dt { color:var(--crm-text-muted, #667085); font-size:12px; }
.work-progress__metrics dd { margin:6px 0 0; font-size:14px; font-weight:600; font-variant-numeric:tabular-nums; overflow-wrap:anywhere; }
.work-progress__materials summary { cursor:pointer; font-size:12px; font-weight:600; }
.work-progress__materials li { display:flex; justify-content:space-between; gap:12px; padding:7px 0; font-size:12px; }
@media (max-width:600px) { .work-progress { padding:14px; } .work-progress header { align-items:flex-start; } .work-progress__metrics { grid-template-columns:repeat(2, minmax(0, 1fr)); } }
@media (max-width:600px) {
  .work-progress .work-progress__item { grid-template-columns:minmax(0, 1fr); gap:10px; }
  .work-progress .work-progress__tools { flex-direction:row; flex-wrap:wrap; justify-content:space-between; align-items:center; width:100%; }
  .work-progress__actions { justify-content:flex-start; }
}
:global(:root[data-crm-theme="dark"] .work-progress__status[data-status="2"]) { color:#ffb17e; border-color:#76513c; background:#3c2b22; }
:global(:root[data-crm-theme="dark"] .work-progress__status[data-status="3"]) { color:#fbbf24; border-color:#66531e; background:#382f18; }
:global(:root[data-crm-theme="dark"] .work-progress__status[data-status="4"]) { color:#6ee7b7; border-color:#26634d; background:#173a31; }
</style>

<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { UiButton, UiTooltip } from '@opencrm/ui'
import {
  adjustWorkTime, completeWorkForEmployee, decideWorkReport, loadProductionReport, loadWorkProgress,
  loadWorkReports, returnWorkForRework, type ProductionReport, type WorkProgress, type WorkReport
} from '../api/work'
import { formatWorkDuration, getWorkDurationSeconds, hasRecordedWorkTime } from '../formatWorkDuration'

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
const managementTitle = computed(() => props.teamAvailable === false ? 'История работ' : 'Исполнители и корректировки')
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
  'Учтённое время — время по таймеру с учётом предыдущих корректировок.',
  currentWorkSeconds.value === 0 ? 'Время не зафиксировано. Отметка о выполнении сама по себе время не добавляет.' : '',
  timeWork.value?.status === 2 ? 'Таймер запущен: итог продолжит увеличиваться до остановки работы.' : ''
].filter(Boolean).join(' '))
const minimumTimeMinutes = computed(() => Math.max(-10080, -Math.floor((currentWorkSeconds.value ?? 0) / 60)))
const adjustedWorkSeconds = computed(() => currentWorkSeconds.value !== null && Number.isInteger(timeMinutes.value)
  ? currentWorkSeconds.value + timeMinutes.value * 60
  : null)
const timeValidationError = computed(() => {
  if (!timeWork.value) return ''
  if (currentWorkSeconds.value === null) return 'Не удалось определить текущее время. Закройте форму и обновите данные.'
  if (!Number.isInteger(timeMinutes.value) || timeMinutes.value < -10080 || timeMinutes.value > 10080) {
    return 'Введите целое число минут от −10 080 до 10 080.'
  }
  if (adjustedWorkSeconds.value !== null && adjustedWorkSeconds.value < 0) {
    return minimumTimeMinutes.value === 0
      ? 'Уменьшить время нельзя: учтено меньше минуты.'
      : `Можно убрать не больше ${-minimumTimeMinutes.value} мин.`
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
  if (hours) parts.push(`${hours} ч`)
  if (minutes) parts.push(`${minutes} мин`)
  if (remainder) parts.push(`${remainder} с`)

  return `${seconds < 0 ? '−' : ''}${parts.join(' ') || '0 мин'}`
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
    ? failures[0] instanceof Error ? failures[0].message : 'Не удалось загрузить данные о работах.'
    : ''
}

async function decide(decision: 'approved' | 'rejected') {
  if (!props.canReturn || !decisionReport.value || busy.value) return
  if (decision === 'approved' && props.teamAvailable === false) return
  if (decision === 'approved' && (decisionPrice.value == null || decisionPrice.value < 0)) {
    reportError.value = 'Укажите согласованную стоимость.'
    return
  }
  if (decision === 'approved' && !decisionVatRateCode.value) {
    reportError.value = 'Укажите ставку НДС.'
    return
  }
  if (decision === 'rejected' && !decisionReason.value.trim()) {
    reportError.value = 'Укажите причину отказа.'
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
    reportError.value = cause instanceof Error ? cause.message : 'Не удалось сохранить решение.'
    await load()
  } finally {
    busy.value = false
  }
}

function reportKindName(kind: WorkReport['kind']) {
  return kind === 'defect' ? 'Дефект' : kind === 'extra' ? 'Дополнительная работа'
    : kind === 'time' ? 'Корректировка времени' : 'Комментарий'
}

function formatMoney(value: number | null | undefined) {
  return value == null ? '—' : new Intl.NumberFormat('ru-RU', {
    style: 'currency', currency: 'RUB', maximumFractionDigits: 2
  }).format(value)
}

function formatInstant(value: string) {
  return new Intl.DateTimeFormat('ru-RU', {
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
    timeError.value = 'Укажите изменение времени в минутах и причину.'
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
      timeError.value = cause instanceof Error ? cause.message : 'Не удалось исправить время работы.'
    }
  } finally {
    busy.value = false
  }
}

async function returnForRework() {
  if (!props.canReturn || !selected.value || busy.value) return
  if (!reason.value.trim()) {
    managementError.value = 'Укажите причину возврата.'
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
    managementError.value = cause instanceof Error ? cause.message : 'Не удалось вернуть работу.'
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
    completionError.value = 'Укажите причину завершения (до 1000 символов).'
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
      completionError.value = cause instanceof Error ? cause.message : 'Не удалось завершить работу.'
    }
  } finally {
    busy.value = false
  }
}

function statusName(status: number) {
  return status === 2 ? 'В работе' : status === 3 ? 'На паузе' : status === 4 ? 'Выполнена' : 'Назначена'
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
  <section v-if="!simpleWorkflow || error || reports.length || works.some(work => work.version > 0 || work.status >= 2)" class="work-progress" aria-label="Ход работ">
    <header class="ui-section-header"><strong>{{ compact ? 'Контроль работ' : 'Ход работ' }}</strong><span v-if="!compact">{{ completionPercent }}% · {{ completedCount }} из {{ works.length }}</span></header>
    <div v-if="!compact" class="work-progress__track" role="progressbar" :aria-valuenow="completionPercent" aria-valuemin="0" aria-valuemax="100" aria-label="Выполнено работ">
      <span :style="{ width: `${completionPercent}%` }" />
    </div>
    <p v-if="awaitsReview && !simpleWorkflow" class="work-progress__review">Все операции завершены. Проверьте результат и подтвердите готовность к выдаче.</p>
    <p v-if="error" class="work-progress__error" role="alert">{{ error }} <button type="button" @click="load">Повторить</button></p>
    <div class="work-progress__navigation">
      <UiButton v-if="canOpenManagement" variant="secondary" @click="managementDialog?.showModal()">{{ managementTitle }}</UiButton>
      <UiButton v-if="reports.length" variant="secondary" @click="reportsDialog?.showModal()">Замечания и дополнительные работы <span class="work-progress__count">{{ reports.length }}</span></UiButton>
      <span v-if="pendingReportCount" class="work-progress__pending">{{ pendingReportCount }} ждут решения</span>
    </div>
    <div v-if="productionReport" class="work-progress__summary">
      <strong>Предварительный отчёт</strong>
      <dl class="work-progress__metrics">
        <div><dt>Учтённое время работы</dt><dd>{{ hasRecordedWorkTime(productionReport.activeTime) ? formatWorkDuration(productionReport.activeTime) : 'Не зафиксировано' }}</dd></div>
        <div><dt>Корректировки</dt><dd>{{ productionReport.timeAdjustmentsCount }}</dd></div>
        <div v-if="productionReport.materialCostAmount != null"><dt>Материалы</dt><dd>{{ formatMoney(productionReport.materialCostAmount) }}</dd></div>
        <div v-if="productionReport.laborCostAmount != null"><dt>Труд</dt><dd>{{ formatMoney(productionReport.laborCostAmount) }}</dd></div>
        <div v-if="productionReport.revenueAmount != null"><dt>Выручка</dt><dd>{{ formatMoney(productionReport.revenueAmount) }}</dd></div>
        <div v-if="productionReport.grossMarginAmount != null"><dt>Валовая маржа</dt><dd>{{ formatMoney(productionReport.grossMarginAmount) }}</dd></div>
      </dl>
      <details v-if="productionReport.materials.length" class="work-progress__materials">
        <summary>Использованные материалы · {{ productionReport.materials.length }}</summary>
        <ul><li v-for="(material, index) in productionReport.materials" :key="index">{{ material.name }} <span>{{ material.quantity }} {{ material.unit }}</span></li></ul>
      </details>
    </div>
    <dialog ref="managementDialog" class="work-completion-dialog work-progress-dialog" aria-labelledby="work-management-title" @cancel.prevent="closeManagement">
      <header class="work-completion-dialog__header ui-form-header">
        <h3 id="work-management-title">{{ managementTitle }}</h3>
        <button type="button" :aria-label="`Закрыть: ${managementTitle}`" :disabled="busy" @click="closeManagement">×</button>
      </header>
      <div class="work-completion-dialog__body work-progress__management">
        <p v-if="error" class="work-progress__error" role="alert">{{ error }} <button type="button" @click="load">Повторить</button></p>
        <p v-if="!works.length">Нет данных о работах.</p>
        <ul class="work-progress__items">
          <li v-for="work in works" :key="work.detailId" class="work-progress__item">
            <div class="work-progress__identity">
              <strong>{{ work.name }}</strong>
              <small>{{ work.employeeName || 'Исполнитель не назначен' }}</small>
              <div class="work-progress__time"><span>Учтённое время</span><strong>{{ hasRecordedWorkTime(work.activeTime) ? formatWorkDuration(work.activeTime) : 'Не зафиксировано' }}</strong></div>
              <small v-if="work.pauseReason">Причина паузы: {{ work.pauseReason }}</small>
            </div>
            <div class="work-progress__tools">
              <span class="work-progress__status" :data-status="work.status">{{ statusName(work.status) }}</span>
              <div v-if="canCompleteWork(work) || canReturn && work.status !== 1" class="work-progress__actions">
                <UiButton v-if="canCompleteWork(work)" variant="secondary" :disabled="busy" @click="openCompletion(work)">Отметить выполненной</UiButton>
                <UiButton v-if="canReturn && maintenanceStatus === 2 && work.status === 4" variant="secondary" :disabled="busy" @click="selected = work; reason = ''; managementError = ''">Вернуть в работу</UiButton>
                <UiButton v-if="canReturn && work.status !== 1" variant="secondary" :disabled="busy" @click="openTimeAdjustment(work)">Исправить время</UiButton>
              </div>
            </div>
            <button v-if="work.completionReason" type="button" class="work-progress__completion-link" @click="openCompletionDetails(work)">Завершено администратором</button>
          </li>
        </ul>
        <p v-if="managementError" class="work-progress__error" role="alert">{{ managementError }}</p>
        <div v-if="selected" class="work-progress__return">
          <label for="work-return-reason">Причина возврата</label>
          <textarea id="work-return-reason" v-model="reason" maxlength="1000" :disabled="busy" rows="2" />
          <div><UiButton :disabled="busy" @click="returnForRework">Подтвердить возврат</UiButton><UiButton variant="secondary" :disabled="busy" @click="selected = null">Отмена</UiButton></div>
        </div>
      </div>
      <footer class="work-completion-dialog__footer"><UiButton variant="secondary" :disabled="busy" @click="closeManagement">Закрыть</UiButton></footer>
    </dialog>
    <dialog ref="reportsDialog" class="work-completion-dialog work-progress-dialog" aria-labelledby="work-reports-title" @cancel.prevent="closeReports">
      <header class="work-completion-dialog__header ui-form-header">
        <h3 id="work-reports-title">Замечания и дополнительные работы</h3>
        <button type="button" aria-label="Закрыть замечания и дополнительные работы" :disabled="busy" @click="closeReports">×</button>
      </header>
      <div class="work-completion-dialog__body work-progress__reports">
        <p v-if="error" class="work-progress__error" role="alert">{{ error }} <button type="button" @click="load">Повторить</button></p>
        <p v-if="!reports.length">Замечаний и дополнительных работ нет.</p>
        <ul>
          <li v-for="report in reports" :key="report.id">
            <div><strong>{{ reportKindName(report.kind) }}</strong><span>{{ report.employeeName }}</span></div>
            <small>{{ formatInstant(report.createdAt) }}</small>
            <p>{{ report.text }}</p>
            <small v-if="report.kind === 'time'">{{ (report.timeAdjustmentMinutes ?? 0) > 0 ? '+' : '' }}{{ report.timeAdjustmentMinutes }} мин</small>
            <small v-if="report.kind === 'extra'">{{ report.decision === 'approved' ? `Согласована · ${report.approvedPrice} ₽` : report.decision === 'rejected' ? `Отклонена · ${report.decisionReason || ''}` : 'Ожидает согласования' }}</small>
            <UiButton v-if="canReturn && report.kind === 'extra' && !report.decision" variant="secondary" :disabled="busy" @click="decisionReport = report; decisionPrice = null; decisionVatRateCode = ''; decisionReason = ''; reportError = ''">Рассмотреть</UiButton>
          </li>
        </ul>
        <p v-if="reportError" class="work-progress__error" role="alert">{{ reportError }}</p>
        <div v-if="decisionReport" class="work-progress__return">
          <strong>Решение по дополнительной работе</strong>
          <template v-if="teamAvailable !== false">
            <label for="work-extra-price">Согласованная стоимость</label>
            <input id="work-extra-price" v-model.number="decisionPrice" type="number" min="0" step="0.01" :disabled="busy">
            <label for="work-extra-vat">НДС</label>
            <select id="work-extra-vat" v-model="decisionVatRateCode" :disabled="busy"><option value="">Выберите</option><option value="without_vat">Без НДС</option><option value="0">0 %</option><option value="5">5 %</option><option value="7">7 %</option><option value="10">10 %</option><option value="20">20 %</option><option value="22">22 %</option></select>
          </template>
          <label for="work-extra-reason">Причина отказа или комментарий</label>
          <textarea id="work-extra-reason" v-model="decisionReason" maxlength="1000" rows="2" :disabled="busy" />
          <div><UiButton v-if="teamAvailable !== false" :disabled="busy" @click="decide('approved')">Согласовать</UiButton><UiButton variant="secondary" :disabled="busy" @click="decide('rejected')">Отклонить</UiButton><UiButton variant="secondary" :disabled="busy" @click="decisionReport = null">Отмена</UiButton></div>
        </div>
      </div>
      <footer class="work-completion-dialog__footer"><UiButton variant="secondary" :disabled="busy" @click="closeReports">Закрыть</UiButton></footer>
    </dialog>
    <dialog ref="auditDialog" class="work-completion-dialog" aria-labelledby="work-audit-title" @cancel.prevent="closeCompletionDetails">
      <header class="work-completion-dialog__header ui-form-header">
        <h3 id="work-audit-title">Завершено администратором</h3>
        <button type="button" aria-label="Закрыть сведения о завершении" @click="closeCompletionDetails">×</button>
      </header>
      <div class="work-completion-dialog__body">
        <strong>{{ auditWork?.name }}</strong>
        <dl class="work-progress__audit">
          <div><dt>Завершил</dt><dd>{{ auditWork?.completedBy || 'Администратор' }}</dd></div>
          <div><dt>Причина</dt><dd>{{ auditWork?.completionReason }}</dd></div>
        </dl>
      </div>
      <footer class="work-completion-dialog__footer"><UiButton variant="secondary" @click="closeCompletionDetails">Закрыть</UiButton></footer>
    </dialog>
    <dialog ref="timeDialog" class="work-completion-dialog" aria-labelledby="work-time-title" @cancel.prevent="closeTimeAdjustment">
      <form @submit.prevent="adjustTime">
        <header class="work-completion-dialog__header ui-form-header">
          <h3 id="work-time-title">Исправить время работы</h3>
          <button type="button" aria-label="Закрыть корректировку времени" :disabled="busy" @click="closeTimeAdjustment">×</button>
        </header>
        <div class="work-completion-dialog__body">
          <strong>{{ timeWork?.name }}</strong>
          <dl class="work-time-summary">
            <div>
              <dt>Учтено сейчас <UiTooltip :text="timeExplanation"><button type="button" class="work-time-help" aria-label="Как считается учтённое время">?</button></UiTooltip></dt>
              <dd>{{ formatAdjustmentTime(currentWorkSeconds) }}</dd>
            </div>
            <div><dt>После сохранения</dt><dd :class="{ 'work-progress__error': adjustedWorkSeconds !== null && adjustedWorkSeconds < 0 }" aria-live="polite">{{ formatAdjustmentTime(adjustedWorkSeconds) }}</dd></div>
          </dl>
          <label for="work-time-minutes">Изменение времени, мин.</label>
          <input id="work-time-minutes" v-model.number="timeMinutes" type="number" step="1" :min="minimumTimeMinutes" max="10080" required :disabled="busy" autofocus :aria-invalid="Boolean(timeValidationError)" aria-describedby="work-time-hint work-time-validation" @input="timeError = ''">
          <small id="work-time-hint">Положительное число добавит время, отрицательное — уменьшит.</small>
          <label for="work-time-reason">Причина корректировки <span aria-hidden="true">*</span></label>
          <textarea id="work-time-reason" v-model="timeReason" required maxlength="1000" rows="3" :disabled="busy" />
          <p v-if="timeValidationError || timeError" id="work-time-validation" class="work-progress__error" role="alert">{{ timeValidationError || timeError }}</p>
        </div>
        <footer class="work-completion-dialog__footer">
          <UiButton type="button" variant="secondary" :disabled="busy" @click="closeTimeAdjustment">Отмена</UiButton>
          <UiButton type="submit" :disabled="busy || Boolean(timeValidationError) || !timeMinutes || !timeReason.trim()">{{ busy ? 'Сохранение…' : 'Сохранить' }}</UiButton>
        </footer>
      </form>
    </dialog>
    <dialog ref="completionDialog" class="work-completion-dialog" aria-labelledby="work-completion-title" @cancel.prevent="closeCompletion">
      <form @submit.prevent="completeForEmployee">
        <header class="work-completion-dialog__header ui-form-header">
          <h3 id="work-completion-title">Завершить работу за сотрудника</h3>
          <button type="button" aria-label="Закрыть завершение работы" :disabled="busy" @click="closeCompletion">×</button>
        </header>
        <div class="work-completion-dialog__body">
          <strong>{{ completionWork?.name }}</strong>
          <p>Исполнитель: {{ completionWork?.employeeName }}</p>
          <label for="work-completion-reason">Причина завершения <span aria-hidden="true">*</span></label>
          <textarea id="work-completion-reason" v-model="completionReason" required maxlength="1000" rows="3" :disabled="busy" autofocus />
          <small>Вы и причина завершения будут сохранены в истории. Исполнитель работы не изменится.</small>
          <p v-if="completionError" class="work-progress__error" role="alert">{{ completionError }}</p>
        </div>
        <footer class="work-completion-dialog__footer">
          <UiButton type="button" variant="secondary" :disabled="busy" @click="closeCompletion">Отмена</UiButton>
          <UiButton type="submit" :disabled="busy || !completionReason.trim()">{{ busy ? 'Сохранение…' : 'Подтвердить завершение' }}</UiButton>
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

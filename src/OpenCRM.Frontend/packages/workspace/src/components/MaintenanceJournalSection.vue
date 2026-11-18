<script setup lang="ts">
import { formattingCulture } from '@opencrm/ui/i18n'
import { useWorkspaceCurrency } from '../workspaceCurrency'
import { useLocalization } from '@opencrm/ui'
import '../schedulerMessages'

import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton, UiCard, UiDatePicker, UiLoader, UiPagination, UiSearchInput, UiSelect, formatDateOnly, parseDateOnly } from '@opencrm/ui'
import WorkspacePageHelpButton from './WorkspacePageHelpButton.vue'
import { loadMaintenanceJournal, type MaintenanceJournalData, type MaintenanceJournalItem } from '../api/maintenanceJournal'
import type { WorkspaceContext } from '../types'

const workspaceCurrency = useWorkspaceCurrency()

const props = defineProps<{ context: WorkspaceContext }>()
const { t } = useLocalization()
const status = ref('0')
const period = ref('month')
const search = ref('')
const employeeId = ref('')
const page = ref(1)
const pageSize = ref('25')
const today = new Date(`${formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')}T12:00:00`)
const dateFrom = ref(formatDateOnly(new Date(today.getFullYear(), today.getMonth(), 1)))
const dateTo = ref(formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC'))
const data = ref<MaintenanceJournalData | null>(null)
const isLoading = ref(false)
const errorMessage = ref('')
const expandedIds = ref<string[]>([])
let searchTimer: number | undefined
let latestRequest = 0
let controller: AbortController | undefined
let isRestoringState = false
const title = computed(() => props.context.shell.scenario?.code === 'detailing' ? t('maintenanceJournal.serviceJournal') : t('maintenanceJournal.maintenanceJournal'))
const storageKey = computed(() => `opencrm:${props.context.user.id}:${props.context.organization.id}:maintenance-journal:v1`)
const createAction = computed(() => props.context.workspaceAccess?.isReadOnly === false
  ? props.context.shell.quickActions?.find(action => action.isVisible && (action.code === 'create-maintenance' || action.formCode === 'maintenance-create')) : undefined)
const statusOptions = computed(() => [
  { value: '0', label: t('maintenanceJournal.allStatuses') }, { value: '1', label: t('maintenanceJournal.scheduled') },
  { value: '2', label: t('maintenanceJournal.inProgress') }, { value: '3', label: t('maintenanceJournal.allCompleted') },
  { value: 'ready', label: t('maintenanceJournal.ready') }, { value: 'handedOver', label: t('maintenanceJournal.handedOver') }, { value: '4', label: t('maintenanceJournal.cancelled') }
])
const periodOptions = computed(() => [
  { value: 'all', label: t('maintenanceJournal.allTime') }, { value: 'today', label: t('maintenanceJournal.today') },
  { value: 'week', label: t('maintenanceJournal.lastSevenDays') }, { value: 'month', label: t('maintenanceJournal.thisMonth') },
  { value: 'custom', label: t('maintenanceJournal.customRange') }
])
const pageSizeOptions = ['10', '25', '50', '100'].map(value => ({ value, label: value }))
const performerOptions = computed(() => [{ value: '', label: t('maintenanceJournal.allPerformers') }, ...(data.value?.performers ?? []).map(item => ({ value: item.id, label: item.name }))])
const hasFilters = computed(() => status.value !== '0' || period.value !== 'month' || Boolean(search.value || employeeId.value))
const showPayments = computed(() => data.value?.items.some(item => item.paymentStatus != null || item.paidAmount != null))
const rangeText = computed(() => {
  if (!data.value?.totalCount) return t('maintenanceJournal.recordCount', { count: 0 })
  const from = (data.value.page - 1) * data.value.pageSize + 1
  return t('maintenanceJournal.range', { from, to: Math.min(data.value.page * data.value.pageSize, data.value.totalCount), count: data.value.totalCount })
})
const dateRange = computed(() => {
  if (period.value === 'all') return {}
  if (period.value === 'custom') return { dateFrom: dateFrom.value, dateTo: dateTo.value }
  const from = new Date(`${formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')}T12:00:00`)
  if (period.value === 'week') from.setDate(from.getDate() - 6)
  if (period.value === 'month') from.setDate(1)
  return { dateFrom: formatDateOnly(from), dateTo: formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC') }
})
const intervalText = computed(() => dateRange.value.dateFrom && dateRange.value.dateTo
  ? t('maintenanceJournal.dateRange', { from: formatDate(dateRange.value.dateFrom), to: formatDate(dateRange.value.dateTo) }) : t('maintenanceJournal.allTimeRange'))

function formatDate(value?: string | null) {
  const date = value ? parseDateOnly(value) : null
  return date ? new Intl.DateTimeFormat(formattingCulture.value, { dateStyle: 'short' }).format(date) : '—'
}

function formatMoney(value: number, item?: MaintenanceJournalItem) {
  const rowCurrency = item && typeof item.currencyCode === 'string' && item.currencyCode ? item.currencyCode : workspaceCurrency.value
  return new Intl.NumberFormat(formattingCulture.value, { style: 'currency', currency: rowCurrency, minimumFractionDigits: 0, maximumFractionDigits: 2 }).format(value)
}

function statusLabel(item: MaintenanceJournalItem) {
  if (/выдан|handed over|已交付/i.test(item.statusName)) return t('maintenanceJournal.handedOver')
  if (/готов.*выдач|ready for handover|已准备交付/i.test(item.statusName)) return t('maintenanceJournal.ready')
  const names: Record<number, string> = {
    1: t('maintenanceJournal.scheduled'),
    2: t('maintenanceJournal.inProgress'),
    3: t('maintenanceJournal.completed'),
    4: t('maintenanceJournal.cancelled')
  }

  return names[item.status] ?? t('maintenanceJournal.statusNotSpecified')
}

function paymentLabel(item: MaintenanceJournalItem) {
  if (item.paymentStatus === 'no_payment_required') return t('maintenanceJournal.noPaymentRequired')
  if (item.paidAmount == null) return ''
  if (item.paymentStatus === 'unpaid' || item.paidAmount === 0) return t('maintenanceJournal.unpaid')
  if (item.paidAmount > item.amount) return t('maintenanceJournal.overpaid', { amount: formatMoney(item.paidAmount - item.amount, item) })
  return debt(item) === 0 ? t('maintenanceJournal.paid') : t('maintenanceJournal.paidAmount', { amount: formatMoney(item.paidAmount, item) })
}

function debt(item: MaintenanceJournalItem) {
  return Math.max(0, Math.round((item.amount - (item.paidAmount ?? 0)) * 100) / 100)
}

function toggleDetails(id: string) {
  expandedIds.value = expandedIds.value.includes(id) ? expandedIds.value.filter(value => value !== id) : [...expandedIds.value, id]
}

function saveState() {
  try {
    window.sessionStorage.setItem(storageKey.value, JSON.stringify({ status: status.value, period: period.value, search: search.value,
      employeeId: employeeId.value, page: page.value, pageSize: pageSize.value, dateFrom: dateFrom.value, dateTo: dateTo.value }))
  } catch {
    // Недоступное хранилище не мешает работе журнала.
  }
}

function restoreState() {
  isRestoringState = true
  status.value = '0'
  period.value = 'month'
  search.value = ''
  employeeId.value = ''
  page.value = 1
  pageSize.value = '25'
  const today = new Date(`${formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')}T12:00:00`)
  dateFrom.value = formatDateOnly(new Date(today.getFullYear(), today.getMonth(), 1))
  dateTo.value = formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')
  try {
    const state = JSON.parse(window.sessionStorage.getItem(storageKey.value) ?? 'null')
    if (!state || typeof state !== 'object') return
    if (statusOptions.value.some(item => item.value === state.status)) status.value = state.status
    if (periodOptions.value.some(item => item.value === state.period)) period.value = state.period
    if (typeof state.search === 'string') search.value = state.search.slice(0, 200)
    if (typeof state.employeeId === 'string') employeeId.value = state.employeeId
    if (Number.isSafeInteger(state.page) && state.page > 0) page.value = state.page
    if (pageSizeOptions.some(item => item.value === state.pageSize)) pageSize.value = state.pageSize
    if (typeof state.dateFrom === 'string' && parseDateOnly(state.dateFrom)) dateFrom.value = state.dateFrom
    if (typeof state.dateTo === 'string' && parseDateOnly(state.dateTo)) dateTo.value = state.dateTo
  } catch {
    // Повреждённые настройки списка заменяются начальными значениями.
  } finally {
    isRestoringState = false
  }
}
restoreState()

async function loadItems() {
  window.clearTimeout(searchTimer)
  controller?.abort()
  const request = ++latestRequest
  const range = dateRange.value
  errorMessage.value = ''
  if (period.value === 'custom' && (!range.dateFrom || !range.dateTo || range.dateFrom > range.dateTo)) {
    errorMessage.value = t('maintenanceJournal.invalidRange')
    isLoading.value = false
    return
  }
  const requestController = new AbortController()
  controller = requestController
  isLoading.value = true
  saveState()
  try {
    const result = await loadMaintenanceJournal(props.context.organization.id, {
      status: ['ready', 'handedOver'].includes(status.value) ? 3 : Number(status.value),
      handover: ['ready', 'handedOver'].includes(status.value) ? status.value : undefined,
      ...range, employeeId: employeeId.value, search: search.value.trim(), page: page.value, pageSize: Number(pageSize.value)
    }, requestController.signal)
    if (request !== latestRequest) return
    data.value = result
    page.value = result.page
    expandedIds.value = []
    saveState()
  } catch (error) {
    if (request === latestRequest && !requestController.signal.aborted) errorMessage.value = error instanceof Error ? error.message : t('maintenanceJournal.loadError')
  } finally {
    if (request === latestRequest) isLoading.value = false
  }
}

function changePage(value: number) {
  page.value = value
  void loadItems()
}

function resetFilters() {
  isRestoringState = true
  status.value = '0'
  period.value = 'month'
  search.value = ''
  employeeId.value = ''
  page.value = 1
  isRestoringState = false
  void loadItems()
}

watch([status, period, dateFrom, dateTo, employeeId, pageSize], () => {
  if (isRestoringState) return
  page.value = 1
  void loadItems()
}, { flush: 'sync' })
watch(search, () => {
  if (isRestoringState) return
  window.clearTimeout(searchTimer)
  controller?.abort()
  latestRequest++
  searchTimer = window.setTimeout(() => { page.value = 1; void loadItems() }, 350)
}, { flush: 'sync' })
watch([() => props.context.organization.id, () => props.context.user.id], () => {
  controller?.abort()
  latestRequest++
  data.value = null
  restoreState()
  void loadItems()
})
onMounted(loadItems)
onBeforeUnmount(() => {
  window.clearTimeout(searchTimer)
  latestRequest++
  controller?.abort()
  saveState()
})
</script>

<template>
  <UiCard page class="maintenance-journal" :title="title">
    <template #actions>
      <UiButton v-if="createAction" :href="createAction.urlTemplate">＋ {{ t('maintenanceJournal.newMaintenance') }}</UiButton>
      <WorkspacePageHelpButton section-code="maintenance-journal" :title="title" />
    </template>
    <div class="maintenance-journal__toolbar" :class="{ 'has-reset': hasFilters }">
      <UiSearchInput v-model="search" :label="t('maintenanceJournal.search')" :placeholder="t('maintenanceJournal.searchPlaceholder')" :maxlength="200" />
      <UiSelect v-model="period" :label="t('maintenanceJournal.period')" :options="periodOptions" :searchable="false" required />
      <UiSelect v-model="status" :label="t('maintenanceJournal.status')" :options="statusOptions" :searchable="false" required />
      <UiSelect v-model="employeeId" :label="t('maintenanceJournal.performer')" :options="performerOptions" />
      <UiButton v-if="hasFilters" class="maintenance-journal__reset" variant="secondary" @click="resetFilters">{{ t('maintenanceJournal.resetFilters') }}</UiButton>
    </div>
    <div class="maintenance-journal__interval">
      <template v-if="period === 'custom'"><UiDatePicker v-model="dateFrom" :label="t('maintenanceJournal.from')" /><UiDatePicker v-model="dateTo" :label="t('maintenanceJournal.to')" /></template>
      <span v-else>{{ intervalText }}</span>
    </div>
    <div v-if="errorMessage" class="maintenance-journal__error" role="alert"><span>{{ errorMessage }}</span><UiButton variant="secondary" @click="loadItems">{{ t('maintenanceJournal.retry') }}</UiButton></div>
    <UiLoader v-else-if="isLoading && !data" :text="t('maintenanceJournal.loading')" />
    <template v-else-if="data">
      <div class="maintenance-journal__list" :aria-busy="isLoading" :class="{ 'is-loading': isLoading }">
        <table v-if="data.items.length" :aria-label="title">
          <colgroup><col style="width: 11%" /><col style="width: 13%" /><col style="width: 10%" /><col style="width: 14%" /><col style="width: 11%" /><col style="width: 13%" /><col style="width: 11%" /><col style="width: 17%" /></colgroup>
          <thead><tr><th>{{ t('maintenanceJournal.dateNumber') }}</th><th>{{ t('maintenanceJournal.vehicle') }}</th><th>{{ context.shell.scenario?.code === 'fleet' ? t('maintenanceJournal.departmentContact') : t('maintenanceJournal.client') }}</th><th>{{ t('maintenanceJournal.work') }}</th><th>{{ t('maintenanceJournal.performer') }}</th><th>{{ t('maintenanceJournal.status') }}</th><th>{{ showPayments ? t('maintenanceJournal.payment') : t('maintenanceJournal.amount') }}</th><th><span class="maintenance-journal__sr-only">{{ t('maintenanceJournal.action') }}</span></th></tr></thead>
          <tbody>
            <template v-for="item in data.items" :key="item.id">
              <tr class="maintenance-journal__row">
                <td :data-label="t('maintenanceJournal.dateNumber')" class="maintenance-journal__date"><span>{{ formatDate(item.date || item.completedDate || item.plannedDate) }}</span><small v-if="item.workActNumber">{{ t('maintenanceJournal.actNumber', { number: item.workActNumber }) }}</small><small v-if="item.status !== 3 && item.plannedStartTime">{{ item.plannedStartTime.slice(0, 5) }}</small></td>
                <th scope="row" class="maintenance-journal__vehicle"><a :href="`#maintenance/${item.id}`">{{ item.vehicleDisplayName }}</a><small v-if="item.registrationPlate && !item.vehicleDisplayName.includes(item.registrationPlate)">{{ item.registrationPlate }}</small></th>
                <td class="maintenance-journal__client" :data-label="context.shell.scenario?.code === 'fleet' ? t('maintenanceJournal.department') : t('maintenanceJournal.client')">{{ item.clientName || '—' }}</td>
                <td :data-label="t('maintenanceJournal.work')" class="maintenance-journal__services">{{ item.services[0] || t('maintenanceJournal.serviceComposition') }}<small v-if="item.services.length > 1">{{ t('maintenanceJournal.moreServices', { count: item.services.length - 1 }) }}</small></td>
                <td :data-label="t('maintenanceJournal.performer')" class="maintenance-journal__performer">{{ item.performerNames?.join(', ') || t('maintenanceJournal.unassigned') }}</td>
                <td :data-label="t('maintenanceJournal.status')" class="maintenance-journal__state"><span class="maintenance-journal__status" :data-status="item.status">{{ statusLabel(item) }}</span></td>
                <td :data-label="t('maintenanceJournal.payment')" class="maintenance-journal__payment">
                  <strong>{{ formatMoney(item.amount, item) }}</strong>
                  <small v-if="item.paymentStatus != null || item.paidAmount != null" :class="{ 'is-paid': item.paymentStatus === 'no_payment_required' || (item.paidAmount != null && item.paidAmount > 0 && debt(item) === 0) }">{{ paymentLabel(item) }}</small>
                  <small v-if="item.paidAmount != null && debt(item) > 0" class="is-debt">{{ item.status === 4 ? t('maintenanceJournal.remaining') : t('maintenanceJournal.due') }}: {{ formatMoney(debt(item), item) }}</small>
                </td>
                <td :data-label="t('maintenanceJournal.action')" class="maintenance-journal__action">
                  <div class="maintenance-journal__actions">
                    <button class="maintenance-journal__expand" type="button" :aria-label="t(expandedIds.includes(item.id) ? 'maintenanceJournal.collapseWork' : 'maintenanceJournal.expandWork', { vehicle: item.vehicleDisplayName })" :aria-expanded="expandedIds.includes(item.id)" :aria-controls="`journal-details-${item.id}`" @click="toggleDetails(item.id)"><span class="maintenance-journal__chevron" aria-hidden="true"></span></button>
                    <UiButton variant="secondary" :href="`#maintenance/${item.id}`" :aria-label="t('maintenanceJournal.openMaintenanceFor', { vehicle: item.vehicleDisplayName })">{{ t('maintenanceJournal.open') }}</UiButton>
                  </div>
                </td>
              </tr>
              <tr v-if="expandedIds.includes(item.id)" :id="`journal-details-${item.id}`" class="maintenance-journal__details"><td colspan="8"><section :aria-label="t('maintenanceJournal.workCompositionFor', { vehicle: item.vehicleDisplayName })"><h4>{{ t('maintenanceJournal.serviceComposition') }}</h4><div v-for="(line, index) in item.serviceLines ?? []" :key="index" class="maintenance-journal__line"><strong>{{ line.name }}</strong><span>{{ line.employeeName || t('maintenanceJournal.unassigned') }}</span><span>{{ line.amount == null ? '—' : formatMoney(line.amount, item) }}</span></div><template v-if="!item.serviceLines?.length"><div v-for="(service, index) in item.services" :key="index" class="maintenance-journal__line"><strong>{{ service }}</strong></div><p v-if="!item.services.length">{{ t('maintenanceJournal.noServices') }}</p></template></section></td></tr>
            </template>
          </tbody>
        </table>
        <div v-else class="maintenance-journal__empty"><strong>{{ t('maintenanceJournal.noRecords') }}</strong><span>{{ t('maintenanceJournal.changeFilters') }}</span><UiButton v-if="hasFilters" variant="secondary" @click="resetFilters">{{ t('maintenanceJournal.resetFilters') }}</UiButton></div>
      </div>
      <div v-if="data.totalCount" class="maintenance-journal__footer">
        <div class="maintenance-journal__summary">
          <span aria-live="polite">{{ rangeText }}<template v-if="isLoading"> · {{ t('maintenanceJournal.updating') }}</template></span>
          <div class="maintenance-journal__page-size"><span>{{ t('maintenanceJournal.perPage') }}</span><UiSelect v-model="pageSize" :options="pageSizeOptions" :searchable="false" :aria-label="t('maintenanceJournal.recordsPerPage')" required /></div>
        </div>
        <UiPagination :model-value="page" :total-items="data.totalCount" :items-per-page="data.pageSize" :max-pages-shown="3" :disabled="isLoading" :label="t('maintenanceJournal.pages')" @update:model-value="changePage" />
      </div>
    </template>
  </UiCard>
</template>

<style scoped>
.maintenance-journal { container-type: inline-size; min-width: 0; }
.maintenance-journal :deep(.ui-card__actions .ui-button--primary) { background: #e87532; border-color: #e87532; color: white; }
.maintenance-journal__toolbar { display: grid; grid-template-columns: minmax(230px, 1.7fr) repeat(3, minmax(150px, 1fr)); gap: 12px; align-items: end; }
.maintenance-journal__toolbar.has-reset { grid-template-columns: minmax(230px, 1.7fr) repeat(3, minmax(150px, 1fr)) auto; }
.maintenance-journal__reset { white-space: nowrap; }
.maintenance-journal__toolbar > *, .maintenance-journal__interval > * { min-width: 0; }
.maintenance-journal__interval { display: flex; flex-wrap: wrap; align-items: end; gap: 12px; margin-top: 12px; font-size: 12px; color: var(--crm-text-muted, #667085); }
.maintenance-journal__interval :deep(.ui-date-picker) { max-width: 200px; }
.maintenance-journal__summary { display: flex; flex-wrap: wrap; align-items: center; gap: 16px; color: var(--crm-text-muted, #667085); font-size: 12px; }
.maintenance-journal__list { min-width: 0; margin-top: 20px; }
.maintenance-journal__list.is-loading { opacity: .55; pointer-events: none; }
.maintenance-journal table { border-collapse: collapse; width: 100%; table-layout: fixed; font-size: 13px; }
.maintenance-journal th, .maintenance-journal td { padding: 15px 8px; border-bottom: 1px solid var(--crm-line, #dce2e9); text-align: left; vertical-align: middle; overflow-wrap: anywhere; }
.maintenance-journal__date > span { white-space: nowrap; font-variant-numeric: tabular-nums; }
.maintenance-journal thead th { overflow-wrap: normal; background: var(--crm-surface-soft, #f3f5f8); font-size: 12px; color: var(--crm-text-muted, #667085); font-weight: 500; }
.maintenance-journal__row:hover, .maintenance-journal__row:focus-within { background: var(--crm-surface-soft, #f3f5f8); }
.maintenance-journal small { display: block; font-size: 11px; color: var(--crm-text-muted, #667085); font-weight: 400; margin-top: 5px; }
.maintenance-journal a { color: var(--crm-text, #202631); text-decoration: none; }
.maintenance-journal a:not(.ui-button):hover { color: #e87532; text-decoration: underline; }
.maintenance-journal__vehicle { font-size: 14px; }
.maintenance-journal__expand { display: inline-flex; flex: 0 0 auto; border: 0; border-radius: 50%; background: transparent; padding: 0; cursor: pointer; }
.maintenance-journal__chevron { display: inline-grid; flex: 0 0 32px; width: 32px; height: 32px; place-items: center; border: 1px solid var(--crm-line, #dce2e9); border-radius: 50%; color: var(--crm-text, #202631); background: var(--crm-surface, #fff); }
.maintenance-journal__chevron::before { width: 8px; height: 8px; border-right: 1.5px solid currentColor; border-bottom: 1.5px solid currentColor; content: ''; transform: translateY(-2px) rotate(45deg); }
.maintenance-journal__expand[aria-expanded="true"] .maintenance-journal__chevron::before { transform: translateY(2px) rotate(225deg); }
.maintenance-journal__expand:hover .maintenance-journal__chevron, .maintenance-journal__expand:focus-visible .maintenance-journal__chevron { border-color: #e87532; }
.maintenance-journal__expand:focus-visible { outline: 2px solid #e87532; outline-offset: 3px; }
.maintenance-journal__action { white-space: nowrap; }
.maintenance-journal__actions { display: flex; align-items: center; justify-content: flex-end; gap: 8px; }
.maintenance-journal__sr-only { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; border: 0; }
.maintenance-journal__status { display: inline-block; overflow-wrap: normal; border: 1px solid #8a97a9; border-radius: 15px; padding: 4px 8px; font-size: 11px; }
.maintenance-journal__status[data-status="1"] { color: var(--crm-info-text, #417cab); border-color: #517ca1; }
.maintenance-journal__status[data-status="2"] { color: #e87532; border-color: #b76330; }
.maintenance-journal__status[data-status="3"] { color: var(--crm-success-text, #329987); border-color: #329987; }
.maintenance-journal .maintenance-journal__payment { font-variant-numeric: tabular-nums; }
.maintenance-journal .is-paid { color: var(--crm-success-text, #329987); }
.maintenance-journal .is-debt { color: #e87532; }
.maintenance-journal__details td { padding: 8px 12px 18px; }
.maintenance-journal__details section { padding: 14px; background: var(--crm-surface-soft, #f3f5f8); border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; }
.maintenance-journal__details h4 { margin: 0 0 12px; font-size: 13px; }
.maintenance-journal__line { display: grid; grid-template-columns: minmax(0, 2fr) minmax(0, 1fr) auto; gap: 12px; padding: 10px 0; border-bottom: 1px solid var(--crm-line, #dce2e9); }
.maintenance-journal__line strong { font-weight: 500; }
.maintenance-journal__line span { color: var(--crm-text-muted, #667085); }
.maintenance-journal__footer { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 12px; margin-top: 18px; }
.maintenance-journal__page-size { display: flex; gap: 10px; align-items: center; font-size: 12px; color: var(--crm-text-muted, #667085); }
.maintenance-journal__page-size :deep(.ui-select-field) { width: 80px; }
.maintenance-journal__empty { display: grid; justify-items: center; gap: 12px; padding: 32px 14px; text-align: center; color: var(--crm-text-muted, #667085); }
.maintenance-journal__error { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: center; gap: 12px; padding: 12px; margin-top: 16px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; color: var(--crm-danger-text, #b42318); }
@container (max-width: 1000px) {
  .maintenance-journal__toolbar, .maintenance-journal__toolbar.has-reset { grid-template-columns: repeat(3, minmax(0, 1fr)); }
  .maintenance-journal__toolbar > :first-child { grid-column: 1 / -1; }
  .maintenance-journal__reset { grid-column: 1 / -1; justify-self: end; }
}
@container (max-width: 850px) {
  .maintenance-journal table, .maintenance-journal tbody { display: block; }
  .maintenance-journal colgroup { display: none; }
  .maintenance-journal thead { display: none; }
  .maintenance-journal tbody { display: grid; gap: 12px; }
  .maintenance-journal__row { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); grid-template-areas: "vehicle status" "date client" "services performer" "payment actions"; align-items: start; overflow: hidden; border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; }
  .maintenance-journal__row, .maintenance-journal__row:hover, .maintenance-journal__row:focus-within { background: var(--crm-surface, #fff); }
  .maintenance-journal .maintenance-journal__row td, .maintenance-journal .maintenance-journal__row th { display: block; padding: 12px 16px; border: 0; }
  .maintenance-journal .maintenance-journal__row :is(.maintenance-journal__vehicle, .maintenance-journal__state) { align-self: stretch; background: var(--crm-section-header-bg, #edf1f5); border-bottom: 1px solid var(--crm-line, #dce2e9); }
  .maintenance-journal .maintenance-journal__row :is(.maintenance-journal__payment, .maintenance-journal__action) { align-self: stretch; border-top: 1px solid var(--crm-line, #dce2e9); }
  .maintenance-journal__row .maintenance-journal__status { background: color-mix(in srgb, currentColor 10%, var(--crm-surface, #fff)); }
  .maintenance-journal__row .maintenance-journal__actions { height: 100%; }
  .maintenance-journal__vehicle { grid-area: vehicle; }
  .maintenance-journal__date { grid-area: date; }
  .maintenance-journal__client { grid-area: client; }
  .maintenance-journal__services { grid-area: services; }
  .maintenance-journal__performer { grid-area: performer; }
  .maintenance-journal .maintenance-journal__state { grid-area: status; text-align: right; }
  .maintenance-journal__payment { grid-area: payment; }
  .maintenance-journal__action { grid-area: actions; align-self: end; }
  .maintenance-journal__row td::before { content: attr(data-label); display: block; margin-bottom: 5px; color: var(--crm-text-muted, #667085); font-size: 11px; }
  .maintenance-journal__row .maintenance-journal__state::before, .maintenance-journal__row .maintenance-journal__action::before { display: none; }
  .maintenance-journal__row:has(+ .maintenance-journal__details) { border-bottom: 0; border-radius: 8px 8px 0 0; }
  .maintenance-journal__details { display: block; margin-top: -12px; }
  .maintenance-journal__details section { padding: 14px 16px; background: var(--crm-surface, #fff); border-top: 0; border-radius: 0 0 8px 8px; }
  .maintenance-journal__details .maintenance-journal__line { margin-top: 8px; padding: 10px 12px; border: 0; border-radius: 6px; background: var(--crm-surface-soft, #f6f8fa); }
  .maintenance-journal .maintenance-journal__details td { display: block; padding: 0; border: 0; }
}
@container (max-width: 600px) {
  .maintenance-journal__footer { align-items: start; flex-direction: column; }
  .maintenance-journal__toolbar, .maintenance-journal__toolbar.has-reset { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .maintenance-journal__toolbar > :nth-child(4) { grid-column: 1 / -1; }
  .maintenance-journal .maintenance-journal__row td, .maintenance-journal .maintenance-journal__row th { padding: 12px; }
  .maintenance-journal__details section { padding: 12px; }
  .maintenance-journal__line { grid-template-columns: minmax(0, 1fr) auto; }
  .maintenance-journal__line strong { grid-column: 1 / -1; }
  .maintenance-journal__interval > :not(span) { flex: 1 1 130px; }
  .maintenance-journal__interval :deep(.ui-date-picker) { max-width: none; }
}
@container (max-width: 340px) {
  .maintenance-journal__row { grid-template-areas: "vehicle status" "date client" "services performer" "payment payment" "actions actions"; }
  .maintenance-journal .maintenance-journal__row .maintenance-journal__action { padding-top: 0; border-top: 0; }
}
@media (max-width: 600px) {
  .maintenance-journal :deep(.ui-card__header) { flex-direction: column; align-items: stretch; }
  .maintenance-journal :deep(.ui-card__actions) { width: 100%; justify-content: space-between; }
  .maintenance-journal :deep(.ui-card__title) { width: 100%; }
}
</style>

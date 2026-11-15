<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton } from '@opencrm/ui'
import { buildImportRecoveryReport } from '../importRecoveryReport'
import { remapImportProfileMappings } from '../importProfileStructure.mjs'
import {
  confirmOrganizationImport,
  deleteOrganizationImportProfile,
  downloadOrganizationImportReport,
  loadOrganizationImportAccess,
  loadOrganizationImportBatchPage,
  loadOrganizationImportDraft,
  loadOrganizationImportProfiles,
  loadOrganizationImports,
  patchOrganizationImportBatchRows,
  pauseOrganizationImportBatch,
  queueOrganizationImportBatchMapping,
  saveOrganizationImportBatchSource,
  saveOrganizationImportProfile,
  saveOrganizationImportMapping,
  saveOrganizationImportRows,
  startOrganizationImportBatch,
  uploadOrganizationImportBatch,
  type OrganizationImportCandidate,
  type OrganizationImportDraft,
  type OrganizationImportFieldTransformation,
  type OrganizationImportMapping,
  type OrganizationImportKind,
  type OrganizationImportProfile,
  type OrganizationImportPageQuery,
  type OrganizationImportState,
  type OrganizationImportSummary
} from '../api/imports'

const props = defineProps<{ organizationId: string, userId: string, timeZoneId?: string | null }>()

type MappingKind = OrganizationImportMapping['kind']
type TextCandidateField =
  | 'externalId'
  | 'name'
  | 'phone'
  | 'comment'
  | 'brand'
  | 'model'
  | 'vin'
  | 'plate'
  | 'year'
  | 'clientExternalId'
  | 'clientPhone'
  | 'vehicleExternalId'
  | 'orderExternalId'
  | 'partyName'
  | 'orderStatus'
  | 'occurredAt'
  | 'orderComment'
  | 'orderType'
  | 'detailDescription'
  | 'quantity'
  | 'unitPrice'
  | 'discountAmount'
  | 'amount'
  | 'paymentMethod'
  | 'cancelledAt'
  | 'actorName'
  | 'eventType'
  | 'historyComment'
type CandidateField = TextCandidateField | 'existingClientId' | 'createSeparateClient' | 'excluded'
type MutableCandidateField = CandidateField
type RecoveryRowEdit = { id: string, changes: Partial<Record<MutableCandidateField, string | boolean | null>> }
type LocalRecovery = {
  version: 1
  userId: string
  organizationId: string
  draftId: string
  revision: number
  rowEdits: RecoveryRowEdit[]
  mappings?: OrganizationImportMapping[]
  page?: number
  kind?: string
  status?: string
  search?: string
}

const mappingFields: Array<{ key: string, label: string, kind: OrganizationImportMapping['kind'] }> = [
  { key: 'clientId', label: 'Внешний ID клиента', kind: 'client' },
  { key: 'clientName', label: 'Имя клиента', kind: 'client' },
  { key: 'clientPhone', label: 'Телефон клиента', kind: 'client' },
  { key: 'clientComment', label: 'Комментарий клиента', kind: 'client' },
  { key: 'vehicleId', label: 'ID автомобиля', kind: 'vehicle' },
  { key: 'vehicleExternalId', label: 'Внешний ID автомобиля', kind: 'vehicle' },
  { key: 'vehicleName', label: 'Название автомобиля', kind: 'vehicle' },
  { key: 'brand', label: 'Марка', kind: 'vehicle' },
  { key: 'model', label: 'Модель', kind: 'vehicle' },
  { key: 'vin', label: 'VIN', kind: 'vehicle' },
  { key: 'plate', label: 'Госномер', kind: 'vehicle' },
  { key: 'year', label: 'Год', kind: 'vehicle' },
  { key: 'vehicleClientId', label: 'Внешний ID владельца', kind: 'vehicle' },
  { key: 'vehicleClientPhone', label: 'Телефон владельца', kind: 'vehicle' },
  { key: 'orderId', label: 'Внешний ID заказа', kind: 'order' },
  { key: 'orderStatus', label: 'Статус заказа (completed или cancelled)', kind: 'order' },
  { key: 'occurredAt', label: 'Дата заказа (ISO 8601)', kind: 'order' },
  { key: 'orderComment', label: 'Комментарий к заказу', kind: 'order' },
  { key: 'orderType', label: 'Тип обслуживания', kind: 'order' },
  { key: 'amount', label: 'Сумма', kind: 'order' },
  { key: 'discountAmount', label: 'Скидка', kind: 'order' },
  { key: 'partyClientId', label: 'Внешний ID клиента-покупателя', kind: 'order' },
  { key: 'partyName', label: 'Имя покупателя (снимок)', kind: 'order' },
  { key: 'detailName', label: 'Название услуги или работы', kind: 'order-line' },
  { key: 'detailDescription', label: 'Описание строки', kind: 'order-line' },
  { key: 'quantity', label: 'Количество', kind: 'order-line' },
  { key: 'unitPrice', label: 'Цена за единицу', kind: 'order-line' },
  { key: 'partyClientId', label: 'Внешний ID клиента-плательщика', kind: 'payment' },
  { key: 'partyName', label: 'Имя плательщика (снимок)', kind: 'payment' },
  { key: 'paymentMethod', label: 'Способ оплаты', kind: 'payment' },
  { key: 'amount', label: 'Сумма оплаты', kind: 'payment' },
  { key: 'occurredAt', label: 'Дата оплаты (ISO 8601)', kind: 'payment' },
  { key: 'cancelledAt', label: 'Дата отмены оплаты (ISO 8601)', kind: 'payment' },
  { key: 'actorName', label: 'Автор оплаты', kind: 'payment' },
  { key: 'historyComment', label: 'Комментарий к оплате', kind: 'payment' },
  { key: 'occurredAt', label: 'Дата события (ISO 8601)', kind: 'history' },
  { key: 'actorName', label: 'Исполнитель или автор', kind: 'history' },
  { key: 'eventType', label: 'Тип события', kind: 'history' },
  { key: 'historyComment', label: 'Комментарий события', kind: 'history' }
]

const clientFields: Array<{ key: TextCandidateField, label: string }> = [
  { key: 'externalId', label: 'Внешний ID' },
  { key: 'name', label: 'Имя клиента' },
  { key: 'phone', label: 'Телефон' },
  { key: 'comment', label: 'Комментарий' }
]
const vehicleFields: Array<{ key: TextCandidateField, label: string }> = [
  { key: 'externalId', label: 'Внешний ID' },
  { key: 'name', label: 'Автомобиль' },
  { key: 'brand', label: 'Марка' },
  { key: 'model', label: 'Модель' },
  { key: 'vin', label: 'VIN' },
  { key: 'plate', label: 'Госномер' },
  { key: 'year', label: 'Год' },
  { key: 'clientExternalId', label: 'Внешний ID клиента' },
  { key: 'clientPhone', label: 'Телефон клиента' }
]
const orderFields: Array<{ key: TextCandidateField, label: string }> = [
  { key: 'externalId', label: 'Внешний ID заказа' }, { key: 'vehicleExternalId', label: 'Внешний ID автомобиля' },
  { key: 'orderStatus', label: 'Статус (completed или cancelled)' },
  { key: 'occurredAt', label: 'Дата заказа (ISO 8601)' }, { key: 'orderComment', label: 'Комментарий' },
  { key: 'orderType', label: 'Тип обслуживания' }, { key: 'amount', label: 'Сумма' },
  { key: 'discountAmount', label: 'Скидка' }, { key: 'clientExternalId', label: 'Внешний ID покупателя' },
  { key: 'partyName', label: 'Покупатель (снимок имени)' }
]
const orderLineFields: Array<{ key: TextCandidateField, label: string }> = [
  { key: 'orderExternalId', label: 'Внешний ID заказа' }, { key: 'name', label: 'Услуга или работа' },
  { key: 'detailDescription', label: 'Описание' }, { key: 'quantity', label: 'Количество' },
  { key: 'unitPrice', label: 'Цена за единицу' }
]
const paymentFields: Array<{ key: TextCandidateField, label: string }> = [
  { key: 'orderExternalId', label: 'Внешний ID заказа' }, { key: 'amount', label: 'Сумма' },
  { key: 'paymentMethod', label: 'Способ оплаты' }, { key: 'occurredAt', label: 'Дата оплаты (ISO 8601)' },
  { key: 'cancelledAt', label: 'Дата отмены (ISO 8601)' }, { key: 'actorName', label: 'Автор оплаты' },
  { key: 'historyComment', label: 'Комментарий к оплате' },
  { key: 'clientExternalId', label: 'Внешний ID плательщика' }, { key: 'partyName', label: 'Плательщик (снимок имени)' }
]
const historyFields: Array<{ key: TextCandidateField, label: string }> = [
  { key: 'orderExternalId', label: 'Внешний ID заказа' }, { key: 'occurredAt', label: 'Дата события (ISO 8601)' },
  { key: 'actorName', label: 'Исполнитель или автор' }, { key: 'eventType', label: 'Тип события' },
  { key: 'historyComment', label: 'Комментарий события' }
]
const recoveryCandidateFields: MutableCandidateField[] = [
  'externalId', 'name', 'phone', 'comment', 'brand', 'model', 'vin', 'plate', 'year',
  'clientExternalId', 'clientPhone', 'vehicleExternalId', 'orderExternalId', 'partyName', 'orderStatus', 'occurredAt', 'orderComment',
  'orderType', 'detailDescription', 'quantity', 'unitPrice', 'discountAmount', 'amount', 'paymentMethod',
  'cancelledAt', 'actorName', 'eventType', 'historyComment', 'existingClientId', 'createSeparateClient', 'excluded'
]

const emit = defineEmits<{ accessChanged: [canImport: boolean] }>()
const canImport = ref(false)

watch(canImport, value => emit('accessChanged', value), { immediate: true })

const isAccessLoaded = ref(false)
const drafts = ref<OrganizationImportSummary[]>([])
const draft = ref<OrganizationImportDraft | null>(null)
const mappingDraft = ref<OrganizationImportMapping[]>([])
const profiles = ref<OrganizationImportProfile[]>([])
const sourceKeyDraft = ref('')
const profileNameDraft = ref('')
const selectedProfileId = ref('')
const pendingProfile = ref<OrganizationImportProfile | null>(null)
const profileError = ref('')
const mappingDirty = ref(false)
const rowsDirty = ref(false)
const isBusy = ref(false)
const isSavingRows = ref(false)
const rowSaveStatus = ref<'idle' | 'pending' | 'saving' | 'saved' | 'error'>('idle')
const rowSaveError = ref('')
const recoveryError = ref('')
const pendingRecovery = ref<{ recovery: LocalRecovery, revisionMatches: boolean } | null>(null)
const errorMessage = ref('')
const successMessage = ref('')
const search = ref('')
const searchDraft = ref('')
const statusFilter = ref('all')
const kindFilter = ref('all')
const page = ref(1)
const batchNotice = ref('')
const activeOperation = ref<'upload' | 'mapping' | 'validation' | 'import' | 'pause' | null>(null)
const selectedTableId = ref('')
const expandedRowId = ref<string | null>(null)
const showSource = ref(false)
const confirmSummaryOpen = ref(false)
const pendingMappingRebuild = ref(false)
const undoSnapshot = ref<OrganizationImportCandidate[] | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)
const pendingDraftChange = ref<{ type: 'open', draftId: string } | { type: 'upload', file: File } | null>(null)
let organizationGeneration = 0
let rowEditGeneration = 0
let autosaveTimeout: ReturnType<typeof setTimeout> | null = null
let baselineRows: OrganizationImportCandidate[] = []

const selectedTable = computed(() => draft.value?.data.tables.find(table => table.id === selectedTableId.value) ?? draft.value?.data.tables[0] ?? null)
const selectedMapping = computed(() => mappingDraft.value.find(mapping => mapping.tableId === selectedTable.value?.id) ?? null)
const selectedProfile = computed(() => profiles.value.find(profile => profile.id === selectedProfileId.value) ?? null)
const visibleMappingFields = computed(() => {
  const kind = selectedMapping.value?.kind
  if (kind === 'combined') return mappingFields.filter(field => field.kind === 'client' || field.kind === 'vehicle')
  if (kind && kind !== 'ignore') return mappingFields.filter(field => field.kind === kind)
  return []
})
const rows = computed(() => draft.value?.data.rows ?? [])
const isImported = computed(() => Boolean(draft.value?.importedAt) || draft.value?.state === 'completed')
const isBatch = computed(() => (draft.value?.storageVersion ?? 1) >= 2)
const isBatchFailed = computed(() => isBatch.value && draft.value?.state === 'failed')
const isEditingLocked = computed(() => isBusy.value || isSavingRows.value || isImported.value || isBatchFailed.value || Boolean(pendingDraftChange.value) || Boolean(pendingRecovery.value))
const isInputDisabled = computed(() => isBusy.value || isImported.value || isBatchFailed.value || Boolean(pendingDraftChange.value) || Boolean(pendingRecovery.value))
const isMappingLocked = computed(() => isEditingLocked.value || (isBatch.value && draft.value?.state === 'paused' && (draft.value.processedRows ?? 0) > 0))
const mappingNeedsBuild = computed(() => Boolean(
  draft.value
  && (isBatch.value ? draft.value.totalRows === 0 : draft.value.data.rows.length === 0)
  && mappingDraft.value.some(mapping => mapping.kind !== 'ignore')
))
const mappingResetCount = computed(() => isBatch.value ? draft.value?.totalRows ?? 0 : rows.value.length)
const canSaveMapping = computed(() => Boolean(draft.value && !isMappingLocked.value && !isSavingRows.value && (mappingDirty.value || mappingNeedsBuild.value)))
const sourceKeyChanged = computed(() => Boolean(draft.value && sourceKeyDraft.value.trim() !== (draft.value.sourceKey ?? '')))
const canSaveProfile = computed(() => Boolean(draft.value && profileNameDraft.value.trim() && mappingDraft.value.length && !isBusy.value && !isMappingLocked.value))
const sourceRequiredForBusiness = computed(() => {
  const reconciliation = draft.value?.data.reconciliation
  if (reconciliation) return reconciliation.newOrders > 0 || reconciliation.newPayments > 0 || reconciliation.newHistoryEvents > 0
  return rows.value.some(row => ['order', 'order-line', 'payment', 'history'].includes(row.kind) && !row.excluded && !['skip', 'duplicate'].includes(row.status))
})
const serverMappingPreview = computed(() => rows.value
  .filter(row => row.tableId === selectedTable.value?.id)
  .slice(0, 3)
  .map(row => ({ row, result: `${row.name || row.externalId || row.orderExternalId || row.paymentMethod || 'Без названия'} · ${row.message || statusLabel(row.status)}` })))
const filteredRows = computed(() => isBatch.value ? rows.value : rows.value.filter(row => {
  if (kindFilter.value !== 'all' && row.kind !== kindFilter.value) return false
  if (statusFilter.value !== 'all' && row.status !== statusFilter.value) return false
  if (search.value.trim()) {
    const query = search.value.trim().toLocaleLowerCase('ru-RU')
    const searchable = [row.name, row.phone, row.plate, row.vin, row.externalId, row.message, row.linkedClientName]
      .filter(Boolean).join(' ').toLocaleLowerCase('ru-RU')
    if (!searchable.includes(query)) return false
  }
  return true
}))
const pageSize = computed(() => draft.value?.pageSize || 50)
const filteredRowCount = computed(() => isBatch.value ? draft.value?.filteredRows ?? rows.value.length : filteredRows.value.length)
const pageCount = computed(() => Math.max(1, Math.ceil(filteredRowCount.value / pageSize.value)))
const pageRows = computed(() => isBatch.value ? filteredRows.value : filteredRows.value.slice((page.value - 1) * pageSize.value, page.value * pageSize.value))
const editFields = computed(() => {
  const fields = kindFilter.value === 'client'
    ? clientFields
    : kindFilter.value === 'vehicle'
      ? vehicleFields
      : kindFilter.value === 'order'
        ? orderFields
        : kindFilter.value === 'order-line'
          ? orderLineFields
          : kindFilter.value === 'payment'
            ? paymentFields
            : kindFilter.value === 'history'
              ? historyFields
              : [...clientFields, ...vehicleFields, ...orderFields, ...orderLineFields, ...paymentFields, ...historyFields]
  return [...new Map(fields.map(field => [field.key, field])).values()]
})
const compactFields: Array<{ key: TextCandidateField, label: string }> = [
  { key: 'externalId', label: 'Внешний ID' },
  { key: 'name', label: 'Имя / название' },
  { key: 'occurredAt', label: 'Дата' },
  { key: 'amount', label: 'Сумма' }
]
const tableFields = computed(() => kindFilter.value === 'all' ? compactFields : editFields.value)
const summaryCounts = computed(() => draft.value?.counts ?? {})
const summary = computed(() => {
  if (isBatch.value) {
    const counts = summaryCounts.value
    const reconciliation = draft.value?.data.reconciliation
    return {
      clients: reconciliation?.newClients ?? counts['create-client'] ?? 0,
      vehicles: reconciliation?.newVehicles ?? counts['create-vehicle'] ?? 0,
      linked: reconciliation?.linkedClients ?? counts['link-client'] ?? counts.link ?? 0,
      skipped: (counts.skip ?? 0) + (counts.duplicate ?? 0),
      problems: (counts.error ?? 0) + (counts.conflict ?? 0),
      total: draft.value?.totalRows ?? 0
    }
  }
  const activeRows = rows.value.filter(row => !row.excluded && row.status !== 'skip' && row.status !== 'duplicate')
  return {
    clients: activeRows.filter(row => row.kind === 'client' && row.status === 'create').length,
    vehicles: activeRows.filter(row => row.kind === 'vehicle' && row.status === 'create').length,
    linked: activeRows.filter(row => row.status === 'link').length,
    skipped: rows.value.filter(row => row.excluded || row.status === 'skip' || row.status === 'duplicate').length,
    problems: activeRows.filter(row => row.status === 'error' || row.status === 'conflict').length,
    total: rows.value.length
  }
})
const canConfirm = computed(() => Boolean(
  draft.value
  && !draft.value.importedAt
  && (!isBatch.value || draft.value.state === 'ready')
  && !isBusy.value
  && !isSavingRows.value
  && !pendingRecovery.value
  && (isBatch.value ? summary.value.total > summary.value.skipped : rows.value.length > 0 && rows.value.some(row => !row.excluded))
  && !rowsDirty.value
  && !mappingDirty.value
  && (!sourceRequiredForBusiness.value || Boolean(draft.value.sourceKey?.trim()))
  && summary.value.problems === 0
))

const recoveryKey = computed(() => `opencrm:imports:recovery:v1:${encodeURIComponent(props.userId)}:${encodeURIComponent(props.organizationId)}:${encodeURIComponent(draft.value?.id ?? '')}`)
const hasPendingChanges = computed(() => rowsDirty.value || mappingDirty.value || isSavingRows.value || Boolean(pendingRecovery.value) || rowSaveStatus.value === 'pending' || rowSaveStatus.value === 'saving')

function copyMappings(mappings: OrganizationImportMapping[]) {
  return mappings.map(mapping => ({
    ...mapping,
    fields: Object.fromEntries(Object.entries(mapping.fields ?? {}).map(([key, columns]) => [key, [...columns]])),
    transformations: Object.fromEntries(Object.entries(mapping.transformations ?? {}).map(([key, transformation]) => [key, {
      ...transformation,
      ...(transformation.values ? { values: { ...transformation.values } } : {})
    }]))
  }))
}

function copyRows(rows: OrganizationImportCandidate[]) {
  return rows.map(row => ({ ...row, matches: row.matches.map(match => ({ ...match })) }))
}

function clearAutosave() {
  if (autosaveTimeout) clearTimeout(autosaveTimeout)
  autosaveTimeout = null
}

function rowChangesAgainstBaseline(rowsToCompare: OrganizationImportCandidate[]) {
  const baselineById = new Map(baselineRows.map(row => [row.id, row]))

  return rowsToCompare.flatMap(row => {
    const baseline = baselineById.get(row.id)
    if (!baseline) return []
    const changes: RecoveryRowEdit['changes'] = {}
    recoveryCandidateFields.forEach(field => {
      if (baseline[field] !== row[field]) changes[field] = row[field] ?? null
    })
    return Object.keys(changes).length ? [{ id: row.id, changes }] : []
  })
}

function readRecovery(key = recoveryKey.value): LocalRecovery | null {
  try {
    const serialized = window.sessionStorage.getItem(key)
    if (!serialized) return null
    const value = JSON.parse(serialized) as LocalRecovery
    if (
      !value
      || value.version !== 1
      || value.userId !== props.userId
      || value.organizationId !== props.organizationId
      || value.draftId !== draft.value?.id
      || !Number.isInteger(value.revision)
      || !Array.isArray(value.rowEdits)
      || (value.mappings !== undefined && !Array.isArray(value.mappings))
    ) {
      return null
    }
    return value
  } catch {
    recoveryError.value = 'Локальное восстановление недоступно. Не закрывайте страницу до завершения серверного сохранения.'
    return null
  }
}

function persistRecovery() {
  if (!draft.value || (!rowsDirty.value && !mappingDirty.value)) return
  const rowEdits = rowChangesAgainstBaseline(rows.value)
  if (!rowEdits.length && !mappingDirty.value) {
    clearRecovery(draft.value.id)
    return
  }
  const recovery: LocalRecovery = {
    version: 1,
    userId: props.userId,
    organizationId: props.organizationId,
    draftId: draft.value.id,
    revision: draft.value.revision,
    rowEdits,
    ...(mappingDirty.value ? { mappings: copyMappings(mappingDraft.value) } : {}),
    page: page.value,
    kind: kindFilter.value,
    status: statusFilter.value,
    search: search.value
  }

  try {
    window.sessionStorage.setItem(recoveryKey.value, JSON.stringify(recovery))
    recoveryError.value = ''
  } catch {
    recoveryError.value = 'Не удалось сохранить локальную копию правок. Дождитесь серверного сохранения и не закрывайте страницу.'
  }
}

function clearRecovery(draftId: string, revision?: number) {
  const key = `opencrm:imports:recovery:v1:${encodeURIComponent(props.userId)}:${encodeURIComponent(props.organizationId)}:${encodeURIComponent(draftId)}`
  try {
    const saved = window.sessionStorage.getItem(key)
    if (!saved) {
      recoveryError.value = ''
      return
    }
    const value = JSON.parse(saved) as LocalRecovery
    if (revision !== undefined && value.revision !== revision) return
    window.sessionStorage.removeItem(key)
    recoveryError.value = ''
  } catch {
    recoveryError.value = 'Не удалось очистить локальную копию. Проверьте черновик перед закрытием страницы.'
  }
}

function saveRowsChanged() {
  rowsDirty.value = true
  rowEditGeneration += 1
  rowSaveStatus.value = 'pending'
  rowSaveError.value = ''
  confirmSummaryOpen.value = false
  errorMessage.value = ''
  successMessage.value = ''
  persistRecovery()
  scheduleAutosave()
}

function scheduleAutosave() {
  clearAutosave()
  if (!rowsDirty.value || mappingDirty.value || isImported.value || !draft.value) return
  autosaveTimeout = setTimeout(() => {
    autosaveTimeout = null
    void saveRows()
  }, 800)
}

function current(generation: number, organizationId: string) {
  return generation === organizationGeneration && organizationId === props.organizationId
}

function resetState() {
  clearAutosave()
  canImport.value = false
  isAccessLoaded.value = false
  isBusy.value = false
  isSavingRows.value = false
  rowSaveStatus.value = 'idle'
  rowSaveError.value = ''
  recoveryError.value = ''
  pendingRecovery.value = null
  pendingMappingRebuild.value = false
  drafts.value = []
  profiles.value = []
  draft.value = null
  mappingDraft.value = []
  sourceKeyDraft.value = ''
  profileNameDraft.value = ''
  selectedProfileId.value = ''
  pendingProfile.value = null
  profileError.value = ''
  mappingDirty.value = false
  rowsDirty.value = false
  errorMessage.value = ''
  successMessage.value = ''
  search.value = ''
  searchDraft.value = ''
  statusFilter.value = 'all'
  kindFilter.value = 'all'
  page.value = 1
  selectedTableId.value = ''
  expandedRowId.value = null
  showSource.value = false
  confirmSummaryOpen.value = false
  undoSnapshot.value = null
  pendingDraftChange.value = null
  baselineRows = []
  rowEditGeneration = 0
  batchNotice.value = ''
  activeOperation.value = null
}

async function loadDraftList(generation = organizationGeneration, organizationId = props.organizationId) {
  const items = await loadOrganizationImports(organizationId)
  if (current(generation, organizationId)) drafts.value = items
}

function currentPageQuery(overrides: Partial<OrganizationImportPageQuery> = {}): OrganizationImportPageQuery {
  return {
    page: overrides.page ?? page.value,
    pageSize: 50,
    kind: overrides.kind ?? kindFilter.value,
    status: overrides.status ?? statusFilter.value,
    search: overrides.search ?? search.value
  }
}

function isBatchBusy(state: OrganizationImportState | undefined) {
  return state === 'upload-queued' || state === 'mapping-queued' || state === 'validation-queued' || state === 'import-queued' || state === 'importing'
}

function mergeBatchPage(value: OrganizationImportDraft, preserveLocalEdits = false) {
  const localRows = preserveLocalEdits ? copyRows(rows.value) : value.data.rows
  draft.value = {
    ...value,
    data: {
      ...value.data,
      mappings: mappingDirty.value ? copyMappings(mappingDraft.value) : value.data.mappings,
      rows: localRows
    }
  }
  if (!mappingDirty.value) mappingDraft.value = copyMappings(value.data.mappings)
  if (!value.data.tables.some(table => table.id === selectedTableId.value)) selectedTableId.value = value.data.tables[0]?.id ?? ''
  page.value = value.page || page.value
  if (expandedRowId.value && !draft.value.data.rows.some(row => row.id === expandedRowId.value)) expandedRowId.value = null
  undoSnapshot.value = preserveLocalEdits ? undoSnapshot.value : null
}

async function fetchBatchPage(query = currentPageQuery(), generation = organizationGeneration, organizationId = props.organizationId, draftId = draft.value?.id) {
  if (!draftId) return null
  const result = await loadOrganizationImportBatchPage(organizationId, draftId, query)
  if (!current(generation, organizationId) || draft.value?.id !== draftId) return null
  mergeBatchPage(result, rowsDirty.value)
  return result
}

function operationForState(state: OrganizationImportState): 'upload' | 'mapping' | 'validation' | 'import' | null {
  if (state === 'upload-queued') return 'upload'
  if (state === 'mapping-queued') return 'mapping'
  if (state === 'validation-queued') return 'validation'
  if (state === 'import-queued' || state === 'importing') return 'import'
  return null
}

async function pollBatchUntilSettled(
  initial: OrganizationImportDraft,
  generation = organizationGeneration,
  organizationId = props.organizationId,
  targetDraftId = initial.id,
  operation = operationForState(initial.state)
) {
  if (!operation) return initial
  activeOperation.value = operation
  let latest = initial
  const requestedPage = page.value
  mergeBatchPage(initial, rowsDirty.value)
  if (operation !== 'mapping') page.value = requestedPage
  try {
    while (isBatchBusy(latest.state)) {
      await new Promise(resolve => setTimeout(resolve, 1500))
      if (!current(generation, organizationId) || draft.value?.id !== targetDraftId) return null
      latest = await loadOrganizationImportBatchPage(organizationId, targetDraftId, currentPageQuery())
      if (!current(generation, organizationId) || draft.value?.id !== targetDraftId) return null
      mergeBatchPage(latest, rowsDirty.value)
    }
    if (latest.state === 'failed') {
      errorMessage.value = latest.error || 'Фоновая обработка завершилась с ошибкой.'
    }
    return latest
  } finally {
    if (current(generation, organizationId) && draft.value?.id === targetDraftId) activeOperation.value = null
  }
}

async function initializeOrganization() {
  const generation = ++organizationGeneration
  const organizationId = props.organizationId
  resetState()
  try {
    const access = await loadOrganizationImportAccess(organizationId)
    if (!current(generation, organizationId)) return
    canImport.value = access.canImport
    isAccessLoaded.value = true
    if (!access.canImport) return
    const [draftList, profileList] = await Promise.allSettled([
      loadOrganizationImports(organizationId),
      loadOrganizationImportProfiles(organizationId)
    ])
    if (!current(generation, organizationId)) return
    if (draftList.status === 'fulfilled') drafts.value = draftList.value
    else throw draftList.reason
    if (profileList.status === 'fulfilled') profiles.value = profileList.value
    else profileError.value = profileList.reason instanceof Error ? profileList.reason.message : 'Не удалось загрузить профили импорта.'
  } catch (error) {
    if (!current(generation, organizationId)) return
    isAccessLoaded.value = true
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось загрузить импорты.'
  }
}

watch(() => [props.organizationId, props.userId], () => void initializeOrganization(), { immediate: true })

function handleBeforeUnload(event: BeforeUnloadEvent) {
  if (!hasPendingChanges.value) return
  event.preventDefault()
  event.returnValue = ''
}

onMounted(() => window.addEventListener('beforeunload', handleBeforeUnload))
onBeforeUnmount(() => {
  clearAutosave()
  organizationGeneration += 1
  window.removeEventListener('beforeunload', handleBeforeUnload)
})

function setDraft(value: OrganizationImportDraft) {
  clearAutosave()
  recoveryError.value = ''
  draft.value = value
  mappingDraft.value = copyMappings(value.data.mappings)
  sourceKeyDraft.value = value.sourceKey ?? ''
  baselineRows = copyRows(value.data.rows)
  selectedTableId.value = value.data.tables[0]?.id ?? ''
  expandedRowId.value = null
  mappingDirty.value = false
  rowsDirty.value = false
  isSavingRows.value = false
  rowSaveStatus.value = 'idle'
  rowSaveError.value = ''
  pendingRecovery.value = null
  pendingMappingRebuild.value = false
  rowEditGeneration = 0
  undoSnapshot.value = null
  page.value = value.page || 1
  search.value = ''
  searchDraft.value = ''
  kindFilter.value = 'all'
  statusFilter.value = 'all'
  showSource.value = false
  confirmSummaryOpen.value = false
  pendingProfile.value = null
}

async function openDraft(draftId: string) {
  clearAutosave()
  const generation = organizationGeneration
  const organizationId = props.organizationId
  isBusy.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const storageVersion = drafts.value.find(item => item.id === draftId)?.storageVersion ?? 1
    const result = storageVersion >= 2
      ? await loadOrganizationImportBatchPage(organizationId, draftId, { page: 1, pageSize: 50 })
      : await loadOrganizationImportDraft(organizationId, draftId)
    if (current(generation, organizationId)) {
      setDraft(result)
      search.value = ''
      searchDraft.value = ''
      kindFilter.value = 'all'
      statusFilter.value = 'all'
      recoveryError.value = ''
      if (result.storageVersion >= 2 && isBatchBusy(result.state)) {
        activeOperation.value = operationForState(result.state)
        await pollBatchUntilSettled(result, generation, organizationId, draftId)
      }
      const recovery = readRecovery()
      if (recovery && recovery.revision === draft.value?.revision && draft.value.storageVersion >= 2) {
        page.value = Math.max(1, recovery.page ?? 1)
        kindFilter.value = recovery.kind ?? 'all'
        statusFilter.value = recovery.status ?? 'all'
        search.value = recovery.search ?? ''
        searchDraft.value = search.value
        await fetchBatchPage(currentPageQuery(), generation, organizationId, draftId)
      }
      if (recovery && (recovery.rowEdits.length || recovery.mappings)) {
        pendingRecovery.value = { recovery, revisionMatches: recovery.revision === draft.value?.revision }
      } else if (recovery) {
        clearRecovery(result.id, recovery.revision)
      }
    }
  } catch (error) {
    if (current(generation, organizationId)) errorMessage.value = error instanceof Error ? error.message : 'Не удалось открыть черновик.'
  } finally {
    if (current(generation, organizationId)) {
      isBusy.value = false
      if (rowsDirty.value) scheduleAutosave()
    }
  }
}

function requestOpenDraft(draftId: string) {
  if (draft.value?.id === draftId) return
  if (rowsDirty.value || mappingDirty.value) {
    pendingDraftChange.value = { type: 'open', draftId }
    return
  }

  void openDraft(draftId)
}

function downloadRecovery() {
  if (!pendingRecovery.value) return
  const recovery = pendingRecovery.value.recovery
  const blob = new Blob([buildImportRecoveryReport(recovery)], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `импорт-${recovery.draftId}-локальные-правки-v${recovery.revision}.csv`
  link.click()
  URL.revokeObjectURL(url)
}

function declineRecovery() {
  if (!pendingRecovery.value) return
  clearRecovery(pendingRecovery.value.recovery.draftId, pendingRecovery.value.recovery.revision)
  pendingRecovery.value = null
}

function restoreRecovery() {
  const pending = pendingRecovery.value
  if (!pending || !draft.value || !pending.revisionMatches) return
  if (pending.recovery.mappings) {
    const knownTableIds = new Set(draft.value.data.tables.map(table => table.id))
    if (pending.recovery.mappings.some(mapping => !mapping || !knownTableIds.has(mapping.tableId))) {
      recoveryError.value = 'Локальное сопоставление не подходит к этому файлу. Откройте актуальную версию без локальных правок.'
      return
    }
  }

  const rowsById = new Map(rows.value.map(row => [row.id, row]))
  let restoredRows = 0
  pending.recovery.rowEdits.forEach(edit => {
    if (!edit || typeof edit.id !== 'string' || !edit.changes || typeof edit.changes !== 'object') return
    const row = rowsById.get(edit.id)
    if (!row) return
    let changed = false
    Object.entries(edit.changes).forEach(([field, value]) => {
      if (!recoveryCandidateFields.includes(field as MutableCandidateField)) return
      if (field === 'excluded' || field === 'createSeparateClient' ? typeof value !== 'boolean' : value !== null && typeof value !== 'string') return
      ;(row as unknown as Record<string, unknown>)[field] = value
      changed = true
    })
    if (changed) restoredRows += 1
  })
  if (pending.recovery.mappings) {
    mappingDraft.value = copyMappings(pending.recovery.mappings)
    mappingDirty.value = true
  }
  rowsDirty.value = restoredRows > 0
  pendingRecovery.value = null
  recoveryError.value = ''
  if (rowsDirty.value) {
    rowEditGeneration += 1
    rowSaveStatus.value = 'pending'
  }
  if (mappingDirty.value || rowsDirty.value) {
    persistRecovery()
    scheduleAutosave()
  }
  else clearRecovery(pending.recovery.draftId, pending.recovery.revision)
}

async function uploadFile(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  input.value = ''
  if (file.size > 50 * 1024 * 1024) {
    errorMessage.value = 'Файл превышает ограничение 50 МБ.'
    return
  }
  if (rowsDirty.value || mappingDirty.value) {
    pendingDraftChange.value = { type: 'upload', file }
    return
  }

  await createDraft(file)
}

async function createDraft(file: File) {
  clearAutosave()
  const generation = organizationGeneration
  const organizationId = props.organizationId
  isBusy.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const result = await uploadOrganizationImportBatch(organizationId, file)
    if (!current(generation, organizationId)) return
    setDraft(result)
    await loadDraftList(generation, organizationId)
    if (isBatchBusy(result.state)) await pollBatchUntilSettled(result, generation, organizationId, result.id, 'upload')
  } catch (error) {
    if (current(generation, organizationId)) errorMessage.value = error instanceof Error ? error.message : 'Не удалось загрузить файл.'
  } finally {
    if (current(generation, organizationId)) {
      isBusy.value = false
      if (rowsDirty.value) scheduleAutosave()
    }
  }
}

function confirmDraftChange() {
  const action = pendingDraftChange.value
  if (!action) return
  if (draft.value) clearRecovery(draft.value.id, draft.value.revision)
  pendingDraftChange.value = null
  if (action.type === 'open') {
    void openDraft(action.draftId)
    return
  }

  void createDraft(action.file)
}

function mappingKindChanged(kind: MappingKind) {
  if (isMappingLocked.value) return
  if (!selectedMapping.value) return
  selectedMapping.value.kind = kind
  selectedMapping.value.fields = Object.fromEntries(Object.entries(selectedMapping.value.fields).filter(([key]) =>
    mappingFields.some(field => field.key === key && (kind === 'combined' || field.kind === kind))))
  selectedMapping.value.transformations = Object.fromEntries(Object.entries(selectedMapping.value.transformations ?? {}).filter(([key]) =>
    Object.hasOwn(selectedMapping.value?.fields ?? {}, key)))
  markMappingDirty()
}

function mappingColumnsChanged(fieldKey: string, event: Event) {
  if (isMappingLocked.value) return
  if (!selectedMapping.value) return
  const select = event.target as HTMLSelectElement
  selectedMapping.value.fields[fieldKey] = Array.from(select.selectedOptions, option => Number(option.value))
  if (!selectedMapping.value.fields[fieldKey].length && selectedMapping.value.transformations) {
    delete selectedMapping.value.transformations[fieldKey]
  }
  markMappingDirty()
}

function fieldTransformation(fieldKey: string) {
  return selectedMapping.value?.transformations?.[fieldKey] ?? null
}

function mappingTransformationTypeChanged(fieldKey: string, type: string) {
  if (isMappingLocked.value || !selectedMapping.value) return
  selectedMapping.value.transformations ??= {}
  if (!type) delete selectedMapping.value.transformations[fieldKey]
  else selectedMapping.value.transformations[fieldKey] = { type: type as OrganizationImportFieldTransformation['type'] }
  markMappingDirty()
}

function mappingTransformationFormatChanged(fieldKey: string, format: string) {
  if (isMappingLocked.value || !selectedMapping.value) return
  const transformation = fieldTransformation(fieldKey)
  if (!transformation) return
  transformation.format = format
  markMappingDirty()
}

function mappingValueMapChanged(fieldKey: string, event: Event) {
  if (isMappingLocked.value || !selectedMapping.value) return
  const transformation = fieldTransformation(fieldKey)
  if (!transformation) return
  const lines = (event.target as HTMLTextAreaElement).value.split('\n')
  const values: Record<string, string> = {}
  for (const line of lines) {
    if (!line.trim()) continue
    const separator = line.indexOf('→')
    if (separator < 0) continue
    values[line.slice(0, separator).trim()] = line.slice(separator + 1).trim()
  }
  transformation.values = values
  markMappingDirty()
}

function mappingValueMapText(fieldKey: string) {
  const values = fieldTransformation(fieldKey)?.values ?? {}
  return Object.entries(values).map(([source, target]) => `${source} → ${target}`).join('\n')
}

function transformationPreview(fieldKey: string) {
  const transformation = fieldTransformation(fieldKey)
  if (!transformation || !selectedTable.value || !selectedMapping.value) return []
  const columns = selectedMapping.value.fields[fieldKey] ?? []
  return selectedTable.value.rows.slice(0, 3).map(row => {
    const source = columns.map(index => row.values[index]?.trim()).filter(Boolean).join(' ')
    if (!source) return null
    if (transformation.type === 'value-map') return `${source} → ${transformation.values?.[source] ?? 'нет пары'}`
    if (transformation.type === 'phone') {
      let digits = source.replace(/\D/g, '')
      if (digits.length === 11 && digits.startsWith('8')) digits = `7${digits.slice(1)}`
      return `${source} → ${digits}`
    }
    if (transformation.type === 'decimal') return `${source} → ${source.replace(/[ \u00a0\u202f]/g, '').replace(',', '.')}`
    return `${source} → будет проверено сервером при построении строк`
  }).filter((value): value is string => value !== null)
}

async function saveSourceKey() {
  if (!draft.value || !isBatch.value || isBusy.value) return false
  if (!sourceKeyChanged.value) return true
  isBusy.value = true
  profileError.value = ''
  try {
    let result = await saveOrganizationImportBatchSource(props.organizationId, draft.value.id, {
      revision: draft.value.revision,
      sourceKey: sourceKeyDraft.value.trim()
    })
    if (isBatchBusy(result.state)) {
      const settled = await pollBatchUntilSettled(result, organizationGeneration, props.organizationId, draft.value.id, 'validation')
      if (!settled || settled.state === 'failed') {
        profileError.value = settled?.error || 'Не удалось перепроверить строки после смены источника.'
        return false
      }

      result = settled
    }

    draft.value = { ...draft.value, revision: result.revision, updatedAt: result.updatedAt, sourceKey: result.sourceKey }
    sourceKeyDraft.value = result.sourceKey ?? ''
    return true
  } catch (error) {
    profileError.value = error instanceof Error ? error.message : 'Не удалось сохранить источник данных.'
    return false
  } finally {
    isBusy.value = false
  }
}

function formatImportMoney(value: number | string) {
  const amount = Number(value)
  return Number.isFinite(amount) ? `${amount.toLocaleString('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₽` : '—'
}

function profileTables(): OrganizationImportProfile['tables'] {
  if (!draft.value) return []
  return draft.value.data.tables.map(table => ({ id: table.id, name: table.name, columns: [...table.columns], rows: [] }))
}

async function saveProfile() {
  if (!canSaveProfile.value || !draft.value) return
  if (!(await saveSourceKey())) return
  isBusy.value = true
  profileError.value = ''
  try {
    const profile = await saveOrganizationImportProfile(props.organizationId, {
      name: profileNameDraft.value.trim(),
      sourceKey: sourceKeyDraft.value.trim(),
      mappings: copyMappings(mappingDraft.value),
      tables: profileTables()
    })
    profiles.value = [profile, ...profiles.value.filter(item => item.id !== profile.id)]
    selectedProfileId.value = profile.id
    profileNameDraft.value = ''
    profileError.value = ''
  } catch (error) {
    profileError.value = error instanceof Error ? error.message : 'Не удалось сохранить профиль.'
  } finally {
    isBusy.value = false
  }
}

function profileMatchesCurrentTables(profile: OrganizationImportProfile) {
  return remapImportProfileMappings(profile, profileTables()) !== null
}

function requestApplyProfile() {
  if (!selectedProfile.value || isMappingLocked.value || isBusy.value) return
  if (!profileMatchesCurrentTables(selectedProfile.value)) {
    profileError.value = 'Структура файла не совпадает с профилем. Сопоставление не изменено.'
    return
  }

  profileError.value = ''
  pendingProfile.value = selectedProfile.value
}

async function applyProfile() {
  const profile = pendingProfile.value
  if (!profile || !draft.value || !profileMatchesCurrentTables(profile)) return
  const mappings = remapImportProfileMappings(profile, profileTables())
  if (!mappings) return
  const profileSourceKey = profile.sourceKey ?? ''
  if (profileSourceKey !== sourceKeyDraft.value.trim()) {
    sourceKeyDraft.value = profileSourceKey
    if (!(await saveSourceKey())) return
  }
  mappingDraft.value = copyMappings(mappings)
  sourceKeyDraft.value = profileSourceKey
  mappingDirty.value = true
  pendingProfile.value = null
  markMappingDirty()
  pendingMappingRebuild.value = mappingResetCount.value > 0
  void saveMapping()
}

async function removeProfile() {
  const profile = selectedProfile.value
  if (!profile || isBusy.value) return
  isBusy.value = true
  profileError.value = ''
  try {
    await deleteOrganizationImportProfile(props.organizationId, profile.id)
    profiles.value = profiles.value.filter(item => item.id !== profile.id)
    selectedProfileId.value = ''
  } catch (error) {
    profileError.value = error instanceof Error ? error.message : 'Не удалось удалить профиль.'
  } finally {
    isBusy.value = false
  }
}

function markMappingDirty() {
  mappingDirty.value = true
  pendingMappingRebuild.value = false
  confirmSummaryOpen.value = false
  persistRecovery()
}

function requestSaveMapping() {
  if (!canSaveMapping.value) return
  if (mappingNeedsBuild.value && !rows.value.length) {
    void saveMapping()
    return
  }
  pendingMappingRebuild.value = true
}

function cancelMappingChanges() {
  if (!draft.value || isSavingRows.value) return
  mappingDraft.value = copyMappings(draft.value.data.mappings)
  mappingDirty.value = false
  pendingMappingRebuild.value = false
  confirmSummaryOpen.value = false
  if (rowsDirty.value) {
    persistRecovery()
    scheduleAutosave()
    return
  }
  clearRecovery(draft.value.id, draft.value.revision)
}

async function saveMapping() {
  if (!draft.value || isMappingLocked.value || isSavingRows.value || (!pendingMappingRebuild.value && !mappingNeedsBuild.value) || (!mappingDirty.value && !mappingNeedsBuild.value)) return
  const draftId = draft.value.id
  const revision = draft.value.revision
  const generation = organizationGeneration
  const organizationId = props.organizationId
  isBusy.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const payload = { revision: draft.value.revision, mappings: mappingDraft.value }
    const result = isBatch.value
      ? await queueOrganizationImportBatchMapping(organizationId, draft.value.id, payload)
      : await saveOrganizationImportMapping(organizationId, draft.value.id, payload)
    if (!current(generation, organizationId)) return
    if (isBatch.value) {
      clearRecovery(draftId)
      page.value = 1
      kindFilter.value = 'all'
      statusFilter.value = 'all'
      search.value = ''
      searchDraft.value = ''
      const settled = await pollBatchUntilSettled(result, generation, organizationId, draftId, 'mapping')
      if (!settled || settled.state === 'failed') return
      mergeBatchPage(settled, false)
      baselineRows = copyRows(settled.data.rows)
    } else {
      setDraft(result)
    }
    mappingDirty.value = false
    clearRecovery(draftId)
    successMessage.value = 'Сопоставление сохранено. Строки построены заново, ручные правки сброшены.'
    await loadDraftList(generation, organizationId)
  } catch (error) {
    if (current(generation, organizationId)) errorMessage.value = error instanceof Error ? error.message : 'Не удалось сохранить сопоставление.'
  } finally {
    if (current(generation, organizationId)) {
      isBusy.value = false
      if (rowsDirty.value) scheduleAutosave()
    }
  }
}

function rememberUndo() {
  if (isEditingLocked.value || !draft.value) return
  undoSnapshot.value = rows.value.map(row => ({ ...row, matches: row.matches.map(match => ({ ...match })) }))
}

function updateRow(row: OrganizationImportCandidate, field: CandidateField, value: string | boolean | null) {
  if (isEditingLocked.value || row.appliedAt) return
  const currentRow = draft.value?.data.rows.find(item => item.id === row.id)
  if (!currentRow || currentRow[field] === value) return
  rememberUndo()
  ;(currentRow as unknown as Record<string, unknown>)[field] = value
  saveRowsChanged()
}

function updatePageExcluded(event: Event) {
  const excluded = (event.target as HTMLInputElement).checked
  if (isEditingLocked.value || pageRows.value.some(row => row.appliedAt)) return
  if (!pageRows.value.some(row => row.excluded !== excluded)) return
  rememberUndo()
  pageRows.value.forEach(row => { row.excluded = excluded })
  saveRowsChanged()
}

function updateExistingClient(row: OrganizationImportCandidate, selection: string) {
  if (isEditingLocked.value || row.appliedAt) return
  const currentRow = draft.value?.data.rows.find(item => item.id === row.id)
  if (!currentRow) return
  const createSeparateClient = selection === '__new'
  const existingClientId = createSeparateClient || !selection ? null : selection
  if (currentRow.existingClientId === existingClientId && currentRow.createSeparateClient === createSeparateClient) return
  rememberUndo()
  currentRow.existingClientId = existingClientId
  currentRow.createSeparateClient = createSeparateClient
  saveRowsChanged()
}

function candidateFields(kind: OrganizationImportKind) {
  if (kind === 'client') return clientFields
  if (kind === 'vehicle') return vehicleFields
  if (kind === 'order') return orderFields
  if (kind === 'order-line') return orderLineFields
  if (kind === 'payment') return paymentFields
  return historyFields
}

function fieldForRow(row: OrganizationImportCandidate, field: TextCandidateField): TextCandidateField | null {
  const fields = candidateFields(row.kind)
  if (fields.some(candidate => candidate.key === field)) return field
  if (field === 'externalId' && fields.some(candidate => candidate.key === 'orderExternalId')) return 'orderExternalId'

  return null
}

function candidateValue(row: OrganizationImportCandidate, field: TextCandidateField) {
  const rowField = fieldForRow(row, field)
  if (field === 'externalId') return row.externalId || row.orderExternalId || row.vehicleExternalId || row.clientExternalId || ''

  return rowField ? row[rowField] ?? '' : ''
}

function updateTableCell(row: OrganizationImportCandidate, field: TextCandidateField, event: Event) {
  const rowField = fieldForRow(row, field)
  if (!rowField) return
  updateRow(row, rowField, (event.target as HTMLInputElement).value)
}

function toggleExpandedRow(rowId: string) {
  expandedRowId.value = expandedRowId.value === rowId ? null : rowId
}

function isRowLocked(row: OrganizationImportCandidate) {
  return isEditingLocked.value || Boolean(row.appliedAt)
}

function kindLabel(kind: OrganizationImportKind) {
  return ({ client: 'Клиент', vehicle: 'Автомобиль', order: 'Заказ', 'order-line': 'Строка заказа', payment: 'Оплата', history: 'Событие истории' })[kind]
}

function undoLastEdit() {
  if (isEditingLocked.value || !draft.value || !undoSnapshot.value) return
  draft.value.data.rows = undoSnapshot.value
  undoSnapshot.value = null
  saveRowsChanged()
}

function pasteRows(event: ClipboardEvent) {
  if (isEditingLocked.value) return
  const target = event.target as HTMLInputElement
  const startField = Number(target.dataset.fieldIndex)
  const startRow = Number(target.dataset.rowIndex)
  const text = event.clipboardData?.getData('text/plain') ?? ''
  if (!/[\t\r\n]/.test(text)) return
  event.preventDefault()
  if (target.dataset.rowIndex === undefined || target.dataset.fieldIndex === undefined) {
    errorMessage.value = 'Для вставки диапазона используйте основные ячейки таблицы. В раскрытых полях доступна вставка одной ячейки.'
    return
  }
  if (text.includes('"')) {
    errorMessage.value = 'Вставка диапазона с кавычками или переносами внутри ячеек не поддерживается. Скопируйте обычный диапазон без кавычек.'
    return
  }
  if (!Number.isInteger(startField) || !Number.isInteger(startRow) || startField < 0 || startRow < 0) return

  const lines = text.replace(/\r\n?/g, '\n').split('\n')
  if (lines[lines.length - 1] === '') lines.pop()
  const values = lines.map(line => line.split('\t'))
  const fields = tableFields.value
  const rowsToEdit = pageRows.value
  const maxColumns = Math.max(...values.map(row => row.length))
  if (startRow + values.length > rowsToEdit.length || startField + maxColumns > fields.length) {
    errorMessage.value = 'Диапазон выходит за границы текущей страницы или таблицы. Выберите фильтр «Клиенты» или «Автомобили» и вставьте диапазон ещё раз.'
    return
  }

  const edits: Array<{ row: OrganizationImportCandidate, field: TextCandidateField, value: string }> = []
  for (let rowOffset = 0; rowOffset < values.length; rowOffset += 1) {
    const row = rowsToEdit[startRow + rowOffset]
    if (isRowLocked(row)) {
      errorMessage.value = `Строка ${row.sourceRow} уже импортирована и не редактируется. Вставка отменена полностью.`
      return
    }
    for (let fieldOffset = 0; fieldOffset < values[rowOffset].length; fieldOffset += 1) {
      const field = fields[startField + fieldOffset]
      const rowField = fieldForRow(row, field.key)
      if (!rowField) {
        errorMessage.value = 'Диапазон включает поля другого типа записи. Выберите фильтр «Клиенты» или «Автомобили» и вставьте диапазон ещё раз.'
        return
      }
      edits.push({ row, field: rowField, value: values[rowOffset][fieldOffset] })
    }
  }

  if (!edits.length) return
  rememberUndo()
  edits.forEach(({ row, field, value }) => {
    ;(row as unknown as Record<string, unknown>)[field] = value
  })
  saveRowsChanged()
}

async function saveAndValidate() {
  await saveRows()
}

async function requestQueryChange(next: Partial<OrganizationImportPageQuery>) {
  if (!draft.value || isBusy.value || isSavingRows.value || pendingRecovery.value) return
  if (mappingDirty.value) {
    errorMessage.value = 'Сначала сохраните сопоставление или отмените его, затем меняйте фильтры.'
    return
  }
  clearAutosave()
  if (rowsDirty.value) {
    await saveRows()
    if (rowsDirty.value || rowSaveStatus.value === 'error') return
  }

  const oldQuery = currentPageQuery()
  const nextQuery = currentPageQuery(next)
  expandedRowId.value = null
  page.value = nextQuery.page
  kindFilter.value = nextQuery.kind ?? 'all'
  statusFilter.value = nextQuery.status ?? 'all'
  search.value = nextQuery.search ?? ''
  searchDraft.value = search.value
  if (!isBatch.value) return

  const generation = organizationGeneration
  const organizationId = props.organizationId
  const draftId = draft.value.id
  isBusy.value = true
  errorMessage.value = ''
  try {
    await fetchBatchPage(nextQuery, generation, organizationId, draftId)
  } catch (error) {
    if (current(generation, organizationId) && draft.value?.id === draftId) {
      page.value = oldQuery.page
      kindFilter.value = oldQuery.kind ?? 'all'
      statusFilter.value = oldQuery.status ?? 'all'
      search.value = oldQuery.search ?? ''
      searchDraft.value = search.value
      errorMessage.value = error instanceof Error ? error.message : 'Не удалось загрузить страницу импорта.'
    }
  } finally {
    if (current(generation, organizationId) && draft.value?.id === draftId) isBusy.value = false
  }
}

async function saveRows() {
  if (!draft.value || isEditingLocked.value || isSavingRows.value || mappingDirty.value || !rows.value.length) return
  clearAutosave()
  const targetDraft = draft.value
  const targetDraftId = targetDraft.id
  const targetRevision = targetDraft.revision
  const targetEditGeneration = rowEditGeneration
  const generation = organizationGeneration
  const organizationId = props.organizationId
  const activeElement = document.activeElement
  const focusedInput = activeElement instanceof HTMLInputElement && activeElement.closest('.workspace-import__candidates')
    ? { element: activeElement, selectionStart: activeElement.selectionStart, selectionEnd: activeElement.selectionEnd }
    : null
  isSavingRows.value = true
  rowSaveStatus.value = 'saving'
  rowSaveError.value = ''
  try {
    const payloadRows = isBatch.value
      ? copyRows(targetDraft.data.rows.filter(row => rowChangesAgainstBaseline([row]).length > 0))
      : copyRows(targetDraft.data.rows)
    if (isBatch.value && !payloadRows.length) {
      rowsDirty.value = false
      clearRecovery(targetDraftId)
      rowSaveStatus.value = 'saved'
      return
    }
    const result = isBatch.value
      ? await patchOrganizationImportBatchRows(organizationId, targetDraftId, { revision: targetRevision, rows: payloadRows })
      : await saveOrganizationImportRows(organizationId, targetDraftId, {
      revision: targetRevision,
      rows: payloadRows
    })
    if (!current(generation, organizationId) || draft.value?.id !== targetDraftId) return
    if (isBatch.value) {
      clearRecovery(targetDraftId)
      const settled = await pollBatchUntilSettled(result, generation, organizationId, targetDraftId, 'validation')
      if (!settled || settled.state === 'failed') {
        rowSaveStatus.value = 'error'
        rowSaveError.value = settled?.error || 'Проверка правок завершилась с ошибкой.'
        return
      }
      mergeBatchPage(settled, false)
      baselineRows = copyRows(settled.data.rows)
      rowsDirty.value = false
      rowSaveStatus.value = 'saved'
      successMessage.value = 'Правки сохранены и проверены.'
      clearRecovery(targetDraftId)
      await loadDraftList(generation, organizationId)
      return
    }
    if (draft.value.revision !== targetRevision) {
      rowSaveStatus.value = 'error'
      rowSaveError.value = 'Версия черновика изменилась на сервере. Откройте его заново перед повторным сохранением.'
      return
    }
    const hasNewerEdits = rowEditGeneration !== targetEditGeneration
    const validatedById = new Map(result.data.rows.map(row => [row.id, row]))
    if (hasNewerEdits) {
      draft.value.data.rows.forEach(row => {
        const validated = validatedById.get(row.id)
        if (!validated) return
        row.status = validated.status
        row.message = validated.message
        row.matches = validated.matches
        row.linkedClientName = validated.linkedClientName
      })
      rowsDirty.value = true
      rowSaveStatus.value = 'pending'
    } else {
      draft.value.data.rows = copyRows(result.data.rows)
      rowsDirty.value = false
      rowSaveStatus.value = 'saved'
      successMessage.value = 'Правки сохранены и проверены.'
    }
    draft.value.revision = result.revision
    draft.value.updatedAt = result.updatedAt
    baselineRows = copyRows(result.data.rows)
    if (rowsDirty.value || mappingDirty.value) persistRecovery()
    else clearRecovery(targetDraftId, targetRevision)
    try {
      await loadDraftList(generation, organizationId)
    } catch (error) {
      if (current(generation, organizationId)) errorMessage.value = error instanceof Error ? error.message : 'Не удалось обновить историю.'
    }
    if (focusedInput?.element.isConnected && document.activeElement === focusedInput.element && focusedInput.selectionStart !== null && focusedInput.selectionEnd !== null) {
      focusedInput.element.setSelectionRange(focusedInput.selectionStart, focusedInput.selectionEnd)
    }
  } catch (error) {
    if (current(generation, organizationId) && draft.value?.id === targetDraftId) {
      rowSaveStatus.value = 'error'
      rowSaveError.value = error instanceof Error ? error.message : 'Не удалось сохранить правки.'
    }
  } finally {
    if (current(generation, organizationId) && draft.value?.id === targetDraftId) {
      isSavingRows.value = false
      if (rowsDirty.value && rowSaveStatus.value !== 'error') scheduleAutosave()
    }
  }
}

async function confirmImport() {
  if (!draft.value || !canConfirm.value) return
  const draftId = draft.value.id
  const revision = draft.value.revision
  const generation = organizationGeneration
  const organizationId = props.organizationId
  isBusy.value = true
  errorMessage.value = ''
  try {
    if (isBatch.value) {
      const response = await startOrganizationImportBatch(organizationId, draftId, revision)
      if (!current(generation, organizationId)) return
      confirmSummaryOpen.value = false
      const settled = await pollBatchUntilSettled(response, generation, organizationId, draftId, 'import')
      if (!settled) return
      mergeBatchPage(settled, false)
      clearRecovery(draftId)
      successMessage.value = settled.state === 'completed'
        ? `Импорт завершён. ${resultLabel(settled.result)}`
        : settled.state === 'paused'
          ? 'Импорт приостановлен. Его можно возобновить.'
          : 'Импорт завершился без полного результата.'
      await loadDraftList(generation, organizationId)
      return
    }
    const result = await confirmOrganizationImport(organizationId, draft.value.id, draft.value.revision)
    if (!current(generation, organizationId)) return
    setDraft(result)
    clearRecovery(draftId, revision)
    successMessage.value = 'Импорт завершён.'
    await loadDraftList(generation, organizationId)
  } catch (error) {
    if (current(generation, organizationId)) errorMessage.value = error instanceof Error ? error.message : 'Не удалось выполнить импорт.'
  } finally {
    if (current(generation, organizationId)) isBusy.value = false
  }
}

async function pauseBatch() {
  if (!draft.value || !isBatch.value || activeOperation.value !== 'import') return
  try {
    const result = await pauseOrganizationImportBatch(props.organizationId, draft.value.id, draft.value.revision)
    if (current(organizationGeneration, props.organizationId) && draft.value?.id === result.id) mergeBatchPage(result, rowsDirty.value)
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось приостановить импорт.'
  }
}

async function resumeBatch() {
  if (!draft.value || !isBatch.value || !['paused', 'failed'].includes(draft.value.state) || isBusy.value) return
  const generation = organizationGeneration
  const organizationId = props.organizationId
  const draftId = draft.value.id
  isBusy.value = true
  errorMessage.value = ''
  try {
    const response = await startOrganizationImportBatch(organizationId, draftId, draft.value.revision, true)
    if (!current(generation, organizationId)) return
    const settled = await pollBatchUntilSettled(response, generation, organizationId, draftId, operationForState(response.state) ?? 'import')
    if (!settled) return
    mergeBatchPage(settled, false)
    successMessage.value = settled.state === 'completed'
      ? `Импорт завершён. ${resultLabel(settled.result)}`
      : settled.state === 'paused'
        ? 'Импорт приостановлен. Его можно возобновить.'
        : settled.state === 'ready'
          ? 'Подготовка завершена. Проверьте сопоставление и строки.'
          : 'Обработка завершилась ошибкой. Можно повторить.'
    await loadDraftList(generation, organizationId)
  } catch (error) {
    if (current(generation, organizationId)) errorMessage.value = error instanceof Error ? error.message : 'Не удалось возобновить импорт.'
  } finally {
    if (current(generation, organizationId) && draft.value?.id === draftId) isBusy.value = false
  }
}

function reloadCurrentDraft() {
  if (!draft.value || isBusy.value || isSavingRows.value) return
  const draftId = draft.value.id
  if ((rowsDirty.value || mappingDirty.value) && recoveryError.value) {
    pendingDraftChange.value = { type: 'open', draftId }
    return
  }
  void openDraft(draftId)
}

async function downloadReport() {
  if (!draft.value) return
  const generation = organizationGeneration
  const organizationId = props.organizationId
  isBusy.value = true
  errorMessage.value = ''
  try {
    const blob = await downloadOrganizationImportReport(organizationId, draft.value.id)
    if (!current(generation, organizationId)) return
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `${draft.value.fileName.replace(/\.[^.]+$/, '')}-отчёт.csv`
    link.click()
    URL.revokeObjectURL(url)
  } catch (error) {
    if (current(generation, organizationId)) errorMessage.value = error instanceof Error ? error.message : 'Не удалось скачать отчёт.'
  } finally {
    if (current(generation, organizationId)) isBusy.value = false
  }
}

function statusLabel(status: OrganizationImportCandidate['status']) {
  return ({ create: 'Создать', link: 'Связать', duplicate: 'Дубль', error: 'Ошибка', conflict: 'Конфликт', skip: 'Пропустить' })[status]
}

function plannedActionLabel(row: OrganizationImportCandidate) {
  if (row.appliedAt) return 'Уже обработано'
  if (isImported.value) {
    if (row.status === 'create') return 'Создано'
    if (row.status === 'link') return 'Использован существующий клиент'
    return 'Строка пропущена'
  }

  if (row.excluded || row.status === 'skip' || row.status === 'duplicate') return 'Строка будет пропущена'
  if (row.kind === 'client' && row.createSeparateClient) return 'Будет создан отдельный клиент'
  if (row.kind === 'client' && row.existingClientId) {
    const matchName = row.matches.find(match => match.id === row.existingClientId)?.name
    return `Будет использован существующий клиент: ${row.linkedClientName || matchName || 'выбранный клиент'}`
  }
  if (row.kind === 'vehicle' && row.linkedClientName) {
    return `Автомобиль будет связан с клиентом: ${row.linkedClientName}`
  }

  return statusLabel(row.status)
}

function stateLabel(state: OrganizationImportState, importedAt: string | null) {
  if (importedAt || state === 'completed') return 'завершён'
  return ({
    'upload-queued': 'загрузка обрабатывается',
    'mapping-queued': 'сопоставление строится',
    'validation-queued': 'строки проверяются',
    ready: 'готов к проверке',
    'import-queued': 'импорт в очереди',
    importing: 'импорт выполняется',
    paused: 'импорт приостановлен',
    failed: 'ошибка обработки'
  } as Partial<Record<OrganizationImportState, string>>)[state] ?? 'черновик'
}

function resultLabel(result: OrganizationImportDraft['result']) {
  if (!result) return 'результат не предоставлен сервером.'
  return `Клиенты: ${result.createdClients}; автомобили: ${result.createdVehicles}; заказы: ${result.createdOrders ?? 0}; строки заказов: ${result.createdOrderLines ?? 0}; оплаты: ${result.createdPayments ?? 0}; события: ${result.createdHistoryEvents ?? 0}`
}

async function reloadHistory() {
  const generation = organizationGeneration
  try {
    await loadDraftList(generation, props.organizationId)
  } catch (error) {
    if (current(generation, props.organizationId)) errorMessage.value = error instanceof Error ? error.message : 'Не удалось обновить историю.'
  }
}
</script>

<template>
  <section v-if="isAccessLoaded && canImport" class="workspace-import workspace-common-subpanel">
    <div class="workspace-subpanel-heading ui-form-header">
      <div>
        <h3>Импорт данных</h3>
        <p class="workspace-import__muted">CSV, XLSX и ZIP с CSV до 50 МБ и 100&nbsp;000 строк; SQL — текстовые INSERT или PostgreSQL COPY FROM stdin.</p>
      </div>
      <UiButton variant="secondary" :disabled="isBusy || isSavingRows || Boolean(pendingDraftChange) || Boolean(pendingRecovery)" @click="fileInput?.click()">Загрузить файл</UiButton>
      <input ref="fileInput" class="workspace-import__file" type="file" accept=".csv,.xlsx,.sql,.zip,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/sql,application/zip" :disabled="isBusy" @change="uploadFile">
    </div>

    <p v-if="errorMessage" class="workspace-import__message workspace-import__message--error" role="alert">{{ errorMessage }}</p>
    <p v-if="successMessage" class="workspace-import__message workspace-import__message--success" role="status">{{ successMessage }}</p>
    <p v-if="recoveryError" class="workspace-import__message workspace-import__message--error" role="alert">{{ recoveryError }}</p>
    <div v-if="pendingDraftChange" class="workspace-import__discard" role="alertdialog" aria-label="Несохранённые правки">
      <span>Есть несохранённые правки. Продолжить и потерять их{{ pendingDraftChange.type === 'upload' ? ` перед загрузкой «${pendingDraftChange.file.name}»` : ' перед открытием другого черновика' }}?</span>
      <UiButton :disabled="isBusy" @click="confirmDraftChange">Продолжить без сохранения</UiButton>
      <UiButton variant="secondary" :disabled="isBusy" @click="pendingDraftChange = null">Остаться</UiButton>
    </div>
    <div v-if="isBusy" class="workspace-import__busy" role="status">Обрабатываем запрос…</div>

    <div v-if="drafts.length" class="workspace-import__history">
      <span>Импорты:</span>
      <button v-for="item in drafts" :key="item.id" type="button" :disabled="isBusy || isSavingRows || Boolean(pendingDraftChange) || Boolean(pendingRecovery)" :class="{ 'is-current': draft?.id === item.id }" @click="requestOpenDraft(item.id)">
        {{ item.fileName }} · {{ stateLabel(item.state, item.importedAt) }}<template v-if="item.totalRows"> · {{ item.processedRows }}/{{ item.totalRows }}</template>
      </button>
      <button type="button" :disabled="isBusy" @click="reloadHistory">Обновить</button>
    </div>

    <template v-if="draft">
      <div v-if="pendingRecovery" class="workspace-import__discard" role="alertdialog" aria-label="Восстановление локальных правок">
        <UiButton variant="secondary" @click="downloadRecovery">Скачать локальные правки (CSV)</UiButton>
        <template v-if="pendingRecovery.revisionMatches">
          <span>Найдена локальная копия для версии {{ pendingRecovery.recovery.revision }}. Восстановить её?</span>
          <UiButton @click="restoreRecovery">Восстановить правки</UiButton>
          <UiButton variant="secondary" @click="declineRecovery">Удалить локальную копию и открыть серверную версию</UiButton>
        </template>
        <template v-else>
          <span>Локальная копия относится к версии {{ pendingRecovery.recovery.revision }}, а на сервере уже версия {{ draft.revision }}. Она не будет применена автоматически.</span>
          <UiButton variant="secondary" @click="declineRecovery">Удалить локальную копию и открыть серверную версию</UiButton>
        </template>
      </div>
      <div class="workspace-import__draft-heading">
        <strong>{{ draft.fileName }}</strong>
        <span>Версия {{ draft.revision }} · {{ stateLabel(draft.state, draft.importedAt) }} · изменён {{ new Date(draft.updatedAt).toLocaleString('ru-RU', { timeZone: props.timeZoneId || 'UTC' }) }}</span>
      </div>
      <div v-if="isBatch" class="workspace-import__source-key">
        <label class="workspace-import__field">Источник CRM{{ sourceRequiredForBusiness ? ' (обязателен)' : '' }}
          <input v-model="sourceKeyDraft" maxlength="120" placeholder="Например: МойСклад, YCLIENTS, crm.example.ru" :disabled="isBusy || isImported || isMappingLocked">
          <small v-if="sourceRequiredForBusiness">Для переноса заказов, оплат или истории укажите источник и сохраните его до запуска импорта.</small>
          <small v-else>Укажите одинаковое имя для файлов одной CRM: оно помогает находить повторы между загрузками. Для импорта только клиентов и автомобилей значение можно оставить пустым.</small>
        </label>
        <UiButton variant="secondary" :disabled="isBusy || isImported || !sourceKeyChanged" @click="saveSourceKey">Сохранить источник</UiButton>
      </div>
      <div v-if="isBatch && profiles.length" class="workspace-import__profile-tools">
        <label class="workspace-import__field">Профиль сопоставления
          <select v-model="selectedProfileId" :disabled="isBusy || isImported">
            <option value="">Выберите профиль</option>
            <option v-for="profile in profiles" :key="profile.id" :value="profile.id">{{ profile.name }} · {{ profile.sourceKey }}</option>
          </select>
        </label>
        <UiButton variant="secondary" :disabled="!selectedProfile || isBusy || isImported || isMappingLocked" @click="requestApplyProfile">Применить профиль</UiButton>
        <UiButton variant="secondary" :disabled="!selectedProfile || isBusy" @click="removeProfile">Удалить профиль</UiButton>
      </div>
      <div v-if="isBatch && !isImported" class="workspace-import__profile-tools">
        <label class="workspace-import__field">Сохранить профиль без данных клиентов
          <input v-model="profileNameDraft" maxlength="100" placeholder="Например: История из YCLIENTS" :disabled="isBusy || isMappingLocked">
        </label>
        <UiButton variant="secondary" :disabled="!canSaveProfile" @click="saveProfile">Сохранить профиль</UiButton>
      </div>
      <p v-if="profileError" class="workspace-import__message workspace-import__message--error" role="alert">{{ profileError }}</p>
      <div v-if="pendingProfile" class="workspace-import__confirm" role="group" aria-label="Подтверждение применения профиля">
        <p>Профиль «{{ pendingProfile.name }}» заменит сопоставление текущего файла. Сервер заново построит строки и проверит результат. {{ mappingResetCount ? 'Текущие строки и ручные правки будут потеряны.' : '' }}</p>
        <UiButton :disabled="isBusy || isMappingLocked" @click="applyProfile">Применить и проверить</UiButton>
        <UiButton variant="secondary" :disabled="isBusy" @click="pendingProfile = null">Отмена</UiButton>
      </div>
      <div v-if="isBatch" class="workspace-import__progress" role="status">
        <span>{{ draft.processedRows.toLocaleString('ru-RU') }} из {{ draft.totalRows.toLocaleString('ru-RU') }} строк обработано</span>
        <progress :value="draft.totalRows ? draft.processedRows : 0" :max="Math.max(1, draft.totalRows)"></progress>
        <span v-if="draft.error" class="workspace-import__status--error">{{ draft.error }}</span>
        <span v-if="draft.result">{{ isImported ? 'Результат' : 'Частичный результат' }}: {{ resultLabel(draft.result) }}</span>
      </div>

      <section class="workspace-import__panel">
        <div class="workspace-import__section-heading ui-section-header">
          <h4>Листы и сопоставление</h4>
          <UiButton variant="secondary" :disabled="isBusy" @click="showSource = !showSource">{{ showSource ? 'Скрыть исходную таблицу' : 'Показать исходную таблицу' }}</UiButton>
        </div>
        <label class="workspace-import__field">
          Лист файла
          <select v-model="selectedTableId" :disabled="isBusy">
            <option v-for="table in draft.data.tables" :key="table.id" :value="table.id">{{ table.name }}</option>
          </select>
        </label>
        <div v-if="selectedMapping && selectedTable" class="workspace-import__mapping">
          <label class="workspace-import__field">
            Назначение листа
            <select :value="selectedMapping.kind" :disabled="isMappingLocked" @change="mappingKindChanged(($event.target as HTMLSelectElement).value as MappingKind)">
              <option value="client">Клиенты</option>
              <option value="vehicle">Автомобили</option>
              <option value="order">Заказы</option>
              <option value="order-line">Строки заказов</option>
              <option value="payment">Оплаты</option>
              <option value="history">История</option>
              <option value="combined">Клиенты и автомобили</option>
              <option value="ignore">Не импортировать</option>
            </select>
          </label>
          <div v-if="visibleMappingFields.length" class="workspace-import__mapping-grid">
            <label v-for="field in visibleMappingFields" :key="field.key" class="workspace-import__field">
              {{ field.label }}
              <select multiple :disabled="isMappingLocked" @change="mappingColumnsChanged(field.key, $event)">
                <option v-for="(column, index) in selectedTable.columns" :key="index" :value="index" :selected="selectedMapping.fields[field.key]?.includes(index) === true">
                  {{ column || `Столбец ${index + 1}` }}
                </option>
              </select>
              <small>Можно выбрать несколько столбцов, например для составного имени.</small>
              <template v-if="selectedMapping.fields[field.key]?.length">
                <select :value="fieldTransformation(field.key)?.type ?? ''" :disabled="isMappingLocked" :aria-label="`Преобразование поля ${field.label}`" @change="mappingTransformationTypeChanged(field.key, ($event.target as HTMLSelectElement).value)">
                  <option value="">Без дополнительного преобразования</option>
                  <option value="phone">Телефон: только цифры, 8 в начале → 7</option>
                  <option value="date-exact">Дата по точному формату → ISO</option>
                  <option value="decimal">Десятичное число: запятая и пробелы</option>
                  <option value="value-map">Словарь значений</option>
                </select>
                <select v-if="fieldTransformation(field.key)?.type === 'date-exact'" :value="fieldTransformation(field.key)?.format ?? ''" :disabled="isMappingLocked" aria-label="Формат даты в исходном файле" @change="mappingTransformationFormatChanged(field.key, ($event.target as HTMLSelectElement).value)">
                  <option value="">Выберите формат исходной даты</option>
                  <option v-for="format in ['yyyy-MM-dd', 'dd.MM.yyyy', 'd.M.yyyy', 'MM/dd/yyyy', 'dd/MM/yyyy', 'yyyyMMdd', 'yyyy-MM-dd HH:mm:ss', 'yyyy-MM-ddTHH:mm:ss', 'yyyy-MM-ddTHH:mm:ssZ', 'dd.MM.yyyy HH:mm:ss', 'dd.MM.yyyy H:mm', 'dd.MM.yyyy HH:mm', 'dd/MM/yyyy HH:mm:ss', 'MM/dd/yyyy HH:mm:ss']" :key="format" :value="format">{{ format }}</option>
                </select>
                <textarea v-if="fieldTransformation(field.key)?.type === 'value-map'" :value="mappingValueMapText(field.key)" rows="3" maxlength="30000" placeholder="Одно правило на строку: Исходное значение → целевое" :disabled="isMappingLocked" aria-label="Словарь преобразования значений" @input="mappingValueMapChanged(field.key, $event)"></textarea>
                <small v-if="fieldTransformation(field.key)?.type === 'value-map'">До 100 пар, например «Завершён → completed». Каждое исходное значение должно иметь пару.</small>
                <small v-for="preview in transformationPreview(field.key)" :key="preview" class="workspace-import__transform-preview">{{ preview }}</small>
              </template>
            </label>
          </div>
          <div v-if="serverMappingPreview.length" class="workspace-import__server-preview" role="status">
            <strong>Первые результаты серверного сопоставления</strong>
            <span v-for="item in serverMappingPreview" :key="item.row.id">Строка {{ item.row.sourceRow }}: {{ item.result }}</span>
          </div>
          <p v-if="mappingDirty" class="workspace-import__warning" role="status">После сохранения сопоставления строки будут построены заново, ручные правки сбросятся.</p>
          <div v-if="canSaveMapping" class="workspace-import__actions">
            <UiButton :disabled="!canSaveMapping" @click="requestSaveMapping">{{ mappingNeedsBuild ? 'Построить записи' : 'Сохранить сопоставление' }}</UiButton>
            <UiButton v-if="mappingDirty" variant="secondary" :disabled="isBusy || isSavingRows" @click="cancelMappingChanges">Отменить изменения сопоставления</UiButton>
            <span v-if="mappingNeedsBuild">Сервер предложил сопоставления. Проверьте их или измените перед построением строк.</span>
            <span v-else>Сохранённые строки останутся доступны до подтверждения пересоздания.</span>
          </div>
          <div v-if="pendingMappingRebuild" class="workspace-import__confirm" role="group" aria-label="Подтверждение пересоздания строк">
            <p v-if="mappingResetCount">Сопоставление будет сохранено, {{ mappingResetCount }} строк будут пересозданы и их ручные правки будут потеряны.</p>
            <p v-else>Сопоставление будет сохранено, затем записи будут построены.</p>
            <UiButton :disabled="!canSaveMapping" @click="saveMapping">Подтвердить и продолжить</UiButton>
            <UiButton variant="secondary" :disabled="isBusy" @click="pendingMappingRebuild = false">Отмена</UiButton>
          </div>
          <div v-if="showSource" class="workspace-import__source">
            <div class="workspace-import__table-wrap">
              <table>
                <thead><tr><th>#</th><th v-for="(column, index) in selectedTable.columns" :key="index">{{ column || `Столбец ${index + 1}` }}</th></tr></thead>
                <tbody>
                  <tr v-for="row in selectedTable.rows.slice(0, 20)" :key="row.number">
                    <td>{{ row.number }}</td><td v-for="(column, index) in selectedTable.columns" :key="index">{{ row.values[index] }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
            <small>Показаны первые 20 исходных строк из {{ selectedTable.rows.length }}.</small>
          </div>
        </div>
      </section>

      <section class="workspace-import__panel">
        <div class="workspace-import__section-heading ui-section-header">
          <h4>{{ isImported ? 'Результаты импорта' : 'Будущие записи' }}</h4>
          <div class="workspace-import__actions">
            <UiButton variant="secondary" :disabled="isEditingLocked || !undoSnapshot" @click="undoLastEdit">Отменить правку</UiButton>
            <UiButton :disabled="isBusy || isSavingRows || isImported || isBatchFailed || mappingDirty || !rows.length" @click="saveAndValidate">{{ rowSaveStatus === 'error' ? 'Повторить сохранение' : 'Сохранить и проверить' }}</UiButton>
          </div>
        </div>
        <p v-if="rowSaveStatus === 'pending'" class="workspace-import__busy" role="status">{{ mappingDirty ? 'Сохраните или отмените сопоставление, чтобы продолжить автосохранение строк.' : 'Изменения будут сохранены автоматически.' }}</p>
        <p v-else-if="rowSaveStatus === 'saving'" class="workspace-import__busy" role="status">Сохраняем изменения…</p>
        <p v-else-if="rowSaveStatus === 'saved'" class="workspace-import__message workspace-import__message--success" role="status">Изменения сохранены на сервере.</p>
        <div v-else-if="rowSaveStatus === 'error'" class="workspace-import__message workspace-import__message--error" role="alert">
          {{ rowSaveError }} Нажмите «Повторить сохранение» или обновите версию черновика.
          <UiButton variant="secondary" :disabled="isSavingRows" @click="reloadCurrentDraft">Обновить черновик</UiButton>
        </div>
        <div class="workspace-import__filters">
          <label class="workspace-import__field">Поиск
            <input v-model="searchDraft" type="search" :disabled="isBusy || isSavingRows" @keydown.enter.prevent="requestQueryChange({ page: 1, search: searchDraft })">
          </label>
          <UiButton variant="secondary" :disabled="isBusy || isSavingRows" @click="requestQueryChange({ page: 1, search: searchDraft })">Найти</UiButton>
          <label class="workspace-import__field">Тип
            <select :value="kindFilter" :disabled="isBusy || isSavingRows" @change="requestQueryChange({ page: 1, kind: ($event.target as HTMLSelectElement).value })">
              <option value="all">Все</option><option value="client">Клиенты</option><option value="vehicle">Автомобили</option><option value="order">Заказы</option><option value="order-line">Строки заказов</option><option value="payment">Оплаты</option><option value="history">История</option>
            </select>
          </label>
          <label class="workspace-import__field">Проверка
            <select :value="statusFilter" :disabled="isBusy || isSavingRows" @change="requestQueryChange({ page: 1, status: ($event.target as HTMLSelectElement).value })">
              <option value="all">Все</option><option value="create">Создать</option><option value="link">Связать</option><option value="duplicate">Дубли</option><option value="error">Ошибки</option><option value="conflict">Конфликты</option><option value="skip">Пропущенные</option>
            </select>
          </label>
          <label class="workspace-import__field workspace-import__exclude"><input type="checkbox" :checked="pageRows.length > 0 && pageRows.every(row => row.excluded)" :disabled="isEditingLocked || pageRows.some(row => row.appliedAt)" @change="updatePageExcluded"> Исключить страницу</label>
          <span>{{ filteredRowCount.toLocaleString('ru-RU') }} строк · страница {{ page }} из {{ pageCount }}</span>
        </div>
        <p v-if="rowsDirty" class="workspace-import__warning">Есть несохранённые правки. Сначала сохраните и проверьте строки.</p>
        <div class="workspace-import__table-wrap" @paste="pasteRows">
            <table class="workspace-import__candidates" :class="{ 'is-compact': kindFilter === 'all' }">
            <thead>
              <tr><th>Строка</th><th>Тип / состояние</th><th>Исключить</th><th v-for="field in tableFields" :key="field.key">{{ field.label }}</th><th v-if="kindFilter === 'all'">Поля</th><th>Связь с клиентом</th><th>Проверка</th></tr>
            </thead>
            <tbody>
              <template v-for="(row, rowIndex) in pageRows" :key="row.id">
              <tr :class="{ 'is-excluded': row.excluded, 'is-applied': row.appliedAt }">
                <td>{{ row.sourceRow }}</td>
                <td>{{ kindLabel(row.kind) }}<small>{{ plannedActionLabel(row) }}</small></td>
                <td><input type="checkbox" :checked="row.excluded" :disabled="isRowLocked(row)" aria-label="Исключить строку" @change="updateRow(row, 'excluded', ($event.target as HTMLInputElement).checked)"></td>
                <td v-for="(field, fieldIndex) in tableFields" :key="field.key">
                  <input
                    v-if="fieldForRow(row, field.key)"
                    :value="candidateValue(row, field.key)"
                    :aria-label="`${field.label}, строка ${row.sourceRow}`"
                    :data-row-index="rowIndex"
                    :data-field-index="fieldIndex"
                    :disabled="isInputDisabled || Boolean(row.appliedAt)"
                    :readonly="isSavingRows"
                    @focus="rememberUndo"
                    @input="updateTableCell(row, field.key, $event)"
                  >
                </td>
                <td v-if="kindFilter === 'all'"><button type="button" class="workspace-import__expand" :disabled="isBusy" :aria-expanded="expandedRowId === row.id" @click="toggleExpandedRow(row.id)">{{ expandedRowId === row.id ? 'Скрыть поля' : 'Все поля' }}</button></td>
                <td>
                  <select v-if="row.kind === 'client'" :value="row.createSeparateClient ? '__new' : row.existingClientId ?? ''" :aria-label="`Решение о совпадениях для строки ${row.sourceRow}`" :disabled="isRowLocked(row)" @focus="rememberUndo" @change="updateExistingClient(row, ($event.target as HTMLSelectElement).value)">
                    <option value="">Проверить совпадения</option>
                    <option value="__new">Создать отдельного клиента</option>
                    <option v-for="match in row.matches" :key="match.id" :value="match.id">{{ match.name }} · {{ match.phone }}</option>
                  </select>
                  <span v-else>{{ row.linkedClientName || row.clientExternalId || row.clientPhone || 'Не указан' }}</span>
                </td>
                <td><span :class="`workspace-import__status workspace-import__status--${row.status}`">{{ row.appliedAt ? (row.status === 'create' ? 'Создано' : row.status === 'link' ? 'Связано' : 'Пропущено') : row.message || statusLabel(row.status) }}</span></td>
              </tr>
              <tr v-if="kindFilter === 'all' && expandedRowId === row.id" class="workspace-import__expanded-row">
                <td :colspan="tableFields.length + 6">
                  <div class="workspace-import__expanded-fields">
                    <label v-for="field in candidateFields(row.kind)" :key="field.key" class="workspace-import__field">
                      {{ field.label }}
                      <input
                        :value="row[field.key] ?? ''"
                        :aria-label="`${field.label}, строка ${row.sourceRow}`"
                        :disabled="isInputDisabled || Boolean(row.appliedAt)"
                        :readonly="isSavingRows"
                        @focus="rememberUndo"
                        @input="updateRow(row, field.key, ($event.target as HTMLInputElement).value)"
                      >
                    </label>
                  </div>
                </td>
              </tr>
              </template>
              <tr v-if="!pageRows.length"><td :colspan="tableFields.length + (kindFilter === 'all' ? 6 : 5)">Нет строк по выбранным условиям.</td></tr>
            </tbody>
          </table>
        </div>
        <div class="workspace-import__pagination">
          <button type="button" :disabled="page <= 1 || isBusy || isSavingRows" @click="requestQueryChange({ page: page - 1 })">Назад</button>
          <button type="button" :disabled="page >= pageCount || isBusy || isSavingRows" @click="requestQueryChange({ page: page + 1 })">Дальше</button>
          <span>Вставьте диапазон из Excel через Ctrl+V в ячейку таблицы.</span>
        </div>
      </section>

      <section class="workspace-import__panel workspace-import__review">
        <div class="workspace-import__section-heading ui-section-header"><h4>Проверка перед импортом</h4><span>{{ draft.importedAt ? 'Импорт уже выполнен' : 'Подтвердите итог после сохранения правок' }}</span></div>
        <p v-if="mappingDirty" class="workspace-import__warning" role="status">Сопоставление изменено и не сохранено. Подтверждение заблокировано.</p>
        <p v-if="sourceRequiredForBusiness && !draft.sourceKey?.trim()" class="workspace-import__warning" role="status">Для бизнес-данных сохраните источник CRM. Импорт заказов, оплат и истории без источника заблокирован.</p>
        <div class="workspace-import__totals">
          <template v-if="!isBatch || draft.result">
            <span>Клиенты: <strong>{{ draft.result?.createdClients ?? summary.clients }}</strong></span>
            <span>Автомобили: <strong>{{ draft.result?.createdVehicles ?? summary.vehicles }}</strong></span>
          </template>
          <span v-if="isBatch && !draft.result">К переносу готово: <strong>{{ summaryCounts.create ?? 0 }}</strong></span>
          <span v-if="isBatch && !draft.result">Связаны с существующим клиентом: <strong>{{ summaryCounts.link ?? 0 }}</strong></span>
          <template v-if="draft.result">
            <span>Заказы: <strong>{{ draft.result.createdOrders }}</strong></span>
            <span>Строки заказов: <strong>{{ draft.result.createdOrderLines }}</strong></span>
            <span>Оплаты: <strong>{{ draft.result.createdPayments }}</strong></span>
            <span>История: <strong>{{ draft.result.createdHistoryEvents }}</strong></span>
          </template>
          <span v-if="!isBatch || draft.result">Связаны клиенты: <strong>{{ draft.result?.linkedClients ?? summary.linked }}</strong></span>
          <span>Пропущено: <strong>{{ draft.result?.skipped ?? summary.skipped }}</strong></span>
          <span v-if="summary.problems" class="workspace-import__status--error">Ошибок и конфликтов: {{ summary.problems }}</span>
        </div>
        <div v-if="isBatch && !draft.result && draft.data.reconciliation" class="workspace-import__reconciliation">
          <strong>Предварительный итог</strong>
          <span>Новые клиенты: {{ draft.data.reconciliation.newClients }} · существующие связи: {{ draft.data.reconciliation.linkedClients }}</span>
          <span>Новые автомобили: {{ draft.data.reconciliation.newVehicles }} · уже есть: {{ draft.data.reconciliation.existingVehicles }}</span>
          <span>Заказы: {{ draft.data.reconciliation.newOrders }} · оплаты: {{ draft.data.reconciliation.newPayments }} · события: {{ draft.data.reconciliation.newHistoryEvents }}</span>
          <span>Сумма заказов: {{ formatImportMoney(draft.data.reconciliation.orderTotal) }} · активные оплаты: {{ formatImportMoney(draft.data.reconciliation.activePaymentsTotal) }} · ожидаемый долг: {{ formatImportMoney(draft.data.reconciliation.outstandingTotal) }}</span>
          <small v-if="summary.problems">Суммы предварительные: в файле есть ошибки или конфликты, итог изменится после их исправления.</small>
        </div>
        <div class="workspace-import__actions">
          <UiButton v-if="isImported" variant="secondary" :disabled="isBusy" @click="downloadReport">Скачать CSV-отчёт</UiButton>
          <UiButton v-else-if="isBatch && ['paused', 'failed'].includes(draft.state)" :disabled="isBusy" @click="resumeBatch">{{ draft.state === 'failed' ? 'Повторить обработку' : 'Возобновить импорт' }}</UiButton>
          <UiButton v-else-if="isBatch && activeOperation === 'import'" variant="secondary" :disabled="activeOperation !== 'import'" @click="pauseBatch">Приостановить после текущей партии</UiButton>
          <UiButton v-else-if="!isImported" :disabled="!canConfirm" @click="confirmSummaryOpen = true">Подтвердить импорт</UiButton>
        </div>
        <div v-if="confirmSummaryOpen" class="workspace-import__confirm" role="group" aria-label="Подтверждение импорта">
          <p v-if="isBatch">Будут обработаны {{ draft.totalRows.toLocaleString('ru-RU') }} строк. К запуску готовы: {{ summaryCounts.create ?? 0 }}, существующие связи: {{ summaryCounts.link ?? 0 }}, пропуски: {{ summary.skipped }}. Ошибок и конфликтов: {{ summary.problems }}.</p>
          <p v-else>Будут созданы {{ summary.clients }} клиентов и {{ summary.vehicles }} автомобилей; с существующими клиентами будут связаны {{ summary.linked }}, пропусков: {{ summary.skipped }}.</p>
          <UiButton :disabled="!canConfirm" @click="confirmImport">{{ isBatch ? 'Запустить импорт' : `Импортировать ${summary.clients + summary.vehicles + summary.linked} записей` }}</UiButton>
          <UiButton variant="secondary" :disabled="isBusy" @click="confirmSummaryOpen = false">Отмена</UiButton>
        </div>
      </section>
    </template>

    <div v-else-if="!drafts.length && !isBusy" class="workspace-import__empty">Загрузите CSV, XLSX, ZIP с CSV или поддерживаемую SQL-выгрузку, чтобы подготовить перенос данных организации.</div>
  </section>
  <div v-else-if="isAccessLoaded && errorMessage" class="workspace-common-subpanel workspace-import-access-error" role="alert">
    {{ errorMessage }}
    <UiButton variant="secondary" @click="initializeOrganization">Повторить проверку доступа</UiButton>
  </div>
</template>

<style scoped>
.workspace-import {
  grid-column: 1 / -1;
  display: grid;
  gap: 12px;
  min-width: 0;
}

.workspace-import__muted,
.workspace-import__draft-heading span,
.workspace-import__actions span,
.workspace-import__pagination span,
.workspace-import__section-heading > span,
.workspace-import__empty,
.workspace-import__source small,
.workspace-import__field small {
  color: var(--crm-text-muted, #667085);
  font-size: 12px;
}

.workspace-import__muted { margin: 4px 0 0; }
.workspace-import__file { display: none; }
.workspace-import__history,
.workspace-import__draft-heading,
.workspace-import__section-heading,
.workspace-import__actions,
.workspace-import__filters,
.workspace-import__pagination,
.workspace-import__totals {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.workspace-import__history { font-size: 12px; }
.workspace-import__history button,
.workspace-import__pagination button {
  border: 1px solid var(--crm-line, #d9e0e9);
  border-radius: 6px;
  padding: 5px 8px;
  background: var(--crm-surface, #fff);
  color: inherit;
  font: inherit;
  cursor: pointer;
}
.workspace-import__history button.is-current { border-color: var(--crm-accent, #e77924); }
.workspace-import__history button:disabled,
.workspace-import__pagination button:disabled { opacity: .55; cursor: wait; }
.workspace-import__draft-heading { justify-content: space-between; }
.workspace-import__progress { display: grid; grid-template-columns: 1fr; gap: 5px; font-size: 12px; color: var(--crm-text-muted, #667085); }
.workspace-import__progress progress { width: 100%; height: 8px; accent-color: var(--crm-accent, #e77924); }
.workspace-import__panel { display: grid; gap: 10px; min-width: 0; padding-top: 10px; border-top: 1px solid var(--crm-line, #d9e0e9); }
.workspace-import__section-heading { justify-content: space-between; }
.workspace-import h4 { margin: 0; font-size: 14px; }
.workspace-import__field { display: grid; align-content: start; gap: 4px; min-width: 145px; font-size: 12px; font-weight: 600; }
.workspace-import__field input:not([type='checkbox']),
.workspace-import__field select,
.workspace-import__mapping select,
.workspace-import__candidates input:not([type='checkbox']),
.workspace-import__candidates select {
  box-sizing: border-box;
  min-height: 32px;
  max-width: 100%;
  border: 1px solid var(--crm-line, #d9e0e9);
  border-radius: 6px;
  padding: 5px 7px;
  background: var(--crm-surface, #fff);
  color: var(--crm-text, #172033);
  font: inherit;
  font-weight: 400;
}
.workspace-import__field select[multiple] { min-height: 86px; }
.workspace-import__mapping { display: grid; gap: 10px; }
.workspace-import__mapping-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 8px; }
.workspace-import__warning { margin: 0; color: var(--crm-warning-text, #8a4b08); font-size: 12px; }
.workspace-import__source { display: grid; gap: 5px; min-width: 0; }
.workspace-import__source-key,
.workspace-import__profile-tools { display: flex; align-items: end; flex-wrap: wrap; gap: 8px; }
.workspace-import__source-key .workspace-import__field,
.workspace-import__profile-tools .workspace-import__field { flex: 1 1 240px; }
.workspace-import__transform-preview,
.workspace-import__server-preview span { color: var(--crm-text-muted, #667085); font-size: 12px; font-weight: 400; }
.workspace-import__server-preview { display: grid; gap: 4px; padding: 8px; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 7px; }
.workspace-import__reconciliation { display: grid; gap: 4px; padding: 8px; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 7px; font-size: 12px; }
.workspace-import__reconciliation small { color: var(--crm-warning-text, #8a4b08); }
.workspace-import__table-wrap { max-width: 100%; overflow: auto; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 7px; }
.workspace-import table { width: 100%; border-collapse: collapse; font-size: 12px; }
.workspace-import th,
.workspace-import td { max-width: 260px; padding: 6px 8px; border-bottom: 1px solid var(--crm-line, #d9e0e9); text-align: left; vertical-align: top; white-space: nowrap; }
.workspace-import th { position: sticky; top: 0; background: var(--crm-surface-soft, #f5f7fa); font-weight: 600; }
.workspace-import__candidates { min-width: 1200px; }
.workspace-import__candidates.is-compact { min-width: 1020px; }
.workspace-import__candidates td small { display: block; margin-top: 3px; color: var(--crm-text-muted, #667085); }
.workspace-import__candidates input:not([type='checkbox']) { width: 150px; }
.workspace-import__expand { border: 1px solid var(--crm-line, #d9e0e9); border-radius: 6px; padding: 5px 8px; background: var(--crm-surface, #fff); color: inherit; font: inherit; cursor: pointer; }
.workspace-import__expand:disabled { opacity: .55; cursor: wait; }
.workspace-import__expanded-row td { white-space: normal; background: var(--crm-surface-soft, #f5f7fa); }
.workspace-import__expanded-fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 8px; }
.workspace-import__expanded-fields input { width: 100%; }
.workspace-import__candidates tr.is-excluded { opacity: .55; }
.workspace-import__candidates tr.is-applied { background: var(--crm-surface-soft, #f5f7fa); }
.workspace-import__status { color: var(--crm-text-muted, #667085); white-space: normal; }
.workspace-import__status--error,
.workspace-import__status--conflict { color: var(--crm-danger-text, #b42318); }
.workspace-import__status--duplicate { color: var(--crm-warning-text, #8a4b08); }
.workspace-import__status--link { color: var(--crm-success-text, #067647); }
.workspace-import__pagination { justify-content: flex-end; font-size: 12px; }
.workspace-import__pagination span { margin-right: auto; }
.workspace-import__exclude { display: flex; align-items: center; }
.workspace-import__totals { font-size: 13px; }
.workspace-import__totals > span { padding: 5px 8px; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 6px; }
.workspace-import__confirm { display: flex; align-items: center; flex-wrap: wrap; gap: 8px; padding: 10px; border: 1px solid var(--crm-accent, #e77924); border-radius: 7px; }
.workspace-import__confirm p { flex: 1 1 100%; margin: 0; font-size: 13px; }
.workspace-import__message { margin: 0; font-size: 13px; }
.workspace-import__message--error { color: var(--crm-danger-text, #b42318); }
.workspace-import__message--success { color: var(--crm-success-text, #067647); }
.workspace-import__busy { color: var(--crm-text-muted, #667085); font-size: 12px; }
.workspace-import-access-error { display: flex; align-items: center; flex-wrap: wrap; gap: 8px; }
:global(:root[data-crm-theme='dark']) .workspace-import {
  --crm-warning-text: #fbbf24;
  --crm-success-text: #4ade80;
}

@media (max-width: 640px) {
  .workspace-import__section-heading { align-items: flex-start; }
  .workspace-import__section-heading > .workspace-import__actions { width: 100%; }
  .workspace-import__filters { align-items: stretch; }
  .workspace-import__filters > .workspace-import__field { flex: 1 1 130px; }
  .workspace-import__exclude { flex: 1 1 100%; }
  .workspace-import__totals { align-items: stretch; }
  .workspace-import__totals > span { flex: 1 1 125px; }
}
</style>

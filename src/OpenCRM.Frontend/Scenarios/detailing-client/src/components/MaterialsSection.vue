<script setup lang="ts">
import { formattingCulture, formatLocaleCurrency, formatLocaleDate } from '@opencrm/ui/i18n'
import { useLocalization } from '@opencrm/ui'
import { useWorkspaceCurrency } from '@opencrm/workspace'

import { WorkspaceAccessNotice } from '@opencrm/workspace'
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { UiAlertDialog, UiButton, UiCard, UiDatePicker, UiInput, UiLoader, UiPagination, UiSearchInput, UiSelect, UiTimePicker, formatDateOnly } from '@opencrm/ui'
import { organizationUtcTime, WorkspacePageHelpButton, createMaterialNumberMask, isDiscreteMaterialUnit, materialUnitOptions, normalizeMaterialUnit } from '@opencrm/workspace'
import { loadScenarioCatalog, type DetailingCatalogItem, type DetailingWorkspaceContext } from '@/api/detailingBff'
import {
  adjustMaterialStock,
  createMaterialIssue,
  createMaterialReceipt,
  loadMaterials,
  type MaterialInventory,
  type MaterialMovement,
  type MaterialStock
} from '@/api/materials'

import '@opencrm/workspace/materialMessages'

const workspaceCurrency = useWorkspaceCurrency()
const { t } = useLocalization()

const props = defineProps<{
  context: DetailingWorkspaceContext
}>()

const inventory = ref<MaterialInventory | null>(null)
const catalogMaterials = ref<DetailingCatalogItem[]>([])
const isLoading = ref(false)
const isSaving = ref(false)
const errorMessage = ref('')
const errorDialogMessage = ref('')
const successMessage = ref('')
let successTimer: ReturnType<typeof setTimeout> | undefined
watch(successMessage, message => {
  clearTimeout(successTimer)
  if (message) successTimer = setTimeout(() => { successMessage.value = '' }, 5000)
})
const activeView = ref<'adjustment' | 'stocks' | 'receipt' | 'issue' | 'movements' | 'movement-detail' | 'stock-detail' | 'snapshot'>('stocks')
const selectedMovement = ref<MaterialMovement | null>(null)
const selectedStock = ref<MaterialStock | null>(null)
const movementReturnView = ref<'movements' | 'stock-detail'>('movements')
const issueStockLocked = ref(false)
const receiptForm = reactive({
  catalogItemId: '',
  name: '',
  unit: 'шт',
  quantity: '',
  packageQuantity: '',
  unitCost: '',
  comment: ''
})
const movementFilters = reactive({
  date: '',
  type: ''
})
const stockFilters = reactive({
  search: '',
  filter: '',
  page: 1,
  pageSize: 10
})
const issueFilters = reactive({ search: '', page: 1, pageSize: 20 })
const selectedPageSize = ref('10')
const stockPageSizeOptions = [
  { value: '10', label: '10' },
  { value: '25', label: '25' },
  { value: '50', label: '50' }
]
let stockSearchTimer: ReturnType<typeof setTimeout> | undefined
let latestRequest = 0
const snapshotForm = reactive({
  date: getTodayDate(),
  time: getCurrentTime()
})
const issueForm = reactive({
  stockId: '',
  quantity: '',
  comment: ''
})

const adjustmentForm = reactive({ quantity: '', unitCost: '', reason: '', expectedUpdatedAt: '' })

const hasInlineLoading = computed(() => activeView.value === 'stocks' || (activeView.value === 'issue' && !issueStockLocked.value))
const canWrite = computed(() => props.context.workspaceAccess?.isReadOnly !== true)
const readOnlyMessage = computed(() => props.context.workspaceAccess?.readOnlyMessage ?? '')
const billingHref = computed(() => '#billing')
const currentTitle = computed(() => {
  const keys: Record<typeof activeView.value, string> = {
    adjustment: 'adjustmentTitle', stocks: 'title', receipt: 'receipt', issue: 'issue',
    movements: 'movements', 'movement-detail': 'movement', 'stock-detail': 'material', snapshot: 'snapshot'
  }
  return t(`materials.${keys[activeView.value]}`)
})
const materialBreadcrumbs = computed(() => {
  if (activeView.value === 'stocks') return []

  const items = [{ title: t('materials.title'), href: '#materials', onClick: () => openView('stocks') }]
  if (activeView.value === 'adjustment' || (activeView.value === 'issue' && issueStockLocked.value)
    || (activeView.value === 'movement-detail' && movementReturnView.value === 'stock-detail')) {
    items.push({ title: t('materials.material'), href: '#materials', onClick: () => openView('stock-detail') })
  } else if (activeView.value === 'movement-detail') {
    items.push({ title: t('materials.movements'), href: '#materials', onClick: () => openView('movements') })
  }

  return items
})
const movementTypeOptions = computed(() => [
  { value: '', label: t('materials.allTypes') },
  { value: 'receipt', label: t('materials.receipt') },
  { value: 'issue', label: t('materials.manualIssue') },
  { value: 'maintenance_issue', label: t('materials.serviceIssue') },
  { value: 'adjustment', label: t('materials.adjustment') }
])
const stockFilterOptions = computed(() => [
  { value: '', label: t('materials.allStocks') },
  { value: 'out', label: t('materials.outOfStock') },
  { value: 'low', label: t('materials.belowMinimum') },
  { value: 'ok', label: t('materials.inRange') }
])
const materialOptions = computed(() => [
  { value: '', label: t('materials.newStockItem') },
  ...catalogMaterials.value
    .filter(item => item.isActive !== false && item.name?.trim())
    .map(item => ({ value: item.id, label: item.name.trim() }))
])
const stockOptions = computed(() => [
  { value: '', label: t('materials.chooseStock') },
  ...(inventory.value?.stocks ?? []).map(stock => ({
    value: stock.id,
    label: `${stock.name} · ${formatQuantity(stock.quantityOnHand)} ${formatUnit(stock.unit)}`
  }))
])
const selectedIssueStock = computed(() => inventory.value?.stocks.find(stock => stock.id === issueForm.stockId) ?? null)
const snapshotDateTimeIso = computed(() => toLocalIso(snapshotForm.date, snapshotForm.time || '23:59'))
const receiptUnitLabel = computed(() => normalizeMaterialUnit(receiptForm.unit || 'шт'))
const localizedMaterialUnitOptions = computed(() => materialUnitOptions.map(option => ({ ...option, label: formatUnit(option.value) })))
const receiptBaseUnitLabel = computed(() => receiptUnitLabel.value === 'кг' ? 'г' : receiptUnitLabel.value === 'л' ? 'мл' : receiptUnitLabel.value)
const receiptUnitCostLabel = computed(() => t('materials.unitCostLabel', { unit: formatUnit(receiptUnitLabel.value) }))
const receiptTotalCost = computed(() => calculateLineTotal(receiptForm.quantity, receiptForm.unitCost))
const issueUnitLabel = computed(() => selectedIssueStock.value ? formatUnit(selectedIssueStock.value.unit) : t('materials.unit'))
const issueTotalCost = computed(() => selectedIssueStock.value && !issueErrors.value.quantity ? Number(issueForm.quantity) * selectedIssueStock.value.averageUnitCost : null)
const isReceiptDiscrete = computed(() => isDiscreteMaterialUnit(receiptUnitLabel.value))
const isIssueDiscrete = computed(() => selectedIssueStock.value ? isDiscreteMaterialUnit(selectedIssueStock.value.unit) : false)
const receiptQuantityMask = computed(() => createMaterialNumberMask(isReceiptDiscrete.value ? 0 : 3))
const receiptPackageMask = computed(() => createMaterialNumberMask(isDiscreteMaterialUnit(receiptBaseUnitLabel.value) ? 0 : 3))
const receiptCostMask = createMaterialNumberMask(4)
const issueQuantityMask = computed(() => createMaterialNumberMask(isIssueDiscrete.value ? 0 : 3))
const adjustmentQuantityMask = computed(() => createMaterialNumberMask(isDiscreteMaterialUnit(selectedStock.value?.unit) ? 0 : 3))
const adjustmentCostMask = createMaterialNumberMask(8)
const stocksPageCount = computed(() => Math.max(1, Math.ceil((inventory.value?.stocksTotalCount ?? 0) / stockFilters.pageSize)))
const stocksFirstItem = computed(() => inventory.value?.stocksTotalCount ? ((stockFilters.page - 1) * stockFilters.pageSize) + 1 : 0)
const stocksLastItem = computed(() => Math.min(stockFilters.page * stockFilters.pageSize, inventory.value?.stocksTotalCount ?? 0))
const selectedStockMovements = computed(() => inventory.value?.recentMovements ?? [])
const maxMaterialQuantity = 999999999

const validationAttempted = ref(false)
const receiptErrors = computed(() => ({
  name: receiptForm.catalogItemId ? '' : !receiptForm.name.trim() ? t('materials.enterMaterialName') : textError(receiptForm.name, 160),
  unit: receiptForm.unit ? '' : t('materials.chooseReceiptUnit'),
  quantity: numberError(receiptForm.quantity, isReceiptDiscrete.value ? 1 : 0.001, isReceiptDiscrete.value ? 0 : 3),
  packageQuantity: numberError(receiptForm.packageQuantity, isDiscreteMaterialUnit(receiptBaseUnitLabel.value) ? 1 : 0.001, isDiscreteMaterialUnit(receiptBaseUnitLabel.value) ? 0 : 3, false),
  unitCost: numberError(receiptForm.unitCost, 0, 4, false),
  comment: textError(receiptForm.comment, 1000)
}))
const issueErrors = computed(() => {
  const stock = selectedIssueStock.value
  const quantityError = numberError(issueForm.quantity, isIssueDiscrete.value ? 1 : 0.001, isIssueDiscrete.value ? 0 : 3)
  return {
    stockId: stock ? '' : t('materials.chooseIssueMaterial'),
    quantity: quantityError || (stock && Number(issueForm.quantity) > stock.availableQuantity
      ? t('materials.availableToIssue', { quantity: formatQuantity(stock.availableQuantity), unit: formatUnit(stock.unit) }) : ''),
    comment: textError(issueForm.comment, 1000)
  }
})
const adjustmentErrors = computed(() => {
  const stock = selectedStock.value
  const quantityError = numberError(adjustmentForm.quantity, 0, stock && isDiscreteMaterialUnit(stock.unit) ? 0 : 3)
  return {
    quantity: quantityError || (stock && Number(adjustmentForm.quantity) < stock.reservedQuantity
      ? t('materials.reservedMinimum', { quantity: formatQuantity(stock.reservedQuantity), unit: formatUnit(stock.unit) }) : ''),
    unitCost: adjustmentCostError(),
    reason: adjustmentForm.reason.trim() ? textError(adjustmentForm.reason, 800) : t('materials.enterAdjustmentReason')
  }
})

watch(activeView, () => { validationAttempted.value = false })

function adjustmentCostError() {
  const value = String(adjustmentForm.unitCost).trim().replace(',', '.')
  if (value === String(selectedStock.value?.averageUnitCost)) return ''
  if (!/^\d+(?:\.\d+)?$/.test(value)) return t('materials.nonnegativeCost')
  const fraction = value.split('.')[1]?.replace(/0+$/, '') ?? ''
  if (fraction.length > 8) return t('materials.maxFraction', { digits: 8 })
  if (Number(value) === maxMaterialQuantity && fraction) return t('materials.maxValue')

  return numberError(value, 0, 8)
}

function textError(value: string, maximum: number) {
  return value.trim().length > maximum ? t('materials.maxCharacters', { maximum }) : ''
}

function numberError(value: string | number, minimum: number, decimals: number, required = true) {
  if (!String(value ?? '').trim()) return required ? t('materials.required') : ''

  const number = Number(String(value).replace(',', '.'))
  if (!Number.isFinite(number)) return t('materials.enterNumber')
  if (number < minimum) return minimum > 0 ? t('materials.positiveQuantity') : t('materials.nonnegativeValue')
  if (number > maxMaterialQuantity) return t('materials.maxValue')
  if (number !== Number(number.toFixed(decimals))) return decimals === 0 ? t('materials.integerQuantity') : t('materials.maxFraction', { digits: decimals })

  return ''
}

function getTodayDate() {
  return formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')
}

function getCurrentTime() {
  return formatLocaleDate(new Date(), { timeZone: props.context.organization.timeZoneId || 'UTC', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
}

function toLocalIso(date: string, time = '00:00') {
  if (!date) {
    return null
  }

  try {
    return organizationUtcTime(`${date}T${time}`, props.context.organization.timeZoneId || 'UTC')
  } catch {
    return null
  }
}

function toPositiveNumber(value: string | number | null | undefined, max = maxMaterialQuantity) {
  const parsed = Number(String(value ?? '').replace(',', '.').trim())
  return Number.isFinite(parsed) && parsed > 0 && parsed <= max ? parsed : null
}

function calculateLineTotal(quantityValue: string | number | null | undefined, unitCostValue: string | number | null | undefined) {
  const quantity = toPositiveNumber(quantityValue)
  const unitCost = toPositiveNumber(unitCostValue)
  return quantity && unitCost ? quantity * unitCost : null
}

function formatDate(value: string) {
  const isDateOnly = /^\d{4}-\d{2}-\d{2}$/.test(value)
  return formatLocaleDate(isDateOnly ? `${value}T00:00:00Z` : value, { timeZone: isDateOnly ? 'UTC' : props.context.organization.timeZoneId || 'UTC' })
}

function formatDateTime(value: string) {
  return formatLocaleDate(value, {
    timeZone: props.context.organization.timeZoneId || 'UTC',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  })
}

function formatUnit(unit: string) {
  const keys: Record<string, string> = { шт: 'piece', л: 'liter', мл: 'milliliter', кг: 'kilogram', г: 'gram', м: 'meter', м2: 'squareMeter', компл: 'set', уп: 'package' }
  const key = keys[unit]
  return key ? t(`materials.units.${key}`) : unit
}

function formatQuantity(value: number) {
  return new Intl.NumberFormat(formattingCulture.value, { maximumFractionDigits: 3 }).format(value)
}

function formatMoney(value?: number | null, maximumFractionDigits = 2) {
  if (value === null || value === undefined) {
    return formatLocaleCurrency(0, workspaceCurrency.value)
  }

  return new Intl.NumberFormat(formattingCulture.value, { style: 'currency', currency: workspaceCurrency.value, maximumFractionDigits }).format(value)
}

function getMovementTypeName(typeCode: string) {
  switch (typeCode) {
    case 'receipt':
      return t('materials.receipt')
    case 'issue':
      return t('materials.issue')
    case 'maintenance_issue':
      return t('materials.service')
    case 'adjustment':
      return t('materials.adjustment')
    default:
      return t('materials.movementDefault')
  }
}

function getMovementTone(typeCode: string) {
  switch (typeCode) {
    case 'receipt':
      return 'receipt'
    case 'issue':
    case 'maintenance_issue':
      return 'issue'
    case 'adjustment':
      return 'adjustment'
    default:
      return 'default'
  }
}

function getMovementQuantityLabel(typeCode: string) {
  switch (typeCode) {
    case 'receipt':
      return t('materials.receipt')
    case 'issue':
    case 'maintenance_issue':
      return t('materials.expense')
    case 'adjustment':
      return t('materials.difference')
    default:
      return t('materials.change')
  }
}

function getMovementSourceKindName(movement: MaterialMovement) {
  if (movement.maintenanceId || movement.sourceEntityType === 'maintenance') {
    return t('materials.service')
  }

  if (movement.sourceEntityType === 'manual') {
    return t('materials.manualOperation')
  }

  return movement.sourceEntityType || t('materials.warehouse')
}

function getMovementReason(movement: MaterialMovement) {
  if (movement.comment) {
    return movement.comment
  }

  switch (movement.typeCode) {
    case 'receipt':
      return t('materials.receiptReason')
    case 'issue':
      return t('materials.issueReason')
    case 'maintenance_issue':
      return t('materials.autoServiceIssueReason')
    case 'adjustment':
      return t('materials.adjustmentReason')
    default:
      return t('materials.balanceMovementReason')
  }
}

function showError(message: string) {
  errorDialogMessage.value = message
}

function openView(view: typeof activeView.value) {
  clearTimeout(stockSearchTimer)
  latestRequest++
  isLoading.value = false
  activeView.value = view
  errorMessage.value = ''
  successMessage.value = ''
  if (view === 'stocks' || view === 'movements' || view === 'snapshot' || (view === 'issue' && !issueStockLocked.value)) {
    void loadSection()
  }
}

async function openStock(stock: MaterialStock) {
  clearTimeout(stockSearchTimer)
  selectedStock.value = stock
  activeView.value = 'stock-detail'
  errorMessage.value = ''
  successMessage.value = ''
  await loadSection()
}

function openMovement(movement: MaterialMovement) {
  selectedMovement.value = movement
  movementReturnView.value = activeView.value === 'stock-detail' ? 'stock-detail' : 'movements'
  openView('movement-detail')
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

function openAdjustment() {
  if (!selectedStock.value) return
  adjustmentForm.quantity = String(selectedStock.value.quantityOnHand)
  adjustmentForm.unitCost = String(selectedStock.value.averageUnitCost ?? 0)
  adjustmentForm.reason = ''
  adjustmentForm.expectedUpdatedAt = selectedStock.value.updatedAt
  activeView.value = 'adjustment'
}

async function saveAdjustment() {
  if (isSaving.value || !canWrite.value || !selectedStock.value) return
  validationAttempted.value = true
  if (Object.values(adjustmentErrors.value).some(Boolean)) return

  const quantity = Number(String(adjustmentForm.quantity).replace(',', '.'))
  const unitCost = String(adjustmentForm.unitCost).trim().replace(',', '.')
  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    selectedStock.value = await adjustMaterialStock(props.context.organization.id, selectedStock.value.id, {
      quantityOnHand: quantity,
      expectedUpdatedAt: adjustmentForm.expectedUpdatedAt,
      averageUnitCost: unitCost === String(selectedStock.value.averageUnitCost) ? null : unitCost,
      comment: adjustmentForm.reason.trim()
    })
    activeView.value = 'stock-detail'
    await loadSection()
    successMessage.value = t('materials.adjustmentSaved')
  } catch (error) {
    showError(error instanceof Error ? error.message : t('materials.saveAdjustmentError'))
  } finally {
    isSaving.value = false
  }
}

function openIssue(stock?: MaterialStock) {
  issueForm.stockId = stock?.id ?? ''
  issueForm.quantity = ''
  issueForm.comment = ''
  issueStockLocked.value = Boolean(stock)
  issueFilters.search = ''
  issueFilters.page = 1
  openView('issue')
}

function resetReceiptForm() {
  receiptForm.name = ''
  receiptForm.quantity = ''
  receiptForm.packageQuantity = ''
  receiptForm.unitCost = ''
  receiptForm.comment = ''
}

function resetIssueForm() {
  issueForm.stockId = ''
  issueForm.quantity = ''
  issueForm.comment = ''
  issueStockLocked.value = false
}

async function loadSection() {
  const request = ++latestRequest
  isLoading.value = true
  errorMessage.value = ''

  try {
    const movementDateFrom = activeView.value === 'movements' ? toLocalIso(movementFilters.date, '00:00') : null
    const movementDateTo = activeView.value === 'movements' ? toLocalIso(movementFilters.date, '23:59') : null
    const [materialsData, catalog] = await Promise.all([
      loadMaterials(props.context.organization.id, {
        searchQuery: activeView.value === 'stocks' ? stockFilters.search : activeView.value === 'issue' ? issueFilters.search : null,
        stockFilter: activeView.value === 'stocks' ? stockFilters.filter : null,
        stocksPage: activeView.value === 'stocks' ? stockFilters.page : activeView.value === 'issue' ? issueFilters.page : 1,
        stocksPageSize: activeView.value === 'stocks' ? stockFilters.pageSize : issueFilters.pageSize,
        movementType: activeView.value === 'movements' ? movementFilters.type : null,
        movementDateFrom,
        movementDateTo,
        snapshotAt: activeView.value === 'snapshot' ? snapshotDateTimeIso.value : null,
        stockId: activeView.value === 'stock-detail' ? selectedStock.value?.id ?? null : null,
        movementsPageSize: activeView.value === 'stock-detail' ? 100 : 20
      }),
      loadScenarioCatalog(props.context.organization.id)
    ])
    if (request !== latestRequest) return

    inventory.value = materialsData
    catalogMaterials.value = catalog.materials ?? []
    if (activeView.value === 'stocks' && stockFilters.page > stocksPageCount.value) {
      stockFilters.page = stocksPageCount.value
      await loadSection()
    }
  } catch (error) {
    if (request !== latestRequest) return

    const message = error instanceof Error ? error.message : t('materials.loadError')
    if (!inventory.value) errorMessage.value = message
    else showError(message)
  } finally {
    if (request === latestRequest) isLoading.value = false
  }
}

async function applyMovementFilters() {
  await loadSection()
}

function applyStockFilters() {
  clearTimeout(stockSearchTimer)
  stockFilters.page = 1
  void loadSection()
}

function resetStockFilters() {
  stockFilters.search = ''
  stockFilters.filter = ''
}

function selectStockFilter(filter: string) {
  if (!stockFilters.search && stockFilters.filter === filter) {
    applyStockFilters()
    return
  }

  stockFilters.search = ''
  stockFilters.filter = filter
}

watch([() => stockFilters.search, () => stockFilters.filter, selectedPageSize], ([search, filter, size], [previousSearch, previousFilter]) => {
  clearTimeout(stockSearchTimer)
  stockFilters.pageSize = Number(size)
  if (activeView.value !== 'stocks') return

  latestRequest++
  stockFilters.page = 1
  isLoading.value = true
  if (search && search !== previousSearch && filter === previousFilter) {
    stockSearchTimer = setTimeout(() => void loadSection(), 300)
    return
  }

  void loadSection()
})

watch(() => issueFilters.search, () => {
  clearTimeout(stockSearchTimer)
  if (activeView.value !== 'issue' || issueStockLocked.value) return

  latestRequest++
  issueFilters.page = 1
  issueForm.stockId = ''
  isLoading.value = true
  stockSearchTimer = setTimeout(() => void loadSection(), 300)
})

async function changeIssuePage(nextPage: number) {
  clearTimeout(stockSearchTimer)
  issueFilters.page = nextPage
  issueForm.stockId = ''
  await loadSection()
}

async function changeStocksPage(nextPage: number) {
  if (nextPage < 1 || nextPage > stocksPageCount.value || nextPage === stockFilters.page) return

  clearTimeout(stockSearchTimer)
  stockFilters.page = nextPage
  await loadSection()
}

async function loadSnapshot() {
  if (!snapshotDateTimeIso.value) {
    showError(t('materials.chooseSnapshotTime'))
    return
  }

  await loadSection()
}

async function addReceipt() {
  if (isSaving.value || !canWrite.value) {
    return
  }

  validationAttempted.value = true
  if (Object.values(receiptErrors.value).some(Boolean)) return

  const quantity = Number(String(receiptForm.quantity).replace(',', '.'))
  const packageQuantity = String(receiptForm.packageQuantity).trim() ? Number(String(receiptForm.packageQuantity).replace(',', '.')) : null
  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''

  try {
    await createMaterialReceipt(props.context.organization.id, {
      catalogItemId: receiptForm.catalogItemId || null,
      name: receiptForm.catalogItemId ? null : receiptForm.name,
      unit: normalizeMaterialUnit(receiptForm.unit),
      quantity,
      packageQuantity,
      unitCost: String(receiptForm.unitCost).trim() ? Number(String(receiptForm.unitCost).replace(',', '.')) : null,
      comment: receiptForm.comment || null
    })
    resetReceiptForm()
    successMessage.value = t('materials.receiptAdded')
    activeView.value = 'stocks'
    await loadSection()
  } catch (error) {
    showError(error instanceof Error ? error.message : t('materials.addReceiptError'))
  } finally {
    isSaving.value = false
  }
}

async function issueMaterial() {
  if (isSaving.value || !canWrite.value) {
    return
  }

  validationAttempted.value = true
  if (Object.values(issueErrors.value).some(Boolean)) return

  const stock = selectedIssueStock.value!
  const quantity = Number(String(issueForm.quantity).replace(',', '.'))
  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''

  try {
    await createMaterialIssue(props.context.organization.id, {
      stockId: stock.id,
      quantity,
      comment: issueForm.comment || null
    })
    resetIssueForm()
    successMessage.value = t('materials.materialIssued')
    activeView.value = 'stocks'
    await loadSection()
  } catch (error) {
    showError(error instanceof Error ? error.message : t('materials.issueError'))
  } finally {
    isSaving.value = false
  }
}

onMounted(loadSection)
onBeforeUnmount(() => {
  clearTimeout(successTimer)
  clearTimeout(stockSearchTimer)
  latestRequest++
})
</script>

<template>
  <UiCard page :title="currentTitle" :breadcrumbs="materialBreadcrumbs" class="workspace-operations workspace-materials" :class="{ 'materials-editor': ['receipt', 'issue', 'adjustment'].includes(activeView), 'materials-detail': activeView === 'stock-detail' }">
    <template #actions>
      <WorkspacePageHelpButton section-code="materials" :title="currentTitle" />
    </template>

    <UiLoader v-if="isLoading && (!inventory || !hasInlineLoading)" :text="t('materials.loadingWarehouse')" />
    <div v-else-if="errorMessage" class="detailing-state detailing-state-error">{{ errorMessage }}</div>

    <template v-if="inventory && (!isLoading || hasInlineLoading)">
      <div v-if="successMessage" class="materials-notice" role="status">
        <span class="materials-notice__icon" aria-hidden="true">✓</span>
        <span>{{ successMessage }}</span>
        <button type="button" :aria-label="t('materials.closeNotification')" @click="successMessage = ''">×</button>
      </div>
      <WorkspaceAccessNotice :can-manage-billing="context.organization.canManageBilling === true" v-if="readOnlyMessage" :message="readOnlyMessage" :href="billingHref" />
      <div v-if="activeView === 'stocks'" class="materials-overview">
        <div class="materials-stock-filters" role="group" :aria-label="t('materials.stockState')">
          <button type="button" :aria-pressed="!stockFilters.filter && !stockFilters.search" @click="selectStockFilter('')">{{ t('materials.allItems') }} <b>{{ inventory.allStocksCount ?? inventory.stocksTotalCount }}</b></button>
          <button type="button" class="materials-stock-filters__low" :aria-pressed="stockFilters.filter === 'low'" @click="selectStockFilter('low')">{{ t('materials.belowMinimumStatus') }} <b>{{ inventory.lowStockCount }}</b></button>
          <button type="button" class="materials-stock-filters__out" :aria-pressed="stockFilters.filter === 'out'" @click="selectStockFilter('out')">{{ t('materials.noStock') }} <b>{{ inventory.outOfStockCount }}</b></button>
        </div>
        <div class="materials-total-value"><span>{{ t('materials.warehouseCost') }}</span><strong>{{ formatMoney(inventory.totalInventoryValue) }}</strong></div>
      </div>
      <div class="materials-toolbar" :class="{ 'materials-toolbar--stocks': activeView === 'stocks' }">
        <UiButton v-if="activeView !== 'stocks'" variant="secondary" @click="openView(activeView === 'movement-detail' ? movementReturnView : 'stocks')">{{ t('materials.back') }}</UiButton>
        <template v-if="activeView === 'stocks'">
          <UiButton variant="secondary" @click="openView('movements')">{{ t('materials.movements') }}</UiButton>
          <UiButton variant="secondary" @click="openView('snapshot')">{{ t('materials.snapshot') }}</UiButton>
          <UiButton v-if="canWrite" variant="secondary" @click="openIssue()">{{ t('materials.issue') }}</UiButton>
          <UiButton :disabled="context.workspaceAccess?.isReadOnly === true && !context.organization.canManageBilling" v-else :href="billingHref" variant="secondary">{{ t('materials.issue') }}</UiButton>
          <UiButton v-if="canWrite" class="materials-receipt" @click="openView('receipt')">{{ t('materials.addReceiptAction') }}</UiButton>
          <UiButton :disabled="context.workspaceAccess?.isReadOnly === true && !context.organization.canManageBilling" v-else class="materials-receipt" :href="billingHref" variant="secondary">{{ t('materials.addReceiptAction') }}</UiButton>
        </template>
      </div>

      <section v-if="activeView === 'receipt'" class="materials-screen">
        <form novalidate class="materials-action-form materials-action-form--receipt" @submit.prevent="addReceipt">
          <div class="catalog-linked-field materials-form-wide">
            <UiSelect v-model="receiptForm.catalogItemId" :label="t('materials.material')" :options="materialOptions" :disabled="isSaving || !canWrite" />
            <UiButton variant="secondary" href="#catalog?catalog=material" :disabled="isSaving">{{ t('materials.catalog') }}</UiButton>
          </div>
          <UiInput v-if="!receiptForm.catalogItemId" v-model="receiptForm.name" :maxlength="160" required :label-help="t('materials.until160Characters')" :error="validationAttempted ? receiptErrors.name : ''" :label="t('materials.newMaterialName')" :disabled="isSaving || !canWrite" />
          <UiInput v-model="receiptForm.quantity" inputmode="decimal" :mask="receiptQuantityMask" required :label-help="isReceiptDiscrete ? t('materials.discreteQuantityHint') : t('materials.quantityHint')" :error="validationAttempted ? receiptErrors.quantity : ''" :label="t('materials.quantity')" :disabled="isSaving || !canWrite" />
          <UiSelect v-model="receiptForm.unit" required :error="validationAttempted ? receiptErrors.unit : ''" :label="t('materials.unit')" :options="localizedMaterialUnitOptions" :searchable="false" :disabled="isSaving || !canWrite" />
          <UiInput v-model="receiptForm.packageQuantity" inputmode="decimal" :mask="receiptPackageMask" :label-help="t('materials.packageQuantity', { unit: formatUnit(receiptBaseUnitLabel) })" :error="validationAttempted ? receiptErrors.packageQuantity : ''" :label="t('materials.unitsPerPackage', { unit: formatUnit(receiptBaseUnitLabel) })" :disabled="isSaving || !canWrite" />
          <UiInput v-model="receiptForm.unitCost" inputmode="decimal" :mask="receiptCostMask" :label-help="t('materials.costLimitHint', { currency: workspaceCurrency, digits: 4 })" :error="validationAttempted ? receiptErrors.unitCost : ''" :label="receiptUnitCostLabel" :disabled="isSaving || !canWrite" />
          <div v-if="receiptTotalCost" class="materials-form-summary">
            <span>{{ t('materials.batchTotal') }}</span>
            <strong>{{ formatMoney(receiptTotalCost) }}</strong>
            <small>{{ formatQuantity(Number(receiptForm.quantity)) }} {{ formatUnit(receiptUnitLabel) }} × {{ formatMoney(Number(receiptForm.unitCost), 4) }}</small>
          </div>
          <UiInput v-model="receiptForm.comment" :maxlength="1000" :label-help="t('materials.maxCharacters', { maximum: 1000 })" :error="validationAttempted ? receiptErrors.comment : ''" class="materials-form-wide" :label="t('materials.comment')" :disabled="isSaving || !canWrite" />
          <div class="materials-form-actions">
            <UiButton variant="secondary" @click="openView('stocks')">{{ t('materials.cancel') }}</UiButton>
            <UiButton v-if="canWrite" type="submit" :disabled="isSaving">{{ t('materials.addReceipt') }}</UiButton>
            <UiButton :disabled="context.workspaceAccess?.isReadOnly === true && !context.organization.canManageBilling" v-else :href="billingHref" variant="secondary">{{ t('materials.addReceipt') }}</UiButton>
          </div>
        </form>
      </section>

      <section v-else-if="activeView === 'adjustment' && selectedStock" class="materials-screen">
        <form novalidate class="materials-action-form" @submit.prevent="saveAdjustment">
          <div class="materials-selected-stock"><strong>{{ selectedStock.name }}</strong><span>{{ t('materials.now', { quantity: formatQuantity(selectedStock.quantityOnHand), unit: formatUnit(selectedStock.unit), cost: formatMoney(selectedStock.averageUnitCost, 8) }) }}</span></div>
          <UiInput v-model="adjustmentForm.quantity" inputmode="decimal" :mask="adjustmentQuantityMask" required :label-help="t('materials.reserveLimit', { quantity: formatQuantity(selectedStock.reservedQuantity), unit: formatUnit(selectedStock.unit) })" :error="validationAttempted ? adjustmentErrors.quantity : ''" :label="t('materials.onHand')" :disabled="isSaving || !canWrite" />
          <UiInput v-model="adjustmentForm.unitCost" inputmode="decimal" :mask="adjustmentCostMask" required :label-help="t('materials.costLimitHint', { currency: workspaceCurrency, digits: 8 })" :error="validationAttempted ? adjustmentErrors.unitCost : ''" :label="t('materials.unitCost')" :disabled="isSaving || !canWrite" />
          <UiInput v-model="adjustmentForm.reason" :maxlength="800" required :label-help="t('materials.maxCharacters', { maximum: 800 })" :error="validationAttempted ? adjustmentErrors.reason : ''" class="materials-form-wide" :label="t('materials.adjustmentReasonLabel')" :disabled="isSaving || !canWrite" />
          <div class="materials-form-actions">
            <UiButton variant="secondary" :disabled="isSaving" @click="openView('stock-detail')">{{ t('materials.cancel') }}</UiButton>
            <UiButton v-if="canWrite" type="submit" :disabled="isSaving">{{ t('materials.saveAdjustment') }}</UiButton>
          </div>
        </form>
      </section>

      <section v-else-if="activeView === 'issue'" class="materials-screen">
        <form novalidate class="materials-action-form materials-action-form--issue" @submit.prevent="issueMaterial">
          <template v-if="!issueStockLocked">
            <UiInput v-model="issueFilters.search" type="search" :label="t('materials.searchMaterial')" :placeholder="t('materials.materialName')" :disabled="isSaving || !canWrite" />
            <UiSelect v-model="issueForm.stockId" required :error="validationAttempted ? issueErrors.stockId : ''" :label="t('materials.item')" :options="stockOptions" :searchable="false" :disabled="isSaving || isLoading || !canWrite" />
            <UiLoader v-if="isLoading" compact :text="t('materials.refreshingStocks')" />
            <UiPagination class="materials-form-wide" :model-value="issueFilters.page" :total-items="inventory.stocksTotalCount" :items-per-page="issueFilters.pageSize" :max-pages-shown="3" :disabled="isLoading" :label="t('materials.issuePages')" @update:model-value="changeIssuePage" />
          </template>
          <div v-if="selectedIssueStock" class="materials-selected-stock">
            <strong v-if="issueStockLocked">{{ selectedIssueStock.name }}</strong>
            <span>{{ t('materials.onHandAvailable', { onHand: formatQuantity(selectedIssueStock.quantityOnHand), available: formatQuantity(selectedIssueStock.availableQuantity), unit: formatUnit(selectedIssueStock.unit) }) }}</span>
          </div>
          <UiInput v-model="issueForm.quantity" inputmode="decimal" :mask="issueQuantityMask" required :label-help="isIssueDiscrete ? t('materials.discreteIssueHint') : t('materials.issueQuantityHint')" :error="validationAttempted ? issueErrors.quantity : ''" :label="t('materials.quantityWithUnit', { unit: issueUnitLabel })" :disabled="isSaving || !canWrite" />
          <div v-if="selectedIssueStock" class="materials-issue-cost">
            <span>{{ t('materials.costPerUnit', { unit: issueUnitLabel }) }}</span>
            <strong>{{ formatMoney(selectedIssueStock.averageUnitCost, 8) }}</strong>
            <small v-if="issueTotalCost">{{ t('materials.toIssue') }} <b>{{ formatMoney(issueTotalCost, 2) }}</b></small>
          </div>
          <UiInput v-model="issueForm.comment" :maxlength="1000" :label-help="t('materials.maxCharacters', { maximum: 1000 })" :error="validationAttempted ? issueErrors.comment : ''" class="materials-form-wide" :label="t('materials.issueReasonLabel')" :disabled="isSaving || !canWrite" />
          <div class="materials-form-actions">
            <UiButton variant="secondary" @click="openView('stocks')">{{ t('materials.cancel') }}</UiButton>
            <UiButton v-if="canWrite" type="submit" :disabled="isSaving || isLoading">{{ t('materials.writeOff') }}</UiButton>
            <UiButton :disabled="context.workspaceAccess?.isReadOnly === true && !context.organization.canManageBilling" v-else :href="billingHref" variant="secondary">{{ t('materials.writeOff') }}</UiButton>
          </div>
        </form>
      </section>

      <section v-else-if="activeView === 'stocks'" class="materials-screen" :aria-busy="isLoading">
        <form class="materials-filterbar materials-filterbar--stocks" @submit.prevent="applyStockFilters">
          <UiSearchInput v-model="stockFilters.search" class="materials-stock-search" id="material-stock-search" :placeholder="t('materials.materialName')" />
          <label class="materials-sr-only" for="material-stock-search">{{ t('materials.warehouseSearchLabel') }}</label>
          <UiSelect v-model="stockFilters.filter" :aria-label="t('materials.stock')" :options="stockFilterOptions" :searchable="false" />
          <UiButton type="button" variant="secondary" :disabled="!stockFilters.search && !stockFilters.filter" @click="resetStockFilters">{{ t('materials.reset') }}</UiButton>
          <UiLoader v-if="isLoading" compact :text="t('materials.refreshingBalance')" />
        </form>
        <div v-if="!inventory.stocks.length" class="autoservice-empty">{{ t('materials.noItemsForFilters') }}</div>
        <div v-else class="materials-table materials-table--stocks">
          <div class="materials-table__head">
            <span>{{ t('materials.material') }}</span><span>{{ t('materials.onHand') }}</span><span>{{ t('materials.available') }}</span><span>{{ t('materials.unitCostShort') }}</span><span>{{ t('materials.remainingValue') }}</span><span></span>
          </div>
          <article v-for="stock in inventory.stocks" :key="stock.id" class="materials-table__row" :class="{ 'materials-table__row--warning': stock.isLowStock, 'materials-table__row--danger': stock.isOutOfStock }">
            <div class="materials-stock-name">
              <strong>{{ stock.name }}</strong>
              <span v-if="stock.isOutOfStock" class="materials-stock-status materials-stock-status--out">{{ t('materials.noStock') }}</span>
              <span v-else-if="stock.isLowStock" class="materials-stock-status materials-stock-status--low">{{ t('materials.belowMinimumStatus') }}</span>
              <span>{{ t('materials.minimum', { quantity: formatQuantity(stock.minQuantity), unit: formatUnit(stock.unit) }) }}</span>
            </div>
            <div :data-label="t('materials.onHand')"><strong>{{ formatQuantity(stock.quantityOnHand) }} {{ formatUnit(stock.unit) }}</strong></div>
            <div :data-label="t('materials.available')">
              <strong>{{ formatQuantity(stock.availableQuantity) }} {{ formatUnit(stock.unit) }}</strong>
              <span>{{ t('materials.reserved', { quantity: formatQuantity(stock.reservedQuantity), unit: formatUnit(stock.unit) }) }}</span>
            </div>
            <div :data-label="t('materials.unitCostShort')"><strong>{{ formatMoney(stock.averageUnitCost, 8) }} / {{ formatUnit(stock.unit) }}</strong></div>
            <div :data-label="t('materials.remainingValue')"><strong>{{ formatMoney(stock.inventoryValue) }}</strong></div>
            <div class="materials-row-actions">
              <UiButton variant="secondary" :disabled="isLoading" @click="openStock(stock)">{{ t('materials.history') }}</UiButton>
              <UiButton v-if="canWrite" variant="secondary" :disabled="isSaving || isLoading || stock.availableQuantity <= 0" @click="openIssue(stock)">{{ t('materials.writeOff') }}</UiButton>
              <UiButton v-else :disabled="!context.organization.canManageBilling || stock.availableQuantity <= 0" :href="billingHref" variant="secondary">{{ t('materials.writeOff') }}</UiButton>
            </div>
          </article>
        </div>
        <div v-if="inventory.stocksTotalCount" class="materials-pagination materials-pagination--stocks">
          <div class="materials-pagination__summary">
            <span aria-live="polite">{{ t('materials.pageRange', { first: stocksFirstItem, last: stocksLastItem, total: inventory.stocksTotalCount }) }}</span>
            <div class="materials-page-size"><span>{{ t('materials.perPage') }}</span><UiSelect v-model="selectedPageSize" :options="stockPageSizeOptions" :searchable="false" :aria-label="t('materials.pageSize')" /></div>
          </div>
          <UiPagination :model-value="stockFilters.page" :total-items="inventory.stocksTotalCount" :items-per-page="stockFilters.pageSize" :max-pages-shown="3" :disabled="isLoading" :label="t('materials.warehousePagination')" @update:model-value="changeStocksPage" />
        </div>
      </section>

      <section v-else-if="activeView === 'movements'" class="materials-screen">
        <div class="materials-filterbar">
          <UiDatePicker v-model="movementFilters.date" :label="t('materials.movementDate')" :placeholder="t('materials.allDates')" />
          <UiSelect v-model="movementFilters.type" :label="t('materials.movementType')" :options="movementTypeOptions" :searchable="false" />
          <UiButton variant="secondary" :disabled="isLoading" @click="applyMovementFilters">{{ t('materials.show') }}</UiButton>
        </div>
        <div class="materials-movement-list">
          <div v-if="!inventory.recentMovements.length" class="materials-empty">{{ t('materials.noMovementsForFilters') }}</div>
          <article
            v-for="movement in inventory.recentMovements"
            :key="movement.id"
            class="materials-movement materials-movement--clickable"
            role="button"
            tabindex="0"
            @click="openMovement(movement)"
            @keydown.enter.prevent="openMovement(movement)"
            @keydown.space.prevent="openMovement(movement)"
          >
            <div>
              <div class="materials-movement-title">
                <span class="materials-movement-badge" :class="`materials-movement-badge--${getMovementTone(movement.typeCode)}`">
                  {{ getMovementTypeName(movement.typeCode) }}
                </span>
                <strong>{{ movement.materialName }}</strong>
              </div>
              <span>{{ formatDateTime(movement.createdAt) }}</span>
              <small v-if="movement.comment">{{ movement.comment }}</small>
            </div>
            <div>
              <strong>{{ formatQuantity(movement.quantity) }} {{ formatUnit(movement.unit) }}</strong>
              <span>{{ getMovementQuantityLabel(movement.typeCode) }} · {{ formatMoney(movement.totalCost) }}</span>
            </div>
            <div>
              <strong>{{ formatQuantity(movement.balanceAfter) }}</strong>
              <span>{{ t('materials.balanceAfter', { cost: formatMoney(movement.costBalanceAfter) }) }}</span>
            </div>
          </article>
        </div>
      </section>

      <section v-else-if="activeView === 'movement-detail' && selectedMovement" class="materials-screen">
        <div class="materials-movement-card">
          <div class="materials-movement-card__hero">
            <span class="materials-movement-badge" :class="`materials-movement-badge--${getMovementTone(selectedMovement.typeCode)}`">
              {{ getMovementTypeName(selectedMovement.typeCode) }}
            </span>
            <div>
              <strong>{{ selectedMovement.materialName }}</strong>
              <span>{{ formatDateTime(selectedMovement.createdAt) }}</span>
            </div>
          </div>
          <div class="materials-movement-card__grid">
            <div>
              <span>{{ t('materials.movementChange') }}</span>
              <strong>{{ formatQuantity(selectedMovement.quantity) }} {{ formatUnit(selectedMovement.unit) }}</strong>
              <small>{{ getMovementQuantityLabel(selectedMovement.typeCode) }} · {{ formatMoney(selectedMovement.totalCost) }}</small>
            </div>
            <div>
              <span>{{ t('materials.unitCostAtOperation') }}</span>
              <strong>{{ formatMoney(selectedMovement.unitCost, 8) }}</strong>
              <small>{{ t('materials.atOperation', { unit: formatUnit(selectedMovement.unit) }) }}</small>
            </div>
            <div>
              <span>{{ t('materials.remaining') }}</span>
              <strong>{{ formatQuantity(selectedMovement.balanceAfter) }} {{ formatUnit(selectedMovement.unit) }}</strong>
              <small>{{ formatMoney(selectedMovement.costBalanceAfter) }}</small>
            </div>
            <div>
              <span>{{ t('materials.whoCreated') }}</span>
              <strong>{{ selectedMovement.createdByDisplayName || t('materials.notSpecified') }}</strong>
              <small>{{ selectedMovement.createdByUserId || t('materials.oldOrSystemMovement') }}</small>
            </div>
          </div>
          <div class="materials-movement-card__source">
            <span>{{ t('materials.source') }}</span>
            <strong>{{ selectedMovement.sourceTitle || getMovementSourceKindName(selectedMovement) }}</strong>
            <small>{{ getMovementReason(selectedMovement) }}</small>
            <UiButton v-if="selectedMovement.sourceUrl" variant="secondary" :href="normalizeWorkspaceUrl(selectedMovement.sourceUrl)">{{ t('materials.openSource') }}</UiButton>
          </div>
        </div>
      </section>

      <section v-else-if="activeView === 'stock-detail' && selectedStock" class="materials-screen">
        <UiCard :title="t('materials.allStocks')" class="materials-stock-card">
          <div class="materials-stock-heading">
            <h3>{{ selectedStock.name }}</h3>
            <span class="materials-stock-status" :class="selectedStock.isOutOfStock ? 'materials-stock-status--out' : selectedStock.isLowStock ? 'materials-stock-status--low' : 'materials-stock-status--available'">
              {{ selectedStock.isOutOfStock ? t('materials.noStock') : selectedStock.isLowStock ? t('materials.belowMinimumStatus') : t('materials.availableStatus') }}
            </span>
          </div>
          <div class="materials-stock-meta">
            <span>{{ t('materials.updatedAt', { date: formatDate(selectedStock.updatedAt) }) }}</span>
            <span v-if="selectedStock.packageQuantity">{{ t('materials.package', { quantity: formatQuantity(selectedStock.packageQuantity), unit: formatUnit(selectedStock.unit) }) }}</span>
          </div>
          <dl class="materials-stock-metrics">
            <div><dt>{{ t('materials.onHand') }}</dt><dd>{{ formatQuantity(selectedStock.quantityOnHand) }} {{ formatUnit(selectedStock.unit) }}</dd></div>
            <div><dt>{{ t('materials.available') }}</dt><dd>{{ formatQuantity(selectedStock.availableQuantity) }} {{ formatUnit(selectedStock.unit) }}</dd></div>
            <div><dt>{{ t('materials.reserved', { quantity: formatQuantity(selectedStock.reservedQuantity), unit: formatUnit(selectedStock.unit) }) }}</dt><dd>{{ formatQuantity(selectedStock.reservedQuantity) }} {{ formatUnit(selectedStock.unit) }}</dd></div>
            <div><dt>{{ t('materials.minimum', { quantity: formatQuantity(selectedStock.minQuantity), unit: formatUnit(selectedStock.unit) }) }}</dt><dd>{{ formatQuantity(selectedStock.minQuantity) }} {{ formatUnit(selectedStock.unit) }}</dd></div>
            <div><dt>{{ t('materials.costPerUnit', { unit: formatUnit(selectedStock.unit) }) }}</dt><dd>{{ formatMoney(selectedStock.averageUnitCost, 8) }}</dd></div>
            <div><dt>{{ t('materials.remainingValue') }}</dt><dd>{{ formatMoney(selectedStock.inventoryValue) }}</dd></div>
          </dl>
          <div v-if="canWrite" class="materials-stock-actions">
            <UiButton :disabled="isSaving || selectedStock.availableQuantity <= 0" @click="openIssue(selectedStock)">{{ t('materials.writeOff') }}</UiButton>
            <UiButton variant="secondary" :disabled="isSaving" @click="openAdjustment">{{ t('materials.adjustment') }}</UiButton>
          </div>
        </UiCard>

        <UiCard :title="t('materials.movementHistory')" class="materials-stock-history">
          <div class="materials-movement-list">
            <article
              v-for="movement in selectedStockMovements"
              :key="movement.id"
              class="materials-movement materials-movement--clickable"
              role="button"
              tabindex="0"
              @click="openMovement(movement)"
              @keydown.enter.prevent="openMovement(movement)"
              @keydown.space.prevent="openMovement(movement)"
            >
              <div>
                <div class="materials-movement-title">
                  <span class="materials-movement-badge" :class="`materials-movement-badge--${getMovementTone(movement.typeCode)}`">
                    {{ getMovementTypeName(movement.typeCode) }}
                  </span>
                  <strong>{{ movement.sourceTitle || movement.materialName }}</strong>
                </div>
                <span>{{ formatDateTime(movement.createdAt) }}</span>
                <small v-if="movement.comment">{{ movement.comment }}</small>
              </div>
              <div>
                <strong>{{ formatQuantity(movement.quantity) }} {{ formatUnit(movement.unit) }}</strong>
                <span>{{ getMovementQuantityLabel(movement.typeCode) }} · {{ formatMoney(movement.totalCost) }}</span>
              </div>
              <div>
                <strong>{{ formatQuantity(movement.balanceAfter) }} {{ formatUnit(movement.unit) }}</strong>
                <span>{{ t('materials.balanceAfter', { cost: formatMoney(movement.costBalanceAfter) }) }}</span>
              </div>
            </article>
            <div v-if="selectedStockMovements.length === 0" class="autoservice-empty">{{ t('materials.noStockMovements') }}</div>
          </div>
        </UiCard>
      </section>

      <section v-else class="materials-screen">
        <div class="materials-filterbar materials-filterbar--snapshot">
          <UiDatePicker v-model="snapshotForm.date" :label="t('materials.reportDate')" required />
          <UiTimePicker v-model="snapshotForm.time" :label="t('materials.time')" required />
          <UiButton variant="secondary" :disabled="isLoading" @click="loadSnapshot">{{ t('materials.showSnapshot') }}</UiButton>
        </div>
        <div class="scheduler-summary">
          <span><b>{{ inventory.snapshotStocks.length }}</b> {{ t('materials.positions') }}</span>
          <span><b>{{ formatMoney(inventory.snapshotTotalInventoryValue) }}</b> {{ t('materials.snapshotTotal') }}</span>
          <span v-if="inventory.snapshotAt"><b>{{ formatDateTime(inventory.snapshotAt) }}</b> {{ t('materials.snapshotMoment') }}</span>
        </div>
        <div class="materials-table">
          <div class="materials-table__head materials-table__head--snapshot">
            <span>{{ t('materials.item') }}</span>
            <span>{{ t('materials.snapshotBalance') }}</span>
            <span>{{ t('materials.cost') }}</span>
            <span>{{ t('materials.value') }}</span>
          </div>
          <article v-for="stock in inventory.snapshotStocks" :key="stock.stockId" class="materials-table__row materials-table__row--snapshot">
            <div class="materials-stock-name">
              <strong>{{ stock.name }}</strong>
              <span>{{ formatUnit(stock.unit) }}</span>
            </div>
            <div>
              <strong>{{ formatQuantity(stock.quantityOnHand) }} {{ formatUnit(stock.unit) }}</strong>
              <span>{{ t('materials.atSelectedTime') }}</span>
            </div>
            <div>
              <strong>{{ formatMoney(stock.averageUnitCost, 8) }}</strong>
              <span>{{ t('materials.perUnit') }}</span>
            </div>
            <div>
              <strong>{{ formatMoney(stock.inventoryValue) }}</strong>
              <span>{{ t('materials.remaining') }}</span>
            </div>
          </article>
        </div>
      </section>
    </template>
    <UiAlertDialog
      :open="Boolean(errorDialogMessage)"
      :title="t('materials.title')"
      :message="errorDialogMessage"
      tone="danger"
      @close="errorDialogMessage = ''"
    />
  </UiCard>
</template>

<script setup lang="ts">
import { WorkspaceAccessNotice } from '@opencrm/workspace'
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { UiAlertDialog, UiButton, UiCard, UiDatePicker, UiInput, UiLoader, UiPagination, UiSearchInput, UiSelect, UiTimePicker, formatDateOnly, parseDateOnly } from '@opencrm/ui'
import { organizationUtcTime, WorkspacePageHelpButton, createMaterialNumberMask, isDiscreteMaterialUnit, materialUnitOptions, normalizeMaterialUnit } from '@opencrm/workspace'
import { loadScenarioCatalog, type AutoServiceCatalogItem, type AutoServiceWorkspaceContext } from '@/api/autoserviceBff'
import {
  adjustMaterialStock,
  createMaterialIssue,
  createMaterialReceipt,
  loadMaterials,
  type MaterialInventory,
  type MaterialMovement,
  type MaterialStock
} from '@/api/materials'

const props = defineProps<{
  context: AutoServiceWorkspaceContext
}>()

const inventory = ref<MaterialInventory | null>(null)
const catalogMaterials = ref<AutoServiceCatalogItem[]>([])
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
  switch (activeView.value) {
    case 'adjustment':
      return 'Корректировка склада'
    case 'receipt':
      return 'Приход'
    case 'issue':
      return 'Списание'
    case 'movements':
      return 'Движения'
    case 'movement-detail':
      return 'Движение склада'
    case 'stock-detail':
      return 'Материал'
    case 'snapshot':
      return 'Остатки на время'
    default:
      return 'Склад'
  }
})
const materialBreadcrumbs = computed(() => {
  if (activeView.value === 'stocks') return []

  const items = [{ title: 'Склад', href: '#materials', onClick: () => openView('stocks') }]
  if (activeView.value === 'adjustment' || (activeView.value === 'issue' && issueStockLocked.value)
    || (activeView.value === 'movement-detail' && movementReturnView.value === 'stock-detail')) {
    items.push({ title: 'Материал', href: '#materials', onClick: () => openView('stock-detail') })
  } else if (activeView.value === 'movement-detail') {
    items.push({ title: 'Движения', href: '#materials', onClick: () => openView('movements') })
  }

  return items
})
const movementTypeOptions = [
  { value: '', label: 'Все типы' },
  { value: 'receipt', label: 'Приход' },
  { value: 'issue', label: 'Ручный расход' },
  { value: 'maintenance_issue', label: 'Расход по заказ-наряду' },
  { value: 'adjustment', label: 'Корректировка' }
]
const stockFilterOptions = [
  { value: '', label: 'Все остатки' },
  { value: 'out', label: 'Нет в наличии' },
  { value: 'low', label: 'Ниже минимума' },
  { value: 'ok', label: 'В норме' }
]
const materialOptions = computed(() => [
  { value: '', label: 'Новая позиция склада' },
  ...catalogMaterials.value
    .filter(item => item.isActive !== false && item.name?.trim())
    .map(item => ({ value: item.id, label: item.name.trim() }))
])
const stockOptions = computed(() => [
  { value: '', label: 'Выберите позицию' },
  ...(inventory.value?.stocks ?? []).map(stock => ({
    value: stock.id,
    label: `${stock.name} · ${formatQuantity(stock.quantityOnHand)} ${stock.unit}`
  }))
])
const selectedIssueStock = computed(() => inventory.value?.stocks.find(stock => stock.id === issueForm.stockId) ?? null)
const snapshotDateTimeIso = computed(() => toLocalIso(snapshotForm.date, snapshotForm.time || '23:59'))
const receiptUnitLabel = computed(() => normalizeMaterialUnit(receiptForm.unit || 'шт'))
const receiptBaseUnitLabel = computed(() => receiptUnitLabel.value === 'кг' ? 'г' : receiptUnitLabel.value === 'л' ? 'мл' : receiptUnitLabel.value)
const receiptUnitCostLabel = computed(() => `Цена за ${receiptUnitLabel.value}`)
const receiptTotalCost = computed(() => calculateLineTotal(receiptForm.quantity, receiptForm.unitCost))
const issueUnitLabel = computed(() => selectedIssueStock.value?.unit || 'ед.')
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
  name: receiptForm.catalogItemId ? '' : !receiptForm.name.trim() ? 'Укажите название материала.' : textError(receiptForm.name, 160),
  unit: receiptForm.unit ? '' : 'Выберите единицу измерения.',
  quantity: numberError(receiptForm.quantity, isReceiptDiscrete.value ? 1 : 0.001, isReceiptDiscrete.value ? 0 : 3),
  packageQuantity: numberError(receiptForm.packageQuantity, isDiscreteMaterialUnit(receiptBaseUnitLabel.value) ? 1 : 0.001, isDiscreteMaterialUnit(receiptBaseUnitLabel.value) ? 0 : 3, false),
  unitCost: numberError(receiptForm.unitCost, 0, 4, false),
  comment: textError(receiptForm.comment, 1000)
}))
const issueErrors = computed(() => {
  const stock = selectedIssueStock.value
  const quantityError = numberError(issueForm.quantity, isIssueDiscrete.value ? 1 : 0.001, isIssueDiscrete.value ? 0 : 3)
  return {
    stockId: stock ? '' : 'Выберите материал для списания.',
    quantity: quantityError || (stock && Number(issueForm.quantity) > stock.availableQuantity
      ? `Доступно для списания: ${formatQuantity(stock.availableQuantity)} ${stock.unit}.` : ''),
    comment: textError(issueForm.comment, 1000)
  }
})
const adjustmentErrors = computed(() => {
  const stock = selectedStock.value
  const quantityError = numberError(adjustmentForm.quantity, 0, stock && isDiscreteMaterialUnit(stock.unit) ? 0 : 3)
  return {
    quantity: quantityError || (stock && Number(adjustmentForm.quantity) < stock.reservedQuantity
      ? `Остаток не может быть меньше резерва: ${formatQuantity(stock.reservedQuantity)} ${stock.unit}.` : ''),
    unitCost: adjustmentCostError(),
    reason: adjustmentForm.reason.trim() ? textError(adjustmentForm.reason, 800) : 'Укажите причину корректировки.'
  }
})

watch(activeView, () => { validationAttempted.value = false })

function adjustmentCostError() {
  const value = String(adjustmentForm.unitCost).trim().replace(',', '.')
  if (value === String(selectedStock.value?.averageUnitCost)) return ''
  if (!/^\d+(?:\.\d+)?$/.test(value)) return 'Введите неотрицательную себестоимость.'
  const fraction = value.split('.')[1]?.replace(/0+$/, '') ?? ''
  if (fraction.length > 8) return 'Не больше 8 знаков после запятой.'
  if (Number(value) === maxMaterialQuantity && fraction) return 'Не больше 999 999 999.'

  return numberError(value, 0, 8)
}

function textError(value: string, maximum: number) {
  return value.trim().length > maximum ? `Не больше ${maximum} символов.` : ''
}

function numberError(value: string | number, minimum: number, decimals: number, required = true) {
  if (!String(value ?? '').trim()) return required ? 'Заполните поле.' : ''

  const number = Number(String(value).replace(',', '.'))
  if (!Number.isFinite(number)) return 'Введите число.'
  if (number < minimum) return minimum > 0 ? 'Количество должно быть больше нуля.' : 'Значение не может быть отрицательным.'
  if (number > maxMaterialQuantity) return 'Не больше 999 999 999.'
  if (number !== Number(number.toFixed(decimals))) return decimals === 0 ? 'Введите целое количество.' : `Не больше ${decimals} знаков после запятой.`

  return ''
}

function getTodayDate() {
  return formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')
}

function getCurrentTime() {
  return new Intl.DateTimeFormat('en-GB', { timeZone: props.context.organization.timeZoneId || 'UTC', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(new Date())
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
  return new Intl.DateTimeFormat('ru-RU').format(parseDateOnly(value) ?? new Date(value))
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat('ru-RU', {
    timeZone: props.context.organization.timeZoneId || 'UTC',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  }).format(new Date(value))
}

function formatQuantity(value: number) {
  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 3 }).format(value)
}

function formatMoney(value?: number | null, maximumFractionDigits = 2) {
  if (value === null || value === undefined) {
    return '0 ₽'
  }

  return new Intl.NumberFormat('ru-RU', { style: 'currency', currency: 'RUB', maximumFractionDigits }).format(value)
}

function getMovementTypeName(typeCode: string) {
  switch (typeCode) {
    case 'receipt':
      return 'Приход'
    case 'issue':
      return 'Списание'
    case 'maintenance_issue':
      return 'Работа'
    case 'adjustment':
      return 'Корректировка'
    default:
      return 'Движение'
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
      return 'Приход'
    case 'issue':
    case 'maintenance_issue':
      return 'Расход'
    case 'adjustment':
      return 'Разница'
    default:
      return 'Изменение'
  }
}

function getMovementSourceKindName(movement: MaterialMovement) {
  if (movement.maintenanceId || movement.sourceEntityType === 'maintenance') {
    return 'Работа'
  }

  if (movement.sourceEntityType === 'manual') {
    return 'Ручная операция'
  }

  return movement.sourceEntityType || 'Склад'
}

function getMovementReason(movement: MaterialMovement) {
  if (movement.comment) {
    return movement.comment
  }

  switch (movement.typeCode) {
    case 'receipt':
      return 'Материал добавили на склад.'
    case 'issue':
      return 'Материал списали вручную.'
    case 'maintenance_issue':
      return 'Материал списался автоматически из карточки заказ-наряда.'
    case 'adjustment':
      return 'Остаток изменили корректировкой.'
    default:
      return 'Движение изменило складской остаток.'
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
    successMessage.value = 'Корректировка сохранена.'
  } catch (error) {
    showError(error instanceof Error ? error.message : 'Не удалось сохранить корректировку.')
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

    const message = error instanceof Error ? error.message : 'Не удалось загрузить склад.'
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
    showError('Выберите дату и время отчета.')
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
    successMessage.value = 'Приход добавлен.'
    activeView.value = 'stocks'
    await loadSection()
  } catch (error) {
    showError(error instanceof Error ? error.message : 'Не удалось добавить приход.')
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
    successMessage.value = 'Материал списан.'
    activeView.value = 'stocks'
    await loadSection()
  } catch (error) {
    showError(error instanceof Error ? error.message : 'Не удалось списать материал.')
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

    <UiLoader v-if="isLoading && (!inventory || !hasInlineLoading)" text="Загружаем склад..." />
    <div v-else-if="errorMessage" class="autoservice-state autoservice-state-error">{{ errorMessage }}</div>

    <template v-if="inventory && (!isLoading || hasInlineLoading)">
      <div v-if="successMessage" class="materials-notice" role="status">
        <span class="materials-notice__icon" aria-hidden="true">✓</span>
        <span>{{ successMessage }}</span>
        <button type="button" aria-label="Закрыть уведомление" @click="successMessage = ''">×</button>
      </div>
      <WorkspaceAccessNotice :can-manage-billing="context.organization.canManageBilling === true" v-if="readOnlyMessage" :message="readOnlyMessage" :href="billingHref" />
      <div v-if="activeView === 'stocks'" class="materials-overview">
        <div class="materials-stock-filters" role="group" aria-label="Состояние остатков">
          <button type="button" :aria-pressed="!stockFilters.filter && !stockFilters.search" @click="selectStockFilter('')">Все позиции <b>{{ inventory.allStocksCount ?? inventory.stocksTotalCount }}</b></button>
          <button type="button" class="materials-stock-filters__low" :aria-pressed="stockFilters.filter === 'low'" @click="selectStockFilter('low')">Ниже минимума <b>{{ inventory.lowStockCount }}</b></button>
          <button type="button" class="materials-stock-filters__out" :aria-pressed="stockFilters.filter === 'out'" @click="selectStockFilter('out')">Нет в наличии <b>{{ inventory.outOfStockCount }}</b></button>
        </div>
        <div class="materials-total-value"><span>Стоимость склада</span><strong>{{ formatMoney(inventory.totalInventoryValue) }}</strong></div>
      </div>
      <div class="materials-toolbar" :class="{ 'materials-toolbar--stocks': activeView === 'stocks' }">
        <UiButton v-if="activeView !== 'stocks'" variant="secondary" @click="openView(activeView === 'movement-detail' ? movementReturnView : 'stocks')">Назад</UiButton>
        <template v-if="activeView === 'stocks'">
          <UiButton variant="secondary" @click="openView('movements')">Движения</UiButton>
          <UiButton variant="secondary" @click="openView('snapshot')">Остатки на время</UiButton>
          <UiButton v-if="canWrite" variant="secondary" @click="openIssue()">Списание</UiButton>
          <UiButton :disabled="context.workspaceAccess?.isReadOnly === true && !context.organization.canManageBilling" v-else :href="billingHref" variant="secondary">Списание</UiButton>
          <UiButton v-if="canWrite" class="materials-receipt" @click="openView('receipt')">＋ Приход</UiButton>
          <UiButton :disabled="context.workspaceAccess?.isReadOnly === true && !context.organization.canManageBilling" v-else class="materials-receipt" :href="billingHref" variant="secondary">＋ Приход</UiButton>
        </template>
      </div>

      <section v-if="activeView === 'receipt'" class="materials-screen">
        <form novalidate class="materials-action-form materials-action-form--receipt" @submit.prevent="addReceipt">
          <div class="catalog-linked-field materials-form-wide">
            <UiSelect v-model="receiptForm.catalogItemId" label="Материал" :options="materialOptions" :disabled="isSaving || !canWrite" />
            <UiButton variant="secondary" href="#catalog?catalog=material" :disabled="isSaving">Справочник</UiButton>
          </div>
          <UiInput v-if="!receiptForm.catalogItemId" v-model="receiptForm.name" :maxlength="160" required label-help="До 160 символов" :error="validationAttempted ? receiptErrors.name : ''" label="Название нового материала" :disabled="isSaving || !canWrite" />
          <UiInput v-model="receiptForm.quantity" inputmode="decimal" :mask="receiptQuantityMask" required :label-help="isReceiptDiscrete ? 'Целое число больше 0' : 'Больше 0, до 3 знаков после запятой'" :error="validationAttempted ? receiptErrors.quantity : ''" label="Количество" :disabled="isSaving || !canWrite" />
          <UiSelect v-model="receiptForm.unit" required :error="validationAttempted ? receiptErrors.unit : ''" label="Единица измерения" :options="materialUnitOptions" :searchable="false" :disabled="isSaving || !canWrite" />
          <UiInput v-model="receiptForm.packageQuantity" inputmode="decimal" :mask="receiptPackageMask" :label-help="`Количество ${receiptBaseUnitLabel} в одной упаковке`" :error="validationAttempted ? receiptErrors.packageQuantity : ''" :label="`Единиц в упаковке (${receiptBaseUnitLabel})`" :disabled="isSaving || !canWrite" />
          <UiInput v-model="receiptForm.unitCost" inputmode="decimal" :mask="receiptCostMask" label-help="От 0 до 999 999 999 ₽, до 4 знаков после запятой" :error="validationAttempted ? receiptErrors.unitCost : ''" :label="receiptUnitCostLabel" :disabled="isSaving || !canWrite" />
          <div v-if="receiptTotalCost" class="materials-form-summary">
            <span>Сумма партии</span>
            <strong>{{ formatMoney(receiptTotalCost) }}</strong>
            <small>{{ formatQuantity(Number(receiptForm.quantity)) }} {{ receiptUnitLabel }} × {{ formatMoney(Number(receiptForm.unitCost), 4) }}</small>
          </div>
          <UiInput v-model="receiptForm.comment" :maxlength="1000" label-help="До 1000 символов" :error="validationAttempted ? receiptErrors.comment : ''" class="materials-form-wide" label="Комментарий" :disabled="isSaving || !canWrite" />
          <div class="materials-form-actions">
            <UiButton variant="secondary" @click="openView('stocks')">Отмена</UiButton>
            <UiButton v-if="canWrite" type="submit" :disabled="isSaving">Добавить приход</UiButton>
            <UiButton :disabled="context.workspaceAccess?.isReadOnly === true && !context.organization.canManageBilling" v-else :href="billingHref" variant="secondary">Добавить приход</UiButton>
          </div>
        </form>
      </section>

      <section v-else-if="activeView === 'adjustment' && selectedStock" class="materials-screen">
        <form novalidate class="materials-action-form" @submit.prevent="saveAdjustment">
          <div class="materials-selected-stock"><strong>{{ selectedStock.name }}</strong><span>Сейчас: {{ formatQuantity(selectedStock.quantityOnHand) }} {{ selectedStock.unit }} · {{ formatMoney(selectedStock.averageUnitCost, 8) }} за единицу</span></div>
          <UiInput v-model="adjustmentForm.quantity" inputmode="decimal" :mask="adjustmentQuantityMask" required :label-help="`Не меньше резерва: ${formatQuantity(selectedStock.reservedQuantity)} ${selectedStock.unit}`" :error="validationAttempted ? adjustmentErrors.quantity : ''" label="Фактический остаток" :disabled="isSaving || !canWrite" />
          <UiInput v-model="adjustmentForm.unitCost" inputmode="decimal" :mask="adjustmentCostMask" required label-help="От 0 до 999 999 999 ₽, до 8 знаков после запятой" :error="validationAttempted ? adjustmentErrors.unitCost : ''" label="Себестоимость единицы" :disabled="isSaving || !canWrite" />
          <UiInput v-model="adjustmentForm.reason" :maxlength="800" required label-help="До 800 символов" :error="validationAttempted ? adjustmentErrors.reason : ''" class="materials-form-wide" label="Причина корректировки" :disabled="isSaving || !canWrite" />
          <div class="materials-form-actions">
            <UiButton variant="secondary" :disabled="isSaving" @click="openView('stock-detail')">Отмена</UiButton>
            <UiButton v-if="canWrite" type="submit" :disabled="isSaving">Сохранить корректировку</UiButton>
          </div>
        </form>
      </section>

      <section v-else-if="activeView === 'issue'" class="materials-screen">
        <form novalidate class="materials-action-form materials-action-form--issue" @submit.prevent="issueMaterial">
          <template v-if="!issueStockLocked">
            <UiInput v-model="issueFilters.search" type="search" label="Поиск материала" placeholder="Название материала" :disabled="isSaving || !canWrite" />
            <UiSelect v-model="issueForm.stockId" required :error="validationAttempted ? issueErrors.stockId : ''" label="Позиция" :options="stockOptions" :searchable="false" :disabled="isSaving || isLoading || !canWrite" />
            <UiLoader v-if="isLoading" compact text="Обновляем позиции…" />
            <UiPagination class="materials-form-wide" :model-value="issueFilters.page" :total-items="inventory.stocksTotalCount" :items-per-page="issueFilters.pageSize" :max-pages-shown="3" :disabled="isLoading" label="Страницы позиций для списания" @update:model-value="changeIssuePage" />
          </template>
          <div v-if="selectedIssueStock" class="materials-selected-stock">
            <strong v-if="issueStockLocked">{{ selectedIssueStock.name }}</strong>
            <span>На складе: {{ formatQuantity(selectedIssueStock.quantityOnHand) }} {{ selectedIssueStock.unit }} · Доступно: {{ formatQuantity(selectedIssueStock.availableQuantity) }} {{ selectedIssueStock.unit }}</span>
          </div>
          <UiInput v-model="issueForm.quantity" inputmode="decimal" :mask="issueQuantityMask" required :label-help="isIssueDiscrete ? 'Целое число, не больше доступного остатка' : 'До 3 знаков после запятой, не больше доступного остатка'" :error="validationAttempted ? issueErrors.quantity : ''" :label="`Количество, ${issueUnitLabel}`" :disabled="isSaving || !canWrite" />
          <div v-if="selectedIssueStock" class="materials-issue-cost">
            <span>Себестоимость за {{ issueUnitLabel }}</span>
            <strong>{{ formatMoney(selectedIssueStock.averageUnitCost, 8) }}</strong>
            <small v-if="issueTotalCost">К списанию: <b>{{ formatMoney(issueTotalCost, 2) }}</b></small>
          </div>
          <UiInput v-model="issueForm.comment" :maxlength="1000" label-help="До 1000 символов" :error="validationAttempted ? issueErrors.comment : ''" class="materials-form-wide" label="Причина" :disabled="isSaving || !canWrite" />
          <div class="materials-form-actions">
            <UiButton variant="secondary" @click="openView('stocks')">Отмена</UiButton>
            <UiButton v-if="canWrite" type="submit" :disabled="isSaving || isLoading">Списать</UiButton>
            <UiButton :disabled="context.workspaceAccess?.isReadOnly === true && !context.organization.canManageBilling" v-else :href="billingHref" variant="secondary">Списать</UiButton>
          </div>
        </form>
      </section>

      <section v-else-if="activeView === 'stocks'" class="materials-screen" :aria-busy="isLoading">
        <form class="materials-filterbar materials-filterbar--stocks" @submit.prevent="applyStockFilters">
          <UiSearchInput v-model="stockFilters.search" class="materials-stock-search" id="material-stock-search" placeholder="Название материала" />
          <label class="materials-sr-only" for="material-stock-search">Поиск материалов</label>
          <UiSelect v-model="stockFilters.filter" aria-label="Остаток" :options="stockFilterOptions" :searchable="false" />
          <UiButton type="button" variant="secondary" :disabled="!stockFilters.search && !stockFilters.filter" @click="resetStockFilters">Сбросить</UiButton>
          <UiLoader v-if="isLoading" compact text="Обновляем остатки…" />
        </form>
        <div v-if="!inventory.stocks.length" class="autoservice-empty">По выбранным условиям позиций нет.</div>
        <div v-else class="materials-table materials-table--stocks">
          <div class="materials-table__head">
            <span>Материал</span><span>На складе</span><span>Доступно</span><span>Себестоимость за ед.</span><span>Стоимость остатка</span><span></span>
          </div>
          <article v-for="stock in inventory.stocks" :key="stock.id" class="materials-table__row" :class="{ 'materials-table__row--warning': stock.isLowStock, 'materials-table__row--danger': stock.isOutOfStock }">
            <div class="materials-stock-name">
              <strong>{{ stock.name }}</strong>
              <span v-if="stock.isOutOfStock" class="materials-stock-status materials-stock-status--out">Нет в наличии</span>
              <span v-else-if="stock.isLowStock" class="materials-stock-status materials-stock-status--low">Ниже минимума</span>
              <span>Минимум: {{ formatQuantity(stock.minQuantity) }} {{ stock.unit }}</span>
            </div>
            <div data-label="На складе"><strong>{{ formatQuantity(stock.quantityOnHand) }} {{ stock.unit }}</strong></div>
            <div data-label="Доступно">
              <strong>{{ formatQuantity(stock.availableQuantity) }} {{ stock.unit }}</strong>
              <span>В резерве: {{ formatQuantity(stock.reservedQuantity) }} {{ stock.unit }}</span>
            </div>
            <div data-label="Себестоимость за ед."><strong>{{ formatMoney(stock.averageUnitCost, 8) }} / {{ stock.unit }}</strong></div>
            <div data-label="Стоимость остатка"><strong>{{ formatMoney(stock.inventoryValue) }}</strong></div>
            <div class="materials-row-actions">
              <UiButton variant="secondary" :disabled="isLoading" @click="openStock(stock)">История</UiButton>
              <UiButton v-if="canWrite" variant="secondary" :disabled="isSaving || isLoading || stock.availableQuantity <= 0" @click="openIssue(stock)">Списать</UiButton>
              <UiButton v-else :disabled="!context.organization.canManageBilling || stock.availableQuantity <= 0" :href="billingHref" variant="secondary">Списать</UiButton>
            </div>
          </article>
        </div>
        <div v-if="inventory.stocksTotalCount" class="materials-pagination materials-pagination--stocks">
          <div class="materials-pagination__summary">
            <span aria-live="polite">{{ stocksFirstItem }}-{{ stocksLastItem }} из {{ inventory.stocksTotalCount }}</span>
            <div class="materials-page-size"><span>На странице</span><UiSelect v-model="selectedPageSize" :options="stockPageSizeOptions" :searchable="false" aria-label="Размер страницы" /></div>
          </div>
          <UiPagination :model-value="stockFilters.page" :total-items="inventory.stocksTotalCount" :items-per-page="stockFilters.pageSize" :max-pages-shown="3" :disabled="isLoading" label="Навигация по страницам склада" @update:model-value="changeStocksPage" />
        </div>
      </section>

      <section v-else-if="activeView === 'movements'" class="materials-screen">
        <div class="materials-filterbar">
          <UiDatePicker v-model="movementFilters.date" label="Дата движений" placeholder="Все даты" />
          <UiSelect v-model="movementFilters.type" label="Тип движения" :options="movementTypeOptions" :searchable="false" />
          <UiButton variant="secondary" :disabled="isLoading" @click="applyMovementFilters">Показать</UiButton>
        </div>
        <div class="materials-movement-list">
          <div v-if="!inventory.recentMovements.length" class="materials-empty">Движений по выбранным условиям нет.</div>
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
              <strong>{{ formatQuantity(movement.quantity) }} {{ movement.unit }}</strong>
              <span>{{ getMovementQuantityLabel(movement.typeCode) }} · {{ formatMoney(movement.totalCost) }}</span>
            </div>
            <div>
              <strong>{{ formatQuantity(movement.balanceAfter) }}</strong>
              <span>остаток после · {{ formatMoney(movement.costBalanceAfter) }}</span>
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
              <span>Изменение</span>
              <strong>{{ formatQuantity(selectedMovement.quantity) }} {{ selectedMovement.unit }}</strong>
              <small>{{ getMovementQuantityLabel(selectedMovement.typeCode) }} · {{ formatMoney(selectedMovement.totalCost) }}</small>
            </div>
            <div>
              <span>Себестоимость единицы</span>
              <strong>{{ formatMoney(selectedMovement.unitCost, 8) }}</strong>
              <small>на момент операции · {{ selectedMovement.unit }}</small>
            </div>
            <div>
              <span>Остаток после</span>
              <strong>{{ formatQuantity(selectedMovement.balanceAfter) }} {{ selectedMovement.unit }}</strong>
              <small>{{ formatMoney(selectedMovement.costBalanceAfter) }}</small>
            </div>
            <div>
              <span>Кто создал</span>
              <strong>{{ selectedMovement.createdByDisplayName || 'Не указан' }}</strong>
              <small>{{ selectedMovement.createdByUserId || 'старое или системное движение' }}</small>
            </div>
          </div>
          <div class="materials-movement-card__source">
            <span>Источник</span>
            <strong>{{ selectedMovement.sourceTitle || getMovementSourceKindName(selectedMovement) }}</strong>
            <small>{{ getMovementReason(selectedMovement) }}</small>
            <UiButton v-if="selectedMovement.sourceUrl" variant="secondary" :href="normalizeWorkspaceUrl(selectedMovement.sourceUrl)">Открыть источник</UiButton>
          </div>
        </div>
      </section>

      <section v-else-if="activeView === 'stock-detail' && selectedStock" class="materials-screen">
        <UiCard title="Остатки" class="materials-stock-card">
          <div class="materials-stock-heading">
            <h3>{{ selectedStock.name }}</h3>
            <span class="materials-stock-status" :class="selectedStock.isOutOfStock ? 'materials-stock-status--out' : selectedStock.isLowStock ? 'materials-stock-status--low' : 'materials-stock-status--available'">
              {{ selectedStock.isOutOfStock ? 'Нет в наличии' : selectedStock.isLowStock ? 'Ниже минимума' : 'В наличии' }}
            </span>
          </div>
          <div class="materials-stock-meta">
            <span>Обновлено {{ formatDate(selectedStock.updatedAt) }}</span>
            <span v-if="selectedStock.packageQuantity">Упаковка: {{ formatQuantity(selectedStock.packageQuantity) }} {{ selectedStock.unit }}</span>
          </div>
          <dl class="materials-stock-metrics">
            <div><dt>На складе</dt><dd>{{ formatQuantity(selectedStock.quantityOnHand) }} {{ selectedStock.unit }}</dd></div>
            <div><dt>Доступно</dt><dd>{{ formatQuantity(selectedStock.availableQuantity) }} {{ selectedStock.unit }}</dd></div>
            <div><dt>В резерве</dt><dd>{{ formatQuantity(selectedStock.reservedQuantity) }} {{ selectedStock.unit }}</dd></div>
            <div><dt>Минимум</dt><dd>{{ formatQuantity(selectedStock.minQuantity) }} {{ selectedStock.unit }}</dd></div>
            <div><dt>Себестоимость за {{ selectedStock.unit }}</dt><dd>{{ formatMoney(selectedStock.averageUnitCost, 8) }}</dd></div>
            <div><dt>Стоимость остатка</dt><dd>{{ formatMoney(selectedStock.inventoryValue) }}</dd></div>
          </dl>
          <div v-if="canWrite" class="materials-stock-actions">
            <UiButton :disabled="isSaving || selectedStock.availableQuantity <= 0" @click="openIssue(selectedStock)">Списать</UiButton>
            <UiButton variant="secondary" :disabled="isSaving" @click="openAdjustment">Корректировать остаток</UiButton>
          </div>
        </UiCard>

        <UiCard title="История движений" class="materials-stock-history">
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
                <strong>{{ formatQuantity(movement.quantity) }} {{ movement.unit }}</strong>
                <span>{{ getMovementQuantityLabel(movement.typeCode) }} · {{ formatMoney(movement.totalCost) }}</span>
              </div>
              <div>
                <strong>{{ formatQuantity(movement.balanceAfter) }} {{ movement.unit }}</strong>
                <span>Остаток после · {{ formatMoney(movement.costBalanceAfter) }}</span>
              </div>
            </article>
            <div v-if="selectedStockMovements.length === 0" class="autoservice-empty">По этому материалу пока нет движений.</div>
          </div>
        </UiCard>
      </section>

      <section v-else class="materials-screen">
        <div class="materials-filterbar materials-filterbar--snapshot">
          <UiDatePicker v-model="snapshotForm.date" label="Дата отчета" required />
          <UiTimePicker v-model="snapshotForm.time" label="Время" required />
          <UiButton variant="secondary" :disabled="isLoading" @click="loadSnapshot">Показать остатки</UiButton>
        </div>
        <div class="scheduler-summary">
          <span><b>{{ inventory.snapshotStocks.length }}</b> позиций</span>
          <span><b>{{ formatMoney(inventory.snapshotTotalInventoryValue) }}</b> стоимость</span>
          <span v-if="inventory.snapshotAt"><b>{{ formatDateTime(inventory.snapshotAt) }}</b> момент</span>
        </div>
        <div class="materials-table">
          <div class="materials-table__head materials-table__head--snapshot">
            <span>Позиция</span>
            <span>Остаток на момент</span>
            <span>Себестоимость</span>
            <span>Стоимость</span>
          </div>
          <article v-for="stock in inventory.snapshotStocks" :key="stock.stockId" class="materials-table__row materials-table__row--snapshot">
            <div class="materials-stock-name">
              <strong>{{ stock.name }}</strong>
              <span>{{ stock.unit }}</span>
            </div>
            <div>
              <strong>{{ formatQuantity(stock.quantityOnHand) }} {{ stock.unit }}</strong>
              <span>на выбранный момент</span>
            </div>
            <div>
              <strong>{{ formatMoney(stock.averageUnitCost, 8) }}</strong>
              <span>за единицу</span>
            </div>
            <div>
              <strong>{{ formatMoney(stock.inventoryValue) }}</strong>
              <span>остаток</span>
            </div>
          </article>
        </div>
      </section>
    </template>
    <UiAlertDialog
      :open="Boolean(errorDialogMessage)"
      title="Склад"
      :message="errorDialogMessage"
      tone="danger"
      @close="errorDialogMessage = ''"
    />
  </UiCard>
</template>



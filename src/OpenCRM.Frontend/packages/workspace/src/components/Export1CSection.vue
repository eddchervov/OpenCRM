<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { UiButton, UiDatePicker, UiLoader, formatDateOnly } from '@opencrm/ui'
import {
  downloadExport1C,
  loadExport1CPreview,
  loadExport1CCapabilities,
  type Export1CCapabilities,
  type Export1COptions,
  type Export1CPreview
} from '../api/export1C'
import type { WorkspaceContext } from '../types'
import WorkspaceProLock from './WorkspaceProLock.vue'

const props = defineProps<{
  context: WorkspaceContext
  initialDateFrom?: string
  initialDateTo?: string
  accountingExport?: boolean
}>()
const emit = defineEmits<{
  'date-range-change': [dateFrom: string, dateTo: string]
}>()

const today = new Date(`${formatDateOnly(undefined, props.context.organization.timeZoneId || 'UTC')}T12:00:00`)
const thirtyDaysAgo = new Date(today)
thirtyDaysAgo.setDate(today.getDate() - 29)

const filters = reactive({
  dateFrom: isDate(props.initialDateFrom) ? props.initialDateFrom! : toDateInput(thirtyDaysAgo),
  dateTo: isDate(props.initialDateTo) ? props.initialDateTo! : toDateInput(today)
})
const exportMode = ref<'bp3' | 'full'>('full')
const sections = reactive({
  orders: true,
  unfinishedOrders: false,
  payments: true,
  clients: true,
  vehicles: true,
  catalog: true,
  materials: true
})
const capabilities = ref<Export1CCapabilities | null>(null)
const isLoading = ref(true)
const isExporting = ref(false)
const errorMessage = ref('')
const exportMessage = ref('')
const preview = ref<Export1CPreview | null>(null)
const previewOptionsKey = ref('')
const isPreviewing = ref(false)
const previewError = ref('')
let capabilitiesRequest = 0
let previewRequest = 0
let downloadRequest = 0
let capabilitiesController: AbortController | null = null
let previewController: AbortController | null = null
let downloadController: AbortController | null = null
const isPreviewCurrent = computed(() => previewOptionsKey.value === JSON.stringify(exportOptions()))
const isAccountingExportLocked = computed(() => exportMode.value === 'bp3'
  && capabilities.value?.canExport === true
  && (capabilities.value.canExportBp3 === false
    || (capabilities.value.canExportBp3 === undefined && props.accountingExport === false)))
const hasSelectedContent = computed(() => sections.orders || sections.payments
  || (exportMode.value === 'full' && (sections.clients || sections.vehicles || sections.catalog || sections.materials)))

function isDate(value?: string): value is string {
  return Boolean(value && /^\d{4}-\d{2}-\d{2}$/.test(value))
}

function toDateInput(value: Date) {
  const year = value.getFullYear()
  const month = String(value.getMonth() + 1).padStart(2, '0')
  const day = String(value.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

async function loadCapabilities() {
  const request = ++capabilitiesRequest
  const organizationId = props.context.organization.id
  capabilitiesController?.abort()
  capabilitiesController = new AbortController()
  try {
    const result = await loadExport1CCapabilities(organizationId, capabilitiesController.signal)
    if (request !== capabilitiesRequest || props.context.organization.id !== organizationId) return
    capabilities.value = result
    sections.orders = result.canExportOrders
    sections.payments = result.canExportOrders
    sections.clients = result.canExportClients
    sections.vehicles = result.canExportVehicles
    sections.catalog = result.canExportCatalog
    sections.materials = result.canExportMaterials
  } catch (error) {
    if (request === capabilitiesRequest && props.context.organization.id === organizationId) {
      errorMessage.value = error instanceof Error ? error.message : 'Не удалось открыть выгрузку.'
    }
  } finally {
    if (request === capabilitiesRequest && props.context.organization.id === organizationId) isLoading.value = false
  }
}

function exportOptions(): Export1COptions {
  return {
    destinationCode: exportMode.value,
    dateFrom: filters.dateFrom,
    dateTo: filters.dateTo,
    timeZoneId: props.context.organization.timeZoneId || 'UTC',
    includeOrders: sections.orders,
    includeUnfinishedOrders: exportMode.value === 'full' && sections.orders && sections.unfinishedOrders,
    includePayments: sections.payments,
    includeClients: exportMode.value === 'full' && sections.clients,
    includeVehicles: exportMode.value === 'full' && sections.vehicles,
    includeCatalog: exportMode.value === 'full' && sections.catalog,
    includeMaterials: exportMode.value === 'full' && sections.materials
  }
}

async function checkData() {
  previewError.value = ''
  if (isAccountingExportLocked.value) return
  if (!filters.dateFrom || !filters.dateTo || filters.dateFrom > filters.dateTo) {
    previewError.value = 'Проверьте даты периода.'
    return
  }
  isPreviewing.value = true
  const request = ++previewRequest
  const organizationId = props.context.organization.id
  previewController?.abort()
  previewController = new AbortController()
  try {
    const options = exportOptions()
    const result = await loadExport1CPreview(organizationId, options, previewController.signal)
    if (request !== previewRequest || organizationId !== props.context.organization.id || isAccountingExportLocked.value) return
    preview.value = result
    previewOptionsKey.value = JSON.stringify(options)
  } catch (error) {
    if (request === previewRequest && organizationId === props.context.organization.id && !isAccountingExportLocked.value) {
      previewError.value = error instanceof Error ? error.message : 'Не удалось проверить данные.'
    }
  } finally {
    if (request === previewRequest) isPreviewing.value = false
  }
}

async function exportData() {
  exportMessage.value = ''
  if (isAccountingExportLocked.value) return
  if (!filters.dateFrom || !filters.dateTo || filters.dateFrom > filters.dateTo) {
    exportMessage.value = 'Проверьте даты периода.'
    return
  }

  isExporting.value = true
  const request = ++downloadRequest
  const organizationId = props.context.organization.id
  downloadController?.abort()
  downloadController = new AbortController()
  try {
    const issueCount = await downloadExport1C(organizationId, exportOptions(), downloadController.signal)
    if (request !== downloadRequest || organizationId !== props.context.organization.id || isAccountingExportLocked.value) return
    if (issueCount) {
      exportMessage.value = exportMode.value === 'bp3'
        ? `В файле ${issueCount} замечаний к данным. Нажмите «Проверить данные» и исправьте их перед загрузкой в 1С.`
        : `В файле ${issueCount} замечаний к данным. Нажмите «Проверить данные», чтобы просмотреть их.`
    }
  } catch (error) {
    if (request === downloadRequest && organizationId === props.context.organization.id && !isAccountingExportLocked.value) {
      exportMessage.value = error instanceof Error ? error.message : 'Не удалось сформировать выгрузку.'
    }
  } finally {
    if (request === downloadRequest) isExporting.value = false
  }
}

watch([() => filters.dateFrom, () => filters.dateTo], ([dateFrom, dateTo]) => emit('date-range-change', dateFrom, dateTo))
watch(exportMode, () => {
  previewRequest++
  downloadRequest++
  previewController?.abort()
  downloadController?.abort()
  preview.value = null
  previewOptionsKey.value = ''
  isPreviewing.value = false
  isExporting.value = false
})
watch(() => props.context.organization.id, () => {
  capabilitiesRequest++
  previewRequest++
  downloadRequest++
  capabilitiesController?.abort()
  previewController?.abort()
  downloadController?.abort()
  capabilities.value = null
  preview.value = null
  previewOptionsKey.value = ''
  errorMessage.value = ''
  isLoading.value = true
  isPreviewing.value = false
  isExporting.value = false
  void loadCapabilities()
})
onBeforeUnmount(() => {
  capabilitiesRequest++
  previewRequest++
  downloadRequest++
  capabilitiesController?.abort()
  previewController?.abort()
  downloadController?.abort()
})
onMounted(loadCapabilities)
</script>

<template>
  <div class="finance-export-page">
    <section class="finance-export-page__destination" aria-labelledby="export-mode-title">
      <h3 id="export-mode-title" class="ui-section-header">Назначение файла</h3>
      <div class="finance-export-page__mode">
        <label><input v-model="exportMode" type="radio" value="full" :disabled="!capabilities?.canExport" /> Расширенный JSON для другой системы</label>
        <label><input v-model="exportMode" type="radio" value="bp3" :disabled="!capabilities?.canExport" /> Для 1С:Бухгалтерии 3.0 <span v-if="capabilities?.canExport && (capabilities.canExportBp3 === false || (capabilities.canExportBp3 === undefined && accountingExport === false))" class="finance-export-page__pro-badge">PRO</span></label>
      </div>
    </section>

    <p v-if="!isLoading && (errorMessage || !capabilities?.canExport)" class="finance-export-page__error" role="alert">{{ errorMessage || 'Выгрузка недоступна для этой организации.' }}</p>
    <WorkspaceProLock
      v-else
      centered
      :locked="isAccountingExportLocked"
      message="Выгрузка в 1С доступна в PRO" feature="accountingExport"
      class="finance-export-page__locked-content"
      :class="{ 'finance-export-page__locked-content--locked': isAccountingExportLocked }"
    >
      <UiLoader v-if="isLoading" text="Проверяем доступ к выгрузке..." />
      <template v-else-if="capabilities?.canExport">
        <p class="finance-export-page__intro">{{ exportMode === 'bp3' ? 'Выгрузите документы для 1С:Бухгалтерии 3.0.' : 'Выгрузите выбранные данные в формате JSON для другой системы.' }}</p>
      <section class="finance-export-page__period" aria-labelledby="export-period-title">
        <h3 id="export-period-title" class="ui-section-header">Период выгрузки</h3>
        <div class="finance-date-form">
          <UiDatePicker v-model="filters.dateFrom" label="С" required />
          <UiDatePicker v-model="filters.dateTo" label="По" required />
        </div>
        <p>{{ exportMode === 'bp3' ? 'Заказы и оплаты' : 'Заказы, оплаты и движения по складу' }} попадут в файл за этот период.</p>
      </section>

      <section class="finance-export-page__content" aria-labelledby="export-content-title">
        <h3 id="export-content-title" class="ui-section-header">Что включить в файл</h3>
        <div class="finance-export__groups">
          <fieldset>
            <legend>{{ exportMode === 'bp3' ? 'Черновики в 1С' : 'Операции за период' }}</legend>
            <label v-if="capabilities.canExportOrders"><input v-model="sections.orders" type="checkbox" /> {{ exportMode === 'bp3' ? 'Завершённые заказы и работы' : 'Заказы и работы' }}</label>
            <label v-if="exportMode === 'full' && sections.orders" class="finance-export__nested"><input v-model="sections.unfinishedOrders" type="checkbox" /> Включить незавершённые заказы</label>
            <label v-if="capabilities.canExportOrders"><input v-model="sections.payments" type="checkbox" /> Оплаты и отмены</label>
            <label v-if="exportMode === 'full' && capabilities.canExportMaterials"><input v-model="sections.materials" type="checkbox" /> Движения по складу</label>
          </fieldset>
          <fieldset v-if="exportMode === 'full'">
            <legend>Актуальные данные</legend>
            <label v-if="capabilities.canExportClients"><input v-model="sections.clients" type="checkbox" /> Клиенты</label>
            <label v-if="capabilities.canExportVehicles"><input v-model="sections.vehicles" type="checkbox" /> Автомобили</label>
            <label v-if="capabilities.canExportCatalog"><input v-model="sections.catalog" type="checkbox" /> Услуги и материалы</label>
          </fieldset>
        </div>
        <p v-if="exportMode === 'full'">Справочники и остатки попадут в файл в текущем состоянии. Используйте его для обмена с другой системой.</p>
        <p v-else>Отменённые оплаты останутся в файле для сверки; обработка 1С не создаёт по ним новый платёж.</p>
      </section>

      <section class="finance-export-page__content" aria-labelledby="export-check-title">
        <h3 id="export-check-title" class="ui-section-header">Проверка перед выгрузкой</h3>
        <UiButton variant="secondary" :disabled="isPreviewing" @click="checkData">{{ isPreviewing ? 'Проверяем...' : 'Проверить данные' }}</UiButton>
        <p v-if="previewError" role="alert">{{ previewError }}</p>
        <template v-if="preview && isPreviewCurrent">
          <p>Заказов: {{ exportMode === 'bp3' ? (preview.completedOrdersCount ?? preview.ordersCount) : preview.ordersCount }} · оплат: {{ preview.paymentsCount }} · замечаний: {{ preview.validationIssues.length }}</p>
          <p v-if="exportMode === 'bp3' && (preview.ordersNeedingReviewCount || preview.paymentsNeedingReviewCount)">
            До загрузки проверьте заказы: {{ preview.ordersNeedingReviewCount }}, оплаты: {{ preview.paymentsNeedingReviewCount }}.
          </p>
          <ul v-if="preview.validationIssues.length" class="finance-export-page__issues">
            <li v-for="(issue, index) in preview.validationIssues" :key="`${issue.entityType}-${issue.entityId}-${index}`">
              <a v-if="issue.orderId" :href="`#maintenance/${issue.orderId}`">Заказ ↗</a>
              <span>{{ issue.message }}</span>
            </li>
          </ul>
          <p v-else>Замечаний нет.</p>
        </template>
      </section>

      <section v-if="exportMode === 'bp3'" class="finance-export-page__content" aria-labelledby="export-1c-title">
        <h3 id="export-1c-title" class="ui-section-header">Загрузка в 1С</h3>
        <p>В 1С откройте обработку через «Файл → Открыть». Выберите JSON, организацию и услугу; сопоставьте клиентов и создайте черновики. Перед проведением сверьте договоры, даты и оплаты.</p>
        <p>Исходники обработки входят в OpenCRM. Готовый файл для своей версии 1С предоставляет администратор установки.</p>
      </section>

      <div class="finance-export-page__footer">
        <UiButton
          :disabled="isExporting || !hasSelectedContent"
          @click="exportData"
        >{{ isExporting ? 'Формируем файл...' : 'Скачать файл JSON' }}</UiButton>
        <p v-if="exportMessage" role="status">{{ exportMessage }}</p>
      </div>
      </template>
    </WorkspaceProLock>
  </div>
</template>

<style scoped>
.finance-export-page { display: grid; gap: 16px; }
.finance-export-page__destination { display: grid; gap: 14px; padding: 16px; border: 1px solid var(--crm-line, #d8dee8); border-radius: 10px; background: var(--crm-surface, #fff); }
.finance-export-page__destination h3 { margin: 0; }
.finance-export-page__pro-badge { display: inline-flex; align-items: center; min-height: 18px; padding: 1px 6px; border-radius: 999px; background: color-mix(in srgb, var(--crm-accent, #e87532) 12%, transparent); color: var(--crm-accent, #c8551b); font-size: 10px; font-weight: 800; }
.finance-export-page__locked-content { min-height: 180px; }
.finance-export-page__locked-content--locked { max-height: min(420px, calc(88dvh - 250px)); overflow: hidden; }
.finance-export-page__locked-content > :deep(.workspace-pro-lock__overlay) { position: absolute; }
.finance-export-page__locked-content :deep(.workspace-pro-lock__content) { display: grid; gap: 16px; }
.finance-export-page__intro { margin: 0; color: var(--crm-text-muted, #667085); font-size: 13px; }
.finance-export-page__period,
.finance-export-page__content { display: grid; gap: 14px; padding: 16px; border: 1px solid var(--crm-line, #d8dee8); border-radius: 10px; background: var(--crm-surface, #fff); }
.finance-export-page__period h3,
.finance-export-page__content h3 { margin: 0; }
.finance-export-page__period p,
.finance-export-page__content p { margin: 0; color: var(--crm-text-muted, #667085); font-size: 13px; }
.finance-export-page__footer { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; }
.finance-export-page__footer p { margin: 0; }
.finance-export-page__issues { display: grid; gap: 8px; max-height: 280px; overflow: auto; padding-left: 20px; }
.finance-export-page__issues li { overflow-wrap: anywhere; }
.finance-export-page__issues a { margin-right: 8px; color: var(--crm-accent, #c6622b); }
</style>

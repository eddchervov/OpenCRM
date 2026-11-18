<script setup lang="ts">
import { formattingCulture, translate as t } from '@opencrm/ui/i18n'
import { provideWorkspaceCurrency, useWorkspaceCurrency } from '../workspaceCurrency'
import '../maintenanceCardMessages'
import '../workdayPreviewMessages'

import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { UiButton, UiLoader, parseDateOnly } from '@opencrm/ui'
import { loadMaintenance, loadMaintenancePhotos, type WorkspaceMaintenance, type WorkspaceMaintenancePhoto } from '../api/maintenance'
import type { WorkspaceWorkdayItem } from '../api/workday'
import type { WorkspaceContext } from '../types'
import { getVehicleBrandLogo } from '../utils/vehicleBrandLogo'

const inheritedCurrency = useWorkspaceCurrency()

const props = defineProps<{ context: WorkspaceContext, item: WorkspaceWorkdayItem }>()
const emit = defineEmits<{ close: [] }>()
const dialog = ref<HTMLDialogElement | null>(null)
const maintenance = ref<WorkspaceMaintenance | null>(null)
const workspaceCurrency = provideWorkspaceCurrency(computed(() => maintenance.value?.currencyCode ?? inheritedCurrency.value))
const isHistoricalCurrencyReadOnly = computed(() => {
  const orderCurrency = maintenance.value?.currencyCode
  const organizationCurrency = props.context.organization.currencyCode
  const currenciesDiffer = Boolean(orderCurrency && organizationCurrency
    && orderCurrency.toUpperCase() !== organizationCurrency.toUpperCase())

  return maintenance.value?.canChangeFinancialData === false || currenciesDiffer
})
const historicalCurrencyReadOnlyMessage = computed(() => t('maintenanceCard.historicalCurrencyReadOnly', {
  orderCurrency: maintenance.value?.currencyCode ?? inheritedCurrency.value,
  workspaceCurrency: props.context.organization.currencyCode ?? inheritedCurrency.value
}))
const photos = ref<WorkspaceMaintenancePhoto[]>([])
const activePanel = ref<'work' | 'photos' | 'finance'>('work')
const isLoading = ref(false)
const error = ref('')
const photoError = ref('')
const fullHref = computed(() => `#maintenance/${props.item.id}`)
const brandLogo = computed(() => getVehicleBrandLogo(props.item.vehicleBrandLogoKey))
const services = computed(() => maintenance.value?.maintenanceDetails.filter(detail => !(
  detail.materialCatalogItemId || detail.materialName || detail.materialQuantity != null
)) ?? [])
const totalAmount = computed(() => Math.max(0, (maintenance.value?.grossAmount ?? maintenance.value?.price ?? 0) - (maintenance.value?.discountAmount ?? 0)))
const balanceLabel = computed(() => maintenance.value?.paymentStatus === 'overpaid' ? t('workdayPreview.overpaid') : t('workdayPreview.balance'))
const balanceAmount = computed(() => maintenance.value?.paymentStatus === 'overpaid'
  ? Math.max(0, maintenance.value.paidAmount - totalAmount.value)
  : maintenance.value?.debtAmount ?? 0)
const completedCount = computed(() => Math.min(props.item.workTotalCount, Math.max(0, props.item.workCompletedCount)))
const status = computed(() => props.item.handedOverAt ? t('workdayPreview.handedOver')
  : props.item.readyForHandoverAt ? t('workdayPreview.ready')
    : maintenance.value?.status === 2 ? t('workdayPreview.inProgress')
      : maintenance.value?.status === 3 ? t('workdayPreview.completed')
        : maintenance.value?.status === 4 ? t('workdayPreview.cancelled') : t('workdayPreview.planned'))
let isDisposed = false
let previousOverflow = ''

function formatDate(value: string) {
  return parseDateOnly(value.slice(0, 10))?.toLocaleDateString(formattingCulture.value, { year: 'numeric', month: '2-digit', day: '2-digit' }) ?? ''
}

function formatMoney(amount: number) {
  return new Intl.NumberFormat(formattingCulture.value, { style: 'currency', currency: workspaceCurrency.value, maximumFractionDigits: 2 }).format(amount)
}

async function load() {
  isLoading.value = true
  error.value = ''
  try {
    const result = await loadMaintenance(props.context.organization.id, props.item.id)
    if (isDisposed) return
    maintenance.value = result
  } catch (cause) {
    if (!isDisposed) error.value = cause instanceof Error ? cause.message : t('workdayPreview.loadError')
  } finally {
    if (!isDisposed) isLoading.value = false
  }
}

async function loadPhotos() {
  try {
    const result = await loadMaintenancePhotos(props.context.organization.id, props.item.id)
    if (!isDisposed) photos.value = result
  } catch {
    if (!isDisposed) photoError.value = t('workdayPreview.photoError')
  }
}

function closeFromBackdrop(event: MouseEvent) {
  if (event.target !== dialog.value || !dialog.value) return
  const bounds = dialog.value.getBoundingClientRect()
  if (event.clientX >= bounds.left && event.clientX <= bounds.right
    && event.clientY >= bounds.top && event.clientY <= bounds.bottom) return
  emit('close')
}

onMounted(() => {
  previousOverflow = document.body.style.overflow
  document.body.style.overflow = 'hidden'
  dialog.value?.showModal()
  void load()
  void loadPhotos()
})

onBeforeUnmount(() => {
  isDisposed = true
  dialog.value?.close()
  document.body.style.overflow = previousOverflow
})
</script>

<template>
  <Teleport to="body">
    <dialog ref="dialog" class="workday-preview" aria-labelledby="workday-preview-title" @cancel.prevent="emit('close')" @click="closeFromBackdrop">
      <header class="workday-preview__header ui-form-header">
        <h2 id="workday-preview-title">{{ t('workdayPreview.title') }}</h2>
        <button type="button" class="workday-preview__close" :aria-label="t('workdayPreview.closePreview')" autofocus @click="emit('close')">×</button>
      </header>
      <div class="workday-preview__body">
        <div class="workday-preview__summary">
          <div class="ui-section-header"><h3><img v-if="brandLogo" :src="brandLogo.src" :alt="t('workdayPreview.logo', { name: brandLogo.name })" class="workday-preview__brand">{{ item.vehicleName }}</h3><span v-if="item.registrationPlate" class="workday-preview__plate">{{ item.registrationPlate }}</span></div>
          <span class="workday-preview__status">{{ status }}</span>
        </div>
        <p v-if="isHistoricalCurrencyReadOnly" class="workday-preview__readonly-notice" role="note">{{ historicalCurrencyReadOnlyMessage }}</p>
        <dl class="workday-preview__details">
          <div v-if="item.clientName"><dt>{{ t('workdayPreview.client') }}</dt><dd>{{ item.clientName }}</dd></div>
          <div><dt>{{ t('workdayPreview.date') }}</dt><dd>{{ formatDate(item.date) }}</dd></div>
          <div v-if="item.startTime"><dt>{{ t('workdayPreview.time') }}</dt><dd>{{ item.startTime.slice(0, 5) }}<template v-if="item.endTime"> — <template v-if="item.endDate && item.endDate.slice(0, 10) !== item.date.slice(0, 10)">{{ formatDate(item.endDate) }} </template>{{ item.endTime.slice(0, 5) }}</template></dd></div>
          <div v-if="item.location"><dt>{{ t('workdayPreview.location') }}</dt><dd>{{ item.location }}</dd></div>
          <div v-if="item.performer"><dt>{{ t('workdayPreview.employee') }}</dt><dd>{{ item.performer }}</dd></div>
        </dl>
        <nav class="workday-preview__tabs" :aria-label="t('workdayPreview.sections')">
          <button type="button" :aria-pressed="activePanel === 'work'" @click="activePanel = 'work'">{{ t('workdayPreview.work') }}</button>
          <button type="button" :aria-pressed="activePanel === 'photos'" @click="activePanel = 'photos'">{{ t('workdayPreview.photos') }}</button>
          <button type="button" :aria-pressed="activePanel === 'finance'" @click="activePanel = 'finance'">{{ t('workdayPreview.finance') }}</button>
        </nav>
        <UiLoader v-if="isLoading" :text="t('workdayPreview.loading')" />
        <div v-else-if="error" class="workday-preview__error" role="alert"><p>{{ error }}</p><UiButton variant="secondary" @click="load">{{ t('workdayPreview.retry') }}</UiButton></div>
        <template v-else-if="maintenance">
          <div v-if="activePanel === 'work'" class="workday-preview__work">
            <div>
              <ul class="workday-preview__services">
                <li v-for="(service, index) in services" :key="service.detailId ?? index">
                  <div><strong>{{ service.name }}</strong><span v-if="service.quantity != null">{{ t('workdayPreview.quantity', { count: service.quantity }) }}</span></div>
                  <span v-if="service.price != null">{{ formatMoney(service.price) }}</span>
                </li>
              </ul>
              <p v-if="!services.length">{{ t('workdayPreview.noServices') }}</p>
              <div v-if="item.workTotalCount > 0" class="workday-preview__progress">
                <progress :value="completedCount" :max="item.workTotalCount" :aria-label="t('workdayPreview.progress', { completed: completedCount, total: item.workTotalCount })" />
                <span>{{ t('workdayPreview.progress', { completed: completedCount, total: item.workTotalCount }) }}</span>
                <span v-if="item.workPausedCount">{{ t('workdayPreview.paused', { count: item.workPausedCount }) }}</span>
              </div>
              <p v-if="maintenance.comment" class="workday-preview__comment">{{ maintenance.comment }}</p>
            </div>
            <dl class="workday-preview__money">
              <div><dt>{{ t('workdayPreview.cost') }}</dt><dd>{{ formatMoney(totalAmount) }}</dd></div>
              <div><dt>{{ t('workdayPreview.paid') }}</dt><dd>{{ formatMoney(maintenance.paidAmount) }}</dd></div>
              <div><dt>{{ balanceLabel }}</dt><dd>{{ formatMoney(balanceAmount) }}</dd></div>
            </dl>
          </div>
          <div v-else-if="activePanel === 'photos'">
            <p v-if="maintenance.intakeNotes">{{ maintenance.intakeNotes }}</p>
            <p v-if="photoError" role="alert">{{ photoError }}</p>
            <div v-else-if="photos.length" class="workday-preview__photos">
              <figure v-for="photo in photos" :key="photo.photoId"><img :src="photo.url" :alt="photo.stage === 1 ? t('workdayPreview.before') : photo.stage === 2 ? t('workdayPreview.after') : t('workdayPreview.photo')" loading="lazy"><figcaption>{{ photo.stage === 1 ? t('workdayPreview.before') : photo.stage === 2 ? t('workdayPreview.after') : t('workdayPreview.photo') }}</figcaption></figure>
            </div>
            <p v-else>{{ t('workdayPreview.noPhotos') }}</p>
          </div>
          <dl v-else class="workday-preview__money">
            <div><dt>{{ t('workdayPreview.gross') }}</dt><dd>{{ formatMoney(maintenance.grossAmount) }}</dd></div>
            <div><dt>{{ t('workdayPreview.discount') }}</dt><dd>{{ formatMoney(maintenance.discountAmount) }}</dd></div>
            <div><dt>{{ t('workdayPreview.paid') }}</dt><dd>{{ formatMoney(maintenance.paidAmount) }}</dd></div>
            <div><dt>{{ balanceLabel }}</dt><dd>{{ formatMoney(balanceAmount) }}</dd></div>
          </dl>
        </template>
      </div>
      <footer class="workday-preview__footer"><UiButton variant="secondary" @click="emit('close')">{{ t('workdayPreview.close') }}</UiButton><UiButton :href="fullHref">{{ t('workdayPreview.openFull') }}</UiButton></footer>
    </dialog>
  </Teleport>
</template>

<style scoped>
.workday-preview { width: min(900px, calc(100vw - 40px)); max-height: calc(100dvh - 40px); padding: 0; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 12px; background: var(--crm-surface, #fff); color: var(--crm-text, #202733); box-shadow: 0 16px 64px var(--crm-shadow, #10182833); }
.workday-preview::backdrop { background: var(--crm-backdrop, #10182866); }
.workday-preview__header, .workday-preview__footer { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 14px 20px; }
.workday-preview__header { border-bottom: 1px solid var(--crm-form-header-border, #e3e6eb); }
.workday-preview__header h2 { margin: 0; font-size: 17px; }
.workday-preview__close { width: 32px; height: 32px; border: 0; border-radius: 6px; background: var(--crm-surface-soft, #f5f6f8); color: inherit; font-size: 24px; cursor: pointer; }
.workday-preview__body { padding: 20px; overflow-wrap: anywhere; }
.workday-preview__readonly-notice { margin: 12px 0; padding: 12px; border: 1px solid var(--crm-warning-border, #efd18a); border-radius: 8px; background: var(--crm-warning-bg, #fff7e6); color: var(--crm-warning-text, #755100); }
.workday-preview__summary { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.workday-preview__summary h3 { margin: 0 0 8px; font-size: 20px; }
.workday-preview__brand { width: 28px; height: 28px; margin-right: 8px; padding: 3px; vertical-align: middle; object-fit: contain; border-radius: 5px; background: #fff; }
.workday-preview__plate, .workday-preview__status { padding: 4px 8px; border-radius: 5px; background: var(--crm-surface-soft, #f5f6f8); font-size: 12px; }
.workday-preview__details { display: flex; flex-wrap: wrap; gap: 12px 24px; margin: 20px 0; font-size: 13px; }
.workday-preview dt { color: var(--crm-text-muted, #667085); }
.workday-preview dd { margin: 4px 0 0; }
.workday-preview__tabs { display: flex; flex-wrap: wrap; gap: 12px; margin: 0 0 18px; border-bottom: 1px solid var(--crm-line, #e3e6eb); }
.workday-preview__tabs button { padding: 10px 0; border: 0; border-bottom: 2px solid transparent; background: none; color: var(--crm-text-muted, #667085); font: inherit; font-size: 13px; cursor: pointer; }
.workday-preview__tabs button[aria-pressed="true"] { border-bottom-color: var(--opencrm-accent, #e67337); color: var(--crm-text, #202733); }
.workday-preview__work { display: grid; grid-template-columns: minmax(0, 1fr) 240px; gap: 24px; }
.workday-preview__services { list-style: none; padding: 0; margin: 0; }
.workday-preview__services li { display: flex; justify-content: space-between; gap: 12px; padding: 12px 0; border-bottom: 1px solid var(--crm-line, #e3e6eb); font-size: 13px; }
.workday-preview__services li > span { flex-shrink: 0; }
.workday-preview__services strong { font-weight: 600; }
.workday-preview__services div > span { display: block; margin-top: 4px; color: var(--crm-text-muted, #667085); font-size: 12px; }
.workday-preview__money { align-self: start; display: grid; gap: 12px; margin: 0; padding: 16px; border-radius: 8px; background: var(--crm-surface-soft, #f5f6f8); font-size: 13px; }
.workday-preview__money div { display: flex; justify-content: space-between; gap: 12px; }
.workday-preview__money dd { margin: 0; white-space: nowrap; }
.workday-preview__money div:last-child { padding-top: 12px; border-top: 1px solid var(--crm-line, #e3e6eb); font-weight: 700; }
.workday-preview__progress { display: grid; gap: 6px; margin-top: 16px; font-size: 12px; color: var(--crm-text-muted, #667085); }
.workday-preview__progress progress { width: 100%; height: 6px; appearance: none; border: 0; border-radius: 6px; overflow: hidden; background: var(--crm-line, #e3e6eb); }
.workday-preview__progress progress::-webkit-progress-bar { background: var(--crm-line, #e3e6eb); }
.workday-preview__progress progress::-webkit-progress-value { background: var(--opencrm-accent, #e67337); border-radius: 6px; }
.workday-preview__progress progress::-moz-progress-bar { background: var(--opencrm-accent, #e67337); border-radius: 6px; }
.workday-preview__comment { white-space: pre-wrap; font-size: 13px; }
.workday-preview__photos { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 12px; }
.workday-preview__photos figure { margin: 0; }
.workday-preview__photos img { display: block; width: 100%; height: 180px; object-fit: contain; border-radius: 6px; background: var(--crm-surface-soft, #f5f6f8); }
.workday-preview__photos figcaption { margin-top: 6px; font-size: 12px; color: var(--crm-text-muted, #667085); }
.workday-preview__footer { justify-content: flex-end; border-top: 1px solid var(--crm-line, #e3e6eb); }
.workday-preview button:focus-visible, .workday-preview a:focus-visible { outline: 2px solid var(--opencrm-accent, #e67337); outline-offset: 3px; }
@media (max-width: 640px) {
  .workday-preview { width: 100%; max-width: 100%; height: 100dvh; max-height: 100dvh; margin: 0; border: 0; border-radius: 0; }
  .workday-preview__work { grid-template-columns: minmax(0, 1fr); gap: 16px; }
  .workday-preview__body { padding: 16px; }
  .workday-preview__footer { flex-wrap: wrap; padding: 12px 16px; }
}
</style>

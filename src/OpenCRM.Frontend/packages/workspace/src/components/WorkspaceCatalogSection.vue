<script setup lang="ts">
import { formatLocaleCurrency, formattingCulture } from '@opencrm/ui/i18n'
import { useWorkspaceCurrency } from '../workspaceCurrency'

import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiInput, UiTextarea, UiMoneyInput, UiNumberInput, useLocalization } from '@opencrm/ui'
import '../catalogMessages'
import { formatServiceDuration } from '../serviceDuration'
import type { WorkspaceScenarioCatalogItem, WorkspaceScenarioCatalogKindCode } from '../api/scenarioCatalog'

const workspaceCurrency = useWorkspaceCurrency()
const { t } = useLocalization()

type CatalogGroup = {
  kind: WorkspaceScenarioCatalogKindCode
  title: string
  emptyText: string
  nameHelp: string
  descriptionHelp: string
}

const props = defineProps<{
  catalogGroups: CatalogGroup[]
  selectedCatalogKind: WorkspaceScenarioCatalogKindCode
  selectedCatalogGroup: CatalogGroup
  canManageCatalog: boolean
  isEditingCatalog: boolean
  isSaving: boolean
  actionErrorMessage: string
  catalogCreateForms: Record<string, { name: string, description: string, unitPrice: string, durationMinutes: string, serviceIds: string[] }>
  catalogDrafts: Record<string, {
    name: string
    description: string
    unitPrice: string
    durationMinutes: string
    serviceIds: string[]
    sortOrder: string
    isActive: boolean
  }>
  getCatalogItems: (kind: WorkspaceScenarioCatalogKindCode) => WorkspaceScenarioCatalogItem[]
  getActiveCatalogCount: (kind: WorkspaceScenarioCatalogKindCode) => number
}>()

const emit = defineEmits<{
  selectCatalogKind: [kind: WorkspaceScenarioCatalogKindCode]
  startCatalogEdit: []
  cancelCatalogEdit: []
  addCatalogItem: [kind: WorkspaceScenarioCatalogKindCode]
  saveCatalogItem: [item: WorkspaceScenarioCatalogItem]
  removeCatalogItem: [item: WorkspaceScenarioCatalogItem]
  changePostActivity: [item: WorkspaceScenarioCatalogItem]
  dialogOpenChanged: [open: boolean]
  clearCatalogError: []
}>()

const dialog = ref<HTMLDialogElement | null>(null)
const mode = ref<'create' | 'view' | 'edit' | null>(null)
const selectedItem = ref<WorkspaceScenarioCatalogItem | null>(null)
const pendingMutation = ref(false)
const serviceSearch = ref('')
const bundleValidationAttempted = ref(false)
const catalogSearch = ref('')
let trigger: HTMLElement | null = null
let previousOverflow = ''
let previousPaddingRight = ''
let isPageLocked = false

const isService = computed(() => props.selectedCatalogKind === 'service')
const isBundle = computed(() => props.selectedCatalogKind === 'service_bundle')
const groupTitleKeys: Partial<Record<WorkspaceScenarioCatalogKindCode, string>> = {
  service: 'catalogEditor.groupServices', service_bundle: 'catalogEditor.groupBundles', work_post: 'catalogEditor.groupPosts', material: 'catalogEditor.groupMaterials',
  work_type: 'catalogEditor.groupWorkTypes', document: 'catalogEditor.groupDocuments', planning_rule: 'catalogEditor.groupPlanningRules'
}
const groupEmptyKeys: Partial<Record<WorkspaceScenarioCatalogKindCode, string>> = {
  service: 'catalogEditor.emptyServices', service_bundle: 'catalogEditor.emptyBundles', work_post: 'catalogEditor.emptyPosts', material: 'catalogEditor.emptyMaterials',
  work_type: 'catalogEditor.emptyWorkTypes', document: 'catalogEditor.emptyDocuments', planning_rule: 'catalogEditor.emptyPlanningRules'
}
const groupTitle = (kind: WorkspaceScenarioCatalogKindCode) => t(groupTitleKeys[kind] ?? 'catalogEditor.groupOther')
const groupEmpty = (kind: WorkspaceScenarioCatalogKindCode) => t(groupEmptyKeys[kind] ?? 'catalogEditor.emptyOther')
const groupHelpKey = (kind: WorkspaceScenarioCatalogKindCode, field: 'name' | 'description') => {
  if (kind === 'service') return field === 'name' ? 'catalogEditor.serviceNameHelp' : 'catalogEditor.serviceDescriptionHelp'
  if (kind === 'service_bundle') return field === 'name' ? 'catalogEditor.bundleNameHelp' : 'catalogEditor.bundleDescriptionHelp'
  if (kind === 'work_post') return field === 'name' ? 'catalogEditor.postNameHelp' : 'catalogEditor.postDescriptionHelp'
  if (kind === 'material') return field === 'name' ? 'catalogEditor.materialNameHelp' : 'catalogEditor.materialDescriptionHelp'

  return field === 'name' ? 'catalogEditor.genericNameHelp' : 'catalogEditor.genericDescriptionHelp'
}
const title = computed(() => {
  if (mode.value === 'view') return selectedItem.value?.name ?? t('catalogEditor.itemName')
  if (isService.value) return t(mode.value === 'create' ? 'catalogEditor.createService' : 'catalogEditor.editService')
  if (isBundle.value) return t(mode.value === 'create' ? 'catalogEditor.createBundle' : 'catalogEditor.editBundle')
  if (props.selectedCatalogKind === 'work_post') return t(mode.value === 'create' ? 'catalogEditor.createPost' : 'catalogEditor.editPost')
  if (props.selectedCatalogKind === 'material') return t(mode.value === 'create' ? 'catalogEditor.createMaterial' : 'catalogEditor.editMaterial')

  return t(mode.value === 'create' ? 'catalogEditor.addItem' : 'catalogEditor.editItem')
})
const saveLabel = computed(() => {
  if (props.isSaving) return t('catalogEditor.saving')
  if (isService.value) return t(mode.value === 'create' ? 'catalogEditor.addService' : 'catalogEditor.saveService')
  if (isBundle.value) return t(mode.value === 'create' ? 'catalogEditor.addBundle' : 'catalogEditor.saveBundle')
  if (props.selectedCatalogKind === 'work_post') return t(mode.value === 'create' ? 'catalogEditor.addPost' : 'catalogEditor.savePost')
  if (props.selectedCatalogKind === 'material') return t(mode.value === 'create' ? 'catalogEditor.addMaterial' : 'catalogEditor.saveMaterial')

  return t(mode.value === 'create' ? 'catalogEditor.add' : 'catalogEditor.save')
})
const createLabel = computed(() => {
  if (isService.value) return t('catalogEditor.addService')
  if (isBundle.value) return t('catalogEditor.addBundle')
  if (props.selectedCatalogKind === 'work_post') return t('catalogEditor.addPost')
  if (props.selectedCatalogKind === 'material') return t('catalogEditor.addMaterial')

  return t('catalogEditor.addItem')
})
const catalogItems = computed(() => props.getCatalogItems(props.selectedCatalogKind))
const filteredCatalogItems = computed(() => catalogItems.value.filter(item => [item.name, item.description].filter(Boolean)
  .join(' ').toLocaleLowerCase(formattingCulture.value).includes(catalogSearch.value.trim().toLocaleLowerCase(formattingCulture.value))))

const form = computed(() => mode.value === 'create'
  ? props.catalogCreateForms[props.selectedCatalogKind]
  : selectedItem.value ? props.catalogDrafts[selectedItem.value.id] : null)

const availableServices = computed(() => props.getCatalogItems('service')
  .filter(service => service.isActive || (mode.value === 'edit' && form.value?.serviceIds.includes(service.id))))
const filteredServices = computed(() => availableServices.value.filter(service => service.name.toLocaleLowerCase(formattingCulture.value)
  .includes(serviceSearch.value.trim().toLocaleLowerCase(formattingCulture.value))))
const selectedServices = computed(() => (form.value?.serviceIds ?? []).map(id => props.getCatalogItems('service').find(service => service.id === id)))
const bundlePrice = computed(() => !selectedServices.value.length || selectedServices.value.some(service => service?.unitPrice == null)
  ? null : selectedServices.value.reduce((total, service) => total + service!.unitPrice!, 0))
const bundleDuration = computed(() => {
  const override = Number(form.value?.durationMinutes)
  if (override > 0 && Number.isFinite(override)) return override
  if (!selectedServices.value.length || selectedServices.value.some(service => !service?.durationMinutes)) return null

  return selectedServices.value.reduce((total, service) => total + service!.durationMinutes!, 0)
})
const hasUnavailableServices = computed(() => selectedServices.value.some(service => !service?.isActive))
const bundleValidationError = computed(() => bundleValidationAttempted.value && isBundle.value
  && (!form.value?.serviceIds.length || form.value.serviceIds.length > 100)
  ? t('catalogEditor.validationServices', { min: 1, max: 100 }) : '')
const money = (value: number) => `${formatLocaleCurrency(value, workspaceCurrency.value, { maximumFractionDigits: 2 })}`

function canEdit(item: WorkspaceScenarioCatalogItem) {
  return props.canManageCatalog && (!item.isUsed || ['service', 'service_bundle'].includes(item.kindCode))
}

function resetCreateForm() {
  Object.assign(props.catalogCreateForms[props.selectedCatalogKind], {
    name: '', description: '', unitPrice: '', durationMinutes: '', serviceIds: []
  })
}

function resetDraft(item: WorkspaceScenarioCatalogItem) {
  props.catalogDrafts[item.id] = {
    name: item.name,
    description: item.description ?? '',
    unitPrice: item.unitPrice == null ? '' : String(item.unitPrice),
    durationMinutes: item.durationMinutes == null ? '' : String(item.durationMinutes),
    serviceIds: [...(item.serviceIds ?? [])],
    sortOrder: String(item.sortOrder),
    isActive: item.isActive
  }
}

function openItem(item: WorkspaceScenarioCatalogItem, edit = false) {
  if (props.isSaving || (edit && !canEdit(item))) return
  if (mode.value === null) trigger = document.activeElement as HTMLElement
  emit('clearCatalogError')
  selectedItem.value = item
  bundleValidationAttempted.value = false
  serviceSearch.value = ''
  if (edit) resetDraft(item)
  mode.value = edit ? 'edit' : 'view'
}

function restorePage() {
  if (!isPageLocked) return
  document.body.style.overflow = previousOverflow
  document.body.style.paddingRight = previousPaddingRight
  isPageLocked = false
}

function closeDialog(force = false) {
  if (props.isSaving && !force) return
  if (mode.value === 'create') {
    resetCreateForm()
    emit('cancelCatalogEdit')
  }
  if (mode.value === 'edit' && selectedItem.value && !pendingMutation.value) resetDraft(selectedItem.value)
  dialog.value?.close()
  restorePage()
  mode.value = null
  selectedItem.value = null
  pendingMutation.value = false
  bundleValidationAttempted.value = false
  emit('clearCatalogError')
  emit('dialogOpenChanged', false)
  if (trigger?.isConnected) trigger.focus({ preventScroll: true })
  trigger = null
}

function submit() {
  if (props.isSaving || !props.canManageCatalog) return
  bundleValidationAttempted.value = true
  if (bundleValidationError.value) return
  pendingMutation.value = true
  if (mode.value === 'create') emit('addCatalogItem', props.selectedCatalogKind)
  else if (selectedItem.value && canEdit(selectedItem.value)) emit('saveCatalogItem', selectedItem.value)
}

function removeItem() {
  const item = selectedItem.value
  if (!item || props.isSaving || !canEdit(item)) return
  closeDialog()
  emit('removeCatalogItem', item)
}

function serviceName(id: string) {
  return props.getCatalogItems('service').find(service => service.id === id)?.name ?? t('catalogEditor.unavailableService')
}

function statusLabel(item: WorkspaceScenarioCatalogItem) {
  if (item.isActive) return t('catalogEditor.active')

  return item.kindCode === 'service' ? t('catalogEditor.archived') : t('catalogEditor.disabled')
}

watch(() => props.isEditingCatalog, editing => {
  if (editing) {
    bundleValidationAttempted.value = false
    serviceSearch.value = ''
    trigger = document.activeElement as HTMLElement
    emit('clearCatalogError')
    resetCreateForm()
    selectedItem.value = null
    mode.value = 'create'
  } else if (mode.value === 'create' && !props.isSaving) closeDialog()
})

watch(mode, async value => {
  if (value === null) return
  emit('dialogOpenChanged', true)
  await nextTick()
  if (!dialog.value || mode.value === null) return
  if (!dialog.value.open) {
    previousOverflow = document.body.style.overflow
    previousPaddingRight = document.body.style.paddingRight
    const scrollbarWidth = window.innerWidth - document.documentElement.clientWidth
    if (scrollbarWidth > 0) {
      document.body.style.paddingRight = `${parseFloat(getComputedStyle(document.body).paddingRight) + scrollbarWidth}px`
    }
    document.body.style.overflow = 'hidden'
    isPageLocked = true
    dialog.value.showModal()
  }
  if (value !== 'view') dialog.value.querySelector<HTMLInputElement>('input:not(:disabled)')?.focus()
})

watch(() => props.isSaving, (saving, wasSaving) => {
  if (!saving && wasSaving && pendingMutation.value && !props.actionErrorMessage) closeDialog()
})

watch(() => props.selectedCatalogKind, () => {
  catalogSearch.value = ''
  closeDialog(true)
})
watch(() => props.canManageCatalog, allowed => {
  if (!allowed && mode.value !== 'view') closeDialog(true)
})
onBeforeUnmount(() => closeDialog(true))
</script>

<template>
  <div class="workspace-common-stack workspace-catalog">
    <section class="workspace-common-subpanel workspace-catalog-picker">
      <div class="workspace-catalog-switcher" role="group" :aria-label="t('catalogEditor.type')">
        <button
          v-for="group in catalogGroups"
          :key="group.kind"
          type="button"
          :disabled="isSaving"
          :aria-pressed="selectedCatalogKind === group.kind"
          :class="{ active: selectedCatalogKind === group.kind }"
          @click="$emit('selectCatalogKind', group.kind)"
        >
          <span>{{ groupTitle(group.kind) }}</span>
          <strong>{{ getActiveCatalogCount(group.kind) }} / {{ getCatalogItems(group.kind).length }}</strong>
        </button>
      </div>
    </section>

    <section class="workspace-common-subpanel workspace-catalog-panel">
      <div class="workspace-catalog-heading">
        <div>
          <h3 class="ui-section-header">{{ groupTitle(selectedCatalogKind) }}</h3>
          <span>{{ t('catalogEditor.activeCount', { active: getActiveCatalogCount(selectedCatalogKind), total: getCatalogItems(selectedCatalogKind).length }) }}</span>
        </div>
        <UiInput v-model="catalogSearch" class="catalog-search" type="search" :label="t('catalogEditor.search')" :placeholder="t('catalogEditor.searchPlaceholder')" />
        <UiButton v-if="canManageCatalog" class="catalog-add" variant="secondary" :disabled="isSaving" @click="$emit('startCatalogEdit')">
          {{ createLabel }}
        </UiButton>
      </div>

      <div v-if="getCatalogItems(selectedCatalogKind).length === 0" class="workspace-common-empty">
          {{ groupEmpty(selectedCatalogKind) }}
      </div>
      <div v-else-if="!filteredCatalogItems.length" class="workspace-common-empty" role="status">{{ t('catalogEditor.noSearchResults') }}</div>
      <div v-else class="workspace-catalog-list">
        <article
          v-for="item in filteredCatalogItems"
          :key="item.id"
          class="workspace-catalog-item workspace-catalog-item--view catalog-row"
          :class="{ 'workspace-catalog-item--inactive': !item.isActive }"
        >
          <div class="catalog-row__content">
            <strong>{{ item.name }}</strong>
            <div class="catalog-row__meta">
              <span v-if="item.kindCode === 'service' && item.unitPrice != null">{{ formatLocaleCurrency(item.unitPrice, workspaceCurrency) }}</span>
              <span v-if="item.kindCode === 'service_bundle'">{{ t('catalogEditor.serviceCount', { count: item.serviceIds?.length ?? 0 }) }}</span>
              <span v-if="item.durationMinutes != null">{{ formatServiceDuration(item.durationMinutes) }}</span>
              <span>{{ t('catalogEditor.order') }}: {{ item.sortOrder }}</span>
            </div>
          </div>
          <div class="catalog-row__badges">
            <span class="catalog-row__status" :class="{ inactive: !item.isActive }">
              {{ statusLabel(item) }}
            </span>
            <span v-if="item.isUsed" class="catalog-row__used">{{ t('catalogEditor.usedInData') }}</span>
          </div>
          <div class="catalog-row__actions">
            <UiButton variant="secondary" :disabled="isSaving" @click="openItem(item)">{{ t('catalogEditor.details') }}</UiButton>
            <UiButton
              v-if="canManageCatalog && item.kindCode === 'work_post' && item.isUsed"
              variant="secondary"
              :disabled="isSaving"
              @click="$emit('changePostActivity', item)"
            >{{ t(item.isActive ? 'catalogEditor.closePost' : 'catalogEditor.restorePost') }}</UiButton>
            <UiButton v-if="canEdit(item)" variant="secondary" :disabled="isSaving" @click="openItem(item, true)">{{ t('catalogEditor.edit') }}</UiButton>
          </div>
        </article>
      </div>
    </section>
  </div>

  <Teleport to="body">
    <dialog
      ref="dialog"
      class="catalog-dialog"
      :class="{ 'catalog-dialog--bundle': isBundle }"
      aria-labelledby="catalog-dialog-title"
      @cancel.prevent="closeDialog()"
    >
      <header class="catalog-dialog__header ui-form-header">
        <div>
          <small>{{ groupTitle(selectedCatalogKind) }}</small>
          <h2 id="catalog-dialog-title">{{ title }}</h2>
        </div>
        <button type="button" class="catalog-dialog__close" :aria-label="t('catalogEditor.close')" :disabled="isSaving" @click="closeDialog()">×</button>
      </header>

      <div class="catalog-dialog__body">
        <template v-if="mode === 'view' && selectedItem">
          <dl class="catalog-details">
            <div><dt>{{ t('catalogEditor.status') }}</dt><dd>{{ statusLabel(selectedItem) }}</dd></div>
            <div v-if="selectedItem.kindCode === 'service' && selectedItem.unitPrice != null"><dt>{{ t('catalogEditor.price') }}</dt><dd>{{ formatLocaleCurrency(selectedItem.unitPrice, workspaceCurrency) }}</dd></div>
            <div v-if="selectedItem.durationMinutes != null"><dt>{{ t('catalogEditor.duration') }}</dt><dd>{{ formatServiceDuration(selectedItem.durationMinutes) }}</dd></div>
            <div><dt>{{ t('catalogEditor.order') }}</dt><dd>{{ selectedItem.sortOrder }}</dd></div>
          </dl>
          <div v-if="selectedItem.description" class="catalog-details__section">
            <h3 class="ui-section-header">{{ t('catalogEditor.description') }}</h3>
            <p>{{ selectedItem.description }}</p>
          </div>
          <div v-if="selectedItem.kindCode === 'service_bundle'" class="catalog-details__section">
            <h3 class="ui-section-header">{{ t('catalogEditor.bundleServices') }}</h3>
            <ul v-if="selectedItem.serviceIds?.length"><li v-for="id in selectedItem.serviceIds" :key="id">{{ serviceName(id) }}</li></ul>
            <p v-else>{{ t('catalogEditor.noSelectedServices') }}</p>
          </div>
          <p v-if="selectedItem.isUsed" class="catalog-dialog__note">{{ t(canEdit(selectedItem) ? 'catalogEditor.usedReadOnly' : 'catalogEditor.usedCannotEdit') }}</p>
        </template>

        <form v-else-if="form" id="catalog-dialog-form" class="catalog-dialog__form" @submit.prevent="submit">
          <UiInput v-model="form.name" id="catalog-item-name" for="catalog-item-name" :label="t('catalogEditor.itemName')" :label-help="t(groupHelpKey(selectedCatalogKind, 'name'))" :disabled="isSaving" required />
          <UiTextarea v-model="form.description" id="catalog-item-description" for="catalog-item-description" :label="t('catalogEditor.itemDescription')" :label-help="t(groupHelpKey(selectedCatalogKind, 'description'))" :rows="3" :disabled="isSaving" />
          <div class="catalog-dialog__fields">
            <UiMoneyInput v-if="selectedCatalogKind === 'service'" v-model="form.unitPrice" :label="t('catalogEditor.priceCurrency', { currency: workspaceCurrency })" :min="0" :max="999999999" :step="0.01" :disabled="isSaving" />
            <UiNumberInput v-if="isService || isBundle" v-model="form.durationMinutes" id="catalog-item-duration" for="catalog-item-duration" :label="t(isBundle ? 'catalogEditor.bundleDuration' : 'catalogEditor.serviceDuration')" :label-help="t('catalogEditor.durationHelp')" :min="1" :max="525600" :step="1" integer :placeholder="t(isBundle ? 'catalogEditor.durationFromServices' : 'catalogEditor.durationUnset')" :disabled="isSaving" />
            <UiInput v-if="mode === 'edit' && selectedItem" v-model="catalogDrafts[selectedItem.id].sortOrder" :label="t('catalogEditor.order')" type="number" :disabled="isSaving" />
            <label v-if="mode === 'edit' && selectedItem" class="catalog-dialog__active">
              <input v-model="catalogDrafts[selectedItem.id].isActive" type="checkbox" :disabled="isSaving" />
              <span>{{ t(isService ? 'catalogEditor.serviceActive' : 'catalogEditor.inactiveCheckbox') }}</span>
            </label>
          </div>
          <section v-if="isBundle" class="catalog-bundle" :aria-label="t('catalogEditor.bundleContents')">
            <div class="catalog-bundle__heading ui-section-header"><h3>{{ t('catalogEditor.bundleContents') }} <span>{{ form.serviceIds.length }}</span></h3>
              <UiInput v-model="serviceSearch" type="search" :label="t('catalogEditor.serviceSearch')" :placeholder="t('catalogEditor.findService')" :disabled="isSaving" />
            </div>
            <div class="catalog-bundle__columns" aria-hidden="true"><span>{{ t('catalogEditor.service') }}</span><span>{{ t('catalogEditor.serviceDurationColumn') }}</span><span>{{ t('catalogEditor.servicePriceColumn') }}</span></div>
            <div class="catalog-bundle__services">
              <label v-for="service in filteredServices" :key="service.id" class="catalog-bundle__service" :class="{ 'catalog-bundle__service--selected': form.serviceIds.includes(service.id) }">
                <input v-model="form.serviceIds" type="checkbox" :value="service.id" :disabled="isSaving" :aria-label="service.name" />
                <span>{{ service.name }}<small v-if="!service.isActive">{{ t('catalogEditor.archived') }}</small></span>
                <span>{{ service.durationMinutes ? formatServiceDuration(service.durationMinutes) : t('catalogEditor.durationUnset') }}</span>
                <span>{{ service.unitPrice == null ? t('catalogEditor.durationUnset') : money(service.unitPrice) }}</span>
              </label>
              <p v-if="!availableServices.length" class="catalog-bundle__empty">{{ t('catalogEditor.noAvailableServices') }}</p>
              <p v-else-if="!filteredServices.length" class="catalog-bundle__empty" role="status">{{ t('catalogEditor.noServicesFound') }}</p>
            </div>
            <dl class="catalog-bundle__summary" :aria-label="t('catalogEditor.bundleSummary')">
              <div><dt>{{ t('catalogEditor.selectedServices') }}</dt><dd>{{ form.serviceIds.length }}</dd></div>
              <div><dt>{{ t('catalogEditor.priceFromServices') }}</dt><dd>{{ !form.serviceIds.length ? '—' : bundlePrice == null ? t('catalogEditor.unknownPrices') : money(bundlePrice) }}</dd></div>
              <div><dt>{{ t('catalogEditor.bundleDurationSummary') }}</dt><dd>{{ bundleDuration ? formatServiceDuration(bundleDuration) : form.serviceIds.length ? t('catalogEditor.durationIncomplete') : '—' }}</dd><small>{{ t(form.durationMinutes ? 'catalogEditor.durationCustom' : 'catalogEditor.durationSum') }}</small></div>
            </dl>
            <p v-if="hasUnavailableServices" class="catalog-bundle__warning" role="status">{{ t('catalogEditor.unavailableBundleWarning') }}</p>
          </section>
          <p v-if="bundleValidationError || actionErrorMessage" class="catalog-dialog__error" role="alert">{{ bundleValidationError || actionErrorMessage }}</p>
        </form>
      </div>

      <footer class="catalog-dialog__footer">
        <UiButton
          v-if="mode === 'edit' && selectedItem && (selectedItem.kindCode === 'service' ? selectedItem.isActive : !selectedItem.isSystem)"
          variant="secondary"
          class="catalog-dialog__remove"
          :disabled="isSaving"
          @click="removeItem"
        >{{ t(selectedItem.kindCode === 'service' ? 'catalogEditor.archive' : selectedItem.isUsed ? 'catalogEditor.deactivate' : 'catalogEditor.delete') }}</UiButton>
        <UiButton variant="secondary" :disabled="isSaving" @click="closeDialog()">{{ t(mode === 'view' ? 'catalogEditor.close' : 'catalogEditor.cancel') }}</UiButton>
        <UiButton v-if="mode === 'view' && selectedItem && canEdit(selectedItem)" :disabled="isSaving" @click="openItem(selectedItem, true)">{{ t('catalogEditor.edit') }}</UiButton>
        <UiButton v-else-if="mode !== 'view'" type="submit" form="catalog-dialog-form" :disabled="isSaving">
          {{ saveLabel }}
        </UiButton>
      </footer>
    </dialog>
  </Teleport>
</template>

<style scoped>
.workspace-catalog .catalog-row { grid-template-columns: minmax(0, 1fr) auto auto; align-items: center; gap: 12px; }
.catalog-row__content { display: grid; min-width: 0; gap: 4px; }
.catalog-row__content strong { overflow-wrap: anywhere; font-size: 14px; }
.catalog-row__meta { display: flex; flex-wrap: wrap; gap: 4px 14px; color: var(--crm-text-muted, #667085); font-size: 12px; }
.catalog-row__status { padding: 3px 8px; border-radius: 5px; background: var(--crm-surface-soft, #f7f8fa); font-size: 12px; white-space: nowrap; }
.catalog-row__status.inactive { color: var(--crm-text-muted, #667085); }
.catalog-row__badges { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; }
.catalog-row__used { padding: 3px 8px; border: 1px solid color-mix(in srgb, var(--crm-accent, #e87532) 28%, var(--crm-line, #d9e0e9)); border-radius: 5px; background: color-mix(in srgb, var(--crm-accent, #e87532) 10%, var(--crm-surface, #fff)); color: var(--crm-accent, #c85b1c); font-size: 12px; white-space: nowrap; }
.catalog-row__actions { display: flex; flex-wrap: wrap; gap: 6px; }
.catalog-row__actions .ui-button { min-height: 32px; padding: 5px 10px; font-size: 12px; }
.workspace-catalog-heading .catalog-search { flex: 1 1 230px; max-width: 360px; min-width: 0; margin-left: auto; }
.workspace-catalog-heading .catalog-add { align-self: flex-end; }
.catalog-row__status { display: inline-flex; align-items: center; gap: 6px; }
.catalog-row__status::before { content: ''; width: 6px; height: 6px; flex-shrink: 0; border-radius: 50%; background: #309f86; }
.catalog-row__status.inactive::before { background: #8793a2; }
.catalog-dialog { box-sizing: border-box; width: min(640px, calc(100% - 32px)); max-width: none; max-height: calc(100dvh - 32px); padding: 0; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 8px; background: var(--crm-surface, #fff); color: var(--crm-text, #202833); box-shadow: 0 22px 58px var(--crm-shadow, rgba(15, 23, 42, .26)); overflow: visible; }
.catalog-dialog[open] { display: flex; flex-direction: column; }
.catalog-dialog--bundle { width: min(900px, calc(100% - 32px)); }
.catalog-dialog::backdrop { background: var(--crm-backdrop, rgba(18, 18, 18, .56)); }
.catalog-dialog__header { display: flex; flex: 0 0 auto; align-items: center; justify-content: space-between; gap: 16px; padding: 16px 20px; border-bottom: 1px solid var(--crm-form-header-border, #d9e0e9); }
.catalog-dialog__header small { color: var(--crm-text-muted, #667085); font-size: 12px; }
.catalog-dialog__header h2 { margin: 4px 0 0; font-size: 18px; overflow-wrap: anywhere; }
.catalog-dialog__close { flex: 0 0 auto; width: 32px; height: 32px; border: 0; border-radius: 6px; background: var(--crm-surface-soft, #f7f8fa); color: inherit; font-size: 22px; cursor: pointer; }
.catalog-dialog__body { min-height: 0; overflow-y: auto; overscroll-behavior: contain; padding: 20px; }
.catalog-dialog :deep(textarea) { font: inherit; font-size: 14px; }
.catalog-dialog__form { display: grid; gap: 16px; }
.catalog-dialog__fields { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); align-items: end; gap: 16px; }
.catalog-dialog__fields > *, .catalog-dialog :deep(.ui-field) { min-width: 0; }
.catalog-dialog__active { display: flex; justify-self: start; width: fit-content; max-width: 100%; min-height: 38px; align-items: center; gap: 8px; font-size: 14px; cursor: pointer; }
.catalog-dialog input[type='checkbox'] { accent-color: var(--crm-accent, #e77924); }
.catalog-dialog__footer { display: flex; flex: 0 0 auto; flex-wrap: wrap; justify-content: flex-end; gap: 8px; padding: 14px 20px; border-top: 1px solid var(--crm-line, #d9e0e9); }
.catalog-dialog__footer :deep(.ui-button--primary) { border-color: #e87532; background: #e87532; color: #fff; }
.catalog-dialog__remove { margin-right: auto; }
.catalog-dialog__error { margin: 0; color: var(--crm-danger-text, #b42318); font-size: 14px; }
.catalog-dialog__note { margin: 12px 0 0; color: var(--crm-text-muted, #667085); font-size: 13px; }
.catalog-details { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; margin: 0; }
.catalog-details dt { margin-bottom: 4px; color: var(--crm-text-muted, #667085); font-size: 12px; }
.catalog-details dd { margin: 0; font-size: 14px; }
.catalog-details > div { min-width: 0; padding: 12px; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 6px; background: var(--crm-surface, #fff); }
.catalog-details dd { overflow-wrap: anywhere; }
.catalog-details__section { margin-top: 20px; }
.catalog-details__section h3 { margin: 0 0 8px; font-size: 14px; }
.catalog-details__section p { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; font-size: 14px; line-height: 1.6; }
.catalog-details__section ul { margin: 0; padding-left: 20px; font-size: 14px; line-height: 1.6; overflow-wrap: anywhere; }
.catalog-bundle { min-width: 0; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 8px; overflow: hidden; }
.catalog-bundle__heading { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 12px; padding: 14px; background: var(--crm-section-header-bg, #f7f8fa); }
.catalog-bundle__heading h3 { display: flex; align-items: center; gap: 8px; margin: 0; font-size: 15px; }
.catalog-bundle__heading h3 span { padding: 3px 7px; border-radius: 5px; background: var(--crm-surface, #fff); font-size: 12px; }
.catalog-bundle__heading :deep(.ui-field) { flex: 1 1 200px; max-width: 300px; }
.catalog-bundle__columns { display: grid; grid-template-columns: minmax(0, 1fr) 120px 120px; gap: 12px; padding: 10px 14px 10px 44px; font-size: 12px; color: var(--crm-text-muted, #667085); border-block: 1px solid var(--crm-line, #d9e0e9); }
.catalog-bundle__columns > :last-child { text-align: right; }
.catalog-bundle__services { max-height: 300px; overflow-y: auto; scrollbar-gutter: stable; }
.catalog-bundle__service { display: grid; grid-template-columns: 18px minmax(0, 1fr) 120px 120px; gap: 12px; align-items: center; padding: 12px 14px; border-bottom: 1px solid var(--crm-line, #d9e0e9); font-size: 13px; cursor: pointer; }
.catalog-bundle__service:last-child { border-bottom: 0; }
.catalog-bundle__service--selected { background: var(--crm-surface-soft, #f7f8fa); }
.catalog-bundle__service > span { min-width: 0; overflow-wrap: anywhere; }
.catalog-bundle__service > :last-child { text-align: right; }
.catalog-bundle__service input { width: 17px; height: 17px; margin: 0; }
.catalog-bundle__service small { display: block; margin-top: 4px; color: var(--crm-text-muted, #667085); font-size: 11px; }
.catalog-bundle__empty { margin: 0; padding: 20px 14px; font-size: 13px; color: var(--crm-text-muted, #667085); }
.catalog-bundle__summary { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px; margin: 0; padding: 16px 14px; border-top: 1px solid var(--crm-line, #d9e0e9); background: var(--crm-surface-soft, #f7f8fa); }
.catalog-bundle__summary > div { min-width: 0; }
.catalog-bundle__summary dt, .catalog-bundle__summary small { color: var(--crm-text-muted, #667085); font-size: 12px; }
.catalog-bundle__summary dd { margin: 6px 0; font-weight: 600; font-size: 16px; overflow-wrap: anywhere; }
.catalog-bundle__warning { margin: 0; padding: 12px 14px; border-top: 1px solid var(--crm-line, #d9e0e9); font-size: 12px; color: var(--crm-danger-text, #b42318); }
.catalog-dialog button:focus-visible, .catalog-dialog input[type='checkbox']:focus-visible { outline: 2px solid #e87532; outline-offset: 2px; }
@container (max-width: 720px) {
  .workspace-catalog .catalog-row { grid-template-columns: minmax(0, 1fr) auto; }
  .catalog-row__actions { grid-column: 1 / -1; }
}
@media (max-width: 480px) {
  .catalog-dialog { width: calc(100% - 16px); max-height: calc(100dvh - 16px); }
  .catalog-dialog__header, .catalog-dialog__body { padding: 14px; }
  .catalog-dialog__footer { padding: 12px 14px; }
  .catalog-dialog__fields { grid-template-columns: minmax(0, 1fr); gap: 12px; }
  .workspace-catalog-heading .catalog-search { max-width: none; flex-basis: 100%; }
  .catalog-bundle__columns { display: none; }
  .catalog-bundle__service { grid-template-columns: 18px minmax(0, 1fr) auto; gap: 8px; padding: 12px; }
  .catalog-bundle__service > span:nth-of-type(2) { grid-column: 2; font-size: 12px; color: var(--crm-text-muted, #667085); }
  .catalog-bundle__service > span:last-child { grid-column: 3; grid-row: 1 / 3; }
  .catalog-bundle__heading :deep(.ui-field) { max-width: none; }
  .catalog-bundle__summary { grid-template-columns: minmax(0, 1fr); gap: 12px; }
  .catalog-bundle__summary > div { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 4px 12px; align-items: center; }
  .catalog-bundle__summary dd { margin: 0; text-align: right; font-size: 14px; }
  .catalog-bundle__summary small { grid-column: 1 / -1; }
}
</style>

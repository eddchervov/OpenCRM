<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiInput, UiTextarea, UiMoneyInput, UiNumberInput } from '@opencrm/ui'
import { formatServiceDuration } from '../serviceDuration'
import type { WorkspaceScenarioCatalogItem, WorkspaceScenarioCatalogKindCode } from '../api/scenarioCatalog'

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
const title = computed(() => {
  if (mode.value === 'view') return selectedItem.value?.name ?? 'Элемент справочника'
  if (isService.value) return mode.value === 'create' ? 'Новая услуга' : 'Редактирование услуги'
  if (isBundle.value) return mode.value === 'create' ? 'Новый набор' : 'Редактирование набора'
  if (props.selectedCatalogKind === 'work_post') return mode.value === 'create' ? 'Новый пост / бокс' : 'Редактирование поста / бокса'
  if (props.selectedCatalogKind === 'material') return mode.value === 'create' ? 'Новый материал' : 'Редактирование материала'

  return mode.value === 'create' ? 'Добавить элемент' : 'Редактирование элемента'
})
const saveLabel = computed(() => {
  if (props.isSaving) return 'Сохраняем…'
  if (isService.value) return mode.value === 'create' ? 'Добавить услугу' : 'Сохранить услугу'
  if (isBundle.value) return mode.value === 'create' ? 'Добавить набор' : 'Сохранить набор'
  if (props.selectedCatalogKind === 'work_post') return mode.value === 'create' ? 'Добавить пост / бокс' : 'Сохранить пост / бокс'
  if (props.selectedCatalogKind === 'material') return mode.value === 'create' ? 'Добавить материал' : 'Сохранить материал'

  return mode.value === 'create' ? 'Добавить' : 'Сохранить'
})
const createLabel = computed(() => {
  if (isService.value) return 'Добавить услугу'
  if (isBundle.value) return 'Добавить набор'
  if (props.selectedCatalogKind === 'work_post') return 'Добавить пост / бокс'
  if (props.selectedCatalogKind === 'material') return 'Добавить материал'

  return 'Добавить элемент'
})
const catalogItems = computed(() => props.getCatalogItems(props.selectedCatalogKind))
const filteredCatalogItems = computed(() => catalogItems.value.filter(item => [item.name, item.description].filter(Boolean)
  .join(' ').toLocaleLowerCase('ru').includes(catalogSearch.value.trim().toLocaleLowerCase('ru'))))

const form = computed(() => mode.value === 'create'
  ? props.catalogCreateForms[props.selectedCatalogKind]
  : selectedItem.value ? props.catalogDrafts[selectedItem.value.id] : null)

const availableServices = computed(() => props.getCatalogItems('service')
  .filter(service => service.isActive || (mode.value === 'edit' && form.value?.serviceIds.includes(service.id))))
const filteredServices = computed(() => availableServices.value.filter(service => service.name.toLocaleLowerCase('ru')
  .includes(serviceSearch.value.trim().toLocaleLowerCase('ru'))))
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
  ? 'Выберите от 1 до 100 услуг для набора.' : '')
const money = (value: number) => `${value.toLocaleString('ru-RU', { maximumFractionDigits: 2 })} ₽`

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
  return props.getCatalogItems('service').find(service => service.id === id)?.name ?? 'Недоступная услуга'
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
      <div class="workspace-catalog-switcher" role="group" aria-label="Тип справочника">
        <button
          v-for="group in catalogGroups"
          :key="group.kind"
          type="button"
          :disabled="isSaving"
          :aria-pressed="selectedCatalogKind === group.kind"
          :class="{ active: selectedCatalogKind === group.kind }"
          @click="$emit('selectCatalogKind', group.kind)"
        >
          <span>{{ group.title }}</span>
          <strong>{{ getActiveCatalogCount(group.kind) }} / {{ getCatalogItems(group.kind).length }}</strong>
        </button>
      </div>
    </section>

    <section class="workspace-common-subpanel workspace-catalog-panel">
      <div class="workspace-catalog-heading">
        <div>
          <h3 class="ui-section-header">{{ selectedCatalogGroup.title }}</h3>
          <span>{{ getActiveCatalogCount(selectedCatalogKind) }} активных из {{ getCatalogItems(selectedCatalogKind).length }}</span>
        </div>
        <UiInput v-model="catalogSearch" class="catalog-search" type="search" label="Поиск по справочнику" placeholder="Название или описание…" />
        <UiButton v-if="canManageCatalog" class="catalog-add" variant="secondary" :disabled="isSaving" @click="$emit('startCatalogEdit')">
          {{ createLabel }}
        </UiButton>
      </div>

      <div v-if="getCatalogItems(selectedCatalogKind).length === 0" class="workspace-common-empty">
        {{ selectedCatalogGroup.emptyText }}
      </div>
      <div v-else-if="!filteredCatalogItems.length" class="workspace-common-empty" role="status">По вашему запросу ничего не найдено.</div>
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
              <span v-if="item.kindCode === 'service' && item.unitPrice != null">{{ item.unitPrice.toLocaleString('ru-RU') }} ₽</span>
              <span v-if="item.kindCode === 'service_bundle'">Услуг: {{ item.serviceIds?.length ?? 0 }}</span>
              <span v-if="item.durationMinutes != null">{{ formatServiceDuration(item.durationMinutes) }}</span>
              <span>Порядок: {{ item.sortOrder }}</span>
            </div>
          </div>
          <div class="catalog-row__badges">
            <span class="catalog-row__status" :class="{ inactive: !item.isActive }">
              {{ item.isActive ? 'Активен' : item.kindCode === 'service' ? 'В архиве' : 'Отключен' }}
            </span>
            <span v-if="item.isUsed" class="catalog-row__used">Используется в данных</span>
          </div>
          <div class="catalog-row__actions">
            <UiButton variant="secondary" :disabled="isSaving" @click="openItem(item)">Подробнее</UiButton>
            <UiButton
              v-if="canManageCatalog && item.kindCode === 'work_post' && item.isUsed"
              variant="secondary"
              :disabled="isSaving"
              @click="$emit('changePostActivity', item)"
            >{{ item.isActive ? 'Закрыть пост' : 'Вернуть в работу' }}</UiButton>
            <UiButton v-if="canEdit(item)" variant="secondary" :disabled="isSaving" @click="openItem(item, true)">Редактировать</UiButton>
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
          <small>{{ selectedCatalogGroup.title }}</small>
          <h2 id="catalog-dialog-title">{{ title }}</h2>
        </div>
        <button type="button" class="catalog-dialog__close" aria-label="Закрыть" :disabled="isSaving" @click="closeDialog()">×</button>
      </header>

      <div class="catalog-dialog__body">
        <template v-if="mode === 'view' && selectedItem">
          <dl class="catalog-details">
            <div><dt>Статус</dt><dd>{{ selectedItem.isActive ? 'Активен' : selectedItem.kindCode === 'service' ? 'В архиве' : 'Отключен' }}</dd></div>
            <div v-if="selectedItem.kindCode === 'service' && selectedItem.unitPrice != null"><dt>Цена</dt><dd>{{ selectedItem.unitPrice.toLocaleString('ru-RU') }} ₽</dd></div>
            <div v-if="selectedItem.durationMinutes != null"><dt>Длительность</dt><dd>{{ formatServiceDuration(selectedItem.durationMinutes) }}</dd></div>
            <div><dt>Порядок</dt><dd>{{ selectedItem.sortOrder }}</dd></div>
          </dl>
          <div v-if="selectedItem.description" class="catalog-details__section">
            <h3 class="ui-section-header">Описание</h3>
            <p>{{ selectedItem.description }}</p>
          </div>
          <div v-if="selectedItem.kindCode === 'service_bundle'" class="catalog-details__section">
            <h3 class="ui-section-header">Услуги набора</h3>
            <ul v-if="selectedItem.serviceIds?.length"><li v-for="id in selectedItem.serviceIds" :key="id">{{ serviceName(id) }}</li></ul>
            <p v-else>Услуги не выбраны.</p>
          </div>
          <p v-if="selectedItem.isUsed" class="catalog-dialog__note">Используется в данных{{ canEdit(selectedItem) ? '.' : '. Редактирование недоступно.' }}</p>
        </template>

        <form v-else-if="form" id="catalog-dialog-form" class="catalog-dialog__form" @submit.prevent="submit">
          <UiInput v-model="form.name" id="catalog-item-name" for="catalog-item-name" label="Название" :label-help="selectedCatalogGroup.nameHelp" :disabled="isSaving" required />
          <UiTextarea v-model="form.description" id="catalog-item-description" for="catalog-item-description" label="Описание" :label-help="selectedCatalogGroup.descriptionHelp" :rows="3" :disabled="isSaving" />
          <div class="catalog-dialog__fields">
            <UiMoneyInput v-if="selectedCatalogKind === 'service'" v-model="form.unitPrice" label="Цена, ₽" :min="0" :max="999999999" :step="0.01" :disabled="isSaving" />
            <UiNumberInput v-if="isService || isBundle" v-model="form.durationMinutes" id="catalog-item-duration" for="catalog-item-duration" :label="isBundle ? 'Длительность набора, мин' : 'Длительность, мин'" label-help="Время занятости поста, включая ожидание и сушку. У набора пустое поле означает сумму длительностей услуг." :min="1" :max="525600" :step="1" integer :placeholder="isBundle ? 'По услугам' : 'Не задана'" :disabled="isSaving" />
            <UiInput v-if="mode === 'edit' && selectedItem" v-model="catalogDrafts[selectedItem.id].sortOrder" label="Порядок" type="number" :disabled="isSaving" />
            <label v-if="mode === 'edit' && selectedItem" class="catalog-dialog__active">
              <input v-model="catalogDrafts[selectedItem.id].isActive" type="checkbox" :disabled="isSaving" />
              <span>{{ isService ? 'Активна' : 'Активен' }}</span>
            </label>
          </div>
          <section v-if="isBundle" class="catalog-bundle" aria-label="Состав набора">
            <div class="catalog-bundle__heading ui-section-header"><h3>Состав набора <span>{{ form.serviceIds.length }}</span></h3>
              <UiInput v-model="serviceSearch" type="search" label="Поиск услуг" placeholder="Найти услугу…" :disabled="isSaving" />
            </div>
            <div class="catalog-bundle__columns" aria-hidden="true"><span>Услуга</span><span>Длительность</span><span>Цена</span></div>
            <div class="catalog-bundle__services">
              <label v-for="service in filteredServices" :key="service.id" class="catalog-bundle__service" :class="{ 'catalog-bundle__service--selected': form.serviceIds.includes(service.id) }">
                <input v-model="form.serviceIds" type="checkbox" :value="service.id" :disabled="isSaving" :aria-label="service.name" />
                <span>{{ service.name }}<small v-if="!service.isActive">В архиве</small></span>
                <span>{{ service.durationMinutes ? formatServiceDuration(service.durationMinutes) : 'Не задана' }}</span>
                <span>{{ service.unitPrice == null ? 'Не задана' : money(service.unitPrice) }}</span>
              </label>
              <p v-if="!availableServices.length" class="catalog-bundle__empty">Нет доступных услуг.</p>
              <p v-else-if="!filteredServices.length" class="catalog-bundle__empty" role="status">По вашему запросу услуги не найдены.</p>
            </div>
            <dl class="catalog-bundle__summary" aria-label="Итог набора">
              <div><dt>Выбрано услуг</dt><dd>{{ form.serviceIds.length }}</dd></div>
              <div><dt>Стоимость по услугам</dt><dd>{{ !form.serviceIds.length ? '—' : bundlePrice == null ? 'Не все цены заданы' : money(bundlePrice) }}</dd></div>
              <div><dt>Длительность набора</dt><dd>{{ bundleDuration ? formatServiceDuration(bundleDuration) : form.serviceIds.length ? 'Не задана полностью' : '—' }}</dd><small>{{ form.durationMinutes ? 'Задана для набора' : 'Сумма длительностей услуг' }}</small></div>
            </dl>
            <p v-if="hasUnavailableServices" class="catalog-bundle__warning" role="status">В составе есть архивные или недоступные услуги. Проверьте состав перед сохранением.</p>
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
        >{{ selectedItem.kindCode === 'service' ? 'В архив' : selectedItem.isUsed ? 'Отключить' : 'Удалить' }}</UiButton>
        <UiButton variant="secondary" :disabled="isSaving" @click="closeDialog()">{{ mode === 'view' ? 'Закрыть' : 'Отмена' }}</UiButton>
        <UiButton v-if="mode === 'view' && selectedItem && canEdit(selectedItem)" :disabled="isSaving" @click="openItem(selectedItem, true)">Редактировать</UiButton>
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

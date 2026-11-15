<script setup lang="ts">
import EmployeePicker from './EmployeePicker.vue'
import { loadEmployees, type OrganizationEmployee } from '../api/employees'
import { computed, nextTick, onMounted, ref, useId, watch } from 'vue'
import { UiButton, UiInput } from '@opencrm/ui'
import { loadScenarioCatalog, type WorkspaceScenarioCatalogItem } from '../api/scenarioCatalog'
import { calculateServiceDuration, formatServiceDuration, type SelectedServiceBundle } from '../serviceDuration'
import type { WorkspaceMaintenanceDetail } from '../api/maintenance'

const props = defineProps<{ organizationId: string; modelValue: WorkspaceMaintenanceDetail[]; disabled?: boolean; canAssignEmployees?: boolean; canOpenCatalog?: boolean; defaultVatRateCode?: string; plannedStartLocal?: string; plannedEndLocal?: string; appendTo?: string }>()
const emit = defineEmits<{ 'update:modelValue': [value: WorkspaceMaintenanceDetail[]], 'duration-change': [value: number | null | undefined], 'validation-cleared': [] }>()
const employees = ref<OrganizationEmployee[]>([])
const employeeError = ref('')
const services = ref<WorkspaceScenarioCatalogItem[]>([])
const bundles = ref<WorkspaceScenarioCatalogItem[]>([])
const error = ref('')
const loading = ref(true)
const search = ref('')
const tab = ref<'services' | 'bundles'>('services')
const selectedServiceIds = ref<string[]>([])
const selectedBundleIds = ref<string[]>([])
const expandedBundles = ref<string[]>([])
const appliedBundles = ref<SelectedServiceBundle[]>([])
const dialog = ref<HTMLDialogElement | null>(null)
const vatDialog = ref<HTMLDialogElement | null>(null)
const allVatRate = ref('')
const root = ref<HTMLElement | null>(null)
const componentId = useId()
const priceErrors = ref<Record<number, string>>({})
const nameErrors = ref<Record<number, string>>({})
const quantityErrors = ref<Record<number, string>>({})
const vatErrors = ref<Record<number, string>>({})
const priceDrafts = ref(new Map<WorkspaceMaintenanceDetail, string>())
const quantityDrafts = ref(new Map<WorkspaceMaintenanceDetail, string>())
const addedIds = computed(() => new Set(props.modelValue.map(item => item.catalogItemId)))
const chosenBundles = computed(() => bundles.value.filter(item => selectedBundleIds.value.includes(item.id)))
const selected = computed(() => [...new Set([...selectedServiceIds.value, ...chosenBundles.value.flatMap(idsFor)])])
const sharedServices = computed(() => {
  const counts = new Map<string, number>()
  for (const bundle of chosenBundles.value) {
    for (const id of new Set(idsFor(bundle))) counts.set(id, (counts.get(id) ?? 0) + 1)
  }

  return new Map([...counts].filter(([, count]) => count > 1))
})
const filtered = computed(() => (tab.value === 'services' ? services.value : bundles.value)
  .filter(item => item.isActive && [item.name, ...(item.serviceIds ?? []).map(id => services.value.find(service => service.id === id)?.name)]
    .join(' ').toLocaleLowerCase('ru').includes(search.value.trim().toLocaleLowerCase('ru'))))
const selectedServices = computed(() => services.value.filter(item => selected.value.includes(item.id) && !addedIds.value.has(item.id)))
const selectedBundles = computed(() => chosenBundles.value.filter(item => item.durationMinutes
  && idsFor(item).every(id => !sharedServices.value.has(id) && !addedIds.value.has(id)))
  .map(item => ({ serviceIds: [...idsFor(item)], durationMinutes: item.durationMinutes! })))
const selectedPrice = computed(() => selectedServices.value.some(item => item.unitPrice == null)
  ? null : selectedServices.value.reduce((sum, item) => sum + item.unitPrice!, 0))
const selectedDuration = computed(() => calculateServiceDuration(
  selectedServices.value.map(item => ({ catalogItemId: item.id, name: item.name, quantity: 1 })), services.value, selectedBundles.value))
const money = (value: number) => value.toLocaleString('ru-RU', { style: 'currency', currency: 'RUB' })
const unitPrice = (item: WorkspaceMaintenanceDetail) => item.price == null ? '' : String(Math.round(item.price / (item.quantity || 1) * 100) / 100)

async function loadEmployeeOptions() {
  if (props.canAssignEmployees === false) return
  employeeError.value = ''
  try { employees.value = (await loadEmployees(props.organizationId)).items }
  catch (e) { employeeError.value = e instanceof Error ? e.message : 'Не удалось загрузить сотрудников.' }
}
async function loadCatalog() {
  loading.value = true
  error.value = ''
  try {
    const result = await loadScenarioCatalog(props.organizationId)
    services.value = result.services
    bundles.value = result.serviceBundles ?? []
  } catch (e) { error.value = e instanceof Error ? e.message : 'Не удалось загрузить услуги.' }
  finally { loading.value = false }
}
function openMenu() {
  if (props.disabled) return
  selectedServiceIds.value = []
  selectedBundleIds.value = []
  expandedBundles.value = []
  search.value = ''
  dialog.value?.showModal()
  void loadCatalog()
}
function idsFor(item: WorkspaceScenarioCatalogItem) {
  return item.kindCode === 'service_bundle' ? item.serviceIds ?? [] : [item.id]
}
function unavailable(item: WorkspaceScenarioCatalogItem) {
  const ids = idsFor(item)
  return !ids.length || ids.some(id => !services.value.some(service => service.id === id && service.isActive))
}
function missingIds(item: WorkspaceScenarioCatalogItem) { return idsFor(item).filter(id => !addedIds.value.has(id)) }
function isSelected(item: WorkspaceScenarioCatalogItem) {
  return item.kindCode === 'service_bundle'
    ? selectedBundleIds.value.includes(item.id)
    : missingIds(item).every(id => selected.value.includes(id))
}
function bundlePrice(item: WorkspaceScenarioCatalogItem) {
  const items = idsFor(item).map(id => services.value.find(service => service.id === id))
  return !items.length || items.some(service => service?.unitPrice == null)
    ? null : items.reduce((sum, service) => sum + service!.unitPrice!, 0)
}
function itemDuration(item: WorkspaceScenarioCatalogItem) {
  if (item.durationMinutes) return item.durationMinutes
  return calculateServiceDuration(idsFor(item).map(id => ({ catalogItemId: id, name: '', quantity: 1 })), services.value)
}
function toggleExpanded(id: string) {
  expandedBundles.value = expandedBundles.value.includes(id) ? expandedBundles.value.filter(value => value !== id) : [...expandedBundles.value, id]
}
function toggle(item: WorkspaceScenarioCatalogItem) {
  if (props.disabled || loading.value || error.value || unavailable(item) || !missingIds(item).length) return
  const removing = isSelected(item)
  if (item.kindCode === 'service_bundle') {
    selectedBundleIds.value = removing ? selectedBundleIds.value.filter(id => id !== item.id) : [...selectedBundleIds.value, item.id]
    return
  }

  if (!removing) {
    selectedServiceIds.value.push(item.id)
    return
  }

  const affectedBundles = chosenBundles.value.filter(bundle => idsFor(bundle).includes(item.id))
  selectedServiceIds.value = [...new Set([...selectedServiceIds.value, ...affectedBundles.flatMap(idsFor)])].filter(id => id !== item.id)
  selectedBundleIds.value = selectedBundleIds.value.filter(id => !affectedBundles.some(bundle => bundle.id === id))
}
function addSelected() {
  if (props.disabled || loading.value || error.value || !selectedServices.value.length) return
  clearValidationErrors()
  appliedBundles.value.push(...selectedBundles.value)
  emit('update:modelValue', [...props.modelValue, ...selectedServices.value.map(item => ({
    name: item.name, catalogItemId: item.id, description: null, price: item.unitPrice ?? null, vatRateCode: props.defaultVatRateCode ?? null, quantity: 1
  }))])
  dialog.value?.close()
}
async function addCustom() {
  clearValidationErrors()
  emit('update:modelValue', [...props.modelValue, {
    name: '',
    description: null,
    catalogItemId: null,
    price: 0,
    vatRateCode: props.defaultVatRateCode ?? null,
    quantity: 1
  }])
  await nextTick()
  const nameInputs = root.value?.querySelectorAll<HTMLInputElement>('.service-editor__name input[type=text]')
  nameInputs?.[nameInputs.length - 1]?.focus()
}
function patchItem(item: WorkspaceMaintenanceDetail, values: Partial<WorkspaceMaintenanceDetail>) {
  const updated = { ...item, ...values }
  for (const drafts of [priceDrafts.value, quantityDrafts.value]) {
    if (!drafts.has(item)) continue
    drafts.set(updated, drafts.get(item)!)
    drafts.delete(item)
  }

  return updated
}
function patch(index: number, values: Partial<WorkspaceMaintenanceDetail>) {
  emit('update:modelValue', props.modelValue.map((item, i) => i === index ? patchItem(item, values) : item))
}
function removeItem(index: number) {
  clearValidationErrors()
  emit('update:modelValue', props.modelValue.filter((_, i) => i !== index))
}
function clearNumericDrafts() {
  priceDrafts.value.clear()
  quantityDrafts.value.clear()
}
function clearValidationErrors() {
  priceErrors.value = {}
  nameErrors.value = {}
  quantityErrors.value = {}
  vatErrors.value = {}
}
function clearFieldError(index: number, field: 'name' | 'quantity' | 'price' | 'vat') {
  const errors = field === 'name' ? nameErrors : field === 'quantity' ? quantityErrors : field === 'price' ? priceErrors : vatErrors
  if (!(index in errors.value)) return
  const nextErrors = { ...errors.value }
  delete nextErrors[index]
  errors.value = nextErrors
  emit('validation-cleared')
}
function openVatDialog() {
  if (props.disabled || props.modelValue.length < 2) return

  allVatRate.value = ''
  vatDialog.value?.showModal()
}
function setAllVat() {
  if (props.disabled || !allVatRate.value || props.modelValue.length < 2) return

  emit('update:modelValue', props.modelValue.map(item => patchItem(item, { vatRateCode: allVatRate.value })))
  vatErrors.value = {}
  emit('validation-cleared')
  vatDialog.value?.close()
}
function setQuantity(index: number, event: Event) {
  const input = event.target as HTMLInputElement
  if (!input.validity.valid) return
  const quantity = Number(input.value)
  const item = props.modelValue[index]
  clearFieldError(index, 'quantity')
  quantityDrafts.value.delete(item)
  patch(index, { quantity, price: item.price == null ? null : Math.round(Number(unitPrice(item)) * quantity * 100) / 100 })
}
function updateQuantityDraft(index: number, event: Event) {
  const input = event.target as HTMLInputElement
  quantityDrafts.value.set(props.modelValue[index], input.value)
  clearFieldError(index, 'quantity')
}
function setPrice(index: number, event: Event) {
  const input = event.target as HTMLInputElement
  if (input.value && !input.validity.valid) return
  const value = input.value
  clearFieldError(index, 'price')
  priceDrafts.value.delete(props.modelValue[index])
  patch(index, { price: value === '' ? null : Math.round(Number(value) * (props.modelValue[index].quantity || 1) * 100) / 100 })
}
function updatePriceDraft(index: number, event: Event) {
  const input = event.target as HTMLInputElement
  priceDrafts.value.set(props.modelValue[index], input.value)
  clearFieldError(index, 'price')
}
function serviceLabel(item?: WorkspaceMaintenanceDetail) {
  const name = item?.name.trim()
  return name ? `услуги «${name}»` : 'работы'
}
function hasInvalidPrice(item: WorkspaceMaintenanceDetail) {
  return item.price == null || !Number.isFinite(item.price) || item.price < 0
}
function validate() {
  clearValidationErrors()
  const invalid = root.value?.querySelector<HTMLInputElement | HTMLSelectElement>('input:invalid, select:invalid')
  if (invalid) {
    const row = invalid.closest('.service-editor__row')
    const index = row ? [...(root.value?.querySelectorAll('.service-editor__row') ?? [])].indexOf(row) : -1
    const item = index >= 0 ? props.modelValue[index] : undefined
    let field: 'name' | 'quantity' | 'price' | 'vat' | null = null
    let message = 'Проверьте данные работы'
    if (invalid.closest('.service-editor__name')) {
      field = 'name'
      message = 'Укажите название работы'
    }
    if (invalid.closest('.service-editor__quantity')) {
      field = 'quantity'
      message = `Укажите корректное количество для ${serviceLabel(item)}`
    }
    if (invalid.closest('.service-editor__price')) {
      field = 'price'
      message = `Укажите корректную цену ${serviceLabel(item)}`
    }
    if (invalid.closest('.service-editor__vat')) {
      field = 'vat'
      message = `Выберите ставку НДС для ${serviceLabel(item)}`
    }
    if (index >= 0 && field) {
      const errors = field === 'name' ? nameErrors : field === 'quantity' ? quantityErrors : field === 'price' ? priceErrors : vatErrors
      errors.value = { [index]: message }
    }
    invalid.scrollIntoView({ block: 'center' })
    invalid.focus()

    return `${message}.`
  }

  if (!props.modelValue.length) return 'Добавьте хотя бы одну работу.'

  const priceInputs = root.value?.querySelectorAll<HTMLInputElement>('.service-editor__price input') ?? []
  const quantityInputs = root.value?.querySelectorAll<HTMLInputElement>('.service-editor__quantity input') ?? []
  const nextItems = props.modelValue.map((item, index) => {
    const priceInput = priceInputs[index]
    const quantityInput = quantityInputs[index]
    const previousQuantity = item.quantity ?? 1
    const quantity = quantityInput && !quantityInput.disabled ? Number(quantityInput.value) : previousQuantity
    const previousUnitPrice = Number(unitPrice(item))
    const enteredUnitPrice = priceInput && !priceInput.disabled ? Number(priceInput.value) : previousUnitPrice
    const unitPriceChanged = priceInput != null
      && !priceInput.disabled
      && (enteredUnitPrice !== previousUnitPrice || (item.price == null && priceInput.value !== ''))
    const quantityChanged = quantity !== previousQuantity
    const price = priceInput && !priceInput.disabled && (unitPriceChanged || quantityChanged)
      ? Math.round(enteredUnitPrice * quantity * 100) / 100
      : item.price

    return { ...item, quantity, price }
  })

  const invalidLine = nextItems.findIndex(item => !item.name.trim() || !Number.isFinite(item.quantity ?? 1) || (item.quantity ?? 1) < 1 || hasInvalidPrice(item) || (!item.vatRateCode && !props.defaultVatRateCode))
  if (invalidLine >= 0) {
    const item = nextItems[invalidLine]
    const field = !item.name.trim() ? 'name' : !Number.isFinite(item.quantity ?? 1) || (item.quantity ?? 1) < 1 ? 'quantity' : hasInvalidPrice(item) ? 'price' : 'vat'
    const message = field === 'name' ? 'Укажите название работы' : field === 'quantity' ? `Укажите корректное количество для ${serviceLabel(item)}` : field === 'price' ? `Укажите цену ${serviceLabel(item)}` : `Выберите ставку НДС для ${serviceLabel(item)}`
    const errors = field === 'name' ? nameErrors : field === 'quantity' ? quantityErrors : field === 'price' ? priceErrors : vatErrors
    errors.value = { [invalidLine]: message }
    const selector = field === 'name' ? '.service-editor__name input' : field === 'quantity' ? '.service-editor__quantity input' : field === 'price' ? '.service-editor__price input' : '.service-editor__vat select'
    const input = root.value?.querySelectorAll<HTMLInputElement | HTMLSelectElement>(selector)[invalidLine]
      ?? (field === 'name' ? root.value?.querySelectorAll<HTMLElement>('.service-editor__name')[invalidLine] : undefined)
    input?.scrollIntoView({ block: 'center' })
    input?.focus()

    return `${message}.`
  }

  if (!props.disabled && props.defaultVatRateCode && nextItems.some(item => !item.vatRateCode)) {
    for (const item of nextItems) {
      item.vatRateCode = item.vatRateCode || props.defaultVatRateCode || null
    }
  }

  const hasChanges = nextItems.some((item, index) => {
    const previousItem = props.modelValue[index]

    return item.price !== previousItem.price || item.quantity !== previousItem.quantity || item.vatRateCode !== previousItem.vatRateCode
  })
  if (!props.disabled && hasChanges) {
    emit('update:modelValue', nextItems)
  }

  clearNumericDrafts()

  /*
   * Валидация читает черновые числовые поля перед сохранением, даже если Enter отправил форму до blur.
   */
  return null
}
watch(() => props.modelValue.map(item => item.catalogItemId), ids => {
  appliedBundles.value = appliedBundles.value.filter(bundle => bundle.serviceIds.every(id => ids.includes(id)))
}, { flush: 'sync' })
watch(() => props.modelValue, items => {
  clearValidationErrors()
  const currentItems = new Set(items)
  for (const drafts of [priceDrafts.value, quantityDrafts.value]) {
    for (const item of drafts.keys()) {
      if (!currentItems.has(item)) drafts.delete(item)
    }
  }
}, { flush: 'sync' })
watch(() => calculateServiceDuration(props.modelValue, services.value, appliedBundles.value), value => emit('duration-change', value), { immediate: true })

defineExpose({ openMenu, validate })
onMounted(() => { void loadCatalog(); void loadEmployeeOptions() })
</script>

<template>
  <div ref="root" class="service-editor">
    <div v-if="!disabled && modelValue.length > 1" class="service-editor__toolbar">
      <UiButton variant="secondary" aria-haspopup="dialog" @click="openVatDialog">Изменить НДС для всех работ</UiButton>
    </div>
    <div v-for="(item, index) in modelValue" :key="index" class="service-editor__row">
      <div class="service-editor__heading">
        <div class="service-editor__name" :tabindex="nameErrors[index] ? -1 : undefined" :aria-invalid="nameErrors[index] ? 'true' : undefined" :aria-describedby="nameErrors[index] ? `${componentId}-name-error-${index}` : undefined"><span v-if="item.catalogItemId"><small>Услуга</small><strong>{{ item.name }}</strong></span>
          <label v-else>Название работы<input :id="`${componentId}-name-${index}`" :value="item.name" type="text" required :disabled="disabled" placeholder="Например, удаление следов клея" :aria-invalid="nameErrors[index] ? 'true' : undefined" :aria-describedby="nameErrors[index] ? `${componentId}-name-error-${index}` : undefined" @input="clearFieldError(index, 'name'); patch(index, { name: ($event.target as HTMLInputElement).value })" /></label>
          <span v-if="nameErrors[index]" :id="`${componentId}-name-error-${index}`" class="service-editor__field-error" role="alert">{{ nameErrors[index] }}</span>
        </div>
        <div class="service-editor__sum"><small>Сумма</small><strong>{{ item.price == null ? '—' : money(item.price) }}</strong></div>
        <button v-if="!disabled" type="button" class="service-editor__remove" :aria-label="`Удалить ${item.name}`"
          @click="removeItem(index)"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 6h18M9 6V4h6v2M5 6l1 14h12l1-14M10 10v6M14 10v6" /></svg></button>
      </div>
      <div class="service-editor__fields" :class="{ 'service-editor__fields--without-employee': canAssignEmployees === false }">
        <EmployeePicker v-if="canAssignEmployees !== false" class="service-editor__employee" :organization-id="organizationId" :items="employees" :model-value="item.employeeId" :current-name="item.employeeName" :planned-start-local="plannedStartLocal" :planned-end-local="plannedEndLocal" :append-to="appendTo" empty-label="Как в записи" :disabled="disabled || !!employeeError" @update:model-value="patch(index, { employeeId: $event || null, employeeName: employees.find(employee => employee.id === $event)?.name ?? null })" />
        <div v-else-if="item.employeeId || item.employeeName" class="service-editor__saved-employee">
          <span>Сохранённое назначение</span>
          <strong>{{ item.employeeName || 'Ранее назначенный сотрудник' }}</strong>
        </div>
        <label class="service-editor__quantity">Кол-во<input :id="`${componentId}-quantity-${index}`" :value="quantityDrafts.get(item) ?? item.quantity ?? 1" type="number" min="1" max="10000" step="1" required :disabled="disabled" :aria-invalid="quantityErrors[index] ? 'true' : undefined" :aria-describedby="quantityErrors[index] ? `${componentId}-quantity-error-${index}` : undefined" @input="updateQuantityDraft(index, $event)" @change="setQuantity(index, $event)" /><span v-if="quantityErrors[index]" :id="`${componentId}-quantity-error-${index}`" class="service-editor__field-error" role="alert">{{ quantityErrors[index] }}</span></label>
        <label class="service-editor__price">Цена, ₽<input :id="`${componentId}-price-${index}`" :value="priceDrafts.get(item) ?? unitPrice(item)" type="number" min="0" max="999999999" step="0.01" required :disabled="disabled" :aria-invalid="priceErrors[index] ? 'true' : undefined" :aria-describedby="priceErrors[index] ? `${componentId}-price-error-${index}` : undefined" @input="updatePriceDraft(index, $event)" @change="setPrice(index, $event)" /><span v-if="priceErrors[index]" :id="`${componentId}-price-error-${index}`" class="service-editor__field-error" role="alert">{{ priceErrors[index] }}</span></label>
        <label class="service-editor__vat">НДС<select :id="`${componentId}-vat-${index}`" :value="item.vatRateCode || defaultVatRateCode || ''" :required="!defaultVatRateCode" :disabled="disabled" :aria-invalid="vatErrors[index] ? 'true' : undefined" :aria-describedby="vatErrors[index] ? `${componentId}-vat-error-${index}` : undefined" @change="clearFieldError(index, 'vat'); patch(index, { vatRateCode: ($event.target as HTMLSelectElement).value || null })">
          <option value="">Выберите</option><option value="without_vat">Без НДС</option><option value="0">0 %</option><option value="5">5 %</option><option value="7">7 %</option><option value="10">10 %</option><option value="20">20 %</option><option value="22">22 %</option>
        </select><span v-if="vatErrors[index]" :id="`${componentId}-vat-error-${index}`" class="service-editor__field-error" role="alert">{{ vatErrors[index] }}</span></label>
      </div>
      <details v-if="!disabled || item.description" class="service-description"><summary>Описание услуги</summary>
        <textarea :value="item.description ?? ''" :disabled="disabled" rows="2" aria-label="Описание услуги" @change="patch(index, { description: ($event.target as HTMLTextAreaElement).value })" /></details>
    </div>
    <p v-if="employeeError" role="alert">{{ employeeError }} <button type="button" @click="loadEmployeeOptions">Повторить</button></p>
    <div v-if="!disabled" class="service-editor__buttons">
      <UiButton variant="secondary" @click="openMenu">＋ Выбрать услуги</UiButton>
      <UiButton variant="secondary" @click="addCustom">＋ Произвольная работа</UiButton>
    </div>
    <dialog ref="vatDialog" class="service-vat-dialog" aria-label="НДС для всех работ" @close="allVatRate = ''" @click="($event.target === vatDialog) && vatDialog?.close()">
      <header class="ui-form-header">
        <h3>НДС для всех работ</h3>
        <button type="button" aria-label="Закрыть настройку НДС" @click="vatDialog?.close()">×</button>
      </header>
      <div class="service-vat-dialog__body">
        <p>Выбранная ставка будет применена ко всем работам в записи: {{ modelValue.length }}.</p>
        <label>Ставка НДС
          <select v-model="allVatRate" autofocus :disabled="disabled">
            <option disabled value="">Выберите ставку</option><option value="without_vat">Без НДС</option><option value="0">0 %</option><option value="5">5 %</option><option value="7">7 %</option><option value="10">10 %</option><option value="20">20 %</option><option value="22">22 %</option>
          </select>
        </label>
      </div>
      <footer>
        <UiButton variant="secondary" @click="vatDialog?.close()">Отмена</UiButton>
        <UiButton :disabled="disabled || !allVatRate || modelValue.length < 2" @click="setAllVat">Применить ко всем</UiButton>
      </footer>
    </dialog>
    <dialog ref="dialog" class="service-menu" aria-label="Услуги и наборы">
      <header class="ui-form-header"><h3>Услуги и наборы</h3><button type="button" aria-label="Закрыть выбор услуг" @click="dialog?.close()">×</button></header>
      <div class="service-menu__search"><UiInput v-model="search" label="Поиск" placeholder="Название услуги или набора" />
        <div class="service-menu__tabs"><button type="button" :aria-pressed="tab === 'services'" @click="tab = 'services'">Услуги</button>
          <button type="button" :aria-pressed="tab === 'bundles'" @click="tab = 'bundles'">Наборы</button>
          <a v-if="canOpenCatalog" :href="`#catalog?catalog=${tab === 'bundles' ? 'service_bundle' : 'service'}`" target="_blank" rel="noopener" :aria-label="tab === 'bundles' ? 'Изменить наборы в справочнике (новая вкладка)' : 'Изменить услуги в справочнике (новая вкладка)'">Изменить ↗</a></div>
      </div>
      <div class="service-menu__body">
      <div class="service-menu__items" aria-label="Каталог услуг">
        <p v-if="loading">Загружаем услуги…</p>
        <p v-else-if="error" role="alert">{{ error }} <button type="button" @click="loadCatalog">Повторить</button></p>
        <p v-else-if="!filtered.length">{{ tab === 'bundles' ? 'Наборы не найдены. Их можно создать в справочниках.' : 'Услуги не найдены.' }}</p>
        <article v-for="item in loading || error ? [] : filtered" :key="item.id" class="service-menu__item" :class="{ muted: unavailable(item) || !missingIds(item).length, 'service-menu__item--selected': isSelected(item) && missingIds(item).length }">
          <div class="service-menu__item-heading">
            <label class="service-menu__choice">
              <input type="checkbox" :checked="!missingIds(item).length || isSelected(item)" :disabled="disabled || unavailable(item) || !missingIds(item).length" @change="toggle(item)" />
              <span><strong>{{ item.name }}</strong>
                <small v-if="item.kindCode === 'service_bundle'">Услуг: {{ idsFor(item).length }}<template v-if="itemDuration(item)"> · {{ formatServiceDuration(itemDuration(item)!) }}</template></small>
                <small v-else-if="item.durationMinutes">{{ formatServiceDuration(item.durationMinutes) }}</small>
                <small v-if="unavailable(item)">Есть недоступные услуги</small><small v-else-if="!missingIds(item).length">Уже в записи</small>
              </span>
            </label>
            <strong class="service-menu__price">{{ item.kindCode === 'service_bundle' ? (bundlePrice(item) == null ? 'Не все цены заданы' : money(bundlePrice(item)!)) : item.unitPrice == null ? 'Цена не задана' : money(item.unitPrice) }}</strong>
            <button v-if="item.kindCode === 'service_bundle'" type="button" class="service-menu__expand" :aria-label="`Состав набора ${item.name}`" :aria-expanded="expandedBundles.includes(item.id)" @click="toggleExpanded(item.id)">{{ expandedBundles.includes(item.id) ? '⌃' : '⌄' }}</button>
          </div>
          <p v-if="item.kindCode !== 'service_bundle' && item.description" class="service-menu__description">{{ item.description }}</p>
          <div v-if="item.kindCode === 'service_bundle' && expandedBundles.includes(item.id)" class="service-menu__composition">
            <small>Стоимость по услугам набора</small>
            <div v-for="id in idsFor(item)" :key="id">
              <span>{{ services.find(service => service.id === id)?.name ?? 'Недоступная услуга' }}<small v-if="addedIds.has(id)">Уже в записи</small></span>
              <small>{{ services.find(service => service.id === id)?.durationMinutes ? formatServiceDuration(services.find(service => service.id === id)!.durationMinutes!) : '—' }}</small>
              <span>{{ services.find(service => service.id === id)?.unitPrice == null ? '—' : money(services.find(service => service.id === id)!.unitPrice!) }}</span>
            </div>
          </div>
        </article>
      </div>
      <aside class="service-menu__selection" aria-label="Выбранные услуги">
        <h4 class="ui-section-header">Выбрано <span>{{ selectedServices.length }}</span></h4>
        <p v-if="sharedServices.size" class="service-menu__shared" role="status">Общие услуги добавятся один раз. Для пересекающихся наборов длительность рассчитана по отдельным услугам.</p>
        <p v-if="!selectedServices.length" class="service-menu__empty">Выберите услуги или набор в каталоге.</p>
        <div v-for="item in selectedServices" :key="item.id" class="service-menu__selected-row">
          <div><strong>{{ item.name }}</strong><small>{{ item.durationMinutes ? formatServiceDuration(item.durationMinutes) : 'Длительность не задана' }}</small><small v-if="sharedServices.has(item.id)">В {{ sharedServices.get(item.id) }} наборах · 1 раз</small></div>
          <span>{{ item.unitPrice == null ? '—' : money(item.unitPrice) }}</span>
          <button type="button" :aria-label="`Убрать из выбора ${item.name}`" :disabled="disabled" @click="toggle(item)">×</button>
        </div>
        <dl v-if="selectedServices.length" class="service-menu__totals">
          <div><dt>Сумма работ</dt><dd>{{ selectedPrice == null ? 'Не все цены заданы' : money(selectedPrice) }}</dd></div>
          <div><dt>Длительность</dt><dd>{{ selectedDuration ? formatServiceDuration(selectedDuration) : 'Не задана полностью' }}</dd></div>
        </dl>
      </aside>
      </div>
      <footer><span>Выбрано: {{ selectedServices.length }}<strong v-if="selectedServices.length && selectedPrice != null">{{ money(selectedPrice) }}</strong></span><div><UiButton variant="secondary" @click="dialog?.close()">Отмена</UiButton><UiButton :disabled="disabled || loading || !!error || !selectedServices.length" @click="addSelected">Добавить в запись</UiButton></div></footer>
    </dialog>
  </div>
</template>

<style scoped>
.service-editor__employee { min-width:0; }
.service-editor__employee :deep(.ui-field) { gap:5px; }
.service-editor__employee :deep(.ui-field-label) { color:var(--crm-text-muted, #64748b); font-size:12px; font-weight:400; }
.service-editor__employee :deep(.ui-select) { min-height:40px; }
.service-description { padding:0 14px 14px; font-size:12px; color:var(--crm-text-muted, #64748b); }
.service-description summary { cursor: pointer; overflow-wrap: anywhere; }
.service-description textarea { box-sizing:border-box; margin-top:8px; width:100%; border:1px solid var(--crm-line, #d9e0e9); border-radius:6px; background:var(--crm-surface, #fff); color:var(--crm-text, #172033); padding:8px; font:inherit; resize:vertical; }
.service-editor { container-type:inline-size; display:grid; gap:14px; min-width:0; }
.service-editor__buttons { display: flex; flex-wrap: wrap; gap: 8px; }
.service-editor__toolbar { padding-bottom:14px; border-bottom:1px solid var(--crm-line, #e2e8f0); }
.service-vat-dialog { box-sizing:border-box; width:min(480px, calc(100vw - 24px)); max-height:90dvh; padding:0; overflow:auto; border:1px solid var(--crm-line, #d9e0e9); border-radius:10px; background:var(--crm-surface, #fff); color:var(--crm-text, #172033); }
.service-vat-dialog::backdrop { background:#0009; }
.service-vat-dialog header, .service-vat-dialog footer { display:flex; align-items:center; justify-content:space-between; gap:12px; padding:16px 20px; }
.service-vat-dialog header { border-bottom:1px solid var(--crm-line, #d9e0e9); }
.service-vat-dialog h3 { margin:0; font-size:18px; }
.service-vat-dialog header button { border:0; background:transparent; color:inherit; font-size:24px; cursor:pointer; }
.service-vat-dialog__body { padding:20px; }
.service-vat-dialog__body p { margin:0 0 18px; color:var(--crm-text-muted, #64748b); font-size:14px; line-height:1.5; }
.service-vat-dialog footer { justify-content:flex-end; flex-wrap:wrap; border-top:1px solid var(--crm-line, #d9e0e9); }
.service-editor__row { min-width:0; border:1px solid var(--crm-line, #e2e8f0); border-radius:8px; background:var(--crm-surface, #fff); }
.service-editor__heading { display:grid; grid-template-columns:minmax(0, 1fr) auto 28px; align-items:center; gap:12px; padding:12px 14px; border-bottom:1px solid var(--crm-line, #e2e8f0); border-radius:7px 7px 0 0; background:var(--crm-section-header-bg, #f2f4f7); }
.service-editor__fields { display:grid; grid-template-columns:minmax(0, 1fr) 72px 115px 115px; align-items:start; gap:12px; padding:14px; }
.service-editor__fields--without-employee { grid-template-columns:72px minmax(115px, 1fr) 115px; }
.service-editor__saved-employee { grid-column:1 / -1; display:grid; gap:5px; color:var(--crm-text-muted, #64748b); font-size:12px; }
.service-editor__saved-employee strong { color:var(--crm-text, #172033); font-weight:500; }
.service-editor__name { min-width:0; overflow-wrap:anywhere; }
.service-editor__name > span { display: grid; gap: 5px; min-width: 0; }
.service-editor__name > label { width: 100%; max-width: 640px; min-width: 0; }
.service-editor small { display: block; color: var(--crm-text-muted, #64748b); font-size: 12px; }
.service-editor label { display: grid; gap: 5px; color: var(--crm-text-muted, #64748b); font-size: 12px; }
.service-editor select, .service-editor input[type=number], .service-editor input[type=text] { width:100%; min-width:0; height:40px; box-sizing:border-box; padding:8px; border:1px solid var(--crm-line, #d9e0e9); border-radius:6px; background:var(--crm-surface, #fff); color:var(--crm-text, #172033); font:inherit; font-size:13px; color-scheme:inherit; }
.service-editor__sum { display:grid; gap:5px; max-width:160px; overflow-wrap:anywhere; text-align:right; font-size:13px; }
.service-editor__field-error { color:var(--crm-danger-text, #b42318); font-size:12px; }
.service-editor input[aria-invalid=true], .service-editor select[aria-invalid=true] { border-color:var(--crm-danger-text, #b42318); }
.service-editor input[aria-invalid=true]:focus, .service-editor select[aria-invalid=true]:focus { outline:2px solid var(--crm-danger-text, #b42318); outline-offset:2px; }
.service-editor__name[aria-invalid=true] { outline:1px solid var(--crm-danger-text, #b42318); outline-offset:2px; }
.service-editor__remove, .service-menu header button { border: 0; border-radius: 6px; color: var(--crm-text-muted, #64748b); background: transparent; cursor: pointer; padding: 7px; font-size: 20px; }
.service-menu { box-sizing: border-box; padding: 0; width: min(1100px, calc(100vw - 24px)); max-height: min(840px, calc(100dvh - 32px)); border: 1px solid var(--crm-line, #d9e0e9); border-radius: 10px; color: var(--crm-text, #172033); background: var(--crm-surface, #fff); }
.service-menu::backdrop { background: #0009; }
.service-menu[open] { display: flex; flex-direction: column; }
.service-menu header, .service-menu footer { display: flex; flex-shrink: 0; align-items: center; justify-content: space-between; padding: 16px 20px; gap: 12px; }
.service-menu header { border-bottom: 1px solid var(--crm-line, #d9e0e9); }
.service-menu h3 { margin: 0; font-size: 18px; }
.service-menu__search { flex-shrink: 0; padding: 16px 20px 12px; }
.service-menu__tabs { display: flex; align-items: center; flex-wrap: wrap; gap: 5px; margin-top: 12px; }
.service-menu__tabs a { margin-left: auto; color: #e87532; text-decoration: none; font-size: 13px; padding: 8px; }
.service-menu__tabs button { padding: 8px 16px; border: 1px solid transparent; border-radius: 6px; background: transparent; color: var(--crm-text-muted, #64748b); font: inherit; font-size: 13px; cursor: pointer; }
.service-menu__tabs [aria-pressed=true] { border-color: var(--crm-line, #d9e0e9); background: var(--crm-surface-soft, #f7f8fa); color: var(--crm-text, #172033); }
.service-menu__body { display: grid; grid-template-columns: minmax(0, 1fr) 320px; min-height: 0; overflow: auto; }
.service-menu__items { display: grid; align-content: start; gap: 12px; overflow: auto; min-height: 100px; padding: 4px 20px 20px; }
.service-menu__item { min-width: 0; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 8px; background: var(--crm-surface-soft, #f7f8fa); }
.service-menu__item--selected { border-color: #e87532; }
.service-menu__item-heading { display: flex; align-items: center; gap: 12px; padding: 14px; }
.service-menu .service-menu__choice { display: flex; align-items: center; flex: 1; gap: 12px; min-width: 0; color: var(--crm-text, #172033); cursor: pointer; }
.service-menu__choice > span { display: grid; gap: 5px; min-width: 0; overflow-wrap: anywhere; }
.service-menu__item strong { font-size: 14px; }
.service-menu__choice input { accent-color: #e87532; width: 18px; height: 18px; margin: 0; flex-shrink: 0; }
.service-menu__price { max-width: 130px; text-align: right; font-size: 13px; overflow-wrap: anywhere; }
.service-menu__expand { padding: 4px; flex-shrink: 0; border: 0; background: transparent; color: var(--crm-text-muted, #64748b); cursor: pointer; font-size: 22px; }
.service-menu__description { margin: 0; padding: 0 14px 14px 44px; font-size: 12px; line-height: 1.5; color: var(--crm-text-muted, #64748b); overflow-wrap: anywhere; }
.service-menu__composition { display: grid; gap: 12px; padding: 14px; border-top: 1px solid var(--crm-line, #d9e0e9); }
.service-menu__composition > div { display: grid; grid-template-columns: minmax(0, 1fr) 65px 85px; align-items: start; gap: 8px; font-size: 12px; overflow-wrap: anywhere; }
.service-menu__composition > div > :last-child { text-align: right; }
.service-menu__selection { min-width: 0; overflow: auto; padding: 4px 20px 20px; border-left: 1px solid var(--crm-line, #d9e0e9); }
.service-menu__selection h4 { display: flex; gap: 8px; align-items: center; margin: 0 0 16px; font-size: 16px; }
.service-menu__selection h4 span { padding: 3px 7px; border-radius: 5px; background: var(--crm-surface-soft, #f7f8fa); font-size: 12px; }
.service-menu__empty { font-size: 13px; line-height: 1.5; color: var(--crm-text-muted, #64748b); }
.service-menu__shared { margin: 0 0 12px; padding: 10px; border: 1px solid var(--crm-line, #d9e0e9); border-radius: 6px; color: var(--crm-text-muted, #64748b); background: var(--crm-surface-soft, #f7f8fa); font-size: 12px; line-height: 1.5; }
.service-menu__selected-row { display: flex; align-items: start; gap: 8px; padding: 12px 0; border-bottom: 1px solid var(--crm-line, #d9e0e9); font-size: 12px; }
.service-menu__selected-row > div { flex: 1; min-width: 0; display: grid; gap: 6px; overflow-wrap: anywhere; }
.service-menu__selected-row strong { font-size: 13px; }
.service-menu__selected-row > span { white-space: nowrap; }
.service-menu__selected-row button { padding: 0 3px; border: 0; background: transparent; color: var(--crm-text-muted, #64748b); cursor: pointer; font-size: 18px; }
.service-menu__totals { display: grid; gap: 14px; margin: 20px 0 0; font-size: 12px; }
.service-menu__totals > div { display: flex; gap: 12px; justify-content: space-between; }
.service-menu__totals dt { color: var(--crm-text-muted, #64748b); }
.service-menu__totals dd { margin: 0; font-weight: 600; text-align: right; }
.service-menu footer { border-top: 1px solid var(--crm-line, #d9e0e9); font-size: 13px; }
.service-menu footer > span { display: flex; gap: 16px; flex-wrap: wrap; }
.service-menu footer > div { display: flex; gap: 8px; flex-wrap: wrap; }
.service-menu footer :deep(.ui-button--primary) { background: #e87532; border-color: #e87532; color: white; }
.service-menu button:focus-visible, .service-menu a:focus-visible { outline: 2px solid #e87532; outline-offset: 2px; }
.muted { opacity: .65; }
@container (max-width: 700px) {
  .service-editor__fields { grid-template-columns:minmax(0, .7fr) repeat(2, minmax(0, 1fr)); }
  .service-editor__employee { grid-column:1 / -1; }
}
@container (max-width: 360px) {
  .service-editor__heading { grid-template-columns:minmax(0, 1fr) 28px; gap:8px; padding:12px; }
  .service-editor__sum { grid-column:1; grid-row:2; display:flex; align-items:baseline; gap:8px; max-width:none; text-align:left; }
  .service-editor__remove { grid-column:2; grid-row:1; }
  .service-editor__fields { grid-template-columns:repeat(2, minmax(0, 1fr)); padding:12px; }
  .service-editor__vat { grid-column:1 / -1; }
  .service-description { padding:0 12px 12px; }
}
.service-editor__remove svg { display: block; width: 17px; height: 17px; fill: none; stroke: currentColor; stroke-width: 1.7; stroke-linecap: round; stroke-linejoin: round; }
.service-editor__remove:hover { color: var(--crm-danger-text, #b42318); background: var(--crm-danger-surface, #fef3f2); }
@media (max-width: 700px) {
  .service-menu header, .service-menu footer { padding: 12px; }
  .service-menu__search, .service-menu__items { padding-left: 12px; padding-right: 12px; }
  .service-menu__body { display: block; }
  .service-menu__items { overflow: visible; }
  .service-menu__selection { overflow: visible; padding: 16px 12px; border-left: 0; border-top: 1px solid var(--crm-line, #d9e0e9); }
  .service-menu footer { flex-wrap: wrap; }
  .service-menu__item-heading { flex-wrap: wrap; }
  .service-menu__composition > div { grid-template-columns: minmax(0, 1fr) auto; }
  .service-menu__composition > div > small { grid-row: 2; }
}
</style>

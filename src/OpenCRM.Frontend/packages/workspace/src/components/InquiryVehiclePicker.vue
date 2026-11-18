<script setup lang="ts">
import { useLocalization } from '@opencrm/ui/i18n'
import '../inquiryMessages'

import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiInput, UiPagination } from '@opencrm/ui'
import { getVehicleDisplayName, loadVehicle, loadVehiclesPage, type WorkspaceVehicle } from '../api/vehicles'

const { t } = useLocalization()

const props = defineProps<{ organizationId: string; clientId: string; modelValue: string; disabled: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: string]; selected: [vehicle: WorkspaceVehicle] }>()
const query = ref('')
const items = ref<WorkspaceVehicle[]>([])
const selected = ref<WorkspaceVehicle | null>(null)
const selectedElement = ref<HTMLElement | null>(null)
const resultsElement = ref<HTMLElement | null>(null)
const feedbackElement = ref<HTMLElement | null>(null)
const page = ref(1)
const total = ref(0)
const searched = ref(false)
const searching = ref(false)
const loadingSelected = ref(false)
const searchError = ref('')
const selectedError = ref('')
let searchVersion = 0
let selectionVersion = 0

function resetSearch() {
  searchVersion++
  items.value = []
  total.value = 0
  page.value = 1
  searched.value = false
  searching.value = false
  searchError.value = ''
}

async function searchVehicles(requestedPage = 1) {
  if (props.disabled) return
  const version = ++searchVersion
  searching.value = true
  searchError.value = ''
  items.value = []
  searched.value = false
  try {
    const result = await loadVehiclesPage(props.organizationId, requestedPage, 5, {
      searchQuery: query.value.trim(), organizationClientId: props.clientId || undefined
    })
    if (version !== searchVersion) return
    items.value = result.items
    total.value = result.totalCount
    page.value = requestedPage
    searched.value = true
  } catch (caught) {
    if (version === searchVersion) searchError.value = caught instanceof Error ? caught.message : t('inquiries.findVehiclesError')
  } finally {
    if (version === searchVersion) searching.value = false
  }
  await nextTick()
  if (version !== searchVersion) return
  const firstResult = resultsElement.value?.querySelector<HTMLButtonElement>('button')
  if (firstResult) firstResult.focus()
  else feedbackElement.value?.scrollIntoView({ block: 'nearest' })
}

async function loadSelected() {
  const version = ++selectionVersion
  selectedError.value = ''
  loadingSelected.value = false
  if (!props.modelValue) { selected.value = null; return }
  if (selected.value?.id === props.modelValue) return
  selected.value = null
  loadingSelected.value = true
  try {
    const vehicle = await loadVehicle(props.organizationId, props.modelValue)
    if (version === selectionVersion) selected.value = vehicle
  } catch (caught) {
    if (version === selectionVersion) selectedError.value = caught instanceof Error ? caught.message : t('inquiries.selectedVehicleError')
  } finally {
    if (version === selectionVersion) loadingSelected.value = false
  }
}

async function chooseVehicle(vehicle: WorkspaceVehicle) {
  if (props.disabled) return
  selectionVersion++
  selected.value = vehicle
  selectedError.value = ''
  loadingSelected.value = false
  emit('selected', vehicle)
  emit('update:modelValue', vehicle.id)
  query.value = ''
  resetSearch()
  await nextTick()
  selectedElement.value?.focus()
}

function clearSelection() {
  if (props.disabled) return
  selectionVersion++
  selected.value = null
  loadingSelected.value = false
  selectedError.value = ''
  emit('update:modelValue', '')
}

watch(query, resetSearch, { flush: 'sync' })
watch(() => [props.organizationId, props.clientId], () => { query.value = ''; resetSearch() }, { flush: 'sync' })
watch(() => props.organizationId, () => { selected.value = null }, { flush: 'sync' })
watch(() => [props.organizationId, props.modelValue], loadSelected, { immediate: true, flush: 'sync' })
onBeforeUnmount(() => { searchVersion++; selectionVersion++ })
</script>

<template>
  <div class="inquiry-vehicle-picker">
    <div v-if="modelValue" ref="selectedElement" class="inquiry-vehicle-picker__selected" tabindex="-1" :aria-label="t('inquiries.selectedVehicle')">
      <div><span class="inquiry-vehicle-picker__caption">{{ t('inquiries.selectedVehicle') }}</span><strong>{{ loadingSelected ? t('inquiries.loadingVehicle') : selected ? getVehicleDisplayName(selected, true) : t('inquiries.vehicleSelected') }}</strong><small v-if="selected?.organizationClientName">{{ selected.organizationClientName }}</small></div>
      <UiButton variant="ghost" :disabled="disabled" @click="clearSelection">{{ t('inquiries.remove') }}</UiButton>
    </div>
    <p v-if="selectedError" class="inquiry-vehicle-picker__error" role="alert">{{ selectedError }} <button type="button" @click="loadSelected">{{ t('inquiries.retry') }}</button></p>
    <div class="inquiry-vehicle-picker__search">
      <UiInput v-model="query" :label="t('inquiries.searchVehicle')" :placeholder="t('inquiries.vehicleSearchPlaceholder')" :disabled="disabled" @keydown.enter.prevent="searchVehicles()" />
      <UiButton variant="secondary" :disabled="disabled || searching" @click="searchVehicles()">{{ searching ? t('inquiries.searching') : t('inquiries.find') }}</UiButton>
    </div>
    <div ref="feedbackElement" class="inquiry-vehicle-picker__feedback" aria-live="polite" role="status">
      <p v-if="searching">{{ t('inquiries.searchingVehicles') }}</p>
      <p v-else-if="searched && !items.length">{{ t('inquiries.noVehicles') }}</p>
      <p v-else-if="searched">{{ t('inquiries.chooseFoundVehicle', { count: total }) }}</p>
    </div>
    <p v-if="searchError" class="inquiry-vehicle-picker__error" role="alert">{{ searchError }}</p>
    <ul v-if="searched && items.length" ref="resultsElement" class="inquiry-vehicle-picker__results" :aria-label="t('inquiries.foundVehicles')">
      <li v-for="vehicle in items" :key="vehicle.id">
        <button type="button" class="inquiry-vehicle-picker__result" :disabled="disabled" @click="chooseVehicle(vehicle)">
          <span><strong>{{ getVehicleDisplayName(vehicle, true) }}</strong><small v-if="vehicle.organizationClientName">{{ vehicle.organizationClientName }}</small><small v-else-if="vehicle.vin">VIN {{ vehicle.vin }}</small></span>
          <span class="inquiry-vehicle-picker__choose">{{ t('inquiries.select') }} <span aria-hidden="true">→</span></span>
        </button>
      </li>
    </ul>
    <UiPagination :model-value="page" :total-items="searched ? total : 0" :items-per-page="5" :disabled="disabled || searching" :label="t('inquiries.vehiclePages')" @update:model-value="searchVehicles" />
  </div>
</template>

<style scoped>
.inquiry-vehicle-picker { display: grid; gap: 12px; min-width: 0; }
.inquiry-vehicle-picker__search { display: flex; align-items: end; gap: 8px; }
.inquiry-vehicle-picker__search > :first-child { flex: 1; min-width: 0; }
.inquiry-vehicle-picker__selected { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 12px 14px; border: 1px solid var(--crm-line, #e3e6eb); border-left: 3px solid var(--crm-accent, #df6c22); border-radius: 8px; background: var(--crm-surface-soft, #f5f6f8); }
.inquiry-vehicle-picker__selected > div, .inquiry-vehicle-picker__result > span:first-child { display: grid; gap: 5px; min-width: 0; }
.inquiry-vehicle-picker__selected:focus-visible, .inquiry-vehicle-picker__result:focus-visible { outline: 2px solid var(--crm-accent, #df6c22); outline-offset: 2px; }
.inquiry-vehicle-picker small { overflow-wrap: anywhere; }
.inquiry-vehicle-picker__result:focus-visible { outline-offset: -2px; }
.inquiry-vehicle-picker__caption, .inquiry-vehicle-picker small { color: var(--crm-text-muted, #667085); font-size: 12px; }
.inquiry-vehicle-picker strong { font-size: 13px; overflow-wrap: anywhere; }
.inquiry-vehicle-picker__feedback { color: var(--crm-text-muted, #667085); font-size: 12px; }
.inquiry-vehicle-picker__feedback:empty { display: none; }
.inquiry-vehicle-picker__feedback p { margin: 0; }
.inquiry-vehicle-picker__results { display: grid; margin: 0; padding: 0; list-style: none; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 8px; overflow: hidden; }
.inquiry-vehicle-picker__results li + li { border-top: 1px solid var(--crm-line, #e3e6eb); }
.inquiry-vehicle-picker__result { display: flex; align-items: center; justify-content: space-between; gap: 16px; width: 100%; padding: 12px 14px; border: 0; text-align: left; font: inherit; color: var(--crm-text, #263142); background: var(--crm-surface, #fff); cursor: pointer; }
.inquiry-vehicle-picker__result:hover { background: var(--crm-surface-soft, #f5f6f8); }
.inquiry-vehicle-picker__result:disabled { cursor: default; opacity: .6; }
.inquiry-vehicle-picker__choose { flex: 0 0 auto; color: var(--crm-accent, #df6c22); font-size: 12px; }
.inquiry-vehicle-picker__error { margin: 0; color: var(--crm-danger-text, #bd4141); font-size: 12px; }
@media (max-width: 550px) { .inquiry-vehicle-picker__result { gap: 10px; padding: 12px; } .inquiry-vehicle-picker__selected { flex-wrap: wrap; } }
</style>

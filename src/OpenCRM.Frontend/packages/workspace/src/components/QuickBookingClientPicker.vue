<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiInput, UiLoader, UiPhoneInput } from '@opencrm/ui'
import { loadClientLookup, type WorkspaceClientLookupItem } from '../api/clients'

const props = withDefaults(defineProps<{
  organizationId: string
  modelValue: string
  clientName: string
  clientPhone: string
  selectedName?: string
  disabled?: boolean
  canCreate?: boolean
}>(), { selectedName: '', disabled: false, canCreate: true })
const emit = defineEmits<{
  'update:modelValue': [value: string]
  'update:clientName': [value: string]
  'update:clientPhone': [value: string]
}>()
const root = ref<HTMLElement | null>(null)
const query = ref('')
const items = ref<WorkspaceClientLookupItem[]>([])
const selected = ref<WorkspaceClientLookupItem | null>(null)
const isOpen = ref(false)
const isCreating = ref(Boolean(props.clientName || props.clientPhone))
const isLoading = ref(false)
const errorMessage = ref('')
const phoneValidity = ref<boolean | null>(null)
const page = ref(1)
const totalCount = ref(0)
const selectedLabel = computed(() => selected.value?.id === props.modelValue ? selected.value.name : props.selectedName || 'Клиент выбран')
const selectedPhone = computed(() => selected.value?.id === props.modelValue ? selected.value.phone : null)
const name = computed({ get: () => props.clientName, set: value => emit('update:clientName', value) })
const phone = computed({ get: () => props.clientPhone, set: value => { phoneValidity.value = null; emit('update:clientPhone', value) } })
let timer: ReturnType<typeof setTimeout> | undefined
let requestId = 0

function cancelSearch() {
  requestId++
  if (timer !== undefined) clearTimeout(timer)
  timer = undefined
  isLoading.value = false
}

async function search(append = false) {
  if (props.disabled || isCreating.value || props.modelValue) return
  const request = ++requestId
  const organizationId = props.organizationId
  const searchQuery = query.value.trim()
  if (searchQuery.length < 2) {
    items.value = []
    totalCount.value = 0
    isLoading.value = false
    return
  }
  const requestedPage = append ? page.value + 1 : 1
  isLoading.value = true
  errorMessage.value = ''
  try {
    const result = await loadClientLookup(organizationId, searchQuery, requestedPage)
    if (request !== requestId) return
    items.value = append ? [...items.value, ...result.items] : result.items
    page.value = requestedPage
    totalCount.value = result.totalCount
  } catch (error) {
    if (request !== requestId) return
    errorMessage.value = error instanceof Error ? error.message : 'Не удалось найти клиентов.'
  } finally {
    if (request === requestId) isLoading.value = false
  }
}

function openSearch() {
  if (props.disabled || isCreating.value || props.modelValue) return
  isOpen.value = true
  void search()
}

function selectClient(client: WorkspaceClientLookupItem) {
  if (props.disabled) return
  cancelSearch()
  selected.value = client
  emit('update:clientName', '')
  emit('update:clientPhone', '')
  emit('update:modelValue', client.id)
  isOpen.value = false
}

function clearClient() {
  cancelSearch()
  selected.value = null
  isCreating.value = false
  phoneValidity.value = null
  emit('update:modelValue', '')
  emit('update:clientName', '')
  emit('update:clientPhone', '')
  query.value = ''
  errorMessage.value = ''
  isOpen.value = false
}

function startNewClient() {
  if (props.disabled || !props.canCreate) return
  cancelSearch()
  const text = query.value.trim()
  const isPhone = Boolean(text) && /^[+\d\s().-]+$/.test(text)
  emit('update:modelValue', '')
  emit('update:clientPhone', isPhone ? text : '')
  emit('update:clientName', isPhone ? '' : text)
  selected.value = null
  isCreating.value = true
  isOpen.value = false
  errorMessage.value = ''
  phoneValidity.value = null
  void nextTick(() => root.value?.querySelector<HTMLInputElement>('input[type="tel"]')?.focus())
}

function validate() {
  if (props.modelValue) return null
  if (!isCreating.value) return query.value.trim()
    ? props.canCreate ? 'Выберите клиента из списка или нажмите «Новый клиент».' : 'Выберите клиента из списка.'
    : null
  if (!props.clientName.trim() && !props.clientPhone.trim()) return 'Укажите телефон или имя нового клиента.'
  if (props.clientPhone.trim() && phoneValidity.value === false) return 'Проверьте номер телефона нового клиента.'
  return null
}

function focusFirstResult() {
  root.value?.querySelector<HTMLButtonElement>('[data-client-result]')?.focus()
}

function closeOnBlur(event: FocusEvent) {
  if (!event.relatedTarget || !root.value?.contains(event.relatedTarget as Node)) isOpen.value = false
}

watch(query, () => {
  cancelSearch()
  items.value = []
  totalCount.value = 0
  if (props.disabled || isCreating.value || props.modelValue || !isOpen.value) return
  if (query.value.trim().length < 2) return
  isLoading.value = true
  timer = setTimeout(() => { void search() }, 250)
})
watch(() => props.modelValue, value => {
  if (!value) return
  cancelSearch()
  isCreating.value = false
  isOpen.value = false
  emit('update:clientName', '')
  emit('update:clientPhone', '')
})
watch(() => props.organizationId, clearClient)
watch(() => props.disabled, disabled => { if (disabled) { cancelSearch(); isOpen.value = false } })
onBeforeUnmount(cancelSearch)
defineExpose({ validate })
</script>

<template>
  <div ref="root" class="booking-client-picker" @focusout="closeOnBlur">
    <template v-if="modelValue">
      <span class="ui-field-label booking-client-picker__label">Клиент</span>
      <div class="booking-client-picker__selected">
        <div><strong>{{ selectedLabel }}</strong><span v-if="selectedPhone">{{ selectedPhone }}</span></div>
        <UiButton variant="ghost" :disabled="disabled" @click="clearClient">Изменить</UiButton>
      </div>
    </template>
    <template v-else-if="isCreating">
      <div class="booking-client-picker__heading"><strong>Новый клиент</strong><UiButton variant="ghost" :disabled="disabled" @click="clearClient">Отмена</UiButton></div>
      <div class="booking-client-picker__new-fields">
        <UiPhoneInput v-model="phone" :disabled="disabled" label="Телефон" @validity-change="phoneValidity = $event" />
        <UiInput v-model="name" :disabled="disabled" label="Имя" placeholder="Необязательно" :maxlength="120" />
      </div>
    </template>
    <template v-else>
      <div @keydown.down.prevent="focusFirstResult" @keydown.enter.prevent="focusFirstResult" @keydown.esc.stop.prevent="isOpen = false">
        <UiInput v-model="query" label="Клиент" placeholder="Имя или телефон" :disabled="disabled" :maxlength="120" @focusin="openSearch" />
      </div>
      <div v-if="isOpen && !disabled" class="booking-client-picker__results">
        <UiLoader v-if="isLoading && !items.length" compact text="Ищем клиентов…" />
        <p v-else-if="errorMessage" role="alert">{{ errorMessage }} <button type="button" @click="search()">Повторить</button></p>
        <p v-else-if="!items.length">{{ query.trim().length < 2 ? 'Введите имя или телефон' : 'Клиенты не найдены' }}</p>
        <div v-if="items.length" class="booking-client-picker__list" role="group" aria-label="Найденные клиенты">
          <button v-for="client in items" :key="client.id" type="button" data-client-result @click="selectClient(client)">
            <strong>{{ client.name }}</strong><span v-if="client.phone">{{ client.phone }}</span>
          </button>
          <UiButton v-if="items.length < totalCount" variant="ghost" :disabled="isLoading" @click="search(true)">Показать ещё</UiButton>
        </div>
        <UiButton v-if="canCreate" variant="secondary" class="booking-client-picker__create" @click="startNewClient">＋ Новый клиент</UiButton>
      </div>
    </template>
  </div>
</template>

<style scoped>
.booking-client-picker { display: grid; gap: 8px; min-width: 0; }
.booking-client-picker__new-fields { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
.booking-client-picker__label { font-size: 14px; font-weight: 700; }
.booking-client-picker__heading, .booking-client-picker__selected { display: flex; align-items: center; justify-content: space-between; gap: 8px; min-width: 0; }
.booking-client-picker__selected { min-height: 38px; padding: 4px 8px; border: 1px solid var(--crm-line, #d8dfe8); border-radius: 7px; background: var(--crm-surface, #fff); }
.booking-client-picker__selected > div { display: grid; min-width: 0; }
.booking-client-picker__selected strong, .booking-client-picker__selected span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.booking-client-picker__selected span { font-size: 12px; color: var(--crm-text-muted, #64748b); }
.booking-client-picker__results { display: grid; gap: 8px; padding: 8px; border: 1px solid var(--crm-line, #d8dfe8); border-radius: 7px; background: var(--crm-surface, #fff); }
.booking-client-picker__results p { margin: 0; font-size: 13px; }
.booking-client-picker__list { display: grid; gap: 4px; max-height: 240px; overflow-y: auto; }
.booking-client-picker__list > button:not(.ui-button) { display: grid; gap: 3px; width: 100%; padding: 8px; border: 0; border-radius: 4px; background: transparent; color: var(--crm-text, inherit); text-align: left; }
.booking-client-picker__list > button:not(.ui-button):hover, .booking-client-picker__list > button:not(.ui-button):focus-visible { background: var(--crm-surface-soft, #f4f6f8); outline: 2px solid var(--crm-accent, #e87532); outline-offset: -2px; }
.booking-client-picker__list span { font-size: 12px; color: var(--crm-text-muted, #64748b); }
.booking-client-picker__create { justify-self: start; }
@media (max-width: 550px) { .booking-client-picker__new-fields { grid-template-columns: minmax(0, 1fr); } }
</style>

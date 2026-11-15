<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiDatePicker, UiInput, UiModal, UiPagination, UiSelect, UiTextarea, UiTimePicker } from '@opencrm/ui'
import { changeRepeatVisitState, loadRepeatVisitOptions, loadRepeatVisits, saveRepeatVisit, type RepeatVisit, type RepeatVisitSave } from '../api/repeatVisits'
import { inquiryLocalTime, inquiryUtcTime } from '../utils/inquiryTime'

const props = defineProps<{ organizationId: string; clientId?: string; sourceMaintenanceId?: string; canEdit: boolean; compact?: boolean }>()
const listOpen = ref(false)
const items = ref<RepeatVisit[]>([])
const page = ref(1)
const totalCount = ref(0)
const pageSize = 10
const loading = ref(false)
const error = ref('')
const formError = ref('')
const fieldErrors = ref<Record<string, string>>({})
const formElement = ref<HTMLFormElement | null>(null)
const errorSummary = ref<HTMLElement | null>(null)
const busy = ref(false)
const editingId = ref<string | null>(null)
const isCreating = ref(false)
const createCommandId = ref('')
const loadRequest = ref<AbortController | null>(null)
let scopeVersion = 0
const timeZoneId = ref('UTC')
const responsibleOptions = ref<Array<{ value: string; label: string }>>([{ value: '', label: 'Все сотрудники' }])
const canCreateByPermission = ref(false)
const statusMessage = ref('')
const actionError = ref('')
const contactEnabled = ref(false)
const draft = ref({ serviceName: '', reason: '', suggestedVisitDate: '', responsibleUserId: '', contactDate: '', contactTime: '' })
const canCreate = computed(() => props.canEdit && canCreateByPermission.value && Boolean(props.sourceMaintenanceId))
const editingItem = computed(() => items.value.find(item => item.id === editingId.value) ?? null)

function clearDraft() {
  editingId.value = null
  isCreating.value = false
  contactEnabled.value = false
  formError.value = ''
  fieldErrors.value = {}
  draft.value = { serviceName: '', reason: '', suggestedVisitDate: '', responsibleUserId: '', contactDate: '', contactTime: '' }
  createCommandId.value = ''
}

async function load() {
  const org = props.organizationId
  if (!org || (!props.clientId && !props.sourceMaintenanceId)) return
  const currentScope = scopeVersion
  loadRequest.value?.abort()
  const controller = new AbortController()
  loadRequest.value = controller
  loading.value = true
  error.value = ''
  try {
    const [result, options] = await Promise.all([
      loadRepeatVisits(org, { clientId: props.clientId, sourceMaintenanceId: props.sourceMaintenanceId, page: page.value, pageSize }, controller.signal),
      props.canEdit ? loadRepeatVisitOptions(org, controller.signal) : Promise.resolve(null)
    ])
    if (controller.signal.aborted || loadRequest.value !== controller || currentScope !== scopeVersion || org !== props.organizationId) return
    const lastPage = Math.max(1, Math.ceil(result.totalCount / pageSize))
    if (page.value > lastPage) {
      page.value = lastPage
      return
    }
    items.value = result.items
    totalCount.value = result.totalCount
    timeZoneId.value = result.timeZoneId || 'UTC'
    responsibleOptions.value = [{ value: '', label: 'Все сотрудники' }, ...(options?.responsibles ?? []).map(item => ({ value: item.id, label: item.name }))]
    canCreateByPermission.value = options?.canCreate === true
  } catch (caught) {
    if (!controller.signal.aborted && loadRequest.value === controller && currentScope === scopeVersion && org === props.organizationId) error.value = caught instanceof Error ? caught.message : 'Не удалось загрузить рекомендации.'
  } finally {
    if (loadRequest.value === controller && currentScope === scopeVersion) loading.value = false
  }
}

function startCreate() {
  if (!canCreate.value || busy.value) return
  clearDraft()
  isCreating.value = true
  createCommandId.value = crypto.randomUUID()
}

function startEdit(item: RepeatVisit) {
  if (!item.canEdit || busy.value) return
  clearDraft()
  editingId.value = item.id
  isCreating.value = false
  contactEnabled.value = Boolean(item.contactByUtc)
  const contactBy = item.contactByUtc ? inquiryLocalTime(item.contactByUtc, item.timeZoneId) : ''
  draft.value = {
    serviceName: item.serviceName,
    reason: item.reason ?? '',
    suggestedVisitDate: item.suggestedVisitDate,
    responsibleUserId: item.responsibleUserId ?? '',
    contactDate: contactBy.split('T')[0] ?? '',
    contactTime: contactBy.split('T')[1] ?? ''
  }
}

function closeEditor() {
  if (!busy.value) clearDraft()
}

function closeList() {
  if (busy.value) return
  listOpen.value = false
  statusMessage.value = ''
}

async function showFormError(message: string) {
  formError.value = message
  await nextTick()
  const invalidField = formElement.value?.querySelector<HTMLElement>('[aria-invalid="true"]')
  const focusTarget = invalidField ?? errorSummary.value
  focusTarget?.focus()
}

async function save() {
  if (busy.value) return
  fieldErrors.value = {}
  if (!draft.value.serviceName.trim()) fieldErrors.value.serviceName = 'Укажите рекомендуемую услугу.'
  else if (draft.value.serviceName.length > 240) fieldErrors.value.serviceName = 'Не больше 240 символов.'
  if (draft.value.reason.length > 1000) fieldErrors.value.reason = 'Не больше 1000 символов.'
  if (!draft.value.suggestedVisitDate) fieldErrors.value.suggestedVisitDate = 'Выберите предлагаемую дату визита.'
  if (contactEnabled.value && !draft.value.contactDate) fieldErrors.value.contactDate = 'Выберите дату связи с клиентом.'
  if (contactEnabled.value && !draft.value.contactTime) fieldErrors.value.contactTime = 'Выберите время связи с клиентом.'
  if (Object.keys(fieldErrors.value).length) {
    await showFormError('Рекомендация не сохранена. Проверьте выделенные поля.')
    return
  }
  const current = items.value.find(item => item.id === editingId.value)
  let contactByUtc: string | null = current?.contactCompletedAtUtc ? current.contactByUtc ?? null : null
  if (contactEnabled.value && !current?.contactCompletedAtUtc) {
    try {
      contactByUtc = inquiryUtcTime(`${draft.value.contactDate}T${draft.value.contactTime}`, timeZoneId.value)
    } catch (caught) {
      await showFormError(caught instanceof Error ? caught.message : 'Укажите корректный срок связи.')
      return
    }
  }
  const body: RepeatVisitSave = {
    ...(isCreating.value ? { id: createCommandId.value } : {}),
    sourceMaintenanceId: current?.sourceMaintenanceId ?? props.sourceMaintenanceId ?? '',
    serviceName: draft.value.serviceName.trim(), reason: draft.value.reason.trim() || null,
    suggestedVisitDate: draft.value.suggestedVisitDate,
    responsibleUserId: current?.contactCompletedAtUtc ? current.responsibleUserId ?? null : contactEnabled.value ? draft.value.responsibleUserId || null : null,
    contactByUtc,
    ...(current ? { version: current.version } : {})
  }
  busy.value = true
  formError.value = ''
  const org = props.organizationId
  const currentScope = scopeVersion
  try {
    await saveRepeatVisit(org, current?.id ?? null, body)
    if (currentScope !== scopeVersion || org !== props.organizationId) return
    statusMessage.value = 'Рекомендация сохранена.'
    clearDraft()
    page.value = 1
    await load()
  } catch (caught) {
    if (currentScope === scopeVersion && org === props.organizationId) await showFormError(caught instanceof Error ? caught.message : 'Не удалось сохранить рекомендацию.')
  } finally {
    if (currentScope === scopeVersion && org === props.organizationId) busy.value = false
  }
}

async function disable(item: RepeatVisit) {
  if (busy.value || !item.canEdit) return
  busy.value = true
  const org = props.organizationId
  const currentScope = scopeVersion
  actionError.value = ''
  try {
    await changeRepeatVisitState(org, item.id, item.version, !item.isActive)
    if (currentScope !== scopeVersion || org !== props.organizationId) return
    await load()
  } catch (caught) {
    if (currentScope === scopeVersion && org === props.organizationId) actionError.value = caught instanceof Error ? caught.message : 'Не удалось изменить рекомендацию.'
  } finally {
    if (currentScope === scopeVersion && org === props.organizationId) busy.value = false
  }
}

watch(() => [props.organizationId, props.clientId, props.sourceMaintenanceId], () => { scopeVersion++; loadRequest.value?.abort(); listOpen.value = false; clearDraft(); items.value = []; totalCount.value = 0; page.value = 1; busy.value = false; void load() }, { immediate: true })
watch(page, () => { void load() })
onBeforeUnmount(() => { scopeVersion++; loadRequest.value?.abort() })
</script>

<template>
  <UiButton v-if="compact" variant="secondary" aria-haspopup="dialog" :aria-expanded="listOpen" title="Рекомендации по повторному визиту" @click="listOpen = true">Рекомендации <span class="repeat-visits__count">{{ error ? '!' : loading && !items.length ? '…' : totalCount }}</span></UiButton>
  <component :is="compact ? UiModal : 'div'" v-bind="compact ? { open: listOpen && !isCreating && !editingId, title: 'Рекомендации по повторному визиту' } : {}" @close="closeList">
    <section class="repeat-visits" :class="{ 'repeat-visits--modal': compact }" aria-label="Рекомендации по повторному визиту" :aria-busy="loading || busy">
      <header v-if="!compact || canCreate" class="repeat-visits__header" :class="{ 'ui-section-header': !compact }">
        <div v-if="!compact"><h3>Рекомендации по повторному визиту</h3><p v-if="clientId">Рекомендации по возвращению клиента — по желанию.</p></div>
        <UiButton v-if="canCreate && !isCreating && !editingId" variant="secondary" @click="startCreate">Добавить рекомендацию</UiButton>
      </header>
      <p v-if="error" class="repeat-visits__message" role="alert">{{ error }} <button type="button" @click="load">Повторить</button></p>
      <p v-if="actionError" class="repeat-visits__message" role="alert">{{ actionError }}</p>
      <p v-if="statusMessage" class="repeat-visits__message" role="status">{{ statusMessage }}</p>
      <p v-if="!loading && !error && !items.length && !isCreating" class="repeat-visits__empty">Рекомендаций пока нет.</p>
      <article v-for="item in items" :key="item.id" class="repeat-visits__item">
        <div><strong>{{ item.serviceName }}</strong><span>{{ item.vehicleName }} · предлагаемый визит {{ item.suggestedVisitDate.split('-').reverse().join('.') }}</span><p v-if="item.reason">{{ item.reason }}</p><small v-if="item.contactByUtc">Связаться до {{ new Intl.DateTimeFormat('ru-RU', { timeZone: item.timeZoneId, dateStyle: 'short', timeStyle: 'short' }).format(new Date(item.contactByUtc)) }}<template v-if="item.responsibleName"> · {{ item.responsibleName }}</template><template v-else-if="!item.responsibleUserId"> · Все сотрудники</template><template v-if="item.contactCompletedAtUtc"> · контакт выполнен</template></small><small v-if="item.bookedMaintenanceId">Уже записан <a :href="`#maintenance/${item.bookedMaintenanceId}`">Открыть запись</a></small><small v-else-if="!item.isActive">Отключена</small></div>
        <div class="repeat-visits__actions"><UiButton v-if="item.canBook && !item.bookedMaintenanceId" variant="secondary" :href="`#maintenance/new?repeatVisit=${encodeURIComponent(item.id)}`">Создать запись</UiButton><template v-if="canEdit && item.canEdit"><UiButton variant="secondary" :disabled="busy" @click="startEdit(item)">Изменить</UiButton><UiButton variant="secondary" :disabled="busy" @click="disable(item)">{{ item.isActive ? 'Отключить' : 'Включить' }}</UiButton></template></div>
      </article>
      <UiPagination v-if="totalCount > pageSize && !isCreating && !editingId" v-model="page" :total-items="totalCount" :items-per-page="pageSize" label="Рекомендации повторных визитов" />
    </section>
  </component>
  <UiModal :open="isCreating || Boolean(editingId)" :title="isCreating ? 'Новая рекомендация' : 'Изменить рекомендацию'" @close="closeEditor">
    <form ref="formElement" class="repeat-visits__form" novalidate @submit.prevent="save">
      <UiInput v-model="draft.serviceName" label="Рекомендуемая услуга" maxlength="240" :error="fieldErrors.serviceName" :disabled="busy" required />
      <UiTextarea v-model="draft.reason" label="Почему стоит вернуться" :maxlength="1000" :error="fieldErrors.reason" :disabled="busy" />
      <UiDatePicker v-model="draft.suggestedVisitDate" label="Предлагаемая дата визита" :error="fieldErrors.suggestedVisitDate" :disabled="busy" required />
      <label class="repeat-visits__toggle"><input v-model="contactEnabled" type="checkbox" :disabled="busy || Boolean(editingItem?.contactCompletedAtUtc)"> Напомнить связаться с клиентом</label>
      <div v-if="contactEnabled" class="repeat-visits__subform">
        <p>{{ draft.responsibleUserId ? 'Действие появится в рабочем дне выбранного сотрудника.' : 'Без ответственного действие видно всем сотрудникам с доступом к клиентам.' }}</p>
        <UiSelect v-model="draft.responsibleUserId" label="Ответственный сотрудник (необязательно)" placeholder="Все сотрудники" :options="responsibleOptions" :disabled="busy || Boolean(editingItem?.contactCompletedAtUtc)" />
        <div class="repeat-visits__deadline">
          <UiDatePicker v-model="draft.contactDate" label="Связаться с клиентом до" :error="fieldErrors.contactDate" :disabled="busy || Boolean(editingItem?.contactCompletedAtUtc)" required />
          <div class="repeat-visits__time-field">
            <UiTimePicker v-model="draft.contactTime" label="Время" align="end" :disabled="busy || Boolean(editingItem?.contactCompletedAtUtc)" required />
            <span v-if="fieldErrors.contactTime" class="ui-field__error" role="alert">{{ fieldErrors.contactTime }}</span>
          </div>
        </div>
      </div>
      <p v-if="formError" ref="errorSummary" class="repeat-visits__error" role="alert" tabindex="-1">{{ formError }}</p>
      <div class="repeat-visits__actions"><UiButton type="submit" :disabled="busy">{{ busy ? 'Сохраняем…' : 'Сохранить' }}</UiButton><UiButton type="button" variant="secondary" :disabled="busy" @click="closeEditor">Отмена</UiButton></div>
    </form>
  </UiModal>
</template>

<style scoped>
.repeat-visits { display:grid; gap:12px; min-width:0; padding:14px; border:1px solid var(--crm-line,#dce1e8); border-radius:9px; background:var(--crm-surface,#fff); color:var(--crm-text,#202733); }
.repeat-visits.repeat-visits--modal { padding:0; border:0; max-height:max(160px,calc(100dvh - 180px)); overflow-y:auto; }
.repeat-visits__count { min-width:20px; padding:2px 5px; border-radius:5px; background:var(--crm-surface-soft,#f1f3f5); color:var(--crm-text,#263142); font-size:11px; text-align:center; }
.repeat-visits__header,.repeat-visits__item,.repeat-visits__actions { display:flex; align-items:flex-start; justify-content:space-between; flex-wrap:wrap; gap:10px; }
.repeat-visits__header h3 { margin:0; font-size:14px; }.repeat-visits__header p,.repeat-visits__empty,.repeat-visits__item span,.repeat-visits__item p,.repeat-visits__item small,.repeat-visits__message,.repeat-visits__subform p { color:var(--crm-text-muted,#667085); font-size:12px; line-height:1.5; }
.repeat-visits__header p,.repeat-visits__item p,.repeat-visits__item span,.repeat-visits__item small { display:block; margin:4px 0 0; }.repeat-visits__empty,.repeat-visits__message { margin:0; }
.repeat-visits__item { padding:11px 0; border-top:1px solid var(--crm-line,#e6e9ee); }.repeat-visits__item>div:first-child { min-width:0; overflow-wrap:anywhere; }.repeat-visits__form { display:grid; gap:12px; min-width:0; }
.repeat-visits__deadline { display:grid; grid-template-columns:minmax(0,1fr) minmax(0,1fr); gap:10px; }
.repeat-visits__time-field { display:grid; align-content:start; gap:4px; min-width:0; }
.repeat-visits__error { margin:0; padding:10px 12px; border:1px solid var(--crm-danger-border,#f5c6c1); border-radius:6px; background:var(--crm-danger-surface,#fff1ef); color:var(--crm-danger-text,#b42318); font-size:13px; line-height:1.5; }
.repeat-visits__toggle { display:flex; align-items:center; gap:8px; font-size:13px; }.repeat-visits__toggle input { accent-color:var(--crm-header-accent,#e87532); }.repeat-visits__subform { display:grid; gap:10px; padding:12px; border-radius:8px; background:var(--crm-surface-soft,#f5f6f8); }.repeat-visits__subform p { margin:0; }
@media(max-width:600px) { .repeat-visits { padding:12px; }.repeat-visits__header { align-items:stretch; flex-direction:column; }.repeat-visits__actions { width:100%; }.repeat-visits__actions :deep(.ui-button) { flex:1 1 auto; }.repeat-visits__deadline { grid-template-columns:minmax(0,1fr); } }
</style>

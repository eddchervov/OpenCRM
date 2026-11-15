<script setup lang="ts">
import { computed, defineAsyncComponent, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { UiButton, UiCard, UiDatePicker, UiTimePicker, UiInput, UiLoader, UiPhoneInput, UiSelect, UiTextarea } from '@opencrm/ui'
import type { WorkspaceContext } from '../types'
import { changeInquiryStage, closeInquiry, completeInquiryAction, inquirySources, inquiryStages, InquiryRequestError, loadInquiry, loadInquiryHistory, saveInquiry, type Inquiry, type InquiryHistory } from '../api/inquiries'
import { loadClientOptions, type WorkspaceClientOption } from '../api/clients'
import InquiryVehiclePicker from './InquiryVehiclePicker.vue'
import InquiryTimeline from './InquiryTimeline.vue'
import InquiryNoteComposer from './InquiryNoteComposer.vue'
import { createMemberSelectOptions } from '../utils/memberSelectOptions'
import { formatInquiryTime, inquiryLocalTime, inquiryUtcTime, resolveInquiryTimeZone } from '../utils/inquiryTime'

const props = defineProps<{ context: WorkspaceContext; inquiryId: string; canCreate: boolean; canViewClients: boolean; canViewVehicles: boolean; timeZoneId: string | null }>()
const emit = defineEmits<{ changed: [] }>()
const inquiry = ref<Inquiry | null>(null)
const InquiryHistoryDialog = defineAsyncComponent(() => import('./InquiryHistoryDialog.vue'))
const historyOpen = ref(false)
const editing = ref(props.inquiryId === 'new')
const loading = ref(false)
const busy = ref(false)
const error = ref('')
const conflict = ref(false)
const stage = ref('new')
const stageComment = ref('')
const notesBusy = ref(false)
const working = computed(() => busy.value || notesBusy.value || loading.value)
const recentHistory = ref<InquiryHistory | null>(null)
const historyLoading = ref(false)
const historyError = ref('')
let recentRequest: AbortController | undefined
const closing = ref(false)
const closeReason = ref('')
const clients = ref<WorkspaceClientOption[]>([])
const linksError = ref('')
const linksOpen = ref(false)
const formElement = ref<HTMLFormElement>()
const form = reactive({ contactName: '', contactPhone: '', interestText: '', sourceCode: 'phone', organizationClientId: '', vehicleId: '', vehicleDescription: '', responsibleUserId: '', nextActionText: '', nextActionDate: '', nextActionTime: '' })
const isNew = computed(() => props.inquiryId === 'new')
const organizationZone = computed(() => inquiry.value ? inquiry.value.timeZoneId : props.timeZoneId)
const zone = computed(() => resolveInquiryTimeZone(organizationZone.value))
const open = computed(() => !inquiry.value || !['closed', 'booked'].includes(inquiry.value.stageCode))
const canManage = computed(() => (isNew.value ? props.canCreate : inquiry.value?.canManage) && props.context.workspaceAccess?.isReadOnly !== true)
const memberOptions = computed(() => [{ value: '', label: 'Без ответственного' }, ...createMemberSelectOptions((props.context.organization.members ?? []).map(member => ({ ...member, value: member.userId })))])
const clientOptions = computed(() => [{ value: '', label: 'Без связи с клиентом' }, ...clients.value.map(client => ({ value: client.id, label: [client.name, client.phone || client.details].filter(Boolean).join(' · ') }))])
const stageLabel = computed(() => inquiryStages.find(item => item.value === inquiry.value?.stageCode)?.label ?? '')
const zoneLabel = computed(() => new Intl.DateTimeFormat('ru-RU', { timeZone: zone.value, timeZoneName: 'long' }).formatToParts(new Date()).find(part => part.type === 'timeZoneName')?.value ?? zone.value)
const sourceLabel = computed(() => inquirySources.find(item => item.value === inquiry.value?.sourceCode)?.label || inquiry.value?.sourceCode || 'Не указан')

function updateFromHistory(updated: Inquiry) {
  inquiry.value = updated
  emit('changed')
}

function fill() {
  if (!inquiry.value) return
  const localTime = inquiryLocalTime(inquiry.value.nextActionAt, zone.value)
  for (const key of Object.keys(form) as Array<keyof typeof form>) {
    form[key] = key === 'nextActionDate' ? localTime.slice(0, 10) : key === 'nextActionTime' ? localTime.slice(11, 16) : String(inquiry.value[key] ?? '')
  }
  linksOpen.value = !!(form.organizationClientId || form.vehicleId)
  stage.value = inquiry.value.stageCode
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    inquiry.value = await loadInquiry(props.context.organization.id, props.inquiryId)
    fill()
    conflict.value = false
  } catch (caught) { error.value = caught instanceof Error ? caught.message : 'Не удалось открыть обращение.' }
  finally { loading.value = false }
}

async function refreshAfterConflict() {
  const draft = { ...form }
  const targetStage = stage.value
  const comment = stageComment.value
  const wereLinksOpen = linksOpen.value
  await load()
  if (editing.value) Object.assign(form, draft)
  stage.value = targetStage
  linksOpen.value = wereLinksOpen
  await nextTick()
  stageComment.value = comment
}

async function loadLinks() {
  linksError.value = ''
  try {
    if (props.canViewClients || inquiry.value?.canViewClients) clients.value = await loadClientOptions(props.context.organization.id)
  } catch (caught) { linksError.value = caught instanceof Error ? caught.message : 'Не удалось загрузить связанные данные.' }
}

async function edit() { fill(); editing.value = true; await loadLinks() }

async function run(action: () => Promise<Inquiry>, after?: () => void) {
  if (working.value) return
  busy.value = true
  error.value = ''
  try {
    inquiry.value = await action()
    fill()
    after?.()
    emit('changed')
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Не удалось сохранить обращение.'
    conflict.value = caught instanceof InquiryRequestError && caught.status === 409
  } finally { busy.value = false }
}

async function save() {
  if (!formElement.value?.reportValidity()) return
  let nextActionAt: string | null
  if (!!form.nextActionDate !== !!form.nextActionTime) { error.value = 'Укажите дату и время следующего действия.'; return }
  try { nextActionAt = inquiryUtcTime(form.nextActionDate ? `${form.nextActionDate}T${form.nextActionTime}` : '', zone.value, inquiry.value?.nextActionAt) }
  catch (caught) { error.value = (caught as Error).message; return }
  if (!!form.nextActionText.trim() !== !!nextActionAt) { error.value = 'Укажите действие и срок вместе.'; return }
  const { nextActionDate, nextActionTime, ...fields } = form
  const payload = { ...fields, organizationClientId: form.organizationClientId || null, vehicleId: form.vehicleId || null, responsibleUserId: form.responsibleUserId || null, nextActionText: form.nextActionText.trim() || null, nextActionAt, version: inquiry.value?.version }
  if (isNew.value) {
    busy.value = true
    error.value = ''
    try {
      const created = await saveInquiry(props.context.organization.id, null, payload)
      emit('changed')
      window.location.hash = `#inquiries/${created.id}`
    } catch (caught) { error.value = caught instanceof Error ? caught.message : 'Не удалось создать обращение.' }
    finally { busy.value = false }
    return
  }
  await run(() => saveInquiry(props.context.organization.id, props.inquiryId, payload), () => { editing.value = false })
}

async function loadRecentHistory() {
  if (!inquiry.value) return
  recentRequest?.abort()
  const active = new AbortController()
  recentRequest = active
  historyLoading.value = true
  historyError.value = ''
  try {
    const result = await loadInquiryHistory(props.context.organization.id, inquiry.value.id, 1, active.signal, 3)
    if (!active.signal.aborted) recentHistory.value = result
  } catch (caught) {
    if (!active.signal.aborted) historyError.value = caught instanceof Error ? caught.message : 'Не удалось загрузить последние события.'
  } finally {
    if (recentRequest === active) historyLoading.value = false
  }
}

async function applyStage() {
  if (!inquiry.value || stage.value === inquiry.value.stageCode) return
  await run(() => changeInquiryStage(props.context.organization.id, props.inquiryId, inquiry.value!.version, stage.value, stageComment.value), () => { stageComment.value = '' })
}

watch(stage, () => { stageComment.value = '' })
watch(() => [inquiry.value?.id, inquiry.value?.version, editing.value], () => {
  if (editing.value) recentRequest?.abort()
  else if (inquiry.value) void loadRecentHistory()
})
onBeforeUnmount(() => recentRequest?.abort())

watch(() => [props.canViewClients, props.canViewVehicles], () => { if (editing.value) void loadLinks() })

onMounted(async () => { if (!isNew.value) await load(); else await loadLinks() })
</script>

<template>
  <UiCard page :title="isNew ? 'Новое обращение' : inquiry?.contactName || 'Обращение'">
    <div class="inquiry-detail">
      <UiLoader v-if="loading" />
      <p v-if="error" class="inquiry-detail__error" role="alert">{{ error }}</p>
      <div v-if="conflict" class="inquiry-detail__conflict"><span>Данные изменились. Черновик сохранён в форме.</span><UiButton variant="secondary" @click="refreshAfterConflict" :disabled="working">Загрузить актуальные данные</UiButton></div>
      <form v-if="editing" ref="formElement" class="inquiry-detail__editor" @submit.prevent="save">
        <fieldset :disabled="working || !canManage">
          <div class="inquiry-detail__columns">
            <div class="inquiry-detail__main">
              <section class="inquiry-panel">
                <header class="inquiry-panel__header"><h3>Контакт и запрос</h3></header>
                <div class="inquiry-detail__fields">
                  <UiInput v-model="form.contactName" label="Контакт" required :maxlength="160" />
                  <UiPhoneInput v-model="form.contactPhone" label="Телефон" />
                  <UiTextarea v-model="form.interestText" label="Что интересует клиента" required :maxlength="4000" class="inquiry-detail__wide" />
                  <UiInput v-model="form.vehicleDescription" label="Автомобиль со слов клиента" :maxlength="500" />
                  <UiSelect v-model="form.sourceCode" label="Источник" :options="inquirySources" />
                </div>
              </section>
              <details v-if="canViewClients || inquiry?.canViewClients || canViewVehicles || inquiry?.canViewVehicles" class="inquiry-panel inquiry-links" :open="linksOpen" @toggle="linksOpen = ($event.target as HTMLDetailsElement).open">
                <summary>Связи с клиентом и автомобилем в базе</summary>
                <div class="inquiry-detail__fields">
                  <UiSelect v-if="canViewClients || inquiry?.canViewClients" v-model="form.organizationClientId" label="Клиент" :options="clientOptions" class="inquiry-detail__wide" @update:model-value="form.vehicleId = ''" />
                  <InquiryVehiclePicker v-if="canViewVehicles || inquiry?.canViewVehicles" v-model="form.vehicleId" :organization-id="context.organization.id" :client-id="form.organizationClientId" :disabled="working || !canManage" class="inquiry-detail__wide" />
                </div>
              </details>
              <p v-if="linksError" role="alert" class="inquiry-detail__error">{{ linksError }}</p>
            </div>
            <section class="inquiry-panel inquiry-panel--work">
              <header class="inquiry-panel__header"><h3>Следующее действие</h3></header>
              <div class="inquiry-detail__work-fields">
                <UiSelect v-model="form.responsibleUserId" label="Ответственный" :options="memberOptions" />
                <UiInput v-model="form.nextActionText" label="Что сделать" placeholder="Например, согласовать стоимость" :maxlength="500" />
                <div class="inquiry-detail__deadline"><UiDatePicker v-model="form.nextActionDate" label="Дата" :disabled="working || !canManage" :required="!!form.nextActionText.trim()" /><UiTimePicker v-model="form.nextActionTime" label="Время" placeholder="ЧЧ:ММ" align="end" :disabled="working || !canManage" :required="!!form.nextActionText.trim()" /></div>
                <small class="inquiry-panel__meta">{{ organizationZone ? 'Время организации' : 'Ваше местное время' }} · {{ zoneLabel }}</small>
              </div>
            </section>
          </div>
        </fieldset>
        <footer class="inquiry-detail__editor-actions"><UiButton v-if="canManage" type="submit" :disabled="working">{{ working ? 'Сохраняем…' : isNew ? 'Создать обращение' : 'Сохранить изменения' }}</UiButton><UiButton v-if="!isNew" type="button" variant="secondary" :disabled="working" @click="editing = false">Отмена</UiButton><UiButton v-else variant="secondary" href="#inquiries">Отмена</UiButton></footer>
      </form>
      <template v-else-if="inquiry">
        <div class="inquiry-detail__toolbar">
          <span class="inquiry-detail__stage" :class="'inquiry-detail__stage--' + inquiry.stageCode"><span aria-hidden="true"></span>{{ stageLabel }}</span>
          <div class="inquiry-detail__actions">
            <UiButton variant="secondary" :disabled="working" @click="historyOpen = true"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 8a9 9 0 1 1-1 8M4 3v5h5M12 7v5l3 2" /></svg>История и заметки</UiButton>
            <UiButton v-if="canManage && open" variant="secondary" :disabled="working" @click="edit">Изменить</UiButton>
            <UiButton v-if="inquiry.canCreateBooking && open" :href="'#maintenance/new?inquiry=' + inquiry.id" :disabled="working">Создать запись</UiButton>
            <UiButton v-if="inquiry.maintenanceId && inquiry.canViewLinkedMaintenance" :href="'#maintenance/' + inquiry.maintenanceId">Открыть запись</UiButton>
          </div>
        </div>
        <div class="inquiry-detail__columns" :class="{ 'inquiry-detail__columns--complete': !open }">
          <div class="inquiry-detail__main">
            <section class="inquiry-panel inquiry-request">
              <header class="inquiry-panel__header ui-form-header"><h3>Запрос клиента</h3><span class="inquiry-panel__meta">Источник · {{ sourceLabel }}</span></header>
              <div class="inquiry-request__body">
                <p class="inquiry-request__text">{{ inquiry.interestText }}</p>
                <dl class="inquiry-detail__summary">
                  <div><dt>Телефон</dt><dd><a v-if="inquiry.contactPhone" :href="'tel:' + inquiry.contactPhone">{{ inquiry.contactPhone }}</a><span v-else class="inquiry-detail__missing">—</span></dd></div>
                  <div><dt>Автомобиль</dt><dd><a v-if="inquiry.vehicleId && inquiry.canViewLinkedVehicle" :href="'#vehicles/' + inquiry.vehicleId">{{ inquiry.vehicleName || 'Карточка автомобиля' }}</a><span v-else :class="{ 'inquiry-detail__missing': !inquiry.vehicleDescription }">{{ inquiry.vehicleDescription || '—' }}</span><small v-if="inquiry.vehicleId && inquiry.vehicleDescription">{{ inquiry.vehicleDescription }}</small></dd></div>
                  <div v-if="inquiry.organizationClientId && inquiry.canViewLinkedClient"><dt>Клиент в базе</dt><dd><a :href="'#clients/' + inquiry.organizationClientId">{{ inquiry.organizationClientName || 'Карточка клиента' }}</a></dd></div>
                </dl>
                <div v-if="inquiry.closeReason" class="inquiry-request__closed"><span>Причина закрытия</span><p>{{ inquiry.closeReason }}</p></div>
              </div>
            </section>
          </div>
          <aside v-if="open" class="inquiry-panel inquiry-panel--work">
            <header class="inquiry-panel__header"><h3>Следующее действие</h3><button v-if="canManage && !inquiry.nextActionText" type="button" class="inquiry-detail__text-button" :disabled="working" @click="edit">Назначить</button></header>
            <div class="inquiry-next">
              <strong :class="{ 'inquiry-detail__missing': !inquiry.nextActionText }">{{ inquiry.nextActionText || 'Не назначено' }}</strong>
              <p v-if="inquiry.nextActionAt" class="inquiry-next__date"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></svg>{{ formatInquiryTime(inquiry.nextActionAt, zone) }}<span :title="zone">{{ zoneLabel }}</span></p>
              <div class="inquiry-next__owner"><span>Ответственный</span><strong>{{ inquiry.responsibleUserName || '—' }}</strong></div>
              <UiButton v-if="canManage && inquiry.nextActionText" variant="secondary" :disabled="working" @click="run(() => completeInquiryAction(context.organization.id, inquiryId, inquiry!.version))"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 4 4L19 6" /></svg>Отметить выполненным</UiButton>
            </div>
            <template v-if="canManage">
              <form class="inquiry-stage-form" @submit.prevent="applyStage">
                <UiSelect v-model="stage" label="Перевести на этап" :options="inquiryStages.slice(0, 4)" :disabled="working" />
                <UiTextarea v-if="stage !== inquiry.stageCode || stageComment" v-model="stageComment" label="Комментарий к переходу" placeholder="Почему меняем этап" :rows="3" :maxlength="1000" :disabled="working" />
                <UiButton type="submit" variant="secondary" :disabled="working || stage === inquiry.stageCode">Применить</UiButton>
              </form>
              <div class="inquiry-close">
                <button v-if="!closing" type="button" class="inquiry-detail__text-button" :disabled="working" @click="closing = true">Закрыть обращение</button>
                <form v-else @submit.prevent="run(() => closeInquiry(context.organization.id, inquiryId, inquiry!.version, closeReason), () => { closing = false })"><UiTextarea v-model="closeReason" label="Причина закрытия" required :maxlength="1000" :disabled="working" /><div><UiButton type="submit" variant="secondary" :disabled="working || !closeReason.trim()">Подтвердить</UiButton><UiButton variant="ghost" :disabled="working" @click="closing = false">Отмена</UiButton></div></form>
              </div>
            </template>
          </aside>
        </div>
        <section class="inquiry-panel inquiry-activity" aria-label="Заметки и история">
          <header class="inquiry-panel__header"><h3>Заметки и история</h3><UiButton variant="ghost" :disabled="working" @click="historyOpen = true">Вся история<span v-if="recentHistory" class="inquiry-activity__count">{{ recentHistory.totalCount }}</span></UiButton></header>
          <InquiryNoteComposer :context="context" :inquiry="inquiry" :disabled="working" @busy="notesBusy = $event" @updated="updateFromHistory" />
          <div v-if="historyError" class="inquiry-detail__error" role="alert"><p>{{ historyError }}</p><UiButton variant="secondary" @click="loadRecentHistory">Повторить</UiButton></div>
          <UiLoader v-if="historyLoading" />
          <InquiryTimeline v-else-if="recentHistory && !historyError" :events="recentHistory.items" :time-zone-id="inquiry.timeZoneId" :members="context.organization.members" compact />
        </section>
      </template>
    </div>
  </UiCard>
  <InquiryHistoryDialog v-if="historyOpen && inquiry" :context="context" :inquiry="inquiry" @close="historyOpen = false" @updated="updateFromHistory" />
</template>

<style scoped>
.inquiry-detail { display: grid; gap: 20px; min-width: 0; color: var(--crm-text, #263142); }
.inquiry-detail__summary a { color: var(--crm-accent, #df6c22); }
.inquiry-detail__toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; padding-bottom: 18px; border-bottom: 1px solid var(--crm-line, #e3e6eb); }
.inquiry-detail__actions, .inquiry-detail__editor-actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.inquiry-detail__actions svg, .inquiry-next svg { width: 16px; height: 16px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; flex: 0 0 auto; }
.inquiry-detail__actions :deep(.ui-button) { gap: 7px; }
.inquiry-detail__stage { display: inline-flex; align-items: center; gap: 7px; padding: 7px 10px; border-radius: 7px; background: var(--crm-surface-soft, #f5f6f8); color: var(--crm-text-muted, #667085); font-size: 12px; font-weight: 600; }
.inquiry-detail__stage > span { width: 6px; height: 6px; border-radius: 50%; background: currentColor; }
.inquiry-detail__stage--clarifying, .inquiry-detail__stage--proposal, .inquiry-detail__stage--ready { color: var(--crm-accent, #df6c22); background: color-mix(in srgb, var(--crm-accent, #df6c22) 10%, var(--crm-surface, #fff)); }
.inquiry-detail__columns { display: grid; grid-template-columns: minmax(0, 1.7fr) minmax(300px, 1fr); gap: 20px; align-items: stretch; }
.inquiry-detail__columns--complete { grid-template-columns: minmax(0, 1fr); }
.inquiry-detail__main { display: grid; gap: 16px; min-width: 0; }
.inquiry-panel { min-width: 0; padding: 20px; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 12px; background: var(--crm-surface, #fff); }
.inquiry-panel__header { display: flex; justify-content: space-between; align-items: center; gap: 10px 16px; margin-bottom: 18px; flex-wrap: wrap; }
.inquiry-panel__header h3 { margin: 0; font-size: 14px; font-weight: 650; }
.inquiry-panel__meta { color: var(--crm-text-muted, #667085); font-size: 12px; }
.inquiry-panel--work { background: var(--crm-surface-soft, #f5f6f8); }
.inquiry-request { display: flex; flex-direction: column; padding: 0; overflow: hidden; }
.inquiry-request > .inquiry-panel__header { margin: 0; padding: 16px 20px; }
.inquiry-request__body { display: flex; flex: 1; flex-direction: column; gap: 24px; padding: 20px; }
.inquiry-request__body > .inquiry-detail__summary { margin-top: auto; }
.inquiry-request__text { margin: 0; font-size: 15px; line-height: 1.65; white-space: pre-wrap; overflow-wrap: anywhere; }
.inquiry-detail__summary { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 18px 24px; padding-top: 18px; margin: 0; border-top: 1px solid var(--crm-line, #e3e6eb); }
.inquiry-detail__summary dt { color: var(--crm-text-muted, #667085); font-size: 12px; margin-bottom: 7px; }
.inquiry-detail__summary dd { margin: 0; font-size: 14px; overflow-wrap: anywhere; }
.inquiry-detail__summary dd small { display: block; margin-top: 5px; color: var(--crm-text-muted, #667085); font-size: 12px; }
.inquiry-detail__missing { color: var(--crm-text-muted, #667085); font-weight: 400; }
.inquiry-request__closed { margin-top: 0; padding: 12px 14px; border-left: 2px solid var(--crm-line, #e3e6eb); background: var(--crm-surface-soft, #f5f6f8); font-size: 13px; }
.inquiry-request__closed > span { color: var(--crm-text-muted, #667085); font-size: 12px; }
.inquiry-request__closed p { margin: 6px 0 0; white-space: pre-wrap; overflow-wrap: anywhere; }
.inquiry-next { display: grid; gap: 14px; }
.inquiry-next > strong { font-size: 14px; line-height: 1.5; overflow-wrap: anywhere; }
.inquiry-next__date { display: flex; align-items: center; gap: 7px; flex-wrap: wrap; margin: 0; font-size: 12px; }
.inquiry-next__date > span { color: var(--crm-text-muted, #667085); }
.inquiry-next__owner { display: grid; gap: 5px; font-size: 13px; overflow-wrap: anywhere; }
.inquiry-next__owner > span { color: var(--crm-text-muted, #667085); font-size: 12px; }
.inquiry-next__owner strong { font-weight: 500; }
.inquiry-next :deep(.ui-button) { display: inline-flex; align-items: center; justify-content: center; gap: 7px; }
.inquiry-stage-form { display: grid; grid-template-columns: minmax(0, 1fr); gap: 12px; padding-top: 18px; margin-top: 18px; border-top: 1px solid var(--crm-line, #e3e6eb); }
.inquiry-stage-form > :last-child { justify-self: end; }
.inquiry-activity { display: grid; gap: 18px; }
.inquiry-activity .inquiry-panel__header { margin-bottom: 0; }
.inquiry-activity__count { min-width: 20px; padding: 2px 6px; border-radius: 5px; background: var(--crm-surface-soft, #f5f6f8); color: var(--crm-text-muted, #667085); font-size: 11px; font-weight: 600; text-align: center; }
.inquiry-close { padding-top: 16px; }
.inquiry-close form { display: grid; gap: 12px; }
.inquiry-close form > div { display: flex; gap: 8px; }
.inquiry-detail__text-button { padding: 0; border: 0; background: none; color: var(--crm-text-muted, #667085); cursor: pointer; font: inherit; font-size: 12px; text-align: left; }
.inquiry-detail__text-button:hover { color: var(--crm-accent, #df6c22); }
.inquiry-detail__editor { display: grid; gap: 20px; }
.inquiry-detail__editor fieldset { border: 0; padding: 0; margin: 0; min-width: 0; }
.inquiry-detail__fields { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; }
.inquiry-detail__work-fields { display: grid; gap: 16px; }
.inquiry-detail__deadline { display: grid; grid-template-columns: minmax(0, 1.3fr) minmax(0, 1fr); gap: 12px; }
.inquiry-detail__wide { grid-column: 1 / -1; }
.inquiry-links summary { font-size: 13px; font-weight: 600; cursor: pointer; }
.inquiry-links[open] summary { margin-bottom: 18px; }
.inquiry-detail__editor-actions { padding-top: 18px; border-top: 1px solid var(--crm-line, #e3e6eb); }
.inquiry-detail__error { color: var(--crm-danger, #dc5454); }
.inquiry-detail__conflict { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
@media (max-width: 900px) { .inquiry-detail__columns { grid-template-columns: minmax(0, 1fr); } }
@media (max-width: 550px) { .inquiry-detail { gap: 16px; } .inquiry-panel { padding: 16px; } .inquiry-request { padding: 0; } .inquiry-request > .inquiry-panel__header, .inquiry-request__body { padding: 16px; } .inquiry-detail__fields { grid-template-columns: minmax(0, 1fr); } .inquiry-detail__toolbar { align-items: start; } .inquiry-detail__actions { width: 100%; } .inquiry-detail__summary { grid-template-columns: minmax(0, 1fr); } .inquiry-stage-form { grid-template-columns: minmax(0, 1fr); } }
</style>

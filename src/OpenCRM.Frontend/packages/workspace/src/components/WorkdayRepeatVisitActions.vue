<script setup lang="ts">
import { formattingCulture, translate as t } from '@opencrm/ui/i18n'
import '../workdayActionMessages'
import { onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiDatePicker, UiModal, UiTimePicker, parseDateOnly } from '@opencrm/ui'
import { completeRepeatVisitAction, loadRepeatVisits, rescheduleRepeatVisitAction, type RepeatVisit, type RepeatVisitPage } from '../api/repeatVisits'
import { inquiryLocalTime, inquiryUtcTime } from '../utils/inquiryTime'

const props = defineProps<{ organizationId: string; userId: string; date: string }>()
const result = ref<RepeatVisitPage | null>(null)
const error = ref('')
const actionError = ref('')
const isOpen = ref(false)
const busyId = ref('')
const page = ref(1)
const rescheduleId = ref('')
const rescheduleDraft = ref({ date: '', time: '' })
let request: AbortController | undefined
let scopeVersion = 0

async function load() {
  request?.abort()
  const active = new AbortController()
  request = active
  const currentScope = scopeVersion
  error.value = ''
  result.value = null
  actionError.value = ''
  try {
    const loaded = await loadRepeatVisits(props.organizationId, { actionsOnly: true, date: props.date, page: page.value, pageSize: 5 }, active.signal)
    if (active.signal.aborted || request !== active || currentScope !== scopeVersion) return
    result.value = loaded
    if (props.userId) result.value.items = result.value.items.filter(item => (!item.responsibleUserId || item.responsibleUserId === props.userId) && item.contactByUtc && !item.contactCompletedAtUtc && item.isActive)
  } catch (caught) {
    if (!active.signal.aborted && request === active && currentScope === scopeVersion) error.value = caught instanceof Error ? caught.message : t('workdayActions.loadRepeatError')
  }
}

watch(() => [props.organizationId, props.userId, props.date], () => { scopeVersion++; request?.abort(); isOpen.value = false; result.value = null; page.value = 1; busyId.value = ''; rescheduleId.value = ''; rescheduleDraft.value = { date: '', time: '' }; void load() }, { immediate: true })
watch(page, () => { void load() })
onBeforeUnmount(() => { scopeVersion++; request?.abort() })

async function runAction(item: RepeatVisit, action: () => Promise<RepeatVisit>) {
  if (busyId.value) return
  const organizationId = props.organizationId
  const userId = props.userId
  const date = props.date
  const currentScope = scopeVersion
  busyId.value = item.id
  actionError.value = ''
  try {
    await action()
    if (currentScope !== scopeVersion || organizationId !== props.organizationId || userId !== props.userId || date !== props.date) return
    rescheduleId.value = ''
    await load()
  } catch (caught) {
    if (currentScope === scopeVersion && organizationId === props.organizationId && userId === props.userId && date === props.date) actionError.value = caught instanceof Error ? caught.message : t('workdayActions.updateError')
  } finally {
    if (currentScope === scopeVersion && busyId.value === item.id) busyId.value = ''
  }
}

function startReschedule(item: RepeatVisit) {
  rescheduleId.value = item.id
  const contactBy = item.contactByUtc ? inquiryLocalTime(item.contactByUtc, item.timeZoneId) : `${props.date}T09:00`
  rescheduleDraft.value = { date: contactBy.split('T')[0] ?? '', time: contactBy.split('T')[1] ?? '' }
}

function saveReschedule(item: RepeatVisit) {
  try {
    const utc = inquiryUtcTime(`${rescheduleDraft.value.date}T${rescheduleDraft.value.time}`, item.timeZoneId)
    if (!utc) return
    void runAction(item, () => rescheduleRepeatVisitAction(props.organizationId, item.id, item.version, utc))
  } catch (caught) {
    actionError.value = caught instanceof Error ? caught.message : t('workdayActions.invalidContactTime')
  }
}

function formatVisitDate(value: string) {
  return parseDateOnly(value.slice(0, 10))?.toLocaleDateString(formattingCulture.value, { year: 'numeric', month: '2-digit', day: '2-digit' }) ?? ''
}

function formatDeadline(item: RepeatVisit) {
  return item.contactByUtc ? new Intl.DateTimeFormat(formattingCulture.value, { timeZone: item.timeZoneId, day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }).format(new Date(item.contactByUtc)) : ''
}
</script>

<template>
  <UiButton variant="secondary" aria-haspopup="dialog" :aria-expanded="isOpen" @click="isOpen = true">{{ t('workdayActions.repeatVisits') }} <span class="workday-repeat__count">{{ error ? '!' : result?.totalCount ?? '…' }}</span></UiButton>
  <UiModal :open="isOpen" :title="t('workdayActions.repeatVisits')" @close="!busyId && (isOpen = false)">
    <section class="workday-repeat" :aria-label="t('workdayActions.repeatVisits')">
      <header><h3>{{ t('workdayActions.contactRepeats') }} <span v-if="result?.totalCount">{{ result.totalCount }}</span></h3></header>
      <p v-if="!result && !error" role="status">{{ t('workdayActions.loading') }}</p>
      <p v-else-if="result && !result.totalCount" class="workday-repeat__empty">{{ t('workdayActions.empty') }}</p>
      <p v-if="error" class="workday-repeat__error" role="alert">{{ error }} <button type="button" @click="load">{{ t('workdayActions.retry') }}</button></p>
      <p v-if="actionError" class="workday-repeat__error" role="alert">{{ actionError }}</p>
      <article v-for="item in result?.items" :key="item.id" class="workday-repeat__row">
        <div><strong><a :href="`#clients/${item.organizationClientId}`">{{ t('workdayActions.contact', { name: item.clientName }) }}</a> · {{ t('workdayActions.deadline', { time: formatDeadline(item) }) }}</strong><span v-if="!item.responsibleUserId">{{ t('workdayActions.shared') }}</span><span>{{ item.vehicleName }} · {{ item.serviceName }}</span><span v-if="item.reason">{{ item.reason }}</span><small>{{ t('workdayActions.suggestedVisit', { date: formatVisitDate(item.suggestedVisitDate) }) }}</small></div>
        <div v-if="item.canManageContact" class="workday-repeat__actions">
          <UiButton variant="secondary" :disabled="!!busyId" @click="runAction(item, () => completeRepeatVisitAction(organizationId, item.id, item.version))">{{ busyId === item.id ? t('workdayActions.saving') : t('workdayActions.done') }}</UiButton>
          <UiButton variant="secondary" :disabled="!!busyId" @click="startReschedule(item)">{{ t('workdayActions.reschedule') }}</UiButton>
        </div>
        <div v-if="item.canBook && !item.bookedMaintenanceId" class="workday-repeat__booking"><UiButton variant="secondary" :href="`#maintenance/new?repeatVisit=${encodeURIComponent(item.id)}`">{{ t('workdayActions.createBooking') }}</UiButton></div>
        <form v-if="rescheduleId === item.id" class="workday-repeat__reschedule" @submit.prevent="saveReschedule(item)"><UiDatePicker v-model="rescheduleDraft.date" :label="t('workdayActions.contactBy')" :disabled="!!busyId" required /><UiTimePicker v-model="rescheduleDraft.time" :label="t('workdayActions.time')" :disabled="!!busyId" required /><UiButton type="submit" :disabled="!!busyId">{{ t('workdayActions.saveDeadline') }}</UiButton><UiButton type="button" variant="secondary" :disabled="!!busyId" @click="rescheduleId = ''">{{ t('workdayActions.cancel') }}</UiButton></form>
      </article>
      <nav v-if="result && result.totalCount > result.pageSize" class="workday-repeat__pagination" :aria-label="t('workdayActions.pages')"><UiButton variant="secondary" :disabled="page <= 1" @click="page--">{{ t('workdayActions.back') }}</UiButton><span>{{ page }} / {{ Math.ceil(result.totalCount / result.pageSize) }}</span><UiButton variant="secondary" :disabled="page * result.pageSize >= result.totalCount" @click="page++">{{ t('workdayActions.next') }}</UiButton></nav>
    </section>
  </UiModal>
</template>

<style scoped>
.workday-repeat { min-width:0; max-height:max(160px,calc(100dvh - 180px)); overflow-y:auto; color:var(--crm-text,#202733); }.workday-repeat__count { min-width:20px; padding:2px 5px; border-radius:5px; background:var(--crm-surface-soft,#f1f3f5); color:var(--crm-text,#263142); font-size:11px; text-align:center; }.workday-repeat__empty { color:var(--crm-text-muted,#667085); font-size:12px; }.workday-repeat header { display:flex; justify-content:space-between; align-items:center; }.workday-repeat h3 { font-size:14px; margin:0; }.workday-repeat h3 span,.workday-repeat__row span,.workday-repeat__row small,.workday-repeat__pagination span { color:var(--crm-text-muted,#667085); font-size:12px; }.workday-repeat__row { display:grid; grid-template-columns:minmax(0,1fr); gap:10px 14px; padding:12px 0; border-bottom:1px solid var(--crm-line,#e3e6eb); overflow-wrap:anywhere; }.workday-repeat__row:last-of-type { border:0; }.workday-repeat__row>div:first-child { display:grid; gap:5px; }.workday-repeat a { color:var(--crm-text,#263142); }.workday-repeat__actions,.workday-repeat__reschedule,.workday-repeat__pagination { display:flex; flex-wrap:wrap; align-items:center; gap:8px; }.workday-repeat__reschedule { grid-column:1/-1; }.workday-repeat__reschedule label { display:flex; align-items:center; gap:8px; font-size:12px; }.workday-repeat__reschedule input { min-height:36px; padding:6px 9px; border:1px solid var(--crm-line,#dce1e8); border-radius:6px; background:var(--crm-surface,#fff); color:var(--crm-text,#202733); font:inherit; }.workday-repeat__error { color:var(--crm-danger-text,#b42318); font-size:12px; }.workday-repeat__pagination { justify-content:flex-end; margin-top:8px; }
@media(max-width:650px) { .workday-repeat__row { grid-template-columns:minmax(0,1fr); }.workday-repeat__actions :deep(.ui-button) { flex:1 1 auto; }.workday-repeat__reschedule { display:grid; }.workday-repeat__reschedule label { align-items:stretch; flex-direction:column; } }
</style>

<script setup lang="ts">
import { translate as t } from '@opencrm/ui/i18n'
import '../setupMessages'
import { useWorkspaceCurrency } from '../workspaceCurrency'

import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton } from '@opencrm/ui'
import OrganizationWorkingHoursEditor from './OrganizationWorkingHoursEditor.vue'
import { workingScheduleFromSetup, workingScheduleError, schedulePayload } from '../workingHours'
import type { WorkspaceWorkingDay } from '../api/setup'
import { loadScenarioCatalog } from '../api/scenarioCatalog'
import { loadWorkspaceSetup, openWorkspaceSetup, saveWorkspaceSetup, type WorkspaceSetup, type WorkspaceSetupItem } from '../api/setup'

const workspaceCurrency = useWorkspaceCurrency()

const props = defineProps<{ organizationId: string, canWrite: boolean, canCreate: boolean, placement?: 'workday' | 'settings', scenarioCode?: string | null }>()
const state = ref<WorkspaceSetup | null>(null)
const dialog = ref<HTMLDialogElement | null>(null)
const form = ref<HTMLFormElement | null>(null)
const step = ref(0)
const posts = ref<WorkspaceSetupItem[]>([])
const services = ref<WorkspaceSetupItem[]>([])
const weeklySchedule = ref<WorkspaceWorkingDay[]>([])
const busy = ref(false)
const error = ref('')
const loaded = ref(false)
const open = ref(false)
let isMounted = true
const key = computed(() => `opencrm.setup:${props.organizationId}`)
const isAutoService = computed(() => props.scenarioCode === 'service-station')
const titles = computed(() => [t('setup.posts'), t('setup.services'), t('setup.hours'), t('setup.ready')])
const rows = computed(() => step.value === 0 ? posts.value : services.value)

async function refreshState() {
  try {
    state.value = await loadWorkspaceSetup(props.organizationId)
    error.value = ''
    if (isMounted && props.placement !== 'settings' && props.canWrite && state.value.canManage && state.value.shouldAutoOpen && !open.value) {
      await show(true)
    }
  }
  catch (reason) { error.value = reason instanceof Error ? reason.message : t('setup.loadError') }
}

function storeDraft() {
  if (!loaded.value || !state.value || !open.value) return
  try {
    localStorage.setItem(key.value, JSON.stringify({ revision: state.value.revision, step: step.value, posts: posts.value, services: services.value, weeklySchedule: weeklySchedule.value }))
  } catch { /* Черновик необязателен: сохранённые шаги остаются на сервере. */ }
}

async function show(automatic = false) {
  if (busy.value || !isMounted) return
  busy.value = true
  if (!automatic) {
    open.value = true
    await nextTick()
    dialog.value?.showModal()
  }
  loaded.value = false
  error.value = ''
  try {
    const [setup, catalog] = await Promise.all([loadWorkspaceSetup(props.organizationId), loadScenarioCatalog(props.organizationId)])
    if (!isMounted) return
    const shouldOpen = await openWorkspaceSetup(props.organizationId)
    if (!isMounted || (automatic && !shouldOpen)) return
    if (automatic) {
      open.value = true
      await nextTick()
      dialog.value?.showModal()
    }
    state.value = { ...setup, shouldAutoOpen: false }
    step.value = setup.completedAt ? 0 : setup.step
    posts.value = catalog.workPosts.map(item => ({ id: item.id, name: item.name, isActive: item.isActive, isUsed: item.isUsed, unitPrice: null }))
    services.value = catalog.services.map(item => ({ id: item.id, name: item.name, isActive: item.isActive, isUsed: item.isUsed, unitPrice: item.unitPrice ?? null, durationMinutes: item.durationMinutes ?? null }))
    weeklySchedule.value = workingScheduleFromSetup(setup)
    try {
      const raw = localStorage.getItem(key.value)
      const draft = raw ? JSON.parse(raw) : null
      if (draft?.revision === setup.revision && Array.isArray(draft.posts) && Array.isArray(draft.services)
          && Number.isInteger(draft.step) && draft.step >= 0 && draft.step <= 3) {
        posts.value = draft.posts.map((item: WorkspaceSetupItem) => ({
          ...item,
          isUsed: posts.value.find(post => post.id === item.id)?.isUsed ?? item.isUsed ?? false
        }))
        services.value = draft.services.map((item: WorkspaceSetupItem) => ({
          ...item,
          isUsed: services.value.find(service => service.id === item.id)?.isUsed ?? item.isUsed ?? false
        }))
        step.value = draft.step
        if (Array.isArray(draft.weeklySchedule) && draft.weeklySchedule.length === 7
            && draft.weeklySchedule.every((day: WorkspaceWorkingDay) => day && Number.isInteger(day.dayOfWeek)
              && typeof day.isWorking === 'boolean' && Array.isArray(day.breaks)
              && day.breaks.every(pause => typeof pause.startTime === 'string' && typeof pause.endTime === 'string'))) {
          weeklySchedule.value = draft.weeklySchedule
        } else if (typeof draft.start === 'string' && typeof draft.end === 'string' && Array.isArray(draft.days)) {
          weeklySchedule.value = workingScheduleFromSetup({ ...setup, weeklySchedule: [], workdayStart: draft.start, workdayEnd: draft.end, workingDays: draft.days })
        }
      }
    } catch { /* Повреждённый черновик не мешает открыть серверные настройки. */ }
    loaded.value = true
  } catch (reason) { error.value = reason instanceof Error ? reason.message : t('setup.openError') }
  finally { busy.value = false }
}

function close() {
  if (busy.value) return
  storeDraft()
  dialog.value?.close()
  open.value = false
}

function addRow() {
  rows.value.push({ id: crypto.randomUUID(), name: '', unitPrice: null, durationMinutes: null, isActive: true })
  void nextTick(() => form.value?.querySelector<HTMLInputElement>('.setup-row:last-child input[type="text"]')?.focus())
}

async function save(skip = false, create = false) {
  if (busy.value || !state.value || !loaded.value) return
  if (!skip && !form.value?.reportValidity()) return
  if (!skip && step.value === 2) {
    error.value = workingScheduleError(weeklySchedule.value)
    if (error.value) return
  }
  busy.value = true
  error.value = ''
  try {
    state.value = await saveWorkspaceSetup(props.organizationId, {
      step: step.value, revision: state.value.revision, skip,
      items: step.value < 2 ? rows.value.filter(item => item.name.trim() || item.isActive).map(({ id, name, unitPrice, durationMinutes, isActive }) => ({
        id, name, unitPrice, durationMinutes, isActive
      })) : [],
      weeklySchedule: schedulePayload(weeklySchedule.value)
    })
    if (step.value === 3) {
      try { localStorage.removeItem(key.value) } catch { /* Настройка уже сохранена на сервере. */ }
      busy.value = false
      dialog.value?.close(); open.value = false
      if (create) window.location.hash = 'maintenance/new'
      return
    }
    if (skip && step.value < 2) {
      const catalog = await loadScenarioCatalog(props.organizationId)
      if (step.value === 0) posts.value = catalog.workPosts.map(item => ({ id: item.id, name: item.name, isActive: item.isActive, isUsed: item.isUsed, unitPrice: null }))
      else services.value = catalog.services.map(item => ({ id: item.id, name: item.name, isActive: item.isActive, isUsed: item.isUsed, unitPrice: item.unitPrice ?? null, durationMinutes: item.durationMinutes ?? null }))
    }
    step.value++
    storeDraft()
    await nextTick()
    form.value?.querySelector<HTMLElement>('input, button')?.focus()
  } catch (reason) { error.value = reason instanceof Error ? reason.message : t('setup.saveError') }
  finally { busy.value = false }
}

watch([posts, services, weeklySchedule, step], storeDraft, { deep: true })
onMounted(refreshState)
onBeforeUnmount(() => { isMounted = false })
</script>

<template>
  <div v-if="canWrite && (state?.canManage || error) && (placement === 'settings' || !state?.completedAt)" :class="placement === 'settings' ? 'setup-launcher' : 'setup-entry'">
    <div v-if="placement !== 'settings'"><strong>{{ isAutoService ? t('setup.prepareAuto') : t('setup.prepareDetailing') }}</strong><span>{{ t('setup.overview') }}</span></div>
    <UiButton variant="secondary" :disabled="busy" @click="show()">{{ placement === 'settings' ? t('setup.setupWorkspace') : state?.shouldAutoOpen ? (isAutoService ? t('setup.setupAuto') : t('setup.setupDetailing')) : t('setup.continue') }}</UiButton>
  </div>
  <dialog v-if="open" ref="dialog" class="setup-dialog" aria-labelledby="setup-title" @cancel.prevent="close">
    <header class="ui-form-header"><div><span class="setup-eyebrow">{{ isAutoService ? t('setup.autoHeading') : t('setup.detailingHeading') }}</span><h2 id="setup-title">{{ titles[step] }}</h2></div><button type="button" class="setup-close" :aria-label="t('setup.close')" :disabled="busy" @click="close">×</button></header>
    <nav v-if="step < 3" :aria-label="t('setup.steps')"><button v-for="(title, index) in titles.slice(0, 3)" :key="title" type="button" :aria-current="index === step ? 'step' : undefined" :disabled="busy || index > step" @click="step = index"><span>{{ index + 1 }}</span>{{ title }}</button></nav>
    <form ref="form" class="setup-body" @submit.prevent="save()">
      <p v-if="busy && !loaded" role="status">{{ t('setup.loading') }}</p>
      <template v-if="loaded">
        <template v-if="step < 2">
          <p class="setup-hint">{{ step === 0 ? t('setup.postsHint') : (isAutoService ? t('setup.autoServicesHint') : t('setup.detailingServicesHint')) }}</p>
          <div class="setup-labels"><span>{{ t('setup.use') }}</span><span>{{ t('setup.name') }}</span><span v-if="step === 1">{{ t('setup.price', { currency: workspaceCurrency }) }}</span><span v-if="step === 1">{{ t('setup.minutes') }}</span></div>
          <div v-for="(item, index) in rows" :key="item.id" class="setup-row" :class="{ 'setup-row--service': step === 1, 'setup-row--inactive': !item.isActive }">
            <input v-model="item.isActive" type="checkbox" :disabled="busy || (step === 0 && item.isUsed)" :title="step === 0 && item.isUsed ? t('setup.postAvailability') : undefined" :aria-label="t('setup.useItem', { name: item.name || t('setup.item', { index: index + 1 }) })" />
            <input v-model="item.name" type="text" :aria-label="t(step === 0 ? 'setup.postLabel' : 'setup.serviceLabel', { index: index + 1 })" :required="item.isActive" :disabled="!item.isActive || busy || (step === 0 && item.isUsed)" maxlength="160" :placeholder="step === 0 ? (isAutoService ? t('setup.postExample') : t('setup.detailingPostExample')) : t('setup.serviceName')" />
            <span v-if="step === 0 && item.isUsed" class="setup-used-hint" :title="t('setup.usedPostHint')"><a href="#catalog?catalog=work_post">{{ t('setup.managePosts') }}</a></span>
            <label v-if="step === 1" class="setup-price"><span>{{ t('setup.price', { currency: workspaceCurrency }) }}</span><input :value="item.unitPrice ?? ''" type="number" min="0" max="999999999" step="0.01" :disabled="!item.isActive || busy" :aria-label="t('setup.priceLabel', { index: index + 1 })" :placeholder="t('setup.priceNotSet')" @input="item.unitPrice = ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value)" /></label>
            <label v-if="step === 1" class="setup-price"><span>{{ t('setup.minutes') }}</span><input :value="item.durationMinutes ?? ''" type="number" min="1" max="525600" step="1" :disabled="!item.isActive || busy" :aria-label="t('setup.durationLabel', { index: index + 1 })" :placeholder="t('setup.durationNotSet')" @input="item.durationMinutes = ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value)" /></label>
          </div>
          <UiButton variant="ghost" :disabled="busy || rows.length >= 100" @click="addRow">＋ {{ step === 0 ? t('setup.addPost') : t('setup.addService') }}</UiButton>
        </template>
        <template v-else-if="step === 2">
          <p class="setup-hint">{{ t('setup.scheduleHint') }}</p>
          <OrganizationWorkingHoursEditor v-model="weeklySchedule" :disabled="busy" />
        </template>
        <template v-else>
          <h3>{{ t('setup.workspaceReady') }}</h3>
          <p class="setup-hint">{{ t('setup.firstHint') }}</p>
          <div class="setup-summary"><span>{{ t('setup.postCount', { count: posts.filter(item => item.isActive).length }) }}</span><span>{{ t('setup.serviceCount', { count: services.filter(item => item.isActive).length }) }}</span></div>
        </template>
      </template>
      <p v-if="error" class="setup-error" role="alert">{{ error }}</p>
    </form>
    <footer>
      <UiButton variant="ghost" :disabled="busy" @click="close">{{ t('setup.later') }}</UiButton>
      <div v-if="loaded"><UiButton v-if="step < 3" variant="secondary" :disabled="busy" @click="save(true)">{{ t('setup.skip') }}</UiButton><UiButton v-if="step < 3" :disabled="busy" @click="save()">{{ busy ? t('setup.saving') : t('setup.saveNext') }}</UiButton><template v-else><UiButton variant="secondary" :disabled="busy" @click="save()">{{ t('setup.done') }}</UiButton><UiButton v-if="canCreate" :disabled="busy" @click="save(false, true)">{{ t('setup.createFirst') }}</UiButton></template></div>
      <UiButton v-else-if="!busy" variant="secondary" :disabled="busy" @click="show()">{{ t('setup.retry') }}</UiButton>
    </footer>
  </dialog>
</template>

<style scoped>
.setup-entry { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 16px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 10px; background: var(--crm-surface-soft, #f4f6f8); }
.setup-entry strong, .setup-entry span { display: block; }
.setup-entry span { margin-top: 4px; font-size: 13px; color: var(--crm-text-muted, #667085); }
.setup-dialog { width: min(760px, calc(100vw - 24px)); max-height: calc(100dvh - 32px); padding: 0; border: 1px solid var(--crm-line, #dce2e9); border-radius: 14px; background: var(--crm-surface, #ffffff); color: var(--crm-text, #202733); }
.setup-dialog[open] { display: flex; flex-direction: column; }
.setup-dialog::backdrop { background: rgb(0 0 0 / .55); }
.setup-dialog header, .setup-dialog footer { padding: 20px 24px; display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-shrink: 0; }
.setup-dialog header { border-bottom: 1px solid var(--crm-form-header-border, #dce2e9); }
.setup-eyebrow { color: var(--crm-text-muted, #667085); font-size: 10px; letter-spacing: .08em; }
.setup-dialog h2 { font-size: 22px; margin: 6px 0 0; }
.setup-close { background: transparent; border: 0; color: var(--crm-text-muted, #667085); font-size: 28px; }
.setup-dialog nav { display: flex; padding: 16px 24px 0; gap: 16px; }
.setup-dialog nav button { background: none; border: 0; padding: 0; display: flex; align-items: center; gap: 7px; color: var(--crm-text-muted, #667085); font-size: 12px; text-align: left; }
.setup-dialog nav button[aria-current] { color: var(--crm-text, #202733); font-weight: 700; }
.setup-dialog nav span { display: grid; place-items: center; width: 25px; height: 25px; flex-shrink: 0; background: var(--crm-surface-soft, #f4f6f8); border: 1px solid var(--crm-line, #dce2e9); border-radius: 50%; }
.setup-dialog nav button[aria-current] span { background: #c4510a; color: white; border-color: #c4510a; }
.setup-body { padding: 20px 24px; overflow-y: auto; min-height: 0; }
.setup-hint { color: var(--crm-text-muted, #667085); font-size: 13px; margin: 0 0 20px; }
.setup-row { display: grid; grid-template-columns: 28px minmax(0, 1fr); gap: 12px; align-items: center; margin-bottom: 10px; }
.setup-used-hint { grid-column: 2; font-size: 12px; color: var(--crm-text-muted, #667085); }
.setup-row--service { grid-template-columns: 28px minmax(0, 1fr) 110px 110px; }
.setup-row--inactive { opacity: .65; }
.setup-price { min-width: 0; }
.setup-price > span { display: none; }
.setup-labels { display: flex; gap: 12px; font-size: 11px; color: var(--crm-text-muted, #667085); margin-bottom: 8px; }
.setup-labels span:nth-child(2) { flex: 1; }
.setup-labels span:nth-child(n+3) { width: 110px; }
.setup-dialog input[type=text], .setup-dialog input[type=number], .setup-dialog input[type=time] { width: 100%; min-width: 0; padding: 9px 10px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 7px; color: var(--crm-text, #202733); background: var(--crm-surface-soft, #f4f6f8); font: inherit; font-size: 14px; }
.setup-dialog input[type=checkbox] { accent-color: #c4510a; width: 18px; height: 18px; }
.setup-days { display: flex; gap: 8px; flex-wrap: wrap; border: 0; padding: 0; margin: 0 0 24px; }
.setup-days legend, .setup-hours label { font-weight: 600; font-size: 13px; }
.setup-days legend { margin-bottom: 10px; }
.setup-days label { display: flex; align-items: center; gap: 6px; padding: 8px; background: var(--crm-surface-soft, #f4f6f8); border: 1px solid var(--crm-line, #dce2e9); border-radius: 7px; }
.setup-hours { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; max-width: 350px; }
.setup-hours input { margin-top: 8px; }
.setup-dialog footer { border-top: 1px solid var(--crm-line, #dce2e9); }
.setup-dialog footer > div { display: flex; gap: 8px; }
.setup-error { padding: 12px; margin: 16px 0 0; border: 1px solid var(--crm-line, #dce2e9); border-left: 3px solid var(--crm-danger-text, #b42318); color: var(--crm-danger-text, #b42318); border-radius: 6px; }
.setup-summary { display: flex; gap: 32px; margin: 24px 0; }
.setup-summary strong { font-size: 25px; margin-right: 6px; }
@media (max-width: 540px) {
  .setup-entry { align-items: flex-start; flex-direction: column; }
  .setup-dialog header, .setup-dialog footer, .setup-body { padding: 16px; }
  .setup-dialog nav { padding: 12px 16px 0; gap: 8px; }
  .setup-dialog nav button { font-size: 11px; }
  .setup-row--service { grid-template-columns: 24px minmax(0, 1fr); gap: 8px; padding-bottom: 12px; margin-bottom: 12px; border-bottom: 1px solid var(--crm-line, #dce2e9); }
  .setup-price { grid-column: 2; display: flex; align-items: center; gap: 8px; }
  .setup-price > span { display: inline; white-space: nowrap; color: var(--crm-text-muted, #667085); font-size: 12px; }
  .setup-price input { max-width: 140px; }
  .setup-labels { display: none; }
  .setup-labels span:last-child:not(:nth-child(2)) { width: 90px; }
  .setup-dialog footer { flex-wrap: wrap; }
  .setup-dialog footer > div { flex-wrap: wrap; }
}
</style>

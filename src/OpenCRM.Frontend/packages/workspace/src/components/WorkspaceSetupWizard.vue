<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton } from '@opencrm/ui'
import OrganizationWorkingHoursEditor from './OrganizationWorkingHoursEditor.vue'
import { workingScheduleFromSetup, workingScheduleError, schedulePayload } from '../workingHours'
import type { WorkspaceWorkingDay } from '../api/setup'
import { loadScenarioCatalog } from '../api/scenarioCatalog'
import { loadWorkspaceSetup, openWorkspaceSetup, saveWorkspaceSetup, type WorkspaceSetup, type WorkspaceSetupItem } from '../api/setup'

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
const titles = ['Рабочие посты', 'Услуги и цены', 'Рабочее время', 'Можно начинать']
const rows = computed(() => step.value === 0 ? posts.value : services.value)

async function refreshState() {
  try {
    state.value = await loadWorkspaceSetup(props.organizationId)
    error.value = ''
    if (isMounted && props.placement !== 'settings' && props.canWrite && state.value.canManage && state.value.shouldAutoOpen && !open.value) {
      await show(true)
    }
  }
  catch (reason) { error.value = reason instanceof Error ? reason.message : 'Не удалось загрузить настройку.' }
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
  } catch (reason) { error.value = reason instanceof Error ? reason.message : 'Не удалось открыть настройку.' }
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
  } catch (reason) { error.value = reason instanceof Error ? reason.message : 'Не удалось сохранить настройку.' }
  finally { busy.value = false }
}

watch([posts, services, weeklySchedule, step], storeDraft, { deep: true })
onMounted(refreshState)
onBeforeUnmount(() => { isMounted = false })
</script>

<template>
  <div v-if="canWrite && (state?.canManage || error) && (placement === 'settings' || !state?.completedAt)" :class="placement === 'settings' ? 'setup-launcher' : 'setup-entry'">
    <div v-if="placement !== 'settings'"><strong>{{ isAutoService ? 'Подготовим автосервис к первой записи' : 'Подготовим студию к первой записи' }}</strong><span>Посты, услуги и рабочее время</span></div>
    <UiButton variant="secondary" :disabled="busy" @click="show()">{{ placement === 'settings' ? 'Настройка рабочего места' : state?.shouldAutoOpen ? (isAutoService ? 'Настроить автосервис' : 'Настроить студию') : 'Продолжить настройку' }}</UiButton>
  </div>
  <dialog v-if="open" ref="dialog" class="setup-dialog" aria-labelledby="setup-title" @cancel.prevent="close">
    <header class="ui-form-header"><div><span class="setup-eyebrow">{{ isAutoService ? 'НАСТРОЙКА АВТОСЕРВИСА' : 'НАСТРОЙКА ДЕТЕЙЛИНГА' }}</span><h2 id="setup-title">{{ titles[step] }}</h2></div><button type="button" class="setup-close" aria-label="Закрыть настройку" :disabled="busy" @click="close">×</button></header>
    <nav v-if="step < 3" aria-label="Шаги настройки"><button v-for="(title, index) in titles.slice(0, 3)" :key="title" type="button" :aria-current="index === step ? 'step' : undefined" :disabled="busy || index > step" @click="step = index"><span>{{ index + 1 }}</span>{{ title }}</button></nav>
    <form ref="form" class="setup-body" @submit.prevent="save()">
      <p v-if="busy && !loaded" role="status">Загружаем настройки…</p>
      <template v-if="loaded">
        <template v-if="step < 2">
          <p class="setup-hint">{{ step === 0 ? 'Оставьте нужные посты и задайте понятные названия.' : (isAutoService ? 'Отметьте услуги вашего автосервиса. Цену можно уточнить при записи.' : 'Отметьте услуги вашей студии. Цену можно уточнить при записи.') }}</p>
          <div class="setup-labels"><span>Использовать</span><span>Название</span><span v-if="step === 1">Цена, ₽</span><span v-if="step === 1">Время, мин</span></div>
          <div v-for="(item, index) in rows" :key="item.id" class="setup-row" :class="{ 'setup-row--service': step === 1, 'setup-row--inactive': !item.isActive }">
            <input v-model="item.isActive" type="checkbox" :disabled="busy || (step === 0 && item.isUsed)" :title="step === 0 && item.isUsed ? 'Изменяйте доступность в справочнике «Посты / боксы»' : undefined" :aria-label="`Использовать ${item.name || 'позицию ' + (index + 1)}`" />
            <input v-model="item.name" type="text" :aria-label="`${step === 0 ? 'Пост' : 'Услуга'} ${index + 1}`" :required="item.isActive" :disabled="!item.isActive || busy || (step === 0 && item.isUsed)" maxlength="160" :placeholder="step === 0 ? (isAutoService ? 'Например, Пост 1' : 'Например, Полировка') : 'Название услуги'" />
            <span v-if="step === 0 && item.isUsed" class="setup-used-hint" title="Используемые посты нельзя отключить в мастере настройки."><a href="#catalog?catalog=work_post">Управлять в справочнике «Посты / боксы»</a></span>
            <label v-if="step === 1" class="setup-price"><span>Цена, ₽</span><input :value="item.unitPrice ?? ''" type="number" min="0" max="999999999" step="0.01" :disabled="!item.isActive || busy" :aria-label="`Цена услуги ${index + 1}`" placeholder="Не задана" @input="item.unitPrice = ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value)" /></label>
            <label v-if="step === 1" class="setup-price"><span>Время, мин</span><input :value="item.durationMinutes ?? ''" type="number" min="1" max="525600" step="1" :disabled="!item.isActive || busy" :aria-label="`Длительность услуги ${index + 1}, мин`" placeholder="Не задано" @input="item.durationMinutes = ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value)" /></label>
          </div>
          <UiButton variant="ghost" :disabled="busy || rows.length >= 100" @click="addRow">＋ {{ step === 0 ? 'Добавить пост' : 'Добавить услугу' }}</UiButton>
        </template>
        <template v-else-if="step === 2">
          <p class="setup-hint">График задаёт рабочие часы в расписании. Записи вне графика остаются доступны.</p>
          <OrganizationWorkingHoursEditor v-model="weeklySchedule" :disabled="busy" />
        </template>
        <template v-else>
          <h3>Рабочее место подготовлено</h3>
          <p class="setup-hint">Создайте первую запись: выберите автомобиль, услуги и время.</p>
          <div class="setup-summary"><span><strong>{{ posts.filter(item => item.isActive).length }}</strong> постов</span><span><strong>{{ services.filter(item => item.isActive).length }}</strong> услуг</span></div>
        </template>
      </template>
      <p v-if="error" class="setup-error" role="alert">{{ error }}</p>
    </form>
    <footer>
      <UiButton variant="ghost" :disabled="busy" @click="close">Позже</UiButton>
      <div v-if="loaded"><UiButton v-if="step < 3" variant="secondary" :disabled="busy" @click="save(true)">Пропустить</UiButton><UiButton v-if="step < 3" :disabled="busy" @click="save()">{{ busy ? 'Сохраняем…' : 'Сохранить и далее' }}</UiButton><template v-else><UiButton variant="secondary" :disabled="busy" @click="save()">Готово</UiButton><UiButton v-if="canCreate" :disabled="busy" @click="save(false, true)">Создать первую запись</UiButton></template></div>
      <UiButton v-else-if="!busy" variant="secondary" :disabled="busy" @click="show()">Повторить</UiButton>
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

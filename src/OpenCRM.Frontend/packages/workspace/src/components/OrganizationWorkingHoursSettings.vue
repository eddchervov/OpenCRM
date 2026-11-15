<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton, UiLoader } from '@opencrm/ui'
import OrganizationWorkingHoursEditor from './OrganizationWorkingHoursEditor.vue'
import { loadWorkspaceSetup, saveWorkspaceSetup, type WorkspaceSetup, type WorkspaceWorkingDay } from '../api/setup'
import { workingScheduleFromSetup, workingScheduleError, schedulePayload } from '../workingHours'

const props = defineProps<{ organizationId: string; disabled?: boolean; embedded?: boolean }>()
const emit = defineEmits<{ saved: []; closed: []; 'busy-changed': [isBusy: boolean] }>()
const dialog = ref<HTMLDialogElement | null>(null)
const editing = ref(false)
const loading = ref(false)
const saving = ref(false)
const busy = computed(() => loading.value || saving.value)
const error = ref('')
const state = ref<WorkspaceSetup | null>(null)
const schedule = ref<WorkspaceWorkingDay[]>([])
let requestId = 0

async function load() {
  if (!editing.value || busy.value || props.disabled) return
  const currentRequestId = ++requestId
  const organizationId = props.organizationId
  loading.value = true
  error.value = ''
  try {
    const setup = await loadWorkspaceSetup(organizationId)
    if (currentRequestId !== requestId || organizationId !== props.organizationId) return
    if (!setup.canManage) throw new Error('Нет права изменять график организации.')
    state.value = setup
    schedule.value = workingScheduleFromSetup(setup)
  } catch (reason) {
    if (currentRequestId === requestId) {
      error.value = reason instanceof Error ? reason.message : 'Не удалось загрузить график.'
    }
  } finally {
    if (currentRequestId === requestId) loading.value = false
  }
}

async function edit() {
  if (editing.value || busy.value || props.disabled) return
  const organizationId = props.organizationId
  editing.value = true
  error.value = ''
  await nextTick()
  if (!editing.value || organizationId !== props.organizationId) return
  if (!props.embedded) dialog.value?.showModal()
  await load()
}

function reset() {
  requestId++
  if (!props.embedded) dialog.value?.close()
  editing.value = false
  loading.value = false
  saving.value = false
  error.value = ''
  state.value = null
  schedule.value = []
  if (props.embedded) emit('busy-changed', false)
}

function close() {
  if (saving.value) return
  reset()
  if (props.embedded) emit('closed')
}

async function save() {
  if (!editing.value || !state.value?.canManage || busy.value || props.disabled) return
  error.value = workingScheduleError(schedule.value)
  if (error.value) return
  const currentRequestId = ++requestId
  const organizationId = props.organizationId
  saving.value = true
  if (props.embedded) emit('busy-changed', true)
  try {
    await saveWorkspaceSetup(organizationId, {
      step: 2, revision: state.value.revision, skip: false, items: [], weeklySchedule: schedulePayload(schedule.value)
    })
    if (currentRequestId !== requestId || organizationId !== props.organizationId) return
    reset()
    emit('saved')
  } catch (reason) {
    if (currentRequestId === requestId) {
      error.value = reason instanceof Error ? reason.message : 'Не удалось сохранить график.'
    }
  } finally {
    if (currentRequestId === requestId) {
      saving.value = false
      if (props.embedded) emit('busy-changed', false)
    }
  }
}

watch(() => props.organizationId, reset)
onMounted(() => { if (props.embedded) void edit() })
onBeforeUnmount(reset)
</script>

<template>
  <div class="hours-settings" :class="{ 'hours-settings--embedded': embedded }">
    <UiButton v-if="!embedded" variant="secondary" :disabled="disabled || busy" @click="edit">Рабочие часы и перерывы</UiButton>
    <component :is="embedded ? 'section' : 'dialog'" ref="dialog" :class="embedded ? 'hours-panel' : 'hours-dialog'" aria-labelledby="hours-dialog-title" @cancel.prevent="close">
      <form @submit.prevent="save">
        <header class="hours-dialog__header" :class="embedded ? 'ui-section-header' : 'ui-form-header'">
          <h3 id="hours-dialog-title">Рабочие часы и перерывы</h3>
          <button v-if="!embedded" type="button" aria-label="Закрыть график организации" :disabled="saving" @click="close">×</button>
        </header>
        <div class="hours-dialog__body" :aria-busy="loading">
          <UiLoader v-if="loading" compact text="Загружаем график…" />
          <OrganizationWorkingHoursEditor v-else-if="state" v-model="schedule" :disabled="saving" />
          <p v-if="error" class="hours-dialog__error" role="alert">{{ error }}</p>
          <UiButton v-if="error && !state" variant="secondary" :disabled="busy" @click="load">Повторить</UiButton>
        </div>
        <footer class="hours-dialog__footer">
          <UiButton variant="secondary" :disabled="saving" @click="close">Отмена</UiButton>
          <UiButton type="submit" :disabled="busy || !state || disabled">{{ saving ? 'Сохранение…' : 'Сохранить график' }}</UiButton>
        </footer>
      </form>
    </component>
  </div>
</template>

<style scoped>
.hours-settings { margin-top:12px; }
.hours-settings--embedded { margin-top:0; min-width:0; }
.hours-panel { min-width:0; padding:18px; border:1px solid var(--crm-line, #dce2e9); border-radius:10px; background:var(--crm-surface, #fff); color:var(--crm-text, #202733); }
.hours-panel .hours-dialog__header { padding:10px 12px; margin-bottom:16px; }
.hours-panel .hours-dialog__header h3 { font-size:15px; }
.hours-panel .hours-dialog__body { padding:0; }
.hours-panel .hours-dialog__footer { padding:14px 0 0; margin-top:14px; }
.hours-dialog { box-sizing:border-box; width:min(840px, calc(100vw - 24px)); max-height:calc(100dvh - 32px); padding:0; overflow:hidden; border:1px solid var(--crm-line, #dce2e9); border-radius:12px; background:var(--crm-surface, #fff); color:var(--crm-text, #202733); }
.hours-dialog::backdrop { background:var(--crm-backdrop, rgb(0 0 0 / .55)); }
.hours-dialog form { display:flex; flex-direction:column; max-height:calc(100dvh - 34px); }
.hours-dialog__header { display:flex; flex-shrink:0; align-items:center; justify-content:space-between; gap:16px; padding:16px 20px; }
.hours-dialog__header h3 { margin:0; font-size:18px; }
.hours-dialog__header button { flex-shrink:0; width:32px; height:32px; padding:0; border:0; border-radius:8px; background:var(--crm-surface-soft, #f2f3f5); color:inherit; font-size:24px; cursor:pointer; }
.hours-dialog__body { min-height:0; padding:16px 20px; overflow:auto; }
.hours-dialog__error { margin:12px 0 0; color:var(--crm-danger-text, #b42318); font-size:13px; }
.hours-dialog__footer { display:flex; flex-shrink:0; justify-content:flex-end; flex-wrap:wrap; gap:10px; padding:14px 20px; border-top:1px solid var(--crm-line, #dce2e9); }
@media (max-width:540px) {
  .hours-panel { padding:14px; }
  .hours-dialog__header, .hours-dialog__body, .hours-dialog__footer { padding:14px; }
  .hours-dialog__header h3 { font-size:16px; }
}
</style>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiSelect } from '@opencrm/ui'
import { loadOrganizationTimeZone, saveOrganizationTimeZone, type OrganizationTimeZoneSettings } from '../api/organizationTimeZone'
import { organizationTimeZoneName, organizationTimeZones } from '../utils/organizationTimeZone'

const props = defineProps<{ organizationId: string; disabled?: boolean }>()
const emit = defineEmits<{ saved: []; busyChanged: [value: boolean] }>()
const settings = ref<OrganizationTimeZoneSettings | null>(null)
const draft = ref('')
const editing = ref(false)
const saving = ref(false)
const error = ref('')
const fieldError = ref('')
const status = ref('')
let request: AbortController | undefined
let scopeVersion = 0
const options = computed(() => settings.value?.timeZoneId && !organizationTimeZones.some(zone => zone.value === settings.value?.timeZoneId)
  ? [...organizationTimeZones, { value: settings.value.timeZoneId, label: organizationTimeZoneName(settings.value.timeZoneId) }]
  : organizationTimeZones)

async function load() {
  request?.abort()
  const active = new AbortController()
  request = active
  const scope = scopeVersion
  error.value = ''
  try {
    const result = await loadOrganizationTimeZone(props.organizationId, active.signal)
    if (active.signal.aborted || scope !== scopeVersion || request !== active) return
    settings.value = result
    draft.value = result.timeZoneId ?? ''
    editing.value = false
    emit('busyChanged', false)
  } catch (caught) {
    if (!active.signal.aborted && scope === scopeVersion && request === active) error.value = caught instanceof Error ? caught.message : 'Не удалось загрузить часовой пояс.'
  }
}

function startEdit() {
  if (!settings.value?.canManage || props.disabled || saving.value) return
  draft.value = settings.value.timeZoneId ?? ''
  error.value = ''
  fieldError.value = ''
  status.value = ''
  editing.value = true
  emit('busyChanged', true)
}

function cancel() {
  if (saving.value) return
  editing.value = false
  draft.value = settings.value?.timeZoneId ?? ''
  error.value = ''
  fieldError.value = ''
  emit('busyChanged', false)
}

async function save() {
  if (!settings.value?.canManage || props.disabled || saving.value) return
  fieldError.value = draft.value ? '' : 'Выберите часовой пояс организации.'
  if (fieldError.value) return
  const scope = scopeVersion
  saving.value = true
  error.value = ''
  try {
    const result = await saveOrganizationTimeZone(props.organizationId, draft.value, settings.value.timeZoneId)
    if (scope !== scopeVersion) return
    settings.value = result
    draft.value = result.timeZoneId ?? ''
    editing.value = false
    status.value = 'Часовой пояс сохранён.'
    emit('busyChanged', false)
    emit('saved')
  } catch (caught) {
    if (scope === scopeVersion) error.value = caught instanceof Error ? caught.message : 'Не удалось сохранить часовой пояс.'
  } finally {
    if (scope === scopeVersion) saving.value = false
  }
}

watch(() => props.organizationId, () => { scopeVersion++; request?.abort(); settings.value = null; editing.value = false; saving.value = false; status.value = ''; emit('busyChanged', false); void load() }, { immediate: true })
onBeforeUnmount(() => { scopeVersion++; request?.abort(); emit('busyChanged', false) })
</script>

<template>
  <section class="organization-time-zone" aria-label="Часовой пояс организации" :aria-busy="saving">
    <form v-if="editing" class="organization-time-zone__form" @submit.prevent="save">
      <UiSelect v-model="draft" label="Часовой пояс" :options="options" :error="fieldError" :disabled="disabled || saving" required />
      <p v-if="draft && draft !== settings?.timeZoneId" class="organization-time-zone__hint">Время смен и отправки напоминаний будет рассчитано в новом часовом поясе. Указанные даты и часы записей сохранятся.</p>
      <div v-if="error" class="organization-time-zone__retry" role="alert"><p class="organization-time-zone__error">{{ error }}</p><UiButton variant="secondary" :disabled="saving" @click="load">Обновить данные</UiButton></div>
      <div class="organization-time-zone__actions"><UiButton type="button" variant="secondary" :disabled="saving" @click="cancel">Отмена</UiButton><UiButton type="submit" :disabled="disabled || saving">{{ saving ? 'Сохраняем…' : 'Сохранить' }}</UiButton></div>
    </form>
    <dl v-else class="organization-time-zone__info">
      <div class="organization-time-zone__row">
        <dt>Часовой пояс</dt>
        <dd>
          <div v-if="settings" class="organization-time-zone__value">
            <span>{{ organizationTimeZoneName(settings.timeZoneId) }}</span>
            <UiButton v-if="settings.canManage" variant="secondary" aria-label="Изменить часовой пояс" :disabled="disabled || saving" @click="startEdit">{{ settings.timeZoneId ? 'Изменить' : 'Выбрать' }}</UiButton>
          </div>
          <p v-else-if="!error" class="organization-time-zone__hint" role="status">Загружаем…</p>
          <p v-if="settings && !settings.timeZoneId" class="organization-time-zone__hint">До настройки используется UTC.</p>
          <div v-if="error" class="organization-time-zone__retry" role="alert"><p class="organization-time-zone__error">{{ error }}</p><UiButton variant="secondary" :disabled="saving" @click="load">Повторить</UiButton></div>
          <p v-if="status" class="organization-time-zone__hint" role="status">{{ status }}</p>
        </dd>
      </div>
    </dl>
  </section>
</template>

<style scoped>
.organization-time-zone { min-width:0; margin-top:12px; padding-top:12px; border-top:1px solid var(--crm-line,#dce2e9); }
.organization-time-zone__info { margin:0; }
.organization-time-zone__row { display:grid; grid-template-columns:minmax(150px,.7fr) minmax(0,1.4fr); align-items:center; gap:18px; font-size:13px; }
.organization-time-zone__row dt { color:var(--crm-text-muted,#687386); font-weight:400; }
.organization-time-zone__row dd { min-width:0; margin:0; color:var(--crm-text,#202631); }
.organization-time-zone__value,.organization-time-zone__actions { display:flex; align-items:center; justify-content:space-between; flex-wrap:wrap; gap:10px; }
.organization-time-zone__value span { overflow-wrap:anywhere; }
.organization-time-zone__form { display:grid; gap:12px; max-width:720px; }
.organization-time-zone__hint,.organization-time-zone__error { margin:0; font-size:12px; line-height:1.5; }
.organization-time-zone__hint { color:var(--crm-text-muted,#687386); }
.organization-time-zone__row .organization-time-zone__hint { margin-top:5px; }
.organization-time-zone__error { color:var(--crm-danger-text,#b42318); }
.organization-time-zone__retry { display:flex; align-items:center; flex-wrap:wrap; gap:10px; }
.organization-time-zone__actions { justify-content:flex-end; }
@media(max-width:600px) { .organization-time-zone__row { grid-template-columns:minmax(0,1fr); gap:5px; } .organization-time-zone__actions :deep(.ui-button) { flex:1 1 auto; } }
</style>

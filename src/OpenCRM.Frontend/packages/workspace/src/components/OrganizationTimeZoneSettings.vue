<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiSelect, useLocalization } from '@opencrm/ui'
import '../timeZoneMessages'
import { loadOrganizationTimeZone, saveOrganizationTimeZone, type OrganizationTimeZoneSettings } from '../api/organizationTimeZone'
import { organizationTimeZoneName, getOrganizationTimeZones } from '../utils/organizationTimeZone'

const props = defineProps<{ organizationId: string; disabled?: boolean }>()
const { t, locale } = useLocalization()
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
const options = computed(() => {
  const zones = getOrganizationTimeZones(locale.value)
  const selected = settings.value?.timeZoneId
  if (selected && !zones.some(zone => zone.value === selected)) {
    zones.push({ value: selected, label: organizationTimeZoneName(selected, locale.value) })
  }

  return zones
})

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
    if (!active.signal.aborted && scope === scopeVersion && request === active) error.value = caught instanceof Error ? caught.message : t('timeZone.loadError')
  }
}

function startEdit() {
  if (!settings.value?.canManage || settings.value.canChange === false || props.disabled || saving.value) return
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
  if (!settings.value?.canManage || settings.value.canChange === false || props.disabled || saving.value) return
  fieldError.value = draft.value ? '' : t('timeZone.chooseRequired')
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
    status.value = t('timeZone.saved')
    emit('busyChanged', false)
    emit('saved')
  } catch (caught) {
    if (scope === scopeVersion) error.value = caught instanceof Error ? caught.message : t('timeZone.saveError')
  } finally {
    if (scope === scopeVersion) saving.value = false
  }
}

watch(() => props.organizationId, () => { scopeVersion++; request?.abort(); settings.value = null; editing.value = false; saving.value = false; status.value = ''; emit('busyChanged', false); void load() }, { immediate: true })
onBeforeUnmount(() => { scopeVersion++; request?.abort(); emit('busyChanged', false) })
</script>

<template>
  <section class="organization-time-zone" :aria-label="t('timeZone.organizationZone')" :aria-busy="saving">
    <form v-if="editing" class="organization-time-zone__form" @submit.prevent="save">
      <UiSelect v-model="draft" :label="t('timeZone.timeZone')" :options="options" :error="fieldError" :disabled="disabled || saving" required />
      <p v-if="draft && draft !== settings?.timeZoneId" class="organization-time-zone__hint">{{ t('timeZone.changeNotice') }}</p>
      <div v-if="error" class="organization-time-zone__retry" role="alert"><p class="organization-time-zone__error">{{ error }}</p><UiButton variant="secondary" :disabled="saving" @click="load">{{ t('timeZone.refresh') }}</UiButton></div>
      <div class="organization-time-zone__actions"><UiButton type="button" variant="secondary" :disabled="saving" @click="cancel">{{ t('timeZone.cancel') }}</UiButton><UiButton type="submit" :disabled="disabled || saving">{{ saving ? t('timeZone.saving') : t('timeZone.save') }}</UiButton></div>
    </form>
    <dl v-else class="organization-time-zone__info">
      <div class="organization-time-zone__row">
        <dt>{{ t('timeZone.timeZone') }}</dt>
        <dd>
          <div v-if="settings" class="organization-time-zone__value">
            <span>{{ organizationTimeZoneName(settings.timeZoneId, locale, t('timeZone.notConfigured')) }}</span>
            <UiButton v-if="settings.canManage && settings.canChange !== false" variant="secondary" :aria-label="t('timeZone.changeZone')" :disabled="disabled || saving" @click="startEdit">{{ settings.timeZoneId ? t('timeZone.change') : t('timeZone.choose') }}</UiButton>
          </div>
          <p v-else-if="!error" class="organization-time-zone__hint" role="status">{{ t('timeZone.loading') }}</p>
          <p v-if="settings?.canChange === false" class="organization-time-zone__hint">{{ t('timeZone.locked') }}</p>
          <p v-if="settings && !settings.timeZoneId" class="organization-time-zone__hint">{{ t('timeZone.utcDefault') }}</p>
          <div v-if="error" class="organization-time-zone__retry" role="alert"><p class="organization-time-zone__error">{{ error }}</p><UiButton variant="secondary" :disabled="saving" @click="load">{{ t('timeZone.retry') }}</UiButton></div>
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

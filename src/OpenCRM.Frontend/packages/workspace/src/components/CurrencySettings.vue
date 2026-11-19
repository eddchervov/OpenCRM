<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiSelect, useLocalization } from '@opencrm/ui'
import { loadCurrencySettings, saveCurrencySettings, type CurrencySettings } from '../api/currency'
import '../currencyMessages'

const props = defineProps<{ organizationId?: string; disabled?: boolean }>()
const emit = defineEmits<{ saved: [currencyCode: string]; busyChanged: [busy: boolean] }>()
const { t, locale } = useLocalization()
const settings = ref<CurrencySettings>()
const draft = ref('')
const editing = ref(false)
const saving = ref(false)
const loading = ref(false)
const error = ref('')
const status = ref('')
let request: AbortController | undefined
let scopeVersion = 0

const options = computed(() => {
  const names = new Intl.DisplayNames([locale.value], { type: 'currency' })

  return (settings.value?.supportedCurrencies ?? []).map(value => ({ value, label: `${value} · ${names.of(value) ?? value}` }))
})

async function load() {
  request?.abort()
  const active = new AbortController()
  request = active
  const scope = scopeVersion
  loading.value = true
  error.value = ''
  try {
    const result = await loadCurrencySettings(props.organizationId, active.signal)
    if (active.signal.aborted || scope !== scopeVersion) return
    settings.value = result
    draft.value = result.currencyCode
    editing.value = false
    emit('busyChanged', false)
  } catch (caught) {
    if (!active.signal.aborted && scope === scopeVersion) error.value = caught instanceof Error ? caught.message : t('currency.loadError')
  } finally {
    if (!active.signal.aborted && scope === scopeVersion) loading.value = false
  }
}

function startEdit() {
  if (!settings.value?.canChange || !settings.value.canManage || props.disabled || saving.value) return
  draft.value = settings.value.currencyCode
  error.value = ''
  status.value = ''
  editing.value = true
  emit('busyChanged', true)
}

function cancel() {
  if (saving.value) return
  editing.value = false
  error.value = ''
  emit('busyChanged', false)
}

async function save() {
  if (!settings.value?.canChange || !settings.value.canManage || props.disabled || saving.value) return
  const scope = scopeVersion
  saving.value = true
  error.value = ''
  try {
    const result = await saveCurrencySettings(props.organizationId, draft.value, settings.value.currencyCode)
    if (scope !== scopeVersion) return
    settings.value = result
    editing.value = false
    status.value = t('currency.saved')
    emit('busyChanged', false)
    emit('saved', result.currencyCode)
  } catch (caught) {
    if (scope === scopeVersion) error.value = caught instanceof Error ? caught.message : t('currency.saveError')
  } finally {
    if (scope === scopeVersion) saving.value = false
  }
}

watch(() => props.organizationId, () => {
  scopeVersion++
  settings.value = undefined
  saving.value = false
  editing.value = false
  status.value = ''
  emit('busyChanged', false)
  void load()
}, { immediate: true })
onBeforeUnmount(() => { scopeVersion++; request?.abort(); emit('busyChanged', false) })
</script>

<template>
  <section class="currency-settings" :aria-label="t(organizationId ? 'currency.title' : 'currency.personalTitle')" :aria-busy="saving || loading">
    <h3>{{ t(organizationId ? 'currency.title' : 'currency.personalTitle') }}</h3>
    <form v-if="editing" @submit.prevent="save">
      <UiSelect v-model="draft" :label="t('currency.title')" :options="options" :searchable="false" :disabled="saving || disabled" required />
      <p>{{ t('currency.hint') }}</p>
      <div class="currency-settings__actions">
        <UiButton type="button" variant="secondary" :disabled="saving" @click="cancel">{{ t('currency.cancel') }}</UiButton>
        <UiButton type="submit" :disabled="saving || disabled">{{ t(saving ? 'currency.saving' : 'currency.save') }}</UiButton>
      </div>
    </form>
    <template v-else-if="settings">
      <div class="currency-settings__actions">
        <strong>{{ options.find(option => option.value === settings?.currencyCode)?.label ?? settings.currencyCode }}</strong>
        <UiButton v-if="settings.canManage && settings.canChange" type="button" variant="secondary" :disabled="disabled || saving" @click="startEdit">{{ t('currency.change') }}</UiButton>
      </div>
      <p v-if="!settings.canChange" class="currency-settings__locked">{{ t('currency.locked') }}</p>
    </template>
    <p v-else-if="loading" role="status">{{ t('currency.loading') }}</p>
    <div v-if="error" role="alert"><p class="currency-settings__error">{{ error }}</p><UiButton type="button" variant="secondary" :disabled="saving || loading" @click="load">{{ t('currency.retry') }}</UiButton></div>
    <p v-if="status" role="status">{{ status }}</p>
  </section>
</template>

<style scoped>
.currency-settings { border-top: 1px solid var(--crm-line, #dce2e9); margin-top: 16px; padding-top: 16px; }
.currency-settings h3 { font-size: 14px; margin: 0 0 12px; }
.currency-settings form { display: grid; gap: 12px; max-width: 720px; }
.currency-settings p { color: var(--crm-text-muted, #687386); font-size: 13px; line-height: 1.5; }
.currency-settings__actions { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
.currency-settings .currency-settings__error,
.currency-settings .currency-settings__locked { color: var(--crm-danger-text, #b42318); }
.currency-settings__locked { font-weight: 600; }
</style>

<script setup lang="ts">
import { formattingCulture, useLocalization } from '@opencrm/ui/i18n'
import '../adminCatalogMessages'
import { onMounted, ref } from 'vue'
import { getFeatures, saveFeature, toggleFeature } from '@/api/adminOperationsApi'
import type { AdminFeatureDefinition } from '@/models/AdminFeatureDefinition'

const { t } = useLocalization()

const items = ref<AdminFeatureDefinition[]>([])
const editingId = ref<number | null>(null)
const isOpen = ref(false)
const isSaving = ref(false)
const error = ref('')
const form = ref(emptyForm())

onMounted(load)

async function load() {
  try { items.value = await getFeatures() } catch { error.value = t('adminCatalog.loadFeaturesError') }
}

function emptyForm() {
  return { code: '', name: '', description: '', category: null as string | null, scopeCode: 'both', audienceCode: 'user', valueType: 'Boolean', isActive: true }
}

function open(item?: AdminFeatureDefinition) {
  editingId.value = item?.id ?? null
  form.value = item ? { code: item.code, name: item.name, description: item.description, category: item.category, scopeCode: item.scopeCode, audienceCode: item.audienceCode, valueType: item.valueType, isActive: item.isActive } : emptyForm()
  isOpen.value = true
  error.value = ''
}

async function save() {
  isSaving.value = true
  try { await saveFeature(form.value, editingId.value ?? undefined); isOpen.value = false; await load() }
  catch { error.value = t('adminCatalog.saveFeatureError') } finally { isSaving.value = false }
}
</script>

<template>
  <section class="operations-page">
    <h1>{{ t('adminCatalog.featuresTitle') }}</h1>
    <div class="operations-toolbar"><button class="primary-button" type="button" @click="open()">{{ t('adminCatalog.add') }}</button></div>
    <div v-if="error" class="form-message form-message--error">{{ error }}</div>
    <div class="table-wrap"><table class="data-table"><thead><tr><th>{{ t('adminCatalog.name') }}</th><th>{{ t('adminCatalog.code') }}</th><th>{{ t('adminCatalog.scope') }}</th><th>{{ t('adminCatalog.type') }}</th><th>{{ t('adminCatalog.status') }}</th><th></th></tr></thead><tbody>
      <tr v-for="item in items" :key="item.id"><td><strong>{{ item.name }}</strong></td><td>{{ item.code }}</td><td>{{ item.scopeCode }}</td><td>{{ item.valueType }}</td><td>{{ item.isActive ? t('adminCatalog.enabled') : t('adminCatalog.disabled') }}</td><td><button class="table-action" @click="open(item)">{{ t('adminCatalog.edit') }}</button> <button class="table-action" @click="toggleFeature(item.id).then(load)">{{ item.isActive ? t('adminCatalog.disable') : t('adminCatalog.enable') }}</button></td></tr>
    </tbody></table></div>
    <div v-if="isOpen" class="modal-backdrop" @click.self="isOpen = false"><form class="modal-card" @submit.prevent="save"><div class="modal-heading"><strong>{{ editingId ? t('adminCatalog.editFeature') : t('adminCatalog.newFeature') }}</strong><button type="button" @click="isOpen = false">×</button></div><div class="modal-grid">
      <label><span>{{ t('adminCatalog.name') }}</span><input v-model="form.name" required></label><label><span>{{ t('adminCatalog.code') }}</span><input v-model="form.code" required></label><label class="span-2"><span>{{ t('adminCatalog.description') }}</span><textarea v-model="form.description" rows="3"></textarea></label><label><span>{{ t('adminCatalog.category') }}</span><input v-model="form.category"></label><label><span>{{ t('adminCatalog.valueType') }}</span><SearchSelect v-model="form.valueType" :options="[{ value: 'Boolean', label: t('adminCatalog.boolean') }, { value: 'Integer', label: t('adminCatalog.integer') }]" /></label><label><span>{{ t('adminCatalog.scope') }}</span><SearchSelect v-model="form.scopeCode" :options="[{ value: 'user', label: t('adminCatalog.user') }, { value: 'organization', label: t('adminCatalog.organization') }, { value: 'both', label: t('adminCatalog.both') }]" /></label><label><span>{{ t('adminCatalog.audience') }}</span><SearchSelect v-model="form.audienceCode" :options="[{ value: 'user', label: t('adminCatalog.user') }, { value: 'organization', label: t('adminCatalog.organization') }]" /></label>
    </div><div class="modal-actions"><button class="primary-button" :disabled="isSaving">{{ t('adminCatalog.save') }}</button><button class="secondary-button" type="button" @click="isOpen = false">{{ t('adminCatalog.cancel') }}</button></div></form></div>
  </section>
</template>

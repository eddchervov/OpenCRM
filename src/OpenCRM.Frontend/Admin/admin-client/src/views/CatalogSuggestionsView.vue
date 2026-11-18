<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { getCatalogSuggestions, reviewCatalogSuggestion } from '@/api/adminOperationsApi'
import type { AdminCatalogSuggestion } from '@/models/AdminCatalogSuggestion'
import { getVehicleKindLabel } from '@/utils/adminSystemLabels'
import { useAdminCurrentUserStore } from '@/stores/adminCurrentUserStore'
import { useLocalization } from '@opencrm/ui/i18n'
import '../adminOperationsMessages'
const currentUserStore = useAdminCurrentUserStore()
const { t, locale } = useLocalization()
const items = ref<AdminCatalogSuggestion[]>([]), selected = ref<AdminCatalogSuggestion | null>(null), status = ref(''), comment = ref(''), error = ref(''), isSaving = ref(false)
const canManage = computed(() => currentUserStore.isSystem || currentUserStore.currentUser?.permissions.includes('Admin.Content.Manage') === true)
onMounted(load)
async function load() { try { items.value = await getCatalogSuggestions(status.value) } catch { error.value = t('adminOperations.suggestionLoadError') } }
async function review(action: 'approve' | 'reject') { if (!selected.value) return; isSaving.value = true; try { await reviewCatalogSuggestion(selected.value.id, action, comment.value); selected.value = null; await load() } catch { error.value = t('adminOperations.suggestionReviewError') } finally { isSaving.value = false } }
function statusLabel(status: number, statusText: string) {
  if (status === 1) return t('adminOperations.moderation')
  if (status === 2) return t('adminOperations.approved')
  if (status === 3) return t('adminOperations.rejected')
  return statusText
}
function kindLabel(kind: number, kindText: string) {
  return getVehicleKindLabel(kind, kindText, locale.value)
}
</script>
<template><section class="operations-page"><h1>{{ t('adminOperations.catalogSuggestions') }}</h1><div class="operations-toolbar"><SearchSelect v-model="status" :options="[{ value: '', label: t('adminOperations.all') }, { value: '1', label: t('adminOperations.moderation') }, { value: '2', label: t('adminOperations.approved') }, { value: '3', label: t('adminOperations.rejected') }]" @change="load" /></div><div v-if="error" class="form-message form-message--error">{{ error }}</div><div class="table-wrap"><table class="data-table"><thead><tr><th>{{ t('adminOperations.brand') }}</th><th>{{ t('adminOperations.model') }}</th><th>{{ t('adminOperations.kind') }}</th><th>{{ t('adminOperations.author') }}</th><th>{{ t('adminOperations.status') }}</th></tr></thead><tbody><tr v-for="item in items" :key="item.id" @click="selected = item; comment = item.adminComment || ''"><td>{{ item.brandName }}</td><td>{{ item.modelName }}</td><td>{{ kindLabel(item.kind, item.kindText) }}</td><td>{{ item.suggestedByEmail || '—' }}</td><td>{{ statusLabel(item.status, item.statusText) }}</td></tr></tbody></table></div><div v-if="selected" class="modal-backdrop" @click.self="selected = null"><div class="modal-card"><div class="modal-heading"><strong>{{ selected.brandName }} {{ selected.modelName }}</strong><button type="button" @click="selected = null">×</button></div><label><span>{{ t('adminOperations.comment') }}</span><textarea v-model="comment" rows="4" :disabled="!canManage"></textarea></label><div class="modal-actions"><button v-if="canManage" class="primary-button" :disabled="isSaving || selected.status !== 1" @click="review('approve')">{{ t('adminOperations.approve') }}</button><button v-if="canManage" class="danger-button" :disabled="isSaving || selected.status !== 1" @click="review('reject')">{{ t('adminOperations.reject') }}</button><button class="secondary-button" @click="selected = null">{{ canManage ? t('adminOperations.cancel') : t('adminOperations.close') }}</button></div></div></div></section></template>

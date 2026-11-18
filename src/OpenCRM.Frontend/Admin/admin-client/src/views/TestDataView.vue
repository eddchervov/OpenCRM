<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useLocalization } from '@opencrm/ui/i18n'
import '../adminOperationsMessages'
import { generateTestData, getTestOrganizations, getTestVehicles } from '@/api/adminOperationsApi'
import type { AdminTestDataOrganization } from '@/models/AdminTestDataOrganization'
import type { AdminTestDataVehicle } from '@/models/AdminTestDataVehicle'
const organizations = ref<AdminTestDataOrganization[]>([]), vehicles = ref<AdminTestDataVehicle[]>([]), organizationId = ref(''), vehicleId = ref(''), search = ref(''), action = ref<'vehicles'|'maintenances'|'defects'|null>(null), count = ref(10), mode = ref('completed'), error = ref(''), success = ref(''), isSaving = ref(false)
const { t } = useLocalization()
onMounted(loadOrganizations)
async function loadOrganizations() { try { organizations.value = await getTestOrganizations(search.value) } catch { error.value = t('adminOperations.loadOrganizationsError') } }
async function loadVehicles() { vehicleId.value = ''; vehicles.value = organizationId.value ? await getTestVehicles(organizationId.value) : [] }
function open(next: 'vehicles'|'maintenances'|'defects') { action.value = next; count.value = next === 'maintenances' ? 20 : 10 }
async function generate() { if (isSaving.value || !organizationId.value || (action.value !== 'vehicles' && !vehicleId.value)) return; const base = `/api/v1/admin/test-data/organizations/${organizationId.value}`; const path = action.value === 'vehicles' ? `${base}/vehicles/generate` : `${base}/vehicles/${vehicleId.value}/${action.value}/generate`; isSaving.value = true; try { const result = await generateTestData(path, count.value, action.value === 'maintenances' ? mode.value : undefined); success.value = result.message; action.value = null; await loadOrganizations(); await loadVehicles() } catch { error.value = t('adminOperations.createTestDataError') } finally { isSaving.value = false } }
</script>
<template>
  <section class="operations-page operation-form-page">
    <h1>{{ t('adminOperations.testData') }}</h1>
    <div v-if="error" class="form-message form-message--error">{{ error }}</div>
    <div v-if="success" class="form-message form-message--success">{{ success }}</div>
    <div class="standalone-form">
      <div class="operations-toolbar">
        <input v-model="search" :placeholder="t('adminOperations.orgOrOwner')" @keyup.enter="loadOrganizations">
        <button class="secondary-button" @click="loadOrganizations">{{ t('adminOperations.find') }}</button>
      </div>
      <label><span>{{ t('adminOperations.organization') }}</span><SearchSelect v-model="organizationId" :options="organizations.map(item => ({ value: item.id, label: `${item.name} · ${item.ownerEmail}` }))" :placeholder="t('adminOperations.selectOrganization')" @change="loadVehicles" /></label>
      <label><span>{{ t('adminOperations.vehicle') }}</span><SearchSelect v-model="vehicleId" :disabled="!organizationId" :options="vehicles.map(item => ({ value: item.id, label: [item.displayName, item.name].filter(Boolean).join(' · ') }))" :placeholder="t('adminOperations.selectVehicle')" /></label>
      <div class="modal-actions">
        <button class="primary-button" :disabled="!organizationId" @click="open('vehicles')">{{ t('adminOperations.createVehicles') }}</button>
        <button class="secondary-button" :disabled="!vehicleId" @click="open('maintenances')">{{ t('adminOperations.createMaintenances') }}</button>
        <button class="secondary-button" :disabled="!vehicleId" @click="open('defects')">{{ t('adminOperations.createDefects') }}</button>
      </div>
    </div>
    <div v-if="action" class="modal-backdrop" @click.self="action = null">
      <form class="modal-card modal-card--compact" @submit.prevent="generate">
        <div class="modal-heading"><strong>{{ t('adminOperations.createTestData') }}</strong><button type="button" @click="action = null">×</button></div>
        <label><span>{{ t('adminOperations.count') }}</span><input v-model.number="count" type="number" min="1" :max="action === 'maintenances' ? 300 : 100" required></label>
        <label v-if="action === 'maintenances'"><span>{{ t('adminOperations.mode') }}</span><SearchSelect v-model="mode" :options="[{ value: 'completed', label: t('adminOperations.completed') }, { value: 'planned', label: t('adminOperations.planned') }]" /></label>
        <div class="modal-actions"><button class="primary-button" :disabled="isSaving">{{ t('adminOperations.create') }}</button><button class="secondary-button" type="button" @click="action = null">{{ t('adminOperations.cancel') }}</button></div>
      </form>
    </div>
  </section>
</template>

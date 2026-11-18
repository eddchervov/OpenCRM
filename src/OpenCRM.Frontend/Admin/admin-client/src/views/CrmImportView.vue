<script setup lang="ts">
import { computed, ref } from 'vue'
import { ApiError } from '@/api/ApiError'
import {
  confirmCrmImport,
  createCrmImportPreview,
  getCrmImportOrganizations,
  getCrmImportPreviewReport,
  getCrmImportResultReport,
  getCrmImportUsers
} from '@/api/adminCrmImportApi'
import type { CrmImportOrganization, CrmImportPreview, CrmImportPreviewRow, CrmImportResult, CrmImportUser } from '@/api/adminCrmImportApi'
import { useLocalization } from '@opencrm/ui/i18n'
import { importRowMessage } from '@opencrm/workspace/importRowMessage'
import '../adminOperationsMessages'

const { t } = useLocalization()

const userSearch = ref('')
const users = ref<CrmImportUser[]>([])
const organizations = ref<CrmImportOrganization[]>([])
const userId = ref('')
const organizationId = ref('')
const file = ref<File | null>(null)
const preview = ref<CrmImportPreview | null>(null)
const result = ref<CrmImportResult | null>(null)
const error = ref('')
const isLoadingUsers = ref(false)
const isLoadingOrganizations = ref(false)
const isPreviewing = ref(false)
const isImporting = ref(false)
const isDownloading = ref(false)

const canPreview = computed(() => Boolean(userId.value && organizationId.value && file.value) && !isPreviewing.value)
const canConfirm = computed(() => Boolean(preview.value && !preview.value.alreadyImported && preview.value.summary.error === 0) && !isImporting.value)
const userOptions = computed(() => users.value.map(user => ({
  value: user.id,
  label: [user.email, user.phone].filter(Boolean).join(' · ') || user.id
})))
const organizationOptions = computed(() => organizations.value.map(organization => ({
  value: organization.id,
  label: [organization.name, organization.ownerEmail].filter(Boolean).join(' · ')
})))

async function searchUsers(): Promise<void> {
  isLoadingUsers.value = true
  error.value = ''

  try {
    users.value = await getCrmImportUsers(userSearch.value.trim())
  } catch {
    error.value = t('adminOperations.importUsersError')
  } finally {
    isLoadingUsers.value = false
  }
}

async function loadOrganizations(): Promise<void> {
  organizationId.value = ''
  organizations.value = []
  preview.value = null
  result.value = null

  if (!userId.value) {
    return
  }

  isLoadingOrganizations.value = true
  error.value = ''

  try {
    organizations.value = await getCrmImportOrganizations(userId.value)
  } catch {
    error.value = t('adminOperations.importOrganizationsError')
  } finally {
    isLoadingOrganizations.value = false
  }
}

function changeOrganization(): void {
  preview.value = null
  result.value = null
}
function selectFile(event: Event): void {
  const input = event.target as HTMLInputElement
  file.value = input.files?.[0] ?? null
  preview.value = null
  result.value = null
  error.value = ''
}

async function createPreview(): Promise<void> {
  if (!file.value || !userId.value || !organizationId.value) {
    return
  }

  isPreviewing.value = true
  error.value = ''
  result.value = null

  try {
    preview.value = await createCrmImportPreview(userId.value, organizationId.value, file.value)
  } catch (requestError) {
    error.value = requestError instanceof ApiError
      ? requestError.message
      : t('adminOperations.importPreviewError')
  } finally {
    isPreviewing.value = false
  }
}

async function downloadReport(kind: 'preview' | 'result'): Promise<void> {
  if (!preview.value || isDownloading.value) {
    return
  }

  isDownloading.value = true
  error.value = ''

  try {
    const report = kind === 'preview'
      ? await getCrmImportPreviewReport(preview.value.id)
      : await getCrmImportResultReport(preview.value.id)
    const url = URL.createObjectURL(report)
    const link = document.createElement('a')
    link.href = url
    link.download = kind === 'preview' ? 'crm-import-preview.csv' : 'crm-import-result.csv'
    link.click()
    window.setTimeout(() => URL.revokeObjectURL(url), 1000)
  } catch {
    error.value = t('adminOperations.importReportError')
  } finally {
    isDownloading.value = false
  }
}

async function confirmImport(): Promise<void> {
  if (!preview.value || !canConfirm.value) {
    return
  }

  isImporting.value = true
  error.value = ''

  try {
    result.value = await confirmCrmImport(preview.value.id)
    preview.value.alreadyImported = true
  } catch (requestError) {
    error.value = requestError instanceof ApiError
      ? requestError.message
      : t('adminOperations.importConfirmError')
  } finally {
    isImporting.value = false
  }
}

function rowLabel(row: CrmImportPreviewRow): string {
  const values = row.type === 'client'
    ? [row.name, row.phone]
    : [row.name, row.brand, row.model, row.vin, row.registrationCountryCode, row.plate]

  return values.filter(Boolean).join(' · ') || '—'
}

function statusLabel(status: string): string {
  if (status === 'create') return t('adminOperations.willAdd')
  if (status === 'duplicate') return t('adminOperations.duplicate')
  return t('adminOperations.rowError')
}
</script>

<template>
  <section class="operations-page crm-import-page">
    <h1>{{ t('adminOperations.crmImport') }}</h1>

    <div v-if="error" class="form-message form-message--error">{{ error }}</div>
    <div v-if="preview?.alreadyImported && !result" class="form-message form-message--warning">
      {{ t('adminOperations.alreadyImportedWarning') }}
    </div>
    <div v-if="result" class="form-message form-message--success">
      {{ t('adminOperations.importResult', { clients: result.createdClients, vehicles: result.createdVehicles, duplicates: result.skippedDuplicates, errors: result.errors }) }}
    </div>

    <div class="crm-import-form">
      <div class="crm-import-user-search">
        <label for="crm-import-user-search">{{ t('adminOperations.user') }}</label>
        <div class="operations-toolbar">
          <input id="crm-import-user-search" v-model="userSearch" type="search" :placeholder="t('adminOperations.emailOrPhone')" @keyup.enter="searchUsers">
          <button class="secondary-button" type="button" :disabled="isLoadingUsers" @click="searchUsers">
            {{ isLoadingUsers ? t('adminOperations.searching') : t('adminOperations.find') }}
          </button>
        </div>
        <SearchSelect v-model="userId" :options="userOptions" :placeholder="t('adminOperations.chooseUser')" @change="loadOrganizations" />
      </div>

      <label>
        <span>{{ t('adminOperations.organization') }}</span>
        <SearchSelect v-model="organizationId" :disabled="!userId || isLoadingOrganizations" :options="organizationOptions" :placeholder="t('adminOperations.chooseOrganization')" @change="changeOrganization" />
        <small v-if="isLoadingOrganizations">{{ t('adminOperations.loadingOrganizations') }}</small>
      </label>

      <label>
        <span>{{ t('adminOperations.exportFile') }}</span>
        <input type="file" accept=".csv,.xlsx,.json,.xml,text/csv,application/json,application/xml,text/xml,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" @change="selectFile">
        <small>{{ t('adminOperations.supportedFormats') }}</small>
        <small>{{ t('adminOperations.supportedFields') }}</small>
      </label>

      <button class="primary-button" type="button" :disabled="!canPreview" @click="createPreview">
        {{ isPreviewing ? t('adminOperations.checkingFile') : t('adminOperations.showPreview') }}
      </button>
    </div>

    <section v-if="preview" class="crm-import-preview" aria-labelledby="preview-title">
      <div class="crm-import-preview__heading">
        <div>
          <h2 id="preview-title">{{ t('adminOperations.preview', { fileName: preview.fileName }) }}</h2>
          <p>{{ t('adminOperations.clients', { count: preview.summary.clients }) }} · {{ t('adminOperations.vehicles', { count: preview.summary.vehicles }) }} · {{ t('adminOperations.toCreate', { count: preview.summary.create }) }} · {{ t('adminOperations.duplicates', { count: preview.summary.duplicate }) }} · {{ t('adminOperations.errors', { count: preview.summary.error }) }}</p>
        </div>
        <button class="secondary-button" type="button" :disabled="isDownloading" @click="downloadReport('preview')">{{ t('adminOperations.downloadPreview') }}</button>
      </div>

      <div class="table-wrap">
        <table class="data-table">
          <thead>
            <tr><th>{{ t('adminOperations.row') }}</th><th>{{ t('adminOperations.type') }}</th><th>{{ t('adminOperations.record') }}</th><th>{{ t('adminOperations.validationResult') }}</th><th>{{ t('adminOperations.details') }}</th></tr>
          </thead>
          <tbody>
            <tr v-for="row in preview.rows" :key="row.number">
              <td>{{ row.number }}</td>
              <td>{{ row.type }}</td>
              <td>{{ rowLabel(row) }}</td>
              <td><span class="crm-import-status" :class="`crm-import-status--${row.status}`">{{ statusLabel(row.status) }}</span></td>
              <td>{{ importRowMessage(row) || '—' }}</td>
            </tr>
            <tr v-if="preview.rows.length === 0"><td colspan="5">{{ t('adminOperations.noImportRows') }}</td></tr>
          </tbody>
        </table>
      </div>

      <div class="crm-import-preview__actions">
        <span v-if="preview.alreadyImported">{{ t('adminOperations.alreadyProcessed') }}</span>
        <span v-else-if="preview.summary.error > 0">{{ t('adminOperations.fixImportSource') }}</span>
        <span v-else>{{ t('adminOperations.reviewImportReport') }}</span>
        <button class="primary-button" type="button" :disabled="!canConfirm" @click="confirmImport">
          {{ isImporting ? t('adminOperations.importing') : t('adminOperations.confirmImport') }}
        </button>
      </div>
    </section>

    <div v-if="result" class="crm-import-result">
      <h2>{{ t('adminOperations.importSummary') }}</h2>
      <button class="secondary-button" type="button" :disabled="isDownloading" @click="downloadReport('result')">{{ t('adminOperations.downloadResultReport') }}</button>
    </div>
  </section>
</template>

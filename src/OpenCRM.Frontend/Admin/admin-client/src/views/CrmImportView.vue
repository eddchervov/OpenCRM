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
    error.value = 'Не удалось загрузить пользователей.'
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
    error.value = 'Не удалось загрузить организации пользователя.'
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
      : 'Не удалось проверить файл. Убедитесь, что его формат и содержимое поддерживаются.'
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
    error.value = 'Не удалось скачать отчёт.'
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
      : 'Не удалось выполнить импорт. Предпросмотр можно скачать и проверить.'
  } finally {
    isImporting.value = false
  }
}

function rowLabel(row: CrmImportPreviewRow): string {
  const values = row.type === 'client'
    ? [row.name, row.phone]
    : [row.name, row.brand, row.model, row.vin, row.plate]

  return values.filter(Boolean).join(' · ') || '—'
}

function statusLabel(status: string): string {
  if (status === 'create') return 'Будет добавлено'
  if (status === 'duplicate') return 'Дубль'
  return 'Ошибка'
}
</script>

<template>
  <section class="operations-page crm-import-page">
    <h1>Импорт из другой CRM</h1>

    <div v-if="error" class="form-message form-message--error">{{ error }}</div>
    <div v-if="preview?.alreadyImported && !result" class="form-message form-message--warning">
      Эти данные уже импортировались. Повторная запись не выполняется.
    </div>
    <div v-if="result" class="form-message form-message--success">
      Импорт завершён: клиентов добавлено — {{ result.createdClients }}, автомобилей — {{ result.createdVehicles }}, дублей пропущено — {{ result.skippedDuplicates }}, ошибок — {{ result.errors }}.
    </div>

    <div class="crm-import-form">
      <div class="crm-import-user-search">
        <label for="crm-import-user-search">Пользователь</label>
        <div class="operations-toolbar">
          <input id="crm-import-user-search" v-model="userSearch" type="search" placeholder="Email или телефон" @keyup.enter="searchUsers">
          <button class="secondary-button" type="button" :disabled="isLoadingUsers" @click="searchUsers">
            {{ isLoadingUsers ? 'Поиск…' : 'Найти' }}
          </button>
        </div>
        <SearchSelect v-model="userId" :options="userOptions" placeholder="Выберите пользователя" @change="loadOrganizations" />
      </div>

      <label>
        <span>Организация</span>
        <SearchSelect v-model="organizationId" :disabled="!userId || isLoadingOrganizations" :options="organizationOptions" placeholder="Выберите организацию" @change="changeOrganization" />
        <small v-if="isLoadingOrganizations">Загружаем организации…</small>
      </label>

      <label>
        <span>Файл экспорта</span>
        <input type="file" accept=".csv,.xlsx,.json,.xml,text/csv,application/json,application/xml,text/xml,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" @change="selectFile">
        <small>Поддерживаются CSV, Excel (.xlsx), JSON и XML.</small>
        <small>Поля: type (client или vehicle), name, phone, clientPhone, brand, model, vin, plate, year. Подходят и русские названия.</small>
      </label>

      <button class="primary-button" type="button" :disabled="!canPreview" @click="createPreview">
        {{ isPreviewing ? 'Проверяем файл…' : 'Проверить и показать предпросмотр' }}
      </button>
    </div>

    <section v-if="preview" class="crm-import-preview" aria-labelledby="preview-title">
      <div class="crm-import-preview__heading">
        <div>
          <h2 id="preview-title">Предпросмотр: {{ preview.fileName }}</h2>
          <p>Клиенты: {{ preview.summary.clients }} · Автомобили: {{ preview.summary.vehicles }} · К добавлению: {{ preview.summary.create }} · Дубли: {{ preview.summary.duplicate }} · Ошибки: {{ preview.summary.error }}</p>
        </div>
        <button class="secondary-button" type="button" :disabled="isDownloading" @click="downloadReport('preview')">Скачать предпросмотр CSV</button>
      </div>

      <div class="table-wrap">
        <table class="data-table">
          <thead>
            <tr><th>Строка</th><th>Тип</th><th>Запись</th><th>Результат проверки</th><th>Подробности</th></tr>
          </thead>
          <tbody>
            <tr v-for="row in preview.rows" :key="row.number">
              <td>{{ row.number }}</td>
              <td>{{ row.type }}</td>
              <td>{{ rowLabel(row) }}</td>
              <td><span class="crm-import-status" :class="`crm-import-status--${row.status}`">{{ statusLabel(row.status) }}</span></td>
              <td>{{ row.message || '—' }}</td>
            </tr>
            <tr v-if="preview.rows.length === 0"><td colspan="5">В файле не найдены записи для импорта.</td></tr>
          </tbody>
        </table>
      </div>

      <div class="crm-import-preview__actions">
        <span v-if="preview.alreadyImported">Файл уже был обработан. Повторный импорт заблокирован.</span>
        <span v-else-if="preview.summary.error > 0">Исправьте ошибки в источнике и загрузите файл снова.</span>
        <span v-else>Проверьте скачиваемый отчёт перед подтверждением импорта.</span>
        <button class="primary-button" type="button" :disabled="!canConfirm" @click="confirmImport">
          {{ isImporting ? 'Импортируем…' : 'Подтвердить импорт' }}
        </button>
      </div>
    </section>

    <div v-if="result" class="crm-import-result">
      <h2>Итог импорта</h2>
      <button class="secondary-button" type="button" :disabled="isDownloading" @click="downloadReport('result')">Скачать итоговый CSV-отчёт</button>
    </div>
  </section>
</template>

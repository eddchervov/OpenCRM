<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getSupportMessage, getSupportMessages, replyToSupportMessage, updateSupportMessage } from '@/api/adminOperationsApi'
import { ApiError } from '@/api/ApiError'
import type { AdminSupportMessage } from '@/models/AdminSupportMessage'
import { useAdminCurrentUserStore } from '@/stores/adminCurrentUserStore'

const currentUserStore = useAdminCurrentUserStore()
const route = useRoute()
const router = useRouter()
const items = ref<AdminSupportMessage[]>([])
const selected = ref<AdminSupportMessage | null>(null)
const search = ref('')
const status = ref('')
const source = ref('')
const page = ref(1)
const totalCount = ref(0)
const pageSize = ref(50)
const replyText = ref('')
const adminComment = ref('')
const nextStatus = ref('new')
const error = ref('')
const success = ref('')
const isLoading = ref(false)
const isSaving = ref(false)
const canManage = computed(() => currentUserStore.isSystem || currentUserStore.currentUser?.permissions.includes('Admin.Content.Manage') === true)
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize.value)))
const isDetail = computed(() => route.params.messageId !== undefined)

let refreshTimer: ReturnType<typeof setInterval> | undefined

onMounted(() => {
  load()
  void loadSelectedFromRoute()
  refreshTimer = setInterval(() => load(true), 30_000)
})

watch(() => route.params.messageId, () => void loadSelectedFromRoute())

async function loadSelectedFromRoute() {
  const id = Number(route.params.messageId)
  if (!Number.isInteger(id) || id <= 0) {
    selected.value = null
    return
  }

  try {
    const item = await getSupportMessage(id)
    open(item)
  } catch {
    error.value = 'Не удалось открыть обращение.'
  }
}

onUnmounted(() => {
  if (refreshTimer) clearInterval(refreshTimer)
})

async function load(isRefresh = false) {
  if (isLoading.value) return
  isLoading.value = true
  if (!isRefresh) error.value = ''

  try {
    const result = await getSupportMessages({
      status: status.value,
      search: search.value,
      source: source.value,
      page: page.value
    })
    items.value = result.items
    totalCount.value = result.totalCount
    pageSize.value = result.pageSize

    if (selected.value) {
      selected.value = result.items.find(item => item.id === selected.value?.id)
        ?? await getSupportMessage(selected.value.id)
    }
  } catch {
    if (!isRefresh) error.value = 'Не удалось загрузить обращения.'
  } finally {
    isLoading.value = false
  }
}

function applyFilters() {
  page.value = 1
  selected.value = null
  void load()
}

function open(item: AdminSupportMessage) {
  selected.value = item
  nextStatus.value = item.statusCode
  adminComment.value = item.adminComment ?? ''
  replyText.value = ''
  if (route.params.messageId !== String(item.id)) {
    void router.push(`/content/support/${item.id}`)
  }
}

async function sendReply() {
  if (!selected.value || !replyText.value.trim() || !canManage.value) return
  isSaving.value = true
  error.value = ''
  success.value = ''

  try {
    await replyToSupportMessage(selected.value.id, replyText.value.trim())
    replyText.value = ''
    await load()
    success.value = selected.value.userId ? 'Ответ сохранён в переписке.' : 'Ответ поставлен в очередь отправки на email.'
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) {
      error.value = 'Сеанс завершён. Войдите снова и повторите отправку.'
    } else if (cause instanceof ApiError && cause.status === 403) {
      error.value = 'Недостаточно прав для ответа на обращение.'
    } else {
      error.value = cause instanceof ApiError ? cause.message : 'Не удалось отправить ответ.'
    }
  } finally {
    isSaving.value = false
  }
}

function supportAttachmentUrl(id: string) {
  const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''
  return `${apiBaseUrl}/api/v1/admin/support-messages/attachments/${encodeURIComponent(id)}`
}

function formatAttachmentSize(size: number) {
  return `${(size / 1024 / 1024).toFixed(2)} МБ`
}

async function saveDetails() {
  if (!selected.value || !canManage.value) return
  isSaving.value = true
  error.value = ''

  try {
    await updateSupportMessage(selected.value.id, nextStatus.value, adminComment.value)
    await load()
  } catch {
    error.value = 'Не удалось сохранить статус и внутренний комментарий.'
  } finally {
    isSaving.value = false
  }
}

async function changePage(nextPage: number) {
  if (nextPage < 1 || nextPage > totalPages.value) return
  page.value = nextPage
  selected.value = null
  await load()
}

function date(value: string) {
  return new Intl.DateTimeFormat('ru-RU', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value))
}

function sender(item: AdminSupportMessage) {
  return item.userEmail || item.email || item.phone || item.name || 'Гость'
}

function entryLabel(kind: string) {
  if (kind === 'internal_comment' || kind === 'internal') return 'Внутренний комментарий'
  if (kind === 'status') return 'Изменение статуса'
  return ''
}

function deliveryStatusName(status: string | null) {
  if (status === 'delivered' || status === 'opened' || status === 'clicked') return 'Доставлено'
  if (status === 'sent' || status === 'accepted') return 'Принято почтовым сервисом'
  if (status === 'failed' || status === 'hard_bounced' || status === 'soft_bounced' || status === 'spam' || status === 'spam_block' || status === 'unsubscribed') return 'Ошибка доставки'
  if (status === 'simulated') return 'Отправка отключена'
  return 'В очереди отправки'
}
</script>

<template>
  <section class="operations-page support-page">
    <h1>Обращения</h1>

    <button v-if="isDetail" class="secondary-button support-back" type="button" @click="router.push('/content/support')">← К списку обращений</button>

    <div v-if="!isDetail" class="operations-toolbar support-toolbar">
      <input v-model="search" type="search" placeholder="Тема, текст, email или организация" @keyup.enter="applyFilters">
      <SearchSelect v-model="source" :options="[
        { value: '', label: 'Все обращения' },
        { value: 'users', label: 'От пользователей' },
        { value: 'guests', label: 'От гостей' }
      ]" @change="applyFilters" />
      <SearchSelect v-model="status" :options="[
        { value: '', label: 'Все статусы' },
        { value: 'new', label: 'Новые' },
        { value: 'in_progress', label: 'В работе' },
        { value: 'closed', label: 'Закрытые' },
        { value: 'spam', label: 'Спам' }
      ]" @change="applyFilters" />
      <button class="secondary-button" :disabled="isLoading" @click="applyFilters">Найти</button>
      <button class="secondary-button" :disabled="isLoading" @click="load()">Обновить</button>
    </div>

    <div v-if="error" class="form-message form-message--error">{{ error }}</div>
    <div v-if="success" class="form-message form-message--success">{{ success }}</div>

    <div class="support-layout" :class="{ 'support-layout--detail': isDetail }">
      <div v-if="!isDetail" class="table-wrap support-list-wrap">
        <table class="data-table support-list">
          <thead>
            <tr><th>Обращение</th><th>Организация</th><th>Отправитель</th><th>Статус</th></tr>
          </thead>
          <tbody>
            <tr
              v-for="item in items"
              :key="item.id"
              :class="{ selected: selected?.id === item.id }"
              tabindex="0"
              @click="open(item)"
              @keydown.enter="open(item)"
            >
              <td><strong>{{ item.topic }}</strong><small>{{ item.typeName }} · {{ date(item.createdAt) }}</small></td>
              <td>{{ item.organizationName || '—' }}</td>
              <td>{{ sender(item) }}</td>
              <td>{{ item.statusName }}</td>
            </tr>
            <tr v-if="items.length === 0"><td colspan="4" class="support-empty">{{ isLoading ? 'Загрузка…' : 'Обращения не найдены.' }}</td></tr>
          </tbody>
        </table>
        <div class="support-pagination">
          <span>{{ totalCount }} обращений · страница {{ page }} из {{ totalPages }}</span>
          <div>
            <button class="secondary-button" :disabled="page <= 1 || isLoading" @click="changePage(page - 1)">Назад</button>
            <button class="secondary-button" :disabled="page >= totalPages || isLoading" @click="changePage(page + 1)">Далее</button>
          </div>
        </div>
      </div>

      <section v-if="selected" class="support-conversation">
        <header class="support-conversation__header">
          <div>
            <h2>{{ selected.topic }}</h2>
            <p>{{ selected.organizationName || 'Без организации' }} · {{ sender(selected) }}</p>
            <small v-if="selected.organizationId">Организация #{{ selected.organizationId }}</small>
          </div>
          <span class="support-status">{{ selected.statusName }}</span>
        </header>

        <div class="support-history" aria-live="polite">
          <article
            v-for="entry in selected.entries"
            :key="entry.id"
            class="support-entry"
            :class="{
              'support-entry--staff': entry.isStaff,
              'support-entry--internal': entry.kind === 'internal_comment' || entry.kind === 'internal'
            }"
          >
            <div class="support-entry__meta">
              <strong>{{ entryLabel(entry.kind) || (entry.isStaff ? 'Администратор' : sender(selected)) }}</strong>
              <time :datetime="entry.createdAt">{{ date(entry.createdAt) }}</time>
            </div>
            <p>{{ entry.text }}</p>
            <ul v-if="entry.attachments?.length" class="support-attachments" aria-label="Вложения">
              <li v-for="attachment in entry.attachments" :key="attachment.id">
                <a :href="supportAttachmentUrl(attachment.id)" target="_blank" rel="noopener">{{ attachment.fileName }}</a>
                <small>{{ formatAttachmentSize(attachment.sizeBytes) }}</small>
              </li>
            </ul>
            <small v-if="entry.kind === 'guest_email_reply'" class="support-delivery" :class="{ 'support-delivery--error': deliveryStatusName(entry.deliveryStatus) === 'Ошибка доставки' }">
              {{ deliveryStatusName(entry.deliveryStatus) }}<template v-if="entry.deliveryError"> · {{ entry.deliveryError }}</template>
            </small>
          </article>
          <article v-if="selected.entries.length === 0" class="support-entry">
            <div class="support-entry__meta"><strong>{{ sender(selected) }}</strong><time>{{ date(selected.createdAt) }}</time></div>
            <p>{{ selected.text }}</p>
          </article>
        </div>

        <form v-if="canManage && (selected.userId || selected.email)" class="support-reply" @submit.prevent="sendReply">
          <label for="support-reply">{{ selected.userId ? 'Ответ пользователю' : `Ответ гостю на ${selected.email}` }}</label>
          <textarea id="support-reply" v-model="replyText" rows="3" maxlength="4000" placeholder="Напишите ответ…"></textarea>
          <button class="primary-button" :disabled="isSaving || !replyText.trim()">Отправить ответ</button>
        </form>
        <div v-else-if="canManage" class="support-reply">
          <span>У обращения нет email для ответа.</span>
        </div>

        <form v-if="canManage" class="support-admin-details" @submit.prevent="saveDetails">
          <label><span>Статус</span><SearchSelect v-model="nextStatus" :options="[
            { value: 'new', label: 'Новое' },
            { value: 'in_progress', label: 'В работе' },
            { value: 'closed', label: 'Закрыто' },
            { value: 'spam', label: 'Спам' }
          ]" /></label>
          <label><span>Внутренний комментарий</span><textarea v-model="adminComment" rows="2" maxlength="2000" placeholder="Виден только администраторам"></textarea></label>
          <button class="secondary-button" :disabled="isSaving">Сохранить статус и комментарий</button>
        </form>
      </section>
      <section v-else class="support-conversation support-placeholder">Выберите обращение, чтобы открыть переписку.</section>
    </div>
  </section>
</template>

<style scoped>
.support-toolbar { flex-wrap: wrap; }
.support-toolbar > input { min-width: 240px; flex: 1 1 280px; }
.support-layout { display: grid; grid-template-columns: minmax(420px, 1.1fr) minmax(360px, 0.9fr); gap: 16px; align-items: start; }
.support-layout--detail { grid-template-columns: minmax(0, 1fr); }
.support-back { margin-bottom: 16px; }
.support-delivery { display: block; margin-top: 8px; color: var(--crm-text-muted, #697783); }
.support-delivery--error { color: #b42318; }
.support-attachments { display: grid; gap: 6px; margin: 8px 0 0; padding: 0; list-style: none; }
.support-attachments li { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px; overflow-wrap: anywhere; }
.support-attachments li small { color: var(--crm-text-muted, #697783); }
.support-list-wrap { min-width: 0; }
.support-list tbody tr { cursor: pointer; }
.support-list tbody tr.selected { background: var(--crm-surface-muted, #f2f5f7); }
.support-list td { vertical-align: top; }
.support-list td small { display: block; margin-top: 4px; color: var(--crm-text-muted, #697783); }
.support-empty { text-align: center; color: var(--crm-text-muted, #697783); }
.support-pagination { display: flex; justify-content: space-between; align-items: center; gap: 12px; padding: 12px; color: var(--crm-text-muted, #697783); font-size: 13px; }
.support-pagination > div { display: flex; gap: 8px; }
.support-conversation { min-width: 0; border: 1px solid var(--crm-border, #dce2e6); border-radius: 8px; background: var(--crm-surface, #fff); overflow: hidden; }
.support-conversation__header { display: flex; justify-content: space-between; gap: 12px; padding: 16px; border-bottom: 1px solid var(--crm-border, #dce2e6); }
.support-conversation__header h2 { margin: 0 0 6px; font-size: 18px; }
.support-conversation__header p, .support-conversation__header small { margin: 0; color: var(--crm-text-muted, #697783); }
.support-status { align-self: flex-start; white-space: nowrap; padding: 4px 8px; border-radius: 999px; background: var(--crm-surface-muted, #f2f5f7); font-size: 12px; }
.support-history { display: flex; flex-direction: column; gap: 10px; max-height: 440px; overflow-y: auto; padding: 16px; }
.support-entry { max-width: 92%; padding: 10px 12px; border-radius: 8px; background: var(--crm-surface-muted, #f2f5f7); overflow-wrap: anywhere; }
.support-entry--staff { align-self: flex-end; background: var(--crm-accent-soft, #e9f3ff); }
.support-entry--internal { align-self: stretch; max-width: none; border: 1px dashed var(--crm-border, #dce2e6); background: transparent; }
.support-entry__meta { display: flex; justify-content: space-between; gap: 12px; color: var(--crm-text-muted, #697783); font-size: 12px; }
.support-entry p { margin: 7px 0 0; white-space: pre-wrap; }
.support-reply, .support-admin-details { display: grid; gap: 9px; padding: 14px 16px; border-top: 1px solid var(--crm-border, #dce2e6); }
.support-reply label, .support-admin-details label { display: grid; gap: 5px; font-size: 13px; font-weight: 600; }
.support-reply textarea, .support-admin-details textarea { width: 100%; resize: vertical; }
.support-reply button, .support-admin-details > button { justify-self: end; }
.support-placeholder { padding: 24px; color: var(--crm-text-muted, #697783); }
@media (max-width: 1050px) { .support-layout { grid-template-columns: 1fr; } }
@media (max-width: 640px) { .support-list-wrap { overflow-x: auto; } .support-pagination { align-items: flex-start; flex-direction: column; } }
</style>

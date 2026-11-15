<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiPagination, UiSearchInput } from '@opencrm/ui'
import { loadClientLookup, type WorkspaceClientLookupItem } from '../api/clientLookup'
import { linkConversationClient } from '../api/conversations'
import SchedulerBookingDialog from './SchedulerBookingDialog.vue'

const props = defineProps<{
  organizationId: string
  conversationId: string
  currentClientId?: string | null
  currentClientName?: string | null
}>()
const emit = defineEmits<{ changed: []; close: [] }>()
const search = ref('')
const items = ref<WorkspaceClientLookupItem[]>([])
const loading = ref(false)
const error = ref('')
const saveError = ref('')
const page = ref(1)
const totalCount = ref(0)
const selectedClientId = ref<string | null>(null)
const saving = ref(false)
const confirmUnlink = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined
let request: AbortController | undefined
let generation = 0

function invalidateSearch() {
  generation++
  clearTimeout(timer)
  request?.abort()
  request = undefined
  loading.value = false
}

function searchClients() {
  invalidateSearch()
  items.value = []
  totalCount.value = 0
  error.value = ''
  const query = search.value.trim()
  if (query.length < 2) return

  loading.value = true
  const current = generation
  timer = setTimeout(() => { void find(current, query, page.value) }, 300)
}

async function find(current: number, query: string, pageNumber: number) {
  if (current !== generation) return
  const active = new AbortController()
  request = active
  loading.value = true
  error.value = ''
  try {
    const result = await loadClientLookup(props.organizationId, query, pageNumber, active.signal)
    if (current !== generation || active.signal.aborted) return
    totalCount.value = result.totalCount
    const lastPage = Math.max(1, Math.ceil(result.totalCount / 8))
    if (pageNumber > lastPage) {
      page.value = lastPage
      await find(current, query, lastPage)
      return
    }
    items.value = result.items
  } catch (caught) {
    if (current === generation && !active.signal.aborted) {
      error.value = caught instanceof Error ? caught.message : 'Не удалось найти клиентов.'
    }
  } finally {
    if (current === generation) {
      loading.value = false
      if (request === active) request = undefined
    }
  }
}

function changePage(nextPage: number) {
  if (loading.value || nextPage < 1 || nextPage > Math.max(1, Math.ceil(totalCount.value / 8))) return
  page.value = nextPage
  selectedClientId.value = null
  saveError.value = ''
  invalidateSearch()
  items.value = []
  const query = search.value.trim()
  if (query.length >= 2) void find(generation, query, page.value)
}

function searchChanged() {
  page.value = 1
  selectedClientId.value = null
  saveError.value = ''
  confirmUnlink.value = false
  searchClients()
}

async function saveLink(clientId: string | null) {
  if (saving.value || clientId === props.currentClientId) return
  const current = generation
  saving.value = true
  saveError.value = ''
  try {
    await linkConversationClient(props.organizationId, props.conversationId, clientId)
    if (current !== generation) return
    emit('changed')
    emit('close')
  } catch (caught) {
    if (current === generation) saveError.value = caught instanceof Error ? caught.message : 'Не удалось изменить связь с клиентом.'
  } finally {
    if (current === generation) saving.value = false
  }
}

function confirmSelection() {
  if (selectedClientId.value) void saveLink(selectedClientId.value)
}

function chooseClient(clientId: string) {
  selectedClientId.value = clientId
  saveError.value = ''
  confirmUnlink.value = false
}

function confirmRemoval() {
  if (!confirmUnlink.value) {
    selectedClientId.value = null
    saveError.value = ''
    confirmUnlink.value = true
    return
  }
  void saveLink(null)
}

function close() {
  if (saving.value) return
  emit('close')
}

function initials(client: WorkspaceClientLookupItem) {
  return client.name.trim().split(/\s+/).slice(0, 2).map(part => part[0]?.toLocaleUpperCase('ru-RU') ?? '').join('') || 'К'
}

function clientType(client: WorkspaceClientLookupItem) {
  if (client.partyTypeCode === 'company') return 'Организация'
  if (client.partyTypeCode === 'person') return 'Физлицо'
  return ''
}

watch(search, searchChanged)
watch(() => [props.organizationId, props.conversationId], () => {
  invalidateSearch()
  search.value = ''
  page.value = 1
  selectedClientId.value = null
  confirmUnlink.value = false
  items.value = []
  totalCount.value = 0
  error.value = ''
  saveError.value = ''
  saving.value = false
})
onBeforeUnmount(invalidateSearch)
</script>

<template>
  <SchedulerBookingDialog :open="true" :busy="saving" :dismiss-on-backdrop="false" title="Связь с клиентом" @close="close">
    <section class="conversation-link" aria-label="Связь переписки с клиентом">
      <div v-if="currentClientId" class="conversation-link__current">
        <span class="conversation-link__current-label">Сейчас связан</span>
        <strong>{{ currentClientName || 'Клиент' }}</strong>
        <span v-if="confirmUnlink" class="conversation-link__unlink-confirm">Связь будет удалена, а ожидающие сообщения отменены. Продолжить?</span>
        <UiButton v-if="!confirmUnlink" variant="secondary" :disabled="saving" @click="confirmRemoval">Отвязать</UiButton>
        <template v-else>
          <UiButton variant="secondary" :disabled="saving" @click="confirmUnlink = false">Оставить связь</UiButton>
          <UiButton variant="secondary" :disabled="saving" @click="confirmRemoval">Подтвердить отвязку</UiButton>
        </template>
      </div>

      <p class="conversation-link__hint">При сохранении связи неотправленные сообщения этого диалога будут отменены.</p>
      <UiSearchInput v-model="search" label="Найти клиента" placeholder="Имя или телефон клиента" :disabled="saving" />
      <p v-if="search.trim().length < 2" class="conversation-link__state">Введите не менее двух символов, чтобы найти клиента.</p>
      <p v-else-if="loading" class="conversation-link__state" role="status">Ищем клиентов…</p>
      <div v-else-if="error" class="conversation-link__state conversation-link__error" role="alert">
        <span>{{ error }}</span>
        <UiButton variant="secondary" :disabled="saving" @click="searchClients">Повторить</UiButton>
      </div>
      <p v-else-if="!items.length" class="conversation-link__state">Клиенты по этому запросу не найдены.</p>
      <template v-else>
        <ul class="conversation-link__results" aria-label="Результаты поиска клиентов">
          <li v-for="client in items" :key="client.id">
            <article class="conversation-link__client" :class="{ 'conversation-link__client--selected': selectedClientId === client.id, 'conversation-link__client--current': currentClientId === client.id }">
              <span class="conversation-link__avatar" aria-hidden="true">{{ initials(client) }}</span>
              <span class="conversation-link__identity">
                <strong>{{ client.name }}</strong>
                <span>{{ client.phone || 'Телефон не указан' }}<template v-if="clientType(client)"> · {{ clientType(client) }}</template></span>
                <small v-if="currentClientId === client.id">Текущая связь</small>
              </span>
              <UiButton variant="secondary" :disabled="saving || currentClientId === client.id" :aria-pressed="selectedClientId === client.id" @click="chooseClient(client.id)">
                {{ currentClientId === client.id ? 'Связан' : selectedClientId === client.id ? 'Выбран' : 'Выбрать' }}
              </UiButton>
            </article>
          </li>
        </ul>
      </template>

      <div v-if="totalCount" class="conversation-link__pagination">
        <span>Найдено: {{ totalCount }}</span>
        <UiPagination :model-value="page" :total-items="totalCount" :items-per-page="8" :disabled="saving || loading" label="Страницы результатов поиска" @update:model-value="changePage" />
      </div>

      <p v-if="saveError" class="conversation-link__error" role="alert">{{ saveError }}</p>
      <footer class="conversation-link__footer">
        <UiButton variant="secondary" :disabled="saving" @click="close">Отмена</UiButton>
        <UiButton :disabled="saving || !selectedClientId || selectedClientId === currentClientId" @click="confirmSelection">
          {{ saving ? 'Сохраняем…' : currentClientId ? 'Сохранить связь' : 'Связать с клиентом' }}
        </UiButton>
      </footer>
    </section>
  </SchedulerBookingDialog>
</template>

<style scoped>
.conversation-link { display:grid; gap:14px; padding:16px 20px 20px; }
.conversation-link__current { display:flex; align-items:center; flex-wrap:wrap; gap:8px 12px; padding:10px 12px; border:1px solid var(--crm-line, #dce2e9); border-radius:10px; background:var(--crm-surface-soft, #f7f9fb); }
.conversation-link__current-label { color:var(--crm-text-muted, #667085); font-size:12px; }
.conversation-link__current strong { flex:1; min-width:150px; font-size:13px; }
.conversation-link__unlink-confirm { flex-basis:100%; color:var(--crm-text-muted, #667085); font-size:12px; }
.conversation-link__hint, .conversation-link__state { margin:0; color:var(--crm-text-muted, #667085); font-size:12px; line-height:1.5; }
.conversation-link__state { min-height:36px; display:flex; align-items:center; justify-content:center; gap:12px; text-align:center; }
.conversation-link__error { color:var(--crm-danger, #b42318); }
.conversation-link__results { display:grid; grid-template-columns:repeat(2, minmax(0, 1fr)); gap:8px; max-height: min(42vh, 420px); overflow:auto; list-style:none; padding:0; margin:0; }
.conversation-link__client { display:flex; align-items:center; gap:10px; min-height:68px; padding:10px; border:1px solid var(--crm-line, #dce2e9); border-radius:10px; background:var(--crm-surface, #fff); }
.conversation-link__client--selected { border-color:var(--crm-accent, #c65d25); box-shadow:0 0 0 2px color-mix(in srgb, var(--crm-accent, #c65d25) 16%, transparent); }
.conversation-link__client--current { background:var(--crm-surface-soft, #f7f9fb); }
.conversation-link__avatar { display:grid; flex:0 0 38px; width:38px; height:38px; place-items:center; border-radius:50%; color:var(--crm-accent, #ad511e); background:var(--crm-accent-soft, #fff3e9); font-size:12px; font-weight:700; }
.conversation-link__identity { display:grid; flex:1; min-width:0; gap:3px; }
.conversation-link__identity strong, .conversation-link__identity span { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
.conversation-link__identity strong { font-size:13px; }
.conversation-link__identity span, .conversation-link__identity small { color:var(--crm-text-muted, #667085); font-size:11px; }
.conversation-link__identity small { color:var(--crm-accent, #ad511e); }
.conversation-link__pagination, .conversation-link__footer { display:flex; align-items:center; justify-content:space-between; flex-wrap:wrap; gap:10px; }
.conversation-link__pagination { color:var(--crm-text-muted, #667085); font-size:12px; }
.conversation-link__footer { padding-top:12px; border-top:1px solid var(--crm-line, #dce2e9); }
@media (max-width:700px) {
  .conversation-link { gap:12px; padding:12px; }
  .conversation-link__results { grid-template-columns:minmax(0, 1fr); max-height:42dvh; }
  .conversation-link__client { min-height:62px; padding:8px; }
  .conversation-link__client :deep(.ui-button) { flex-shrink:0; }
}
</style>

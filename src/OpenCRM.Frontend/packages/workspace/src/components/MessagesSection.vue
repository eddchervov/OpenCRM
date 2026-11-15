<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiCard, UiLoader, UiSearchInput } from '@opencrm/ui'
import type { WorkspaceContext } from '../types'
import { ConversationRequestError, conversationChannelName, loadClientInbox, type ClientInboxGroup } from '../api/conversations'
import { useConversationUpdates } from '../composables/conversationUpdates'
import ConversationThread from './ConversationThread.vue'
import ClientConversationPanel from './ClientConversationPanel.vue'

const props = defineProps<{ context: WorkspaceContext; conversationId?: string; previewOnly?: boolean }>()
const updates = useConversationUpdates()
const search = ref('')
const onlyUnread = ref(false)
const items = ref<ClientInboxGroup[]>([])
const nextCursor = ref<string | null>(null)
const loading = ref(false)
const error = ref('')
let hasLoadedMore = false
const activeId = computed(() => props.conversationId)
const activeClientId = ref(new URLSearchParams(window.location.hash.split('?')[1] ?? '').get('client') ?? '')
const openedGroup = ref<ClientInboxGroup>()
const listedActiveGroup = computed(() => items.value.find(item => item.clientId === activeClientId.value || item.routes.some(route => route.id === activeId.value)))
const activeGroup = computed(() => listedActiveGroup.value ?? (openedGroup.value && (
  (activeClientId.value && openedGroup.value.clientId === activeClientId.value) || openedGroup.value.routes.some(route => route.id === activeId.value)
) ? openedGroup.value : undefined))
watch(listedActiveGroup, group => { if (group) openedGroup.value = group })
const pageTitle = computed(() => activeId.value || activeClientId.value ? activeGroup.value?.clientName || activeGroup.value?.externalDisplayName || 'Диалог' : 'Сообщения')
const pageBreadcrumbs = computed(() => activeId.value || activeClientId.value
  ? [{ title: 'Сообщения', href: '#messages' }]
  : [])
const activeRoutes = computed(() => activeGroup.value?.routes.filter(route => !route.isReadOnly) ?? [])
function groupHref(group: ClientInboxGroup) {
  const routes = group.routes.filter(route => !route.isReadOnly)
  if (group.clientId && routes.length > 1) return `#messages?client=${encodeURIComponent(group.clientId)}`
  return `#messages/${(routes[0] ?? group.routes[0])?.id ?? ''}`
}
function syncClient() { activeClientId.value = new URLSearchParams(window.location.hash.split('?')[1] ?? '').get('client') ?? '' }
window.addEventListener('hashchange', syncClient)
function back() { window.location.hash = 'messages' }
let request: AbortController | undefined
let timer: ReturnType<typeof setTimeout> | undefined
async function load(more = false, refresh = false) {
  if (props.previewOnly) return
  request?.abort()
  const active = new AbortController()
  request = active
  loading.value = true
  try {
    const result = await loadClientInbox(props.context.organization.id, { search: search.value, onlyUnread: onlyUnread.value, cursor: more ? nextCursor.value ?? undefined : undefined }, active.signal)
    if (active.signal.aborted) return
    if (refresh && hasLoadedMore && onlyUnread.value) {
      const visibleCount = items.value.length
      while (result.nextCursor && result.items.length < visibleCount) {
        const cursor = result.nextCursor
        const following = await loadClientInbox(props.context.organization.id, { search: search.value, onlyUnread: true, cursor }, active.signal)
        if (active.signal.aborted) return
        if (following.nextCursor === cursor) throw new Error('Не удалось обновить список. Повторите запрос.')
        result.items.push(...following.items.filter(item => !result.items.some(existing => existing.groupId === item.groupId)))
        result.nextCursor = following.nextCursor
      }
      items.value = result.items
      nextCursor.value = result.nextCursor
      error.value = ''
      return
    }
    if (more) {
      items.value = [...items.value, ...result.items.filter(item => !items.value.some(existing => existing.groupId === item.groupId))]
      hasLoadedMore = true
    } else if (refresh && hasLoadedMore) {
      const refreshed = new Set(result.items.map(item => item.groupId))
      const refreshedRoutes = new Set(result.items.flatMap(item => item.routes.map(route => route.id)))
      items.value = [...result.items, ...items.value.filter(item => !refreshed.has(item.groupId) && !item.routes.some(route => refreshedRoutes.has(route.id)))]
    } else {
      items.value = result.items
      hasLoadedMore = false
    }
    if (!refresh || !hasLoadedMore) nextCursor.value = result.nextCursor
    error.value = ''
  } catch (caught) {
    if (active.signal.aborted) return
    if (caught instanceof ConversationRequestError && [401, 403, 404].includes(caught.status)) {
      items.value = []
      openedGroup.value = undefined
      nextCursor.value = null
      hasLoadedMore = false
    }
    error.value = caught instanceof Error ? caught.message : 'Не удалось загрузить диалоги.'
  }
  finally { if (request === active) loading.value = false }
}
async function conversationChanged(reason: 'read' | 'sent' | 'linked') {
  if (reason === 'read') {
    for (const group of [...items.value, ...(openedGroup.value ? [openedGroup.value] : [])]) {
      const route = group.routes.find(route => route.id === activeId.value)
      if (!route) continue
      route.unreadCount = 0
      group.unreadCount = group.routes.reduce((count, item) => count + item.unreadCount, 0)
    }
  }
  await load(false, reason !== 'linked')
}
function time(value: string | null) {
  return value ? new Intl.DateTimeFormat('ru-RU', {
    day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit',
    timeZone: props.context.organization.timeZoneId || 'UTC'
  }).format(new Date(value)) : ''
}
watch(search, () => { if (props.previewOnly) return; clearTimeout(timer); timer = setTimeout(() => { void load() }, 300) })
watch(onlyUnread, () => { void load() })
watch(() => props.context.organization.id, () => { items.value = []; openedGroup.value = undefined; hasLoadedMore = false; void load() }, { immediate: true })
watch(updates.revision, () => { void load(false, true) })
onBeforeUnmount(() => { request?.abort(); clearTimeout(timer); window.removeEventListener('hashchange', syncClient) })
</script>

<template>
  <UiCard page :title="pageTitle" :breadcrumbs="pageBreadcrumbs">
    <div class="inbox" :class="{ 'inbox--selected': activeId || activeClientId }">
      <aside class="inbox__sidebar" aria-label="Диалоги организации">
        <div class="inbox__filters"><UiSearchInput v-model="search" placeholder="Клиент или сообщение" aria-label="Поиск диалогов" /><div><button type="button" :aria-pressed="onlyUnread" @click="onlyUnread = !onlyUnread">Непрочитанные <span v-if="updates.unreadCount.value">{{ updates.unreadCount.value }}</span></button><span class="inbox__connection" :class="{ 'inbox__connection--online': updates.connected.value }" :title="updates.connected.value ? 'Обновления подключены' : 'Восстанавливаем обновления'" aria-hidden="true"></span></div></div>
        <div class="inbox__list" :aria-busy="loading">
          <div v-if="previewOnly" class="inbox__preview-list" aria-hidden="true">
            <div v-for="index in 5" :key="index" class="inbox__item"><span class="inbox__avatar"></span><span class="inbox__preview"><i></i><i></i><i></i></span></div>
          </div>
          <p v-else-if="error" class="inbox__empty" role="alert">{{ error }} <UiButton variant="secondary" @click="load()">Повторить</UiButton></p>
          <UiLoader v-else-if="loading && !items.length" text="Загружаем диалоги…" />
          <p v-else-if="!items.length" class="inbox__empty">{{ search || onlyUnread ? 'Диалоги не найдены.' : 'Здесь появятся сообщения клиентов вашим ботам в Telegram и MAX.' }}</p>
          <a v-for="item in items" :key="item.groupId" :href="groupHref(item)" class="inbox__item" :aria-current="item.groupId === activeGroup?.groupId ? 'page' : undefined">
            <span class="inbox__avatar" aria-hidden="true">{{ (item.clientName || item.externalDisplayName || '?').slice(0, 1).toUpperCase() }}</span>
            <span class="inbox__preview"><span class="inbox__title">{{ item.clientName || item.externalDisplayName || 'Новый контакт' }}</span><span class="inbox__text">{{ item.lastMessageText || 'Вложение' }}</span><span class="inbox__meta">{{ [...new Set(item.routes.filter(route => !route.isReadOnly).map(route => conversationChannelName(route.channelCode)))].join(' · ') || 'История переписки' }} <span v-if="!item.clientId">· Не связан с клиентом</span></span></span>
            <span class="inbox__right"><time>{{ time(item.lastMessageAt) }}</time><span v-if="item.unreadCount" class="inbox__badge" :aria-label="`Непрочитанных: ${item.unreadCount}`">{{ item.unreadCount > 99 ? '99+' : item.unreadCount }}</span></span>
          </a>
          <UiButton v-if="nextCursor" variant="secondary" :disabled="loading" @click="load(true)">Ещё диалоги</UiButton>
        </div>
      </aside>
      <div v-if="previewOnly" class="inbox__preview-thread" aria-hidden="true">
        <div class="inbox__preview-title"><i></i></div>
        <div class="inbox__preview-bubbles"><i></i><i></i><i></i></div>
        <div class="inbox__preview-compose"><textarea disabled placeholder="Сообщение клиенту"></textarea><UiButton disabled>Отправить</UiButton></div>
      </div>
      <div v-else-if="activeId" class="inbox__thread"><nav v-if="activeRoutes.length > 1" class="inbox__channels" aria-label="Мессенджер клиента"><a v-for="route in activeRoutes" :key="route.id" :href="`#messages/${route.id}`" :aria-current="route.id === activeId ? 'page' : undefined">{{ conversationChannelName(route.channelCode) }}<span v-if="route.unreadCount"> · {{ route.unreadCount }}</span></a></nav><ConversationThread :key="context.organization.id + ':' + activeId" :context="context" :conversation-id="activeId" show-back @changed="conversationChanged" @back="back" /></div>
      <ClientConversationPanel v-else-if="activeClientId" :key="context.organization.id + ':' + activeClientId" :context="context" :client-id="activeClientId" show-back @back="back" />
      <div v-else class="inbox__placeholder"><svg viewBox="0 0 48 48" aria-hidden="true"><path d="M8 8h32v25H20L8 41V8Z M15 17h18M15 24h12" /></svg><h3>Переписка с клиентами</h3><p>Выберите диалог, чтобы прочитать сообщения и ответить.</p></div>
    </div>
  </UiCard>
</template>

<style scoped>
.inbox { display:grid; grid-template-columns:minmax(250px, 320px) minmax(0, 1fr); min-width:0; overflow:hidden; border:1px solid var(--crm-line, #e0e5ec); border-radius:10px; color:var(--crm-text, #263244); }
.inbox__sidebar { min-width:0; border-right:1px solid var(--crm-line, #e0e5ec); }
.inbox__thread { min-width:0; }
.inbox__preview-list i, .inbox__preview-title i { display: block; height: 11px; width: 85%; border-radius: 5px; background: var(--crm-line, #dce2e9); }
.inbox__preview-list i:nth-child(2) { width: 100%; height: 9px; }
.inbox__preview-list i:last-child { width: 45%; height: 8px; }
.inbox__preview-thread { display: flex; flex-direction: column; min-height: 440px; padding: 20px; gap: 24px; }
.inbox__preview-title i { width: 42%; height: 16px; }
.inbox__preview-bubbles { display: flex; flex-direction: column; gap: 24px; flex: 1; padding: 24px 0; }
.inbox__preview-bubbles i { height: 60px; width: 72%; border-radius: 12px; background: var(--crm-line, #dce2e9); }
.inbox__preview-bubbles i:nth-child(2) { align-self: flex-end; width: 62%; }
.inbox__preview-compose { display: flex; gap: 12px; align-items: flex-end; }
.inbox__preview-compose textarea { min-width: 0; width: 100%; border: 1px solid var(--crm-line, #dce2e9); border-radius: 7px; padding: 10px; background: var(--crm-surface, #fff); color: var(--crm-text-muted, #687386); }
.inbox__channels { display:flex; flex-wrap:wrap; gap:10px; padding:12px 20px; border-bottom:1px solid var(--crm-line); }
.inbox__channels a { padding:8px 12px; border:1px solid var(--crm-line); border-radius:7px; color:inherit; font-size:12px; text-decoration:none; }
.inbox__channels a[aria-current] { border-color:var(--crm-accent); background:var(--crm-accent-soft); }
.inbox__filters { display:grid; gap:12px; padding:16px; border-bottom:1px solid var(--crm-line); }
.inbox__filters > div { display:flex; align-items:center; justify-content:space-between; }
.inbox__filters button { border:1px solid var(--crm-line); border-radius:7px; background:var(--crm-surface); color:var(--crm-text-muted); padding:6px 10px; font:inherit; font-size:12px; cursor:pointer; }
.inbox__filters button[aria-pressed=true] { border-color:var(--crm-accent, #e87532); color:var(--crm-accent, #ad511b); background:var(--crm-accent-soft, #fff4eb); }
.inbox__connection { width:7px; height:7px; border-radius:50%; background:var(--crm-text-muted, #667085); }
.inbox__connection--online { background:#439b75; }
.inbox__list { display:grid; align-content:start; max-height:calc(74dvh - 112px); overflow:auto; min-height:330px; }
.inbox__item { display:flex; gap:10px; min-width:0; padding:16px 12px; border-bottom:1px solid var(--crm-line); color:inherit; text-decoration:none; }
.inbox__item:hover, .inbox__item[aria-current] { background:var(--crm-surface-soft, #f6f7f9); }
.inbox__item[aria-current] { box-shadow:inset 3px 0 var(--crm-accent, #e87532); }
.inbox__avatar { display:grid; place-items:center; flex:0 0 34px; height:34px; border-radius:50%; background:var(--crm-surface-soft, #edf0f5); color:var(--crm-text-muted); font-weight:600; }
.inbox__preview { display:grid; gap:6px; min-width:0; flex:1; }
.inbox__title { font-size:13px; font-weight:650; overflow-wrap:anywhere; }
.inbox__text { font-size:12px; color:var(--crm-text-muted); overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
.inbox__meta { font-size:10px; color:var(--crm-text-muted); }
.inbox__right { display:flex; flex-direction:column; align-items:flex-end; gap:10px; max-width:70px; }
.inbox__right time { font-size:10px; color:var(--crm-text-muted); text-align:right; }
.inbox__badge { padding:3px 6px; min-width:18px; box-sizing:border-box; border-radius:10px; background:var(--crm-accent, #b8551e); color:white; text-align:center; font-size:10px; }
.inbox__placeholder { display:flex; flex-direction:column; justify-content:center; align-items:center; min-height:500px; padding:24px; color:var(--crm-text-muted); text-align:center; }
.inbox__placeholder svg { width:48px; height:48px; fill:none; stroke:currentColor; stroke-width:1.3; }
.inbox__placeholder h3 { margin:20px 0 0; color:var(--crm-text); font-size:16px; }
.inbox__placeholder p, .inbox__empty { font-size:13px; line-height:1.5; }
.inbox__empty { display:grid; justify-items:start; gap:12px; margin:0; padding:24px 16px; color:var(--crm-text-muted); }
@media (max-width:700px) { .inbox { grid-template-columns:minmax(0, 1fr); } .inbox__sidebar { border:0; } .inbox--selected .inbox__sidebar, .inbox__placeholder { display:none; } .inbox__list { max-height:70dvh; } }
</style>

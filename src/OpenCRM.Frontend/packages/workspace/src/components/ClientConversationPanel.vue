<script setup lang="ts">
import { translate as t } from '@opencrm/ui/i18n'
import '../interactionMessages'
import { onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiLoader } from '@opencrm/ui'
import type { WorkspaceContext } from '../types'
import { conversationChannelName, getClientConversationRoutes, type ConversationClientRoutes } from '../api/conversations'
import ConversationThread from './ConversationThread.vue'

const props = defineProps<{ context: WorkspaceContext; clientId: string; maintenanceId?: string; showBack?: boolean }>()
const emit = defineEmits<{ back: [] }>()
const conversationId = ref('')
const routes = ref<ConversationClientRoutes | null>(null)
const loading = ref(false)
const error = ref('')
let request: AbortController | undefined
async function load() {
  request?.abort()
  const active = new AbortController()
  request = active
  loading.value = true
  conversationId.value = ''
  error.value = ''
  try {
    const result = await getClientConversationRoutes(props.context.organization.id, props.clientId, active.signal)
    if (active.signal.aborted) return
    routes.value = result ?? null
    conversationId.value = result?.activeRoutes.length === 1 ? result.activeRoutes[0].conversationId : ''
  } catch (caught) { if (!active.signal.aborted) error.value = caught instanceof Error ? caught.message : t('interaction.openChatError') }
  finally { if (request === active) loading.value = false }
}
watch(() => [props.context.organization.id, props.clientId], () => { void load() }, { immediate: true })
onBeforeUnmount(() => request?.abort())
</script>

<template>
  <UiLoader v-if="loading" :text="t('interaction.openingChat')" />
  <div v-else-if="error" class="client-conversation-empty" role="alert"><p>{{ error }}</p><UiButton variant="secondary" @click="load">{{ t('interaction.retry') }}</UiButton></div>
  <div v-else-if="routes && (routes.activeRoutes.length || routes.historyRoutes.length)">
    <button v-if="showBack && !conversationId" type="button" class="client-conversation-back" @click="emit('back')">{{ t('interaction.backToClients') }}</button>
    <nav v-if="routes.activeRoutes.length > 1" class="client-conversation-channels" :aria-label="t('interaction.clientMessenger')"><button v-for="route in routes.activeRoutes" :key="route.conversationId" type="button" :aria-pressed="conversationId === route.conversationId" @click="conversationId = route.conversationId">{{ conversationChannelName(route.channelCode) }}<span v-if="route.unreadCount"> · {{ t('interaction.unread', { count: route.unreadCount }) }}</span></button></nav>
    <p v-if="!conversationId" class="client-conversation-empty">{{ t('interaction.chooseMessenger') }}</p>
    <ConversationThread v-if="conversationId" :key="context.organization.id + ':' + conversationId" :context="context" :conversation-id="conversationId" :maintenance-id="maintenanceId" :show-back="showBack || routes.activeRoutes.length > 1" @back="routes.activeRoutes.length > 1 ? conversationId = '' : emit('back')" />
    <details v-if="routes.historyRoutes.length" class="client-conversation-history"><summary>{{ t('interaction.disconnectedHistory') }}</summary><button v-for="route in routes.historyRoutes" :key="route.conversationId" type="button" @click="conversationId = route.conversationId">{{ conversationChannelName(route.channelCode) }} · {{ route.displayName }} · {{ t('interaction.readOnly') }}</button></details>
  </div>
  <div v-else class="client-conversation-empty"><h3>{{ t('interaction.noMessengers') }}</h3><p>{{ t('interaction.messengerHelp') }}</p><UiButton v-if="maintenanceId && context.shell.menu?.some(item => item.code === 'clients' && item.isVisible)" variant="secondary" :href="`#clients/${clientId}`">{{ t('interaction.openClient') }}</UiButton></div>
</template>

<style scoped>
.client-conversation-empty { display:grid; justify-items:start; gap:12px; padding:24px; color:var(--crm-text-muted); font-size:13px; }
.client-conversation-empty h3, .client-conversation-empty p { margin:0; }
.client-conversation-empty h3 { color:var(--crm-text); font-size:15px; }
.client-conversation-channels { display:flex; flex-wrap:wrap; gap:10px; padding:12px 20px; border-bottom:1px solid var(--crm-line, #e0e5ec); }
.client-conversation-channels button, .client-conversation-history button { padding:10px 14px; border:1px solid var(--crm-line, #d4dce6); border-radius:8px; background:var(--crm-surface, #fff); color:inherit; font:inherit; cursor:pointer; }
.client-conversation-channels button[aria-pressed="true"] { background:var(--crm-accent-soft, #fff3e9); border-color:var(--crm-accent, #ad511e); }
.client-conversation-history { padding:16px 20px; color:var(--crm-text-muted, #667085); font-size:12px; }
.client-conversation-history button { display:block; margin-top:10px; }
.client-conversation-back { border:0; background:none; color:var(--crm-accent, #ad511e); padding:14px 20px; font:inherit; font-size:12px; cursor:pointer; }
</style>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton, UiLoader } from '@opencrm/ui'
import type { WorkspaceContext } from '../types'
import { ConversationRequestError, conversationUrl, loadConversation, markConversationRead, sendConversationMessage, uploadConversationFile, type ConversationDraftAttachment, type ConversationMessage, type ConversationPage } from '../api/conversations'
import { getConversationDraft, saveConversationDraft, type ConversationSendRequest } from '../utils/conversationDrafts'
import { useConversationUpdates } from '../composables/conversationUpdates'
import ConversationClientLink from './ConversationClientLink.vue'
import ConversationTemplatePicker from './ConversationTemplatePicker.vue'
import ConversationDeliveryHealth from './ConversationDeliveryHealth.vue'
import { conversationChannelName } from '../api/conversations'
import type { MessageTemplatePreview } from '../api/messageTemplates'

const props = defineProps<{ context: WorkspaceContext; conversationId: string; maintenanceId?: string; showBack?: boolean }>()
const emit = defineEmits<{ changed: [reason: 'read' | 'sent' | 'linked']; back: [] }>()
const updates = useConversationUpdates()
const page = ref<ConversationPage | null>(null)
const items = ref<ConversationMessage[]>([])
const loading = ref(false)
const sending = ref(false)
const loadingOlder = ref(false)
const error = ref('')
const sendError = ref('')
const draft = ref('')
const attachment = ref<ConversationDraftAttachment>()
const template = ref<{ messageTemplateId: string; messageTemplateVersion: number }>()
const uploading = ref(false)
const viewport = ref<HTMLElement | null>(null)
const atBottom = ref(true)
const linking = ref(false)
const historyWindowReset = ref(false)
let request: AbortController | undefined
const pendingSend = ref<ConversationSendRequest>()
let draftKey = ''
let readThrough = 0
let reading = false
let generation = 0
const title = computed(() => page.value?.conversation?.clientName || page.value?.conversation?.externalDisplayName || page.value?.conversation?.telegramDisplayName || 'Диалог')
const channelName = computed(() => conversationChannelName(page.value?.conversation?.channelCode))
const writable = computed(() => page.value?.canSend && !page.value.isReadOnly && !props.context.workspaceAccess?.isReadOnly
  && props.context.shell.scenario?.modules?.messaging !== false)
const textLimit = computed(() => page.value?.conversation?.channelCode?.toLowerCase() === 'max' ? 4000 : attachment.value ? 1024 : 4096)
const ordered = computed(() => [...items.value].sort((a, b) => a.sequence - b.sequence))
const outgoing = (item: ConversationMessage) => item.direction === 1 || String(item.direction).toLowerCase() === 'outgoing'
const statusNames = computed(() => ['В очереди', 'Отправляем…', `Принято ${channelName.value}`, 'Ошибка отправки', 'Результат не подтверждён', 'Отменено'])
const statusCodes = ['queued', 'sending', 'accepted', 'failed', 'uncertain', 'cancelled']
function status(item: ConversationMessage) { return typeof item.status === 'number' ? statusNames.value[item.status] : statusNames.value[statusCodes.indexOf(item.status.toLowerCase())] || item.status }
function deliveryHint(item: ConversationMessage) {
  if (item.status === 4 || String(item.status).toLowerCase() === 'uncertain') return 'Мессенджер не подтвердил результат. Перед повторной отправкой проверьте, получил ли клиент сообщение.'
  if (!item.deliveryErrorCode) return ''
  const hints: Record<string, string> = {
    'rate-limited': 'Мессенджер ограничил частоту отправки. Сервис повторит попытку.',
    'delivery-expired': 'Истёк срок ожидания доставки.',
    'bot-unavailable': 'Бот отключён или заменён.',
    'attachment-unavailable': 'Вложение недоступно для отправки.',
    'invalid-message-content': 'Мессенджер отклонил содержимое сообщения.',
    'client-link-changed': 'Отправка отменена после изменения связи с клиентом.',
    'client-channel-disconnected': 'Отправка отменена после отключения клиента.',
    'telegram-http-403': 'Клиент запретил сообщения или отключил бота.'
  }
  return hints[item.deliveryErrorCode] || 'Не удалось завершить доставку. Подробности доступны ответственному за подключение мессенджера.'
}
function formatTime(value: string) {
  return new Intl.DateTimeFormat('ru-RU', { dateStyle: 'medium', timeStyle: 'short', timeZone: page.value?.timeZoneId || 'UTC' }).format(new Date(value))
}
async function markVisibleRead() {
  if (!page.value || reading || !atBottom.value || document.visibilityState !== 'visible' || !document.hasFocus()) return
  const sequence = Math.max(0, ...items.value.filter(item => !outgoing(item)).map(item => item.sequence))
  if (sequence <= readThrough) return
  const current = generation
  reading = true
  try {
    await markConversationRead(props.context.organization.id, props.conversationId, sequence)
    if (current !== generation) return
    readThrough = sequence
    updates.refresh()
    emit('changed', 'read')
  } catch {
    // Неудачная отметка прочтения повторяется при следующем обновлении диалога.
  } finally { reading = false }
}
function onScroll() {
  const element = viewport.value
  if (!element) return
  atBottom.value = element.scrollHeight - element.scrollTop - element.clientHeight < 36
  void markVisibleRead()
}
async function scrollToEnd() {
  await nextTick()
  if (viewport.value) viewport.value.scrollTop = viewport.value.scrollHeight
  atBottom.value = true
  void markVisibleRead()
}
async function load(initial = false) {
  request?.abort()
  const active = new AbortController()
  request = active
  if (initial) loading.value = true
  const shouldScroll = initial || atBottom.value
  try {
    const result = await loadConversation(props.context.organization.id, props.conversationId, undefined, active.signal)
    if (active.signal.aborted) return
    const firstSequence = Math.min(...result.items.map(item => item.sequence), Infinity)
    const lastLoadedSequence = Math.max(0, ...items.value.map(item => item.sequence))
    const hasGap = !initial && lastLoadedSequence > 0 && firstSequence > lastLoadedSequence + 1
    const older = initial || hasGap ? [] : items.value.filter(item => item.sequence < firstSequence)
    historyWindowReset.value = hasGap || (!initial && historyWindowReset.value)
    page.value = result
    items.value = [...older, ...result.items]
    error.value = ''
    if (shouldScroll) await scrollToEnd()
  } catch (caught) {
    if (!active.signal.aborted) {
      error.value = caught instanceof Error ? caught.message : 'Не удалось загрузить диалог.'
      if (caught instanceof ConversationRequestError && [401, 403, 404].includes(caught.status)) { page.value = null; items.value = [] }
    }
  } finally { if (request === active) loading.value = false }
}
async function older() {
  if (!page.value?.nextBeforeSequence || loadingOlder.value) return
  loadingOlder.value = true
  const current = generation
  const element = viewport.value
  const height = element?.scrollHeight ?? 0
  try {
    const result = await loadConversation(props.context.organization.id, props.conversationId, Math.min(...items.value.map(item => item.sequence)))
    if (current !== generation || !page.value) return
    items.value = [...items.value, ...result.items.filter(item => !items.value.some(existing => existing.id === item.id))]
    page.value.nextBeforeSequence = result.nextBeforeSequence
    await nextTick()
    if (element) element.scrollTop += element.scrollHeight - height
  } catch (caught) { if (current === generation) error.value = caught instanceof Error ? caught.message : 'Не удалось загрузить сообщения.' }
  finally { if (current === generation) loadingOlder.value = false }
}
async function send() {
  if (!writable.value || sending.value || uploading.value || (!pendingSend.value && !draft.value.trim() && !attachment.value)) return
  if (draft.value.length > textLimit.value) return
  pendingSend.value ??= { requestId: crypto.randomUUID(), text: draft.value.trim(), maintenanceId: props.maintenanceId, attachmentIds: attachment.value ? [attachment.value.id] : [], ...template.value }
  const current = generation
  const body = pendingSend.value
  sending.value = true
  sendError.value = ''
  try {
    await sendConversationMessage(props.context.organization.id, props.conversationId, body)
    if (current !== generation) return
    draft.value = ''
    attachment.value = undefined
    pendingSend.value = undefined
    template.value = undefined
    saveDraft()
    await load()
    await scrollToEnd()
    updates.refresh()
    emit('changed', 'sent')
  } catch (caught) {
    if (current === generation) {
      sendError.value = caught instanceof Error ? caught.message : 'Не удалось сохранить сообщение. Повторите запрос.'
      if (caught instanceof ConversationRequestError && caught.status >= 400 && caught.status < 500 && ![408, 429].includes(caught.status)) pendingSend.value = undefined
      saveDraft()
    }
  } finally { if (current === generation) sending.value = false }
}
function saveDraft() {
  if (draftKey) saveConversationDraft(draftKey, { text: draft.value, attachment: attachment.value, pending: pendingSend.value, template: template.value })
}
function insertTemplate(preview: MessageTemplatePreview) {
  if (sending.value || pendingSend.value) return
  const text = [draft.value.trimEnd(), preview.text].filter(Boolean).join('\n\n')
  if (text.length > textLimit.value) { sendError.value = `Сообщение с шаблоном превышает ${textLimit.value} символов. Сократите черновик.`; return }
  draft.value = text; template.value = { messageTemplateId: preview.templateId, messageTemplateVersion: preview.templateVersion }; sendError.value = ''; saveDraft()
}
async function upload(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file || uploading.value || sending.value || pendingSend.value) return
  input.value = ''
  if (file.size === 0 || file.size > 20 * 1024 * 1024) { sendError.value = 'Выберите непустой файл размером до 20 МБ.'; return }
  uploading.value = true
  sendError.value = ''
  const current = generation
  try {
    const result = await uploadConversationFile(props.context.organization.id, props.conversationId, file)
    if (current === generation) { attachment.value = result; saveDraft() }
  } catch (caught) { if (current === generation) sendError.value = caught instanceof Error ? caught.message : 'Не удалось загрузить файл.' }
  finally { if (current === generation) uploading.value = false }
}
function linked() { linking.value = false; updates.refresh(); emit('changed', 'linked'); void load() }
watch(() => [props.context.user.id, props.context.organization.id, props.conversationId, props.maintenanceId], () => {
  saveDraft()
  draftKey = [props.context.user.id, props.context.organization.id, props.conversationId, props.maintenanceId ?? ''].join(':')
  const saved = getConversationDraft(draftKey)
  generation++; readThrough = 0; page.value = null; items.value = []; historyWindowReset.value = false; draft.value = saved.text; pendingSend.value = saved.pending; attachment.value = saved.attachment; template.value = saved.template
  sendError.value = ''; sending.value = false; reading = false
  void load(true)
}, { immediate: true })
watch(updates.revision, () => { if (!loading.value) void load() })
onMounted(() => { window.addEventListener('focus', markVisibleRead); document.addEventListener('visibilitychange', markVisibleRead) })
onBeforeUnmount(() => { saveDraft(); generation++; request?.abort(); window.removeEventListener('focus', markVisibleRead); document.removeEventListener('visibilitychange', markVisibleRead) })
</script>

<template>
  <section class="conversation" aria-label="Переписка с клиентом">
    <header class="conversation__heading">
      <button v-if="showBack" type="button" class="conversation__back" aria-label="К списку диалогов" @click="emit('back')">←</button>
      <div><h3>{{ title }}</h3><p>{{ channelName }} <span v-if="page?.conversation?.botUsername">· @{{ page.conversation.botUsername }}</span></p></div>
      <div class="conversation__actions">
        <UiButton v-if="page?.canViewClient && page.conversation?.clientId" variant="secondary" :href="`#clients/${page.conversation.clientId}`">
          <svg aria-hidden="true" viewBox="0 0 20 20"><path d="M10 10a3.25 3.25 0 1 0 0-6.5A3.25 3.25 0 0 0 10 10Zm-6 6.5a6 6 0 0 1 12 0" /></svg>
          Карточка клиента
        </UiButton>
        <ConversationDeliveryHealth v-if="page?.canManage" :key="context.organization.id" :organization-id="context.organization.id" />
        <UiButton v-if="page?.canManage && page.canViewClient && !context.workspaceAccess?.isReadOnly" variant="secondary" :aria-expanded="linking" @click="linking = true">
          <svg aria-hidden="true" viewBox="0 0 20 20"><path d="M8 12 12 8m-6 7H5a3 3 0 0 1 0-6h3m4-4h3a3 3 0 0 1 0 6h-3m-2 2 2 2m-2-2 2-2" /></svg>
          {{ page.conversation?.clientId ? 'Изменить связь' : 'Связать с клиентом' }}
        </UiButton>
      </div>
    </header>
    <ConversationClientLink v-if="linking && page?.canManage && page.canViewClient" :key="`${context.organization.id}:${conversationId}`" :organization-id="context.organization.id" :conversation-id="conversationId" :current-client-id="page.conversation.clientId" :current-client-name="page.conversation.clientName" @changed="linked" @close="linking = false" />
    <div v-if="loading" class="conversation__empty"><UiLoader text="Загружаем диалог…" /></div>
    <p v-if="error" class="conversation__error" role="alert">{{ error }} <button type="button" @click="load()">Повторить</button></p>
    <div v-if="page" ref="viewport" class="conversation__messages" tabindex="0" aria-label="Сообщения диалога" @scroll="onScroll">
      <p v-if="historyWindowReset" class="conversation__hint">Пришло много сообщений. Показаны последние; предыдущие доступны по кнопке «Ранее».</p>
      <UiButton v-if="page.nextBeforeSequence" class="conversation__older" variant="secondary" :disabled="loadingOlder" @click="older">{{ loadingOlder ? 'Загружаем…' : 'Ранее' }}</UiButton>
      <p v-if="!ordered.length" class="conversation__empty">Сообщений пока нет.</p>
      <article v-for="item in ordered" :key="item.id" class="conversation__message" :class="{ 'conversation__message--outgoing': outgoing(item) }">
        <span class="conversation__author">{{ outgoing(item) ? item.actorDisplayName || 'Сотрудник' : title }}</span>
        <p v-if="item.text" class="conversation__text">{{ item.text }}</p>
        <template v-for="file in item.attachments" :key="file.id"><a v-if="file.sizeBytes <= 20 * 1024 * 1024" class="conversation__file" :href="conversationUrl(context.organization.id, `/attachments/${file.id}`)" target="_blank" rel="noopener">📎 {{ file.fileName }}</a><span v-else class="conversation__hint">📎 {{ file.fileName }} · Больше 20 МБ, загрузка недоступна</span></template>
        <a v-if="item.maintenanceId && page.canViewMaintenance" :href="`#maintenance/${item.maintenanceId}`" class="conversation__context">{{ item.maintenanceTitle || 'Обслуживание' }} ↗</a>
        <p v-if="outgoing(item) && deliveryHint(item)" class="conversation__delivery-hint">{{ deliveryHint(item) }}<span v-if="item.retryAt"> Следующая попытка: {{ formatTime(item.retryAt) }}.</span></p>
        <footer><time :datetime="item.createdAt">{{ formatTime(item.createdAt) }}</time><span v-if="outgoing(item)">{{ status(item) }}</span></footer>
      </article>
    </div>
    <button v-if="page && !atBottom" type="button" class="conversation__latest" @click="scrollToEnd">К последним сообщениям ↓</button>
    <form v-if="writable" class="conversation__composer" @submit.prevent="send">
      <div class="conversation__composer-heading"><label :for="`conversation-draft-${conversationId}`">Сообщение клиенту</label><ConversationTemplatePicker v-if="!sending && !pendingSend" :organization-id="context.organization.id" :conversation-id="conversationId" :maintenance-id="maintenanceId" :text-limit="textLimit" @select="insertTemplate" /></div>
      <textarea :id="`conversation-draft-${conversationId}`" v-model="draft" rows="3" :maxlength="textLimit" :disabled="sending || !!pendingSend" placeholder="Напишите сообщение…" />
      <div v-if="attachment" class="conversation__attachment"><span>📎 {{ attachment.fileName }}</span><button type="button" :disabled="sending || !!pendingSend" @click="attachment = undefined">Убрать</button></div>
      <p v-if="attachment" class="conversation__hint">Неотправленный файл хранится сутки.</p>
      <p v-if="draft.length > textLimit" class="conversation__error" role="alert">Сократите текст до {{ textLimit }} символов.</p>
      <p v-if="maintenanceId" class="conversation__hint">Сообщение будет связано с этим обслуживанием.</p>
      <p v-if="sendError" class="conversation__error" role="alert">{{ sendError }}</p>
      <div class="conversation__send"><label class="conversation__upload"><input type="file" :disabled="sending || uploading || !!pendingSend || !!attachment" aria-label="Прикрепить файл до 20 МБ" @change="upload" />{{ uploading ? 'Загружаем…' : 'Прикрепить файл' }}</label><span>{{ draft.length }} / {{ textLimit }}</span><UiButton type="submit" :disabled="sending || uploading || (!draft.trim() && !attachment) || draft.length > textLimit">{{ sending ? 'Сохраняем…' : pendingSend ? 'Повторить запрос' : 'Отправить' }}</UiButton></div>
    </form>
    <p v-else-if="page" class="conversation__readonly">{{ context.shell.scenario?.modules?.messaging === false ? 'История доступна для просмотра. Отправка сообщений через Telegram и MAX доступна в PRO.' : page.isReadOnly ? 'Отправка недоступна. Проверьте подключение бота организации.' : 'Переписка доступна для просмотра.' }}</p>
  </section>
</template>

<style scoped>
.conversation { display:flex; flex-direction:column; min-width:0; height: min(74dvh, 820px); min-height:440px; background:var(--crm-surface, #fff); }
.conversation__heading { display:flex; align-items:center; gap:12px; padding:16px 20px; border-bottom:1px solid var(--crm-line, #e0e5ec); }
.conversation__heading > div:first-of-type { min-width:0; flex:1; }
.conversation__heading h3 { margin:0; font-size:16px; overflow-wrap:anywhere; }
.conversation__heading p { margin:4px 0 0; font-size:12px; color:var(--crm-text-muted, #667085); }
.conversation__actions { display:flex; align-items:center; flex-wrap:wrap; justify-content:flex-end; gap:6px; }
.conversation__actions :deep(.ui-button) { display:inline-flex; align-items:center; gap:6px; white-space:nowrap; font-size:12px; }
.conversation__actions svg { width:15px; height:15px; fill:none; stroke:currentColor; stroke-width:1.6; stroke-linecap:round; stroke-linejoin:round; }
.conversation__back { display:none; border:0; background:transparent; color:inherit; font:inherit; padding:8px; }
.conversation__messages { display:flex; flex-direction:column; align-items:flex-start; gap:12px; flex:1; min-height:0; overflow:auto; overscroll-behavior:contain; padding:20px; background:var(--crm-surface-soft, #f6f7f9); }
.conversation__message { box-sizing:border-box; display:grid; gap:7px; max-width:85%; min-width:min(200px, 85%); padding:12px 14px; border:1px solid var(--crm-line, #e0e5ec); border-radius:12px 12px 12px 2px; background:var(--crm-surface, #fff); overflow-wrap:anywhere; }
.conversation__message--outgoing { align-self:flex-end; border-radius:12px 12px 2px 12px; background:var(--crm-accent-soft, #fff3e9); }
.conversation__author { font-size:12px; font-weight:650; }
.conversation__text { margin:0; white-space:pre-wrap; font-size:14px; line-height:1.5; }
.conversation__message footer { display:flex; flex-wrap:wrap; gap:6px 12px; color:var(--crm-text-muted, #667085); font-size:10px; }
.conversation__message footer span { margin-left:auto; }
.conversation__context, .conversation__file { color:var(--crm-accent, #ad511e); font-size:12px; }
.conversation__delivery-hint { margin:0; line-height:1.5; font-size:11px; color:var(--crm-text-muted, #667085); }
.conversation__empty { padding:24px; margin:auto; color:var(--crm-text-muted, #667085); text-align:center; }
.conversation__older { align-self:center; }
.conversation__composer { display:grid; gap:8px; padding:16px 20px; border-top:1px solid var(--crm-line, #e0e5ec); }
.conversation__composer-heading { display:flex; align-items:center; justify-content:space-between; gap:12px; }
.conversation__composer label { font-size:12px; font-weight:600; }
.conversation__composer textarea { box-sizing:border-box; width:100%; resize:vertical; max-height:200px; min-height:72px; border:1px solid var(--crm-line, #d4dce6); border-radius:8px; padding:10px; font:inherit; font-size:14px; color:inherit; background:var(--crm-surface, #fff); }
.conversation__send { display:flex; align-items:center; justify-content:space-between; gap:12px; }
.conversation__attachment { display:flex; gap:12px; justify-content:space-between; font-size:12px; overflow-wrap:anywhere; }
.conversation__upload { position:relative; overflow:hidden; color:var(--crm-accent, #ad511e); cursor:pointer; }
.conversation__upload input { position:absolute; inset:0; width:100%; opacity:0; cursor:pointer; }
.conversation__upload:focus-within { outline:2px solid var(--crm-accent, #ad511e); outline-offset:3px; }
.conversation__send span, .conversation__hint { color:var(--crm-text-muted, #667085); font-size:11px; margin:0; }
.conversation__error { margin:0; padding:10px; color:var(--crm-danger, #b42318); font-size:12px; }
.conversation__readonly { margin:0; padding:16px; color:var(--crm-text-muted, #667085); font-size:12px; }
.conversation__latest { align-self:center; padding:8px 12px; border:1px solid var(--crm-line); border-radius:8px; color:inherit; background:var(--crm-surface); cursor:pointer; }
@media (max-width:700px) { .conversation__back { display:block; } .conversation__heading { padding:12px; gap:8px; flex-wrap:wrap; } .conversation__heading > div:first-of-type { flex:1 1 calc(100% - 48px); } .conversation__actions { flex:1 1 100%; justify-content:flex-start; } .conversation__messages { padding:12px; } .conversation__message { max-width:94%; } .conversation__composer { padding:12px; } .conversation { height:74dvh; min-height:400px; } }
</style>

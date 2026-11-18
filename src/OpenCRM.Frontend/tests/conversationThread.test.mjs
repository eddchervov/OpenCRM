import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '../packages/workspace/src/conversationMessages.ts'
import { formattingCulture, i18n, setFormattingCulture } from '@opencrm/ui/i18n'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const filename = new URL('../packages/workspace/src/components/ConversationThread.vue', import.meta.url)
const { descriptor } = parse(readFileSync(filename, 'utf8'))
const script = compileScript(descriptor, { id: 'conversation-thread-test' })
const code = ts.transpileModule(script.content, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText

function thread(messagingAvailable = true) {
  const sent = []
  const cursors = []
  let nextItems = []
  const page = () => ({ conversationId: 'chat', conversation: { channelCode: 'max' }, items: nextItems, canSend: true, isReadOnly: false, nextBeforeSequence: nextItems[0]?.sequence ?? null })
  class RequestError extends Error { constructor(status) { super('error'); this.status = status } }
  const api = {
    ConversationRequestError: RequestError, conversationChannelName: () => 'MAX',
    loadConversation: async (_organizationId, _conversationId, cursor) => { cursors.push(cursor); if (nextItems instanceof Error) throw nextItems; return page() },
    sendConversationMessage: async (...args) => { sent.push(args); return { id: 'saved' } }
  }
  const resolve = id => {
    if (regionalTestDependencies[id]) return regionalTestDependencies[id]
    if (id === 'vue') return { ...vue, watch() {}, onMounted() {}, onBeforeUnmount() {} }
    if (id === '../api/conversations') return api
    if (id === '../utils/conversationDrafts') return { getConversationDraft: () => ({ text: '' }), saveConversationDraft() {} }
    if (id === '../composables/conversationUpdates') return { useConversationUpdates: () => ({ revision: vue.ref(''), refresh() {} }) }
    return {}
  }
  const module = { exports: {} }
  new Function('require', 'module', 'exports', 'document', code)(resolve, module, module.exports, { visibilityState: 'hidden', hasFocus: () => false })
  const props = { context: { user: { id: 'user' }, organization: { id: 'org' }, shell: { scenario: { modules: { messaging: messagingAvailable } } } }, conversationId: 'chat', maintenanceId: 'maintenance' }
  const state = module.exports.default.setup(props, { expose() {}, emit() {} })
  state.page.value = page()
  return { state, sent, cursors, setItems(items) { nextItems = items }, deny(status) { nextItems = new RequestError(status) } }
}

test('Статус доставки следует языку, а время сообщения — формату и поясу организации', () => {
  const originalLocale = i18n.global.locale.value
  const originalCulture = formattingCulture.value
  try {
    const { state } = thread()
    state.page.value.timeZoneId = 'Asia/Shanghai'
    i18n.global.locale.value = 'zh-Hans'
    setFormattingCulture('en-GB')
    assert.equal(state.status({ status: 'queued' }), '排队中')
    assert.equal(state.status({ status: 2 }), 'MAX 已接受')
    assert.match(state.formatTime('2026-11-16T09:30:00Z'), /16 Nov 2026, 17:30/)
    state.draft.value = 'Пользовательский текст — 客户消息'
    i18n.global.locale.value = 'en'
    assert.equal(state.draft.value, 'Пользовательский текст — 客户消息')
  } finally { i18n.global.locale.value = originalLocale; setFormattingCulture(originalCulture) }
})

test('Шаблон передаёт серверные имена полей версии вместе с контекстом обслуживания и отредактированным текстом', async () => {
  const { state, sent } = thread()
  state.draft.value = 'Комментарий сотрудника'
  state.insertTemplate({ templateId: 'template', templateVersion: 3, text: 'Подтверждённая запись' })
  await state.send()
  assert.equal(sent.length, 1)
  assert.equal(sent[0][2].messageTemplateId, 'template')
  assert.equal(sent[0][2].messageTemplateVersion, 3)
  assert.equal(sent[0][2].maintenanceId, 'maintenance')
  assert.equal(sent[0][2].text, 'Комментарий сотрудника\n\nПодтверждённая запись')
  assert.ok(sent[0][2].requestId)
})

test('Бесплатный тариф сохраняет историю и черновик без отправки сообщения', async () => {
  const { state, sent } = thread(false)
  state.draft.value = 'Сохранённый текст'
  await state.send()
  assert.equal(sent.length, 0)
  assert.equal(state.draft.value, 'Сохранённый текст')
  assert.ok(state.page.value)
})

test('Слишком длинный шаблон не затирает черновик и не обрезает сообщение', () => {
  const { state } = thread()
  state.draft.value = 'Сохранить этот текст'
  state.insertTemplate({ templateId: 'template', templateVersion: 1, text: 'я'.repeat(4000) })
  assert.equal(state.draft.value, 'Сохранить этот текст')
  assert.equal(state.template.value, undefined)
  assert.match(state.sendError.value, /4000/)
})

test('Большой входящий пакет сохраняет доступ к пропущенному диапазону через правильный курсор истории', async () => {
  const { state, cursors, setItems } = thread()
  state.items.value = Array.from({ length: 50 }, (_, index) => ({ id: String(index + 1), sequence: index + 1 }))
  setItems(Array.from({ length: 50 }, (_, index) => ({ id: String(index + 61), sequence: index + 61 })))
  await state.load()
  assert.equal(state.historyWindowReset.value, true)
  assert.equal(Math.min(...state.items.value.map(item => item.sequence)), 61)
  setItems(Array.from({ length: 50 }, (_, index) => ({ id: String(index + 11), sequence: index + 11 })))
  await state.older()
  assert.equal(cursors.at(-1), 61)
  assert.equal(state.items.value.length, 100)
  assert.equal(Math.min(...state.items.value.map(item => item.sequence)), 11)
})

test('Истёкший вход или отзыв доступа скрывает уже загруженную переписку и форму отправки', async () => {
  for (const status of [401, 403, 404]) {
    const { state, deny } = thread()
    state.items.value = [{ id: 'private-message', sequence: 1, text: 'Текст клиента' }]
    deny(status)
    await state.load()
    assert.equal(state.page.value, null)
    assert.deepEqual(state.items.value, [])
    assert.equal(state.writable.value, undefined)
  }
})

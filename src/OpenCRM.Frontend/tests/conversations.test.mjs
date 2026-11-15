import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import * as vue from 'vue'

function createModule(file, bindings = {}) {
  const code = ts.transpileModule(readFileSync(new URL(file, import.meta.url), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const module = { exports: {} }
  new Function('module', 'exports', ...Object.keys(bindings), code)(module, module.exports, ...Object.values(bindings))
  return module.exports
}
function createApi(fetch) {
  return createModule('../packages/workspace/src/api/conversations.ts', {
    fetch, document: { querySelector: () => ({ content: 'csrf' }) }, URLSearchParams,
    FormData: globalThis.FormData
  })
}

test('Повтор HTTP запроса сохраняет RequestId и контекст обслуживания, ошибки не подменяются успешной отправкой', async () => {
  const sent = []
  const api = createApi(async (url, options) => {
    sent.push({ url, options })
    if (sent.length === 1) throw new TypeError('network')
    return { ok: true, status: 200, json: async () => ({ id: 'saved', status: 'Queued' }) }
  })
  const body = { requestId: 'stable-id', text: 'Готово', maintenanceId: 'record', attachmentIds: ['file'] }
  await assert.rejects(api.sendConversationMessage('org/one', 'chat', body), /network/)
  assert.equal((await api.sendConversationMessage('org/one', 'chat', body)).status, 'Queued')
  assert.equal(sent[0].url, '/api/v1/organizations/org%2Fone/conversations/chat/messages')
  assert.deepEqual(JSON.parse(sent[0].options.body), JSON.parse(sent[1].options.body))
  assert.equal(sent[1].options.headers.RequestVerificationToken, 'csrf')
  assert.equal(sent[1].options.credentials, 'same-origin')
})

test('Ошибка доступа и ограничение частоты остаются различимыми, пустой клиентский диалог возвращает null', async () => {
  const denied = createApi(async () => ({ ok: false, status: 403, json: async () => ({}) }))
  await assert.rejects(denied.loadConversation('org', 'chat'), error => error.status === 403)
  const limited = createApi(async () => ({ ok: false, status: 429, json: async () => ({}) }))
  await assert.rejects(limited.sendConversationMessage('org', 'chat', {}), error => error.status === 429)
  const empty = createApi(async () => ({ ok: true, status: 204 }))
  assert.equal(await empty.getClientConversation('org', 'client'), undefined)
})

test('Черновики и неизвестный результат запроса изолированы по сотруднику, организации и диалогу', () => {
  const drafts = createModule('../packages/workspace/src/utils/conversationDrafts.ts')
  drafts.saveConversationDraft('user:org:one', { text: 'Не потерять', pending: { requestId: 'stable', text: 'Не потерять' } })
  assert.equal(drafts.getConversationDraft('user:org:one').pending.requestId, 'stable')
  assert.equal(drafts.getConversationDraft('other:org:one').text, '')
  assert.equal(drafts.getConversationDraft('user:other:one').text, '')
  drafts.saveConversationDraft('user:org:one', { text: '' })
  assert.equal(drafts.getConversationDraft('user:org:one').pending, undefined)
})

test('Сбой потока повторно проверяет доступ: временная ошибка сохраняет данные, отзыв прав очищает счётчик и обновляет экраны', async () => {
  const streams = []
  const responses = [200, 503, 403]
  class EventSource {
    constructor() { streams.push(this) }
    close() {}
    addEventListener() {}
  }
  const api = createModule('../packages/workspace/src/composables/conversationUpdates.ts', {
    require: id => id === 'vue'
      ? { ...vue, provide() {}, onBeforeUnmount() {}, watch(_source, callback) { callback() } }
      : { conversationUrl: () => '/summary' },
    EventSource,
    document: { visibilityState: 'visible', addEventListener() {}, removeEventListener() {} },
    fetch: async () => {
      const status = responses.shift()
      return { ok: status === 200, status, json: async () => ({ unreadCount: 4, revision: 'saved-revision' }) }
    }
  })
  const state = api.provideConversationUpdates(vue.computed(() => 'org'))
  await new Promise(setImmediate)
  assert.equal(state.unreadCount.value, 4)
  streams[0].onerror()
  await new Promise(setImmediate)
  assert.equal(state.revision.value, 'saved-revision')
  assert.equal(state.unreadCount.value, 4)
  streams[0].onerror()
  await new Promise(setImmediate)
  assert.equal(state.unreadCount.value, 0)
  assert.match(state.revision.value, /^access-denied:/)
})

import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '../packages/workspace/src/conversationMessages.ts'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/MessagesSection.vue', import.meta.url), 'utf8'))
const script = compileScript(descriptor, { id: 'messages-inbox-test' })
const code = ts.transpileModule(script.content, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
const group = id => ({ groupId: id, clientId: `client-${id}`, clientName: id, unreadCount: 1, routes: [
  { id: `telegram-${id}`, channelCode: 'telegram', isReadOnly: false, unreadCount: 1 },
  { id: `max-${id}`, channelCode: 'max', isReadOnly: false, unreadCount: 0 }
] })
class ConversationRequestError extends Error {
  constructor(message, status) { super(message); this.status = status }
}

function inbox() {
  const pages = []
  const queries = []
  const module = { exports: {} }
  const props = vue.reactive({ context: { organization: { id: 'org' } }, conversationId: 'telegram-b' })
  const resolve = id => {
    if (regionalTestDependencies[id]) return regionalTestDependencies[id]
    if (id === 'vue') return { ...vue, watch() {}, onBeforeUnmount() {} }
    if (id === '../api/conversations') return { ConversationRequestError, conversationChannelName: value => value, loadClientInbox: async (_org, query) => { queries.push(query); const page = pages.shift(); if (page instanceof Error) throw page; return page } }
    if (id === '../composables/conversationUpdates') return { useConversationUpdates: () => ({ revision: vue.ref(''), unreadCount: vue.ref(0), connected: vue.ref(true) }) }
    return {}
  }
  const window = { location: { hash: '#messages/telegram-b' }, addEventListener() {}, removeEventListener() {} }
  new Function('require', 'module', 'exports', 'window', code)(resolve, module, module.exports, window)
  const state = module.exports.default.setup(props, { expose() {} })
  return { state, queries, pages, props, window }
}

test('Заголовок и хлебные крошки одинаковы при открытии диалога и выборе клиента с несколькими каналами', () => {
  const { state, props, window } = inbox()
  state.items.value = [group('b')]
  assert.equal(state.pageTitle.value, 'b')
  assert.deepEqual(state.pageBreadcrumbs.value, [{ title: 'Сообщения', href: '#messages' }])

  props.conversationId = undefined
  window.location.hash = '#messages?client=client-b'
  state.syncClient()
  assert.equal(state.pageTitle.value, 'b')
  assert.deepEqual(state.pageBreadcrumbs.value, [{ title: 'Сообщения', href: '#messages' }])

  window.location.hash = '#messages'
  state.syncClient()
  assert.equal(state.pageTitle.value, 'Сообщения')
  assert.deepEqual(state.pageBreadcrumbs.value, [])
})

test('Входящее сообщение обновляет начало списка и сохраняет уже загруженные диалоги с правильным курсором', async () => {
  const { state, pages } = inbox()
  state.items.value = [group('a')]
  state.nextCursor.value = 'page-2'
  pages.push({ items: [group('b')], nextCursor: 'page-3' })
  await state.load(true)
  pages.push({ items: [group('new'), group('a')], nextCursor: 'new-page-2' })
  await state.load(false, true)
  assert.deepEqual(state.items.value.map(item => item.groupId), ['new', 'a', 'b'])
  assert.equal(state.nextCursor.value, 'page-3')
  assert.equal(state.activeGroup.value.groupId, 'b')
})

test('Прочитанная группа уходит из всех страниц фильтра, открытая переписка сохраняет выбор каналов', async () => {
  const { state, pages, queries } = inbox()
  state.onlyUnread.value = true
  state.items.value = [group('a')]
  state.nextCursor.value = 'page-2'
  pages.push({ items: [group('b')], nextCursor: 'page-3' })
  await state.load(true)
  state.openedGroup.value = state.items.value[1]
  pages.push({ items: [group('a')], nextCursor: 'fresh-2' }, { items: [group('c')], nextCursor: null })
  await state.conversationChanged('read')
  assert.deepEqual(state.items.value.map(item => item.groupId), ['a', 'c'])
  assert.equal(queries.at(-1).cursor, 'fresh-2')
  assert.equal(state.nextCursor.value, null)
  assert.equal(state.activeGroup.value.groupId, 'b')
  assert.equal(state.activeGroup.value.unreadCount, 0)
  assert.equal(state.activeRoutes.value.length, 2)
})

test('Отзыв доступа очищает ранее загруженные имена и сообщения, включая открытую группу', async () => {
  const { state, pages } = inbox()
  state.items.value = [group('a')]
  state.openedGroup.value = group('b')
  state.nextCursor.value = 'page-2'
  pages.push(new ConversationRequestError('Нет доступа к переписке.', 403))
  await state.load(false, true)
  assert.deepEqual(state.items.value, [])
  assert.equal(state.openedGroup.value, undefined)
  assert.equal(state.activeGroup.value, undefined)
  assert.equal(state.nextCursor.value, null)
  assert.equal(state.error.value, 'Нет доступа к переписке.')
})

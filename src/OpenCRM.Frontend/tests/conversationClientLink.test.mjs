import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const filename = new URL('../packages/workspace/src/components/ConversationClientLink.vue', import.meta.url)
const { descriptor } = parse(readFileSync(filename, 'utf8'))
const script = compileScript(descriptor, { id: 'conversation-client-link-test' })
const code = ts.transpileModule(script.content, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText

function component({ clients = async () => ({ items: [], totalCount: 0 }), link = async () => {} } = {}) {
  const requests = []
  const links = []
  const emitted = []
  let cleanup = () => {}
  const api = {
    loadClientLookup: (...args) => { requests.push(args); return clients(...args) },
    linkConversationClient: (...args) => { links.push(args); return link(...args) }
  }
  const resolve = id => {
    if (id === 'vue') return { ...vue, onBeforeUnmount: handler => { cleanup = handler } }
    if (id === '../api/clientLookup') return api
    if (id === '../api/conversations') return api
    return {}
  }
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const props = vue.reactive({ organizationId: 'org', conversationId: 'chat', currentClientId: null, currentClientName: null })
  const state = module.exports.default.setup(props, { expose() {}, emit: (...args) => emitted.push(args) })
  return { state, props, requests, links, emitted, cleanup }
}

const wait = duration => new Promise(resolve => setTimeout(resolve, duration))
const result = (items = [], totalCount = items.length) => ({ items, totalCount })
const client = id => ({ id, name: `Клиент ${id}`, phone: '+7 000 000-00-00', vehiclesCount: 0, maintenanceCount: 0 })

test('Не загружает клиентов до ввода двух символов, ищет с debounce и выдаёт страницы по восемь результатов', async () => {
  const { state, requests } = component({ clients: async (_org, _search, page) => result(Array.from({ length: 8 }, (_, index) => client(`${page}-${index}`)), 16) })
  await vue.nextTick()
  assert.equal(requests.length, 0)
  state.search.value = 'И'
  await vue.nextTick()
  await wait(320)
  assert.equal(requests.length, 0)
  state.search.value = 'Ив'
  await vue.nextTick()
  await wait(320)
  assert.equal(requests.length, 1)
  assert.equal(requests[0][1], 'Ив')
  assert.equal(requests[0][2], 1)
  assert.equal(state.items.value.length, 8)
  state.changePage(2)
  assert.equal(requests.length, 2)
  assert.equal(requests[1][1], 'Ив')
  assert.equal(requests[1][2], 2)
})

test('Debounce отменяет прежний запрос, а его поздний ответ не заменяет новые результаты', async () => {
  const pending = []
  const { state, requests } = component({ clients: (...args) => new Promise(resolve => pending.push({ args, resolve })) })
  state.search.value = 'Ив'
  await vue.nextTick()
  await wait(320)
  assert.equal(requests.length, 1)
  state.search.value = 'Иван'
  await vue.nextTick()
  assert.equal(requests[0][3].aborted, true)
  await wait(320)
  assert.equal(requests.length, 2)
  pending[0].resolve(result([client('old')]))
  pending[1].resolve(result([client('new')]))
  await wait(0)
  assert.deepEqual(state.items.value.map(item => item.id), ['new'])
})

test('Ошибка поиска показывается, а отмена запроса при смене организации не становится ошибкой', async () => {
  let rejectSearch
  const { state, props, requests } = component({ clients: () => new Promise((_resolve, reject) => { rejectSearch = reject }) })
  state.search.value = 'Иван'
  await vue.nextTick()
  await wait(320)
  rejectSearch(new Error('Ошибка поиска'))
  await wait(0)
  assert.equal(state.error.value, 'Ошибка поиска')

  state.search.value = 'Мария'
  await vue.nextTick()
  await wait(320)
  props.organizationId = 'another-org'
  await vue.nextTick()
  assert.equal(requests.at(-1)[3].aborted, true)
  rejectSearch(new Error('Отменённый запрос'))
  await wait(0)
  assert.equal(state.error.value, '')
})

test('Выбор карточки не сохраняет связь до подтверждения', async () => {
  const { state, links, emitted } = component()
  state.chooseClient('client-1')
  assert.equal(links.length, 0)
  assert.deepEqual(emitted, [])
  state.confirmSelection()
  await wait(0)
  assert.deepEqual(links, [['org', 'chat', 'client-1']])
  assert.deepEqual(emitted, [['changed'], ['close']])
})

test('Повторное сохранение блокируется, ошибка оставляет диалог открытым и не отправляет событие', async () => {
  let rejectSave
  const { state, links, emitted } = component({ link: () => new Promise((_resolve, reject) => { rejectSave = reject }) })
  state.chooseClient('client-1')
  state.confirmSelection()
  state.confirmSelection()
  assert.equal(links.length, 1)
  rejectSave(new Error('Не удалось сохранить'))
  await wait(0)
  assert.equal(state.saveError.value, 'Не удалось сохранить')
  assert.deepEqual(emitted, [])
})

test('Переход на следующую страницу снимает выбор клиента с предыдущей страницы', () => {
  const { state, links } = component()
  state.search.value = 'Клиент'
  state.totalCount.value = 16
  state.chooseClient('first-page-client')
  state.changePage(2)
  state.confirmSelection()
  assert.equal(state.selectedClientId.value, null)
  assert.deepEqual(links, [])
})

test('Ответ сохранения из прежней организации не закрывает новый диалог', async () => {
  let resolveSave
  const { state, props, emitted } = component({ link: () => new Promise(resolve => { resolveSave = resolve }) })
  state.chooseClient('client-1')
  state.confirmSelection()
  props.organizationId = 'another-org'
  await vue.nextTick()
  resolveSave()
  await wait(0)
  assert.deepEqual(emitted, [])
  assert.equal(state.saving.value, false)
})

test('Если последняя страница исчезла, поиск возвращается на существующую страницу с актуальным числом клиентов', async () => {
  let removed = false
  const { state, requests } = component({ clients: async (_org, _query, page) => removed
    ? result(page === 1 ? [client('remaining')] : [], 1)
    : result(Array.from({ length: 8 }, (_, index) => client(index)), 9) })
  state.search.value = 'Клиент'
  await vue.nextTick()
  await wait(320)
  removed = true
  state.changePage(2)
  await wait(0)
  assert.equal(state.page.value, 1)
  assert.equal(state.totalCount.value, 1)
  assert.deepEqual(state.items.value.map(item => item.id), ['remaining'])
  assert.deepEqual(requests.map(item => item[2]), [1, 2, 1])
})

test('Отвязка снимает предварительный выбор и требует отдельного подтверждения', async () => {
  const { state, props, links } = component()
  props.currentClientId = 'current'
  state.chooseClient('another')
  state.confirmRemoval()
  assert.equal(state.selectedClientId.value, null)
  assert.equal(links.length, 0)
  state.confirmRemoval()
  await wait(0)
  assert.deepEqual(links, [['org', 'chat', null]])
})

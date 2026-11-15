import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import * as exportApi from '../packages/workspace/src/api/organizationExport.ts'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceOrganizationExport.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'organization-export' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const ready = { id: 'job-1', state: 'ready' }

function setup() {
  const props = vue.reactive({ organizationId: 'qa-org' })
  const module = { exports: {} }
  let unmount
  new Function('require', 'module', 'exports', code)(id => {
    if (id === 'vue') return { ...vue, useId: () => 'qa-export', onBeforeUnmount: callback => { unmount = callback } }
    if (id === '../api/organizationExport') return exportApi
    if (id === '@opencrm/ui') return {}
    throw new Error(`Не задана зависимость: ${id}`)
  }, module, module.exports)
  const state = module.exports.default.setup(props, { expose() {} })
  const downloads = []
  state.downloadLink.value = { href: '', click() { downloads.push(this.href) } }
  return { props, state, downloads, unmount: () => unmount?.() }
}

function browser(t, fetchResponse) {
  const original = { fetch: globalThis.fetch, document: globalThis.document, setTimeout: globalThis.setTimeout, clearTimeout: globalThis.clearTimeout }
  const requests = []
  const timers = new Map()
  let timerId = 0
  globalThis.document = { querySelector: () => ({ content: 'qa-csrf-token' }) }
  globalThis.setTimeout = callback => { const id = ++timerId; timers.set(id, callback); return id }
  globalThis.clearTimeout = id => timers.delete(id)
  globalThis.fetch = async (url, options) => {
    requests.push({ url, options })
    return fetchResponse(url, options)
  }
  t.after(() => {
    globalThis.fetch = original.fetch
    globalThis.setTimeout = original.setTimeout
    globalThis.clearTimeout = original.clearTimeout
    if (original.document === undefined) delete globalThis.document
    else globalThis.document = original.document
  })
  return { requests, timers, async tick() {
    const entry = timers.entries().next().value
    if (entry) { timers.delete(entry[0]); entry[1](); await settle() }
  } }
}

async function settle() { for (let index = 0; index < 12; index++) await Promise.resolve() }

test('Запрос очереди отправляет CSRF, ошибки остаются в настройках', async t => {
  const { requests } = browser(t, (url, options) => options.method === 'POST'
    ? Response.json({ message: 'Очередь выгрузок занята.' }, { status: 429 })
    : Response.json({ id: null, state: 'idle' }))
  const { state, downloads, unmount } = setup()
  await settle()
  await state.download()
  assert.equal(state.errorMessage.value, 'Очередь выгрузок занята.')
  assert.equal(requests.at(-1).options.headers.RequestVerificationToken, 'qa-csrf-token')
  assert.equal(requests.at(-1).options.credentials, 'same-origin')
  assert.deepEqual(downloads, [])
  unmount()
})

test('Готовый ZIP передаётся браузеру по URL без загрузки файла в Blob', async t => {
  const { requests } = browser(t, () => Response.json(ready))
  const { state, downloads, unmount } = setup()
  await settle()
  assert.deepEqual(downloads, [])
  await state.download()
  assert.deepEqual(downloads, ['/api/v1/organizations/qa-org/data-export/job-1/file'])
  assert.equal(requests.length, 2)
  assert.ok(requests.every(request => !request.url.endsWith('/file')))
  assert.equal(state.errorMessage.value, '')
  unmount()
})

test('Очередь опрашивается до готовности и скачивается только после клика пользователя', async t => {
  let phase = 'idle'
  const clock = browser(t, (url, options) => {
    if (options.method === 'POST') phase = 'queued'
    return Response.json({ id: 'job-1', state: phase })
  })
  const { state, downloads, unmount } = setup()
  await settle()
  await state.download()
  assert.equal(state.buttonLabel.value, 'В очереди…')
  await state.download()
  assert.equal(clock.requests.filter(request => request.options.method === 'POST').length, 1)
  phase = 'running'
  await clock.tick()
  assert.equal(state.buttonLabel.value, 'Формируем архив…')
  phase = 'ready'
  await clock.tick()
  assert.equal(downloads.length, 1)
  assert.equal(clock.timers.size, 0)
  unmount()
})

test('Ошибка опроса повторяет только GET, не создавая новые задания', async t => {
  let offline = false
  const clock = browser(t, (url, options) => {
    if (offline) throw new Error('Нет связи')
    return Response.json({ id: 'job-1', state: options.method === 'POST' ? 'queued' : 'idle' })
  })
  const { state, unmount } = setup()
  await settle()
  await state.download()
  offline = true
  await clock.tick()
  assert.equal(state.errorMessage.value, 'Нет связи')
  assert.equal(clock.timers.size, 1)
  assert.equal(clock.requests.filter(request => request.options.method === 'POST').length, 1)
  unmount()
})

test('Смена организации и закрытие раздела игнорируют старые ответы и прекращают опрос', async t => {
  let finish
  const clock = browser(t, (url, options) => options.method === 'POST'
    ? new Promise(resolve => { finish = resolve }) : Response.json({ id: null, state: 'idle' }))
  const { state, props, downloads, unmount } = setup()
  await settle()
  const pending = state.download()
  props.organizationId = 'other-org'
  await vue.nextTick()
  finish(Response.json(ready))
  await pending
  assert.deepEqual(downloads, [])
  assert.equal(state.isRequesting.value, false)
  unmount()
  assert.equal(clock.timers.size, 0)
})

test('Сбой native скачивания показывает сообщение, а HTML не отображается как JSON клиенту', async t => {
  browser(t, () => Response.json(ready))
  const { state, unmount } = setup()
  state.downloadLoaded({ target: { contentDocument: { body: { textContent: JSON.stringify({ message: 'Сейчас скачиваются другие архивы.' }) } } } })
  assert.equal(state.errorMessage.value, 'Сейчас скачиваются другие архивы.')
  state.downloadLoaded({ target: { contentDocument: { body: { textContent: 'Вход в аккаунт' } } } })
  assert.match(state.errorMessage.value, /Не удалось скачать/)
  unmount()
})

test('HTML вместо статуса и неструктурированная ошибка обрабатываются безопасно', async t => {
  browser(t, () => new Response('<html>Вход</html>', { headers: { 'Content-Type': 'text/html' } }))
  await assert.rejects(exportApi.requestOrganizationExport('qa-org'), /Не удалось получить выгрузку/)
  globalThis.fetch = async () => new Response('Service unavailable', { status: 503 })
  await assert.rejects(exportApi.loadOrganizationExportStatus('qa-org'), /Не удалось получить выгрузку/)
})

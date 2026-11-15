import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/ClientBotChannel.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'client-bot-channel-test' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const status = (connected = false) => ({ isBotConnected: true, isClientConnected: connected, botUsername: 'studio_bot', recentNotifications: [] })
const flush = async () => { await new Promise(resolve => setImmediate(resolve)); await vue.nextTick() }
function deferred() {
  let resolve
  let reject
  const promise = new Promise((accept, fail) => { resolve = accept; reject = fail })
  return { promise, resolve, reject }
}

function setup({ channel = 'telegram', canWrite = true, load = async () => status(), create = async () => ({ url: 'https://example.test/client-link' }), disconnect = async () => {}, copy = async () => {} } = {}) {
  const calls = []
  const events = []
  let unmount = () => {}
  const api = {}
  for (const provider of ['Telegram', 'Max']) {
    api[`load${provider}ClientStatus`] = (...args) => { calls.push(['load', provider, ...args]); return load(...args) }
    api[`create${provider}ClientLink`] = (...args) => { calls.push(['create', provider, ...args]); return create(...args) }
    api[`disconnect${provider}Client`] = (...args) => { calls.push(['disconnect', provider, ...args]); return disconnect(...args) }
  }
  const resolve = id => {
    if (id === 'vue') return { ...vue, onBeforeUnmount: callback => { unmount = callback } }
    if (id.startsWith('../api/')) return api
    return {}
  }
  const module = { exports: {} }
  new Function('require', 'module', 'exports', 'navigator', code)(resolve, module, module.exports, { clipboard: { writeText: copy } })
  const props = vue.reactive({ channel, organizationId: 'org-1', clientId: 'client-1', canWrite })
  const scope = vue.effectScope()
  const state = scope.run(() => module.exports.default.setup(props, { expose() {}, emit: (...event) => events.push(event) }))
  return { state, props, calls, events, cleanup() { unmount(); scope.stop() } }
}

test('Каналы используют свои API; генерация ссылки не отмечает клиента подключённым', async () => {
  for (const channel of ['telegram', 'max']) {
    const view = setup({ channel })
    await flush()
    assert.deepEqual(view.calls, [['load', channel === 'max' ? 'Max' : 'Telegram', 'org-1', 'client-1']])
    await view.state.createLink()
    assert.equal(view.state.link.value, 'https://example.test/client-link')
    assert.equal(view.state.status.value.isClientConnected, false)
    await view.state.loadStatus()
    assert.equal(view.state.link.value, 'https://example.test/client-link')
    view.cleanup()
  }
})

test('Режим чтения и неподключённый бот блокируют запись даже при прямом вызове', async () => {
  for (const options of [{ canWrite: false, load: async () => status(true) }, { load: async () => ({ ...status(), isBotConnected: false }) }]) {
    const view = setup(options)
    await flush()
    await view.state.createLink()
    await view.state.disconnect()
    assert.equal(view.calls.some(call => call[0] !== 'load'), false)
    view.cleanup()
  }
})

test('Повторное нажатие не дублирует генерацию, ошибка сохраняет возможность повтора', async () => {
  const request = deferred()
  const view = setup({ create: () => request.promise })
  await flush()
  const pending = view.state.createLink()
  await view.state.createLink()
  assert.equal(view.calls.filter(call => call[0] === 'create').length, 1)
  assert.equal(view.state.busy.value, true)
  request.reject(new Error('Нет связи'))
  await pending
  assert.equal(view.state.error.value, 'Нет связи')
  assert.equal(view.state.canChange.value, true)
  view.cleanup()
})

test('Поздний ответ создания ссылки не попадает в карточку другого клиента', async () => {
  const request = deferred()
  const view = setup({ create: () => request.promise })
  await flush()
  const pending = view.state.createLink()
  view.props.clientId = 'client-2'
  await flush()
  request.resolve({ url: 'https://example.test/old-client' })
  await pending
  assert.equal(view.state.link.value, '')
  assert.equal(view.state.saving.value, false)
  assert.deepEqual(view.calls.at(-1), ['load', 'Telegram', 'org-1', 'client-2'])
  view.cleanup()
})

test('Ошибки прежней организации и ответы после размонтирования игнорируются', async () => {
  const oldRequest = deferred()
  let loads = 0
  const view = setup({ load: () => ++loads === 1 ? oldRequest.promise : Promise.resolve(status(true)) })
  view.props.organizationId = 'org-2'
  await flush()
  oldRequest.reject(new Error('Старая ошибка'))
  await flush()
  assert.equal(view.state.error.value, '')
  assert.equal(view.state.status.value.isClientConnected, true)
  const request = deferred()
  const other = setup({ load: () => request.promise })
  other.cleanup()
  request.resolve(status(true))
  await flush()
  assert.equal(other.state.status.value, null)
  assert.deepEqual(other.events.at(-1), ['busy-changed', false])
  view.cleanup()
})

test('Отключение требует подтверждения и сохраняет доступную историю', async () => {
  const notification = { id: 1, body: 'Запись подтверждена' }
  const view = setup({ load: async () => ({ ...status(true), recentNotifications: [notification] }) })
  await flush()
  await view.state.disconnect()
  assert.equal(view.state.confirmDisconnect.value, true)
  assert.equal(view.calls.some(call => call[0] === 'disconnect'), false)
  await view.state.disconnect()
  assert.deepEqual(view.calls.at(-1), ['disconnect', 'Telegram', 'org-1', 'client-1'])
  assert.equal(view.state.status.value.isClientConnected, false)
  assert.deepEqual(view.state.status.value.recentNotifications, [notification])
  view.cleanup()
})

test('Выключенный бот не мешает отключить существующую связь клиента', async () => {
  const view = setup({ load: async () => ({ ...status(true), isBotConnected: false }) })
  await flush()
  assert.equal(view.state.canChange.value, false)
  assert.equal(view.state.canDisconnect.value, true)
  await view.state.disconnect()
  await view.state.disconnect()
  assert.equal(view.calls.filter(call => call[0] === 'disconnect').length, 1)
  assert.equal(view.state.status.value.isClientConnected, false)
  view.cleanup()
})

test('Отмена подтверждения не отключает клиента, а во время запроса модалка не закрывается', async () => {
  const request = deferred()
  const view = setup({ load: async () => status(true), disconnect: () => request.promise })
  await flush()
  await view.state.disconnect()
  view.state.closeDisconnectConfirmation()
  assert.equal(view.state.confirmDisconnect.value, false)
  assert.equal(view.calls.some(call => call[0] === 'disconnect'), false)
  await view.state.disconnect()
  const pending = view.state.disconnect()
  view.state.closeDisconnectConfirmation()
  assert.equal(view.state.confirmDisconnect.value, true)
  request.reject(new Error('Нет связи'))
  await pending
  assert.equal(view.state.confirmDisconnect.value, true)
  assert.equal(view.state.error.value, 'Нет связи')
  view.state.closeDisconnectConfirmation()
  assert.equal(view.state.confirmDisconnect.value, false)
  assert.equal(view.state.status.value.isClientConnected, true)
  view.cleanup()
})

test('Обновление статуса подтверждает подключение; копирование сообщает об успехе', async () => {
  let connected = false
  const copies = []
  const view = setup({ load: async () => status(connected), copy: async value => { copies.push(value) } })
  await flush()
  await view.state.createLink()
  await view.state.copyLink()
  assert.deepEqual(copies, ['https://example.test/client-link'])
  assert.equal(view.state.copied.value, true)
  connected = true
  await view.state.loadStatus()
  assert.equal(view.state.status.value.isClientConnected, true)
  assert.equal(view.state.link.value, '')
  view.cleanup()
})

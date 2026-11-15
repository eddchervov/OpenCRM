import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { getProjectBrand } from '../packages/ui/src/projectBrand.ts'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceNotificationChannel.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'notification-channel' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function setup(channel = 'telegram', canWrite = true, organizationId = 'org-1') {
  const calls = []
  const events = []
  const telegram = {
    loadTelegramBotSettings: async id => {
      calls.push(['loadTelegramBotSettings', id])
      return { isConfigured: true, isEnabled: false, isAvailable: true, notifyBooking: true, notifyReady: false, token: 'secret-from-server' }
    },
    saveTelegramBotSettings: async (id, payload) => {
      calls.push(['saveTelegramBotSettings', id, structuredClone(payload)])
      return { isConfigured: true, isEnabled: payload.isEnabled, isAvailable: true, notifyBooking: payload.notifyBooking, notifyReady: payload.notifyReady }
    }
  }
  const max = {
    loadMaxBotSettings: async id => {
      calls.push(['loadMaxBotSettings', id])
      return { isConfigured: true, isEnabled: true, notifyBooking: true, notifyReady: false, token: 'secret-from-server' }
    },
    saveMaxBotSettings: async (id, payload) => {
      calls.push(['saveMaxBotSettings', id, structuredClone(payload)])
      return { isConfigured: true, isEnabled: payload.isEnabled, notifyBooking: payload.notifyBooking, notifyReady: payload.notifyReady }
    }
  }
  const props = vue.reactive({ channel, canWrite, organizationId })
  const module = { exports: {} }
  let beforeUnmount
  const resolve = id => {
    if (id === 'vue') return { ...vue, onBeforeUnmount(callback) { beforeUnmount = callback } }
    if (id === '@opencrm/ui') return { UiButton: {}, UiInput: {}, getProjectBrand }
    if (id === '@opencrm/ui/projectBrand') return { getProjectBrand }
    if (id === '../api/telegramNotifications') return telegram
    if (id === '../api/maxNotifications') return max
    return {}
  }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const scope = vue.effectScope()
  const state = scope.run(() => module.exports.default.setup(props, {
    expose() {},
    emit: (...event) => events.push(event)
  }))
  return { state, props, calls, events, telegram, max, scope, unmount: () => beforeUnmount() }
}

async function flush() {
  await new Promise(resolve => setImmediate(resolve))
  await vue.nextTick()
}

function deferred() {
  let resolve
  let reject
  const promise = new Promise((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

test('Загружает оба канала по текущей организации и не заполняет поле пароля данными API', async () => {
  for (const [channel, loadName] of [['telegram', 'loadTelegramBotSettings'], ['max', 'loadMaxBotSettings']]) {
    const { state, calls, scope } = setup(channel)
    await flush()
    assert.deepEqual(calls[0], [loadName, 'org-1'])
    assert.equal(state.token.value, '')
    assert.equal(state.settings.value.isConfigured, true)
    scope.stop()
  }
})

test('Режим чтения не разрешает сохранение даже при прямом вызове обработчика', async () => {
  const { state, props, calls, scope } = setup('telegram', false)
  await flush()
  state.settings.value.isEnabled = true
  state.token.value = 'secret-from-user'
  await state.save()
  assert.equal(calls.some(([name]) => name === 'saveTelegramBotSettings'), false)
  props.canWrite = true
  scope.stop()
})

test('Сбрасывает сообщение об успехе при правке и освобождает блокировку родителя при размонтировании', async () => {
  const { state, events, scope, unmount } = setup()
  await flush()
  await state.save()
  assert.equal(state.saved.value, true)

  state.settings.value.notifyReady = true
  await vue.nextTick()
  assert.equal(state.saved.value, false)

  state.isSaving.value = true
  await vue.nextTick()
  assert.deepEqual(events.at(-1), ['busy-changed', true])
  unmount()
  assert.deepEqual(events.at(-1), ['busy-changed', false])
  scope.stop()
})

test('Ошибка сохранения сохраняет токен для повтора, успех очищает его и защищает от дубля', async () => {
  const { state, calls, telegram, scope } = setup()
  await flush()
  state.token.value = 'user-token'
  const pending = deferred()
  telegram.saveTelegramBotSettings = async (id, payload) => {
    calls.push(['saveTelegramBotSettings', id, structuredClone(payload)])
    return pending.promise
  }

  const firstSave = state.save()
  const secondSave = state.save()
  assert.equal(calls.filter(([name]) => name === 'saveTelegramBotSettings').length, 1)
  pending.reject(new Error('Временная ошибка'))
  await Promise.all([firstSave, secondSave])
  assert.equal(state.token.value, 'user-token')
  assert.equal(state.error.value, 'Временная ошибка')

  telegram.saveTelegramBotSettings = async (id, payload) => {
    calls.push(['saveTelegramBotSettings', id, structuredClone(payload)])
    return { isConfigured: true, isEnabled: payload.isEnabled, isAvailable: true, notifyBooking: payload.notifyBooking, notifyReady: payload.notifyReady }
  }
  await state.save()
  const saveCall = calls.filter(([name]) => name === 'saveTelegramBotSettings').at(-1)
  assert.equal(saveCall[2].token, 'user-token')
  assert.equal(state.token.value, '')
  assert.equal(state.saved.value, true)
  scope.stop()
})

test('Повторяет загрузку после ошибки и игнорирует ответ предыдущей организации', async () => {
  const { state, props, calls, telegram, scope } = setup()
  await flush()
  const initialCallCount = calls.filter(([name]) => name === 'loadTelegramBotSettings').length
  let shouldFail = true
  telegram.loadTelegramBotSettings = async id => {
    calls.push(['loadTelegramBotSettings', id])
    if (shouldFail) {
      shouldFail = false
      throw new Error('Ошибка загрузки')
    }

    return { isConfigured: true, isEnabled: false, isAvailable: true, notifyBooking: false, notifyReady: false }
  }
  await state.loadSettings()
  assert.equal(state.error.value, 'Ошибка загрузки')
  assert.equal(state.statusLabel.value, 'Ошибка загрузки')
  await state.loadSettings()
  assert.equal(state.settings.value.isConfigured, true)
  assert.ok(initialCallCount > 0)

  const oldResponse = deferred()
  telegram.loadTelegramBotSettings = id => {
    calls.push(['loadTelegramBotSettings', id])
    return id === 'org-3'
      ? oldResponse.promise
      : Promise.resolve({ isConfigured: false, isEnabled: false, isAvailable: true, notifyBooking: false, notifyReady: true })
  }
  props.organizationId = 'org-3'
  await vue.nextTick()
  props.organizationId = 'org-4'
  await vue.nextTick()
  await flush()
  oldResponse.resolve({ isConfigured: true, isEnabled: true, isAvailable: true, notifyBooking: true, notifyReady: false })
  await flush()
  assert.equal(state.settings.value.isConfigured, false)
  assert.equal(state.settings.value.notifyReady, true)
  assert.equal(calls.some(([name, id]) => name === 'loadTelegramBotSettings' && id === 'org-4'), true)
  scope.stop()
})

test('Смена канала игнорирует ответ предыдущего провайдера', async () => {
  const { state, props, telegram, calls, scope } = setup()
  await flush()
  const oldResponse = deferred()
  telegram.loadTelegramBotSettings = id => {
    calls.push(['loadTelegramBotSettings', id])
    return oldResponse.promise
  }
  const oldLoad = state.loadSettings()
  props.channel = 'max'
  await vue.nextTick()
  await flush()
  oldResponse.resolve({ isConfigured: true, isEnabled: true, isAvailable: true, notifyBooking: true, notifyReady: false })
  await oldLoad
  await flush()

  assert.equal(state.settings.value.isConfigured, true)
  assert.equal(state.settings.value.notifyReady, false)
  assert.equal(calls.some(([name]) => name === 'loadMaxBotSettings'), true)
  scope.stop()
})

test('Канал без сохранённого бота показывает отдельное состояние подключения', async () => {
  const { state, telegram, scope } = setup()
  telegram.loadTelegramBotSettings = async () => ({
    isConfigured: false,
    isEnabled: false,
    isAvailable: true,
    notifyBooking: false,
    notifyReady: false
  })
  await state.loadSettings()
  assert.equal(state.statusLabel.value, 'Не подключён')
  scope.stop()
})

test('Telegram с недоступным ботом не отправляет сохранение', async () => {
  const { state, calls, telegram, scope } = setup()
  telegram.loadTelegramBotSettings = async () => ({
    isConfigured: true,
    isEnabled: false,
    isAvailable: false,
    notifyBooking: false,
    notifyReady: false
  })
  await state.loadSettings()
  state.settings.value.isEnabled = true
  await state.save()
  assert.equal(calls.some(([name]) => name === 'saveTelegramBotSettings'), false)
  scope.stop()
})

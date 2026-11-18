import '../packages/workspace/src/paymentMessages.ts'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const filename = new URL('../packages/workspace/src/components/MaintenancePayments.vue', import.meta.url)
const { descriptor } = parse(readFileSync(filename, 'utf8'))
const script = compileScript(descriptor, { id: 'maintenance-payments-test' })
const code = ts.transpileModule(script.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function setup(api = {}) {
  const calls = []
  const events = []
  const props = vue.reactive({
    organizationId: 'org',
    maintenance: {
      id: 'maintenance', status: 2, canManagePayments: true, grossAmount: 1200, discountAmount: 200,
      paidAmount: 300, debtAmount: 700, buyerClientId: 'buyer', buyerName: 'Александр', buyerPartyTypeCode: 'company', buyerInn: '1234567890'
    }
  })
  const resolve = id => {
    if (id === '../paymentMessages') return {}
    if (Object.hasOwn(regionalTestDependencies, id)) return regionalTestDependencies[id]
    if (id === 'vue') return vue
    if (id === '@opencrm/ui') return {}
    if (id === '../api/maintenance') return {
      loadMaintenancePayments: async () => [],
      saveMaintenanceCommerce: async (...args) => { calls.push(args) },
      ...api.maintenance
    }
    if (id === '../api/clients') return {
      loadClientOptions: async () => [{ id: 'other', name: 'Другой клиент' }],
      ...api.clients
    }
    throw new Error('Не задана зависимость: ' + id)
  }
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const state = module.exports.default.setup(props, { emit: (...args) => events.push(args), expose() {} })
  state.dialog.value = { open: false, showModal() { this.open = true }, close() { this.open = false } }
  return { state, props, calls, events }
}

test('История оплат использует пояс организации и UTC по умолчанию у границы суток', () => {
  const { state, props } = setup()
  const timestamp = '2026-10-29T22:30:00Z'
  const format = timeZone => new Date(timestamp).toLocaleString('ru-RU', { dateStyle: 'short', timeStyle: 'short', timeZone })
  assert.equal(state.date(timestamp), format('UTC'))
  props.timeZoneId = 'Asia/Novosibirsk'
  assert.equal(state.date(timestamp), format('Asia/Novosibirsk'))
  assert.notEqual(state.date(timestamp), format('UTC'))
  props.timeZoneId = null
  assert.equal(state.date(timestamp), format('UTC'))
})

test('Повторный submit во время запроса не отправляет оплату дважды и сохраняет прежний контракт', async () => {
  let finish
  const pending = new Promise(resolve => { finish = resolve })
  const { state, calls, events } = setup({ maintenance: { saveMaintenanceCommerce: async (...args) => { calls.push(args); await pending } } })
  await state.openPayment()
  state.amount.value = '250'
  state.methodCode.value = 'card'
  const first = state.submit()
  const second = state.submit()
  assert.equal(calls.length, 1)
  assert.equal(calls[0][2], 'payments')
  const payload = calls[0][3]
  assert.equal(payload.amount, 250)
  assert.equal(payload.expectedPaidAmount, 300)
  assert.equal(payload.methodCode, 'card')
  assert.ok(payload.id)
  finish()
  await Promise.all([first, second])
  assert.equal(events.length, 1)
})

test('Сумма больше остатка отклоняется до запроса и оставляет оплату открытой', async () => {
  const { state, calls } = setup()
  await state.openPayment()
  state.amount.value = '700.01'
  state.methodCode.value = 'cash'
  await state.submit()
  assert.equal(calls.length, 0)
  assert.match(state.error.value, /не больше остатка/)
  assert.equal(state.dialog.value.open, true)
})

test('При отправке нельзя переключить модалку; после ошибки видна ошибка и повтор использует тот же запрос', async () => {
  let failFirst
  let finishRetry
  const firstAttempt = new Promise((_, reject) => { failFirst = reject })
  const retryAttempt = new Promise(resolve => { finishRetry = resolve })
  const { state, calls } = setup({ maintenance: {
    saveMaintenanceCommerce: async (...args) => {
      calls.push(args)
      if (calls.length === 1) await firstAttempt
      else await retryAttempt
    }
  } })
  await state.openPayment()
  state.amount.value = '250'
  state.methodCode.value = 'card'
  const firstSubmit = state.submit()
  await state.history()
  state.cancel({ id: 'payment', amount: 100 })
  assert.equal(state.mode.value, 'payment')
  assert.equal(state.busy.value, true)
  assert.equal(calls.length, 1)

  failFirst(new Error('Временная ошибка'))
  await firstSubmit
  assert.equal(state.mode.value, 'payment')
  assert.match(state.error.value, /Временная ошибка/)
  assert.equal(state.busy.value, false)

  const retry = state.submit()
  assert.equal(calls.length, 2)
  assert.equal(calls[0][3].id, calls[1][3].id)
  finishRetry()
  await retry
})

test('Устаревшие ответы клиентов и истории не перезаписывают данные после закрытия окна', async () => {
  let finishClients
  let finishPayments
  const pendingClients = new Promise(resolve => { finishClients = resolve })
  const pendingPayments = new Promise(resolve => { finishPayments = resolve })
  const { state } = setup({
    clients: { loadClientOptions: () => pendingClients },
    maintenance: { loadMaintenancePayments: () => pendingPayments }
  })
  const opening = state.openPayment()
  state.close()
  finishClients([{ id: 'stale', name: 'Устаревший клиент' }])
  finishPayments([{ id: 'stale', amount: 1, isOpeningBalance: false, createdAt: '' }])
  await opening
  await Promise.resolve()
  assert.deepEqual(state.clients.value, [])
  assert.deepEqual(state.payments.value, [])
  assert.equal(state.dialog.value.open, false)
})

test('Отмена учёта сохраняет обязательную причину и не меняет трактовку возврата денег', async () => {
  const { state, calls } = setup()
  const payment = { id: 'payment', amount: 100, isOpeningBalance: true }
  state.cancel(payment)
  await state.submit()
  assert.equal(calls.length, 0)
  assert.match(state.error.value, /причину/)
  state.reason.value = 'Дубль'
  await state.submit()
  assert.equal(calls[0][2], 'payments/payment/cancel')
  assert.deepEqual(calls[0][3], { reason: 'Дубль' })
  assert.match(descriptor.template.content, /payments\.cancelNotice/)
  assert.match(regionalTestDependencies['@opencrm/ui/i18n'].translate('payments.cancelNotice', { amount: '100 RUB' }), /деньги клиенту автоматически не возвращаются/)
  assert.match(descriptor.template.content, /payments\.openingHistory/)
  assert.match(regionalTestDependencies['@opencrm/ui/i18n'].translate('payments.openingHistory'), /Ранее учтено · дата получения неизвестна/)
})

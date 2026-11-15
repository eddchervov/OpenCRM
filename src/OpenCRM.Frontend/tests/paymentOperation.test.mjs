import assert from 'node:assert/strict'
import test from 'node:test'
import { getOrCreatePaymentOperation, paymentOperationStorageKey, finishPaymentOperation, acquirePaymentOperation, pollPaymentPurchase, submitPaymentOperation } from '../packages/workspace/src/paymentOperation.ts'
import { purchaseWorkspaceBusinessPlan, extendWorkspaceBusinessPlan, activateWorkspaceDemoPlan, WorkspaceActionError } from '../packages/workspace/src/api/workspaceAdmin.ts'

const scope = { userId: 'user', organizationId: 'org', kind: 'purchase', planId: 1 }
const id = 'af342e75-10ba-4609-b746-8619fd206fdb'
function storage() {
  const data = new Map()
  return { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key), data }
}

test('Повтор, перезагрузка и неопределённый исход сохраняют один ключ без сведений покупателя', () => {
  const saved = storage()
  assert.equal(getOrCreatePaymentOperation(saved, scope, () => id), id)
  for (const code of [undefined, 'PaymentRequired', 'InProgress', 'Conflict', 'ManualReview', 'Invalid']) {
    finishPaymentOperation(saved, scope, id, code)
    assert.equal(getOrCreatePaymentOperation(saved, scope, () => { throw new Error('Новый ключ недопустим') }), id)
  }
  assert.deepEqual([...saved.data], [[paymentOperationStorageKey(scope), id]])
})

test('Только окончательный исход освобождает ключ; чужой новый ключ не удаляется', () => {
  for (const code of ['Completed', 'Canceled', 'Rejected', 'Refunded']) {
    const saved = storage()
    getOrCreatePaymentOperation(saved, scope, () => id)
    finishPaymentOperation(saved, scope, 'other', code)
    assert.equal(saved.getItem(paymentOperationStorageKey(scope)), id)
    finishPaymentOperation(saved, scope, id, code)
    assert.equal(saved.data.size, 0)
  }
})

test('Пользователь, организация, вид команды и тариф имеют раздельные ключи', () => {
  const scopes = [scope, { ...scope, userId: 'other' }, { ...scope, organizationId: 'other' }, { ...scope, kind: 'extend' }, { ...scope, planId: 2 }]
  assert.equal(new Set(scopes.map(paymentOperationStorageKey)).size, 5)
})

test('Недоступное или повреждённое хранилище не разрешает незаметно начать другую оплату', () => {
  const saved = storage()
  saved.setItem(paymentOperationStorageKey(scope), 'broken')
  assert.throws(() => getOrCreatePaymentOperation(saved, scope, () => id))
  assert.throws(() => getOrCreatePaymentOperation({ getItem: () => null, setItem: () => { throw new Error('Blocked') } }, scope, () => id))
  assert.throws(() => getOrCreatePaymentOperation({ getItem: () => null, setItem: () => {} }, scope, () => id))
})

test('Две вкладки получают один сохранённый ключ под блокировкой браузера', async () => {
  const saved = storage()
  globalThis.window = { localStorage: saved }
  let queue = Promise.resolve()
  Object.defineProperty(globalThis, 'navigator', { configurable: true, writable: true, value: { locks: { request: (key, callback) => {
    const result = queue.then(callback)
    queue = result.catch(() => {})
    return result
  } } } })
  const [first, second] = await Promise.all([acquirePaymentOperation(scope), acquirePaymentOperation(scope)])
  assert.equal(first.operationId, second.operationId)
  assert.equal(first.wasExisting, false)
  assert.equal(second.wasExisting, true)
  globalThis.navigator = {}
  await assert.rejects(acquirePaymentOperation(scope))
})

test('HTTP сохраняет прежнее тело покупателя, заголовок отправляется только для оплаты; временное состояние отклоняется', async () => {
  globalThis.document = { querySelector: () => ({ content: 'antiforgery' }) }
  const calls = []
  globalThis.fetch = async (url, request) => {
    calls.push({ url, ...request })
    return { ok: true, json: async () => ({ code: 'PaymentRequired', paymentUrl: 'https://pay.example' }) }
  }
  const buyer = { typeCode: 'business', name: 'Компания', inn: '1234567890' }
  await purchaseWorkspaceBusinessPlan('org', 1, buyer, id)
  await extendWorkspaceBusinessPlan('org', buyer, id)
  await activateWorkspaceDemoPlan('org', 2)
  assert.equal(calls[0].headers['Idempotency-Key'], id)
  assert.equal(calls[1].headers['Idempotency-Key'], id)
  assert.equal(calls[2].headers['Idempotency-Key'], undefined)
  assert.equal(calls[0].headers.RequestVerificationToken, 'antiforgery')
  assert.deepEqual(JSON.parse(calls[0].body), buyer)
  globalThis.fetch = async () => ({ ok: false, json: async () => ({ code: 'InProgress', message: 'Оплата обрабатывается' }) })
  await assert.rejects(extendWorkspaceBusinessPlan('org', buyer, id), error => error instanceof WorkspaceActionError && error.code === 'InProgress')
  globalThis.fetch = async () => { throw new TypeError('Lost response') }
  await assert.rejects(extendWorkspaceBusinessPlan('org', buyer, id))
})

test('Возврат после оплаты не выдаёт старое Completed за новую покупку и не запускает её автоматически', async () => {
  const saved = storage()
  globalThis.window = { localStorage: saved }
  globalThis.navigator = { locks: { request: (key, callback) => callback() } }
  saved.setItem(paymentOperationStorageKey(scope), id)
  let calls = 0
  const send = async operationId => {
    calls++
    return operationId === id ? { code: 'Completed' } : { code: 'PaymentRequired', paymentUrl: 'https://pay.example/new' }
  }
  await assert.rejects(submitPaymentOperation(scope, send), error => error instanceof Error && error.message.includes('уже обработана'))
  assert.equal(calls, 1)
  assert.equal(saved.data.size, 0)
  const next = await submitPaymentOperation(scope, send)
  assert.equal(next.code, 'PaymentRequired')
  assert.equal(calls, 2)
  assert.notEqual(saved.getItem(paymentOperationStorageKey(scope)), id)
})

test('Мгновенное завершение новой оплаты считается успехом без предложения заплатить ещё раз', async () => {
  const saved = storage()
  globalThis.window = { localStorage: saved }
  globalThis.navigator = { locks: { request: (key, callback) => callback() } }
  let calls = 0
  const result = await submitPaymentOperation(scope, async () => {
    calls++
    return { code: 'Completed' }
  })
  assert.equal(result.code, 'Completed')
  assert.equal(calls, 1)
  assert.equal(saved.data.size, 0)
})

test('Асинхронная покупка опрашивает точный id и не создаёт покупку повторно после сетевого сбоя', async () => {
  const saved = storage()
  globalThis.window = { localStorage: saved }
  globalThis.navigator = { locks: { request: (key, callback) => callback() } }
  const savedOperationId = getOrCreatePaymentOperation(saved, scope, () => id)
  let createCalls = 0
  let statusCalls = 0
  const send = async operationId => {
    createCalls++
    assert.equal(operationId, savedOperationId)
    return { code: 'InProgress', purchaseId: 'purchase-1' }
  }
  await assert.rejects(submitPaymentOperation(scope, send, {
    delaysMs: [0, 0],
    loadPurchase: async purchaseId => {
      assert.equal(purchaseId, 'purchase-1')
      statusCalls++
      throw new TypeError('Lost status response')
    }
  }))
  assert.equal(createCalls, 1)
  assert.equal(saved.getItem(paymentOperationStorageKey(scope)), id)

  const result = await submitPaymentOperation(scope, send, {
    delaysMs: [0],
    loadPurchase: async purchaseId => {
      assert.equal(purchaseId, 'purchase-1')
      statusCalls++
      return { code: 'Completed' }
    }
  })
  assert.equal(result.code, 'Completed')
  assert.equal(createCalls, 1)
  assert.equal(statusCalls, 4)
  assert.equal(saved.data.size, 0)
})

test('Ограниченный опрос не выдаёт получение оплаты за активацию', async () => {
  let calls = 0
  const result = await pollPaymentPurchase('purchase-2', async purchaseId => {
    assert.equal(purchaseId, 'purchase-2')
    calls++
    return { code: 'InProgress', isPaid: true }
  }, { delaysMs: [0] })

  assert.equal(calls, 2)
  assert.equal(result.code, 'InProgress')
  assert.equal(result.message, 'Оплата получена, активируем подписку.')
})

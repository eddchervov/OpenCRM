import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const source = readFileSync(new URL('../Admin/admin-client/src/paymentServiceActions.ts', import.meta.url), 'utf8')
const code = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
const module = { exports: {} }
new Function('module', 'exports', code)(module, module.exports)
const { parseRefundAmount, readPaymentActionDraft, getPaymentActionStatus } = module.exports

test('Возврат принимает рубли с запятой или точкой и весь доступный остаток', () => {
  assert.equal(parseRefundAmount('150,25', 420), 150.25)
  assert.equal(parseRefundAmount('150.25', 420), 150.25)
  assert.equal(parseRefundAmount('420', 420), 420)
  assert.equal(parseRefundAmount(' 0,01 ', 0.01), 0.01)
})

test('Возврат не округляет лишние копейки и не пропускает отрицательную, нулевую или превышенную сумму', () => {
  for (const invalid of ['0', '-1', '420,01', '1.999', '1e2', 'Infinity', 'NaN', '', '1 00', '10руб', '1,2,3']) {
    assert.equal(parseRefundAmount(invalid, 420), null, invalid)
  }
  assert.equal(parseRefundAmount('1', NaN), null)
})

test('Сохранённый запрос после потери ответа сохраняет исходные ID, тело и снимок', () => {
  const draft = { operationId: '34fd12bd-a265-4759-8f91-13c73f70fcfc', action: 'refund', amount: 210.55, reason: 'Возврат по обращению', stateToken: 'old-snapshot' }
  const restored = readPaymentActionDraft(JSON.stringify(draft))
  assert.deepEqual(restored, draft)
  assert.equal(JSON.stringify(restored), JSON.stringify(draft))
})

test('Повреждённый запрос не превращается в новую финансовую операцию', () => {
  const draft = { operationId: '34fd12bd-a265-4759-8f91-13c73f70fcfc', action: 'refund', amount: 10, reason: 'Причина', stateToken: 'snapshot' }
  for (const overrides of [{ operationId: '' }, { action: 'mark-paid' }, { action: '__proto__' }, { amount: -1 }, { amount: null }, { reason: '' }, { stateToken: '' }]) {
    assert.throws(() => readPaymentActionDraft(JSON.stringify({ ...draft, ...overrides })))
  }
  assert.throws(() => readPaymentActionDraft('{broken'))
  assert.equal(readPaymentActionDraft(null), null)
})

test('Статус запрошенной проверки или неизвестный статус не выдаётся за успешную операцию', () => {
  assert.equal(getPaymentActionStatus('requested'), 'Запрошено')
  assert.equal(getPaymentActionStatus('queued'), 'В очереди')
  assert.equal(getPaymentActionStatus('new-provider-state'), 'Результат уточняется')
  assert.equal(getPaymentActionStatus('succeeded'), 'Выполнено')
})

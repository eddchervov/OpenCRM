import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { formattingCulture, translate } from '@opencrm/ui/i18n'
import '../Admin/admin-client/src/adminBillingMessages.ts'

const source = readFileSync(new URL('../Admin/admin-client/src/paymentDetails.ts', import.meta.url), 'utf8')
const code = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText
const module = { exports: {} }
new Function('require', 'module', 'exports', code)(id => id === '@opencrm/ui/i18n' ? { formattingCulture } : {}, module, module.exports)
const details = module.exports
const localize = (key, params) => translate(key, params)
const formatMoney = amount => new Intl.NumberFormat('ru-RU', { style: 'currency', currency: 'RUB', maximumFractionDigits: 2 }).format(amount)
const formatPaymentEvent = (event) => details.formatPaymentEvent(event, localize)
const getPaymentAccessStatus = payment => details.getPaymentAccessStatus(payment, localize)
const getPaymentMoneyStatus = payment => details.getPaymentMoneyStatus(payment, localize)
const getPaymentMoneySummary = details.getPaymentMoneySummary
const getPaymentNotice = payment => details.getPaymentNotice(payment, localize, formatMoney)

function payment(overrides = {}) {
  return {
    id: 18,
    userId: 'client-1',
    user: 'Алексей',
    userEmail: 'contact@example.invalid',
    userPhone: null,
    planName: 'PRO на месяц',
    planTypeCode: 'pro',
    amount: 990,
    originalAmount: null,
    promoDiscountAmount: null,
    promoCodeSnapshot: null,
    createdAt: '2026-10-25T10:00:00Z',
    statusUpdatedAt: null,
    paymentMethodName: 'Банковская карта',
    paymentStatus: 'pending',
    statusGroup: 'pending',
    statusName: 'Ожидание',
    transactionId: null,
    externalPaymentId: 'yk-123',
    paymentLink: null,
    hasExternalId: true,
    ...overrides,
  }
}

test('Полный возврат остаётся возвратом, даже если последняя ошибка указывает на несовпадение события', () => {
  const result = payment({
    paidAt: '2026-10-25T10:01:00Z',
    providerStatus: 'Succeeded',
    refundedAmount: 990,
    lastErrorCode: 'operation_event_mismatch',
    applicationStatus: 'needs_attention',
  })
  const notice = getPaymentNotice(result)
  const money = getPaymentMoneySummary(result)

  assert.equal(money.refundKind, 'full')
  assert.equal(money.remaining, 0)
  assert.equal(notice.title, 'Деньги возвращены полностью')
  assert.match(notice.followUp, /Сверьте сумму возврата с платёжным сервисом/)
  assert.match(notice.message, /Доступ по этой покупке не выдавался/)
})

test('Подтверждённая оплата без выдачи доступа не предлагает повторно платить или выдавать доступ вручную', () => {
  const notice = getPaymentNotice(payment({ paidAt: '2026-10-25T10:01:00Z', applicationStatus: 'activation_pending' }))

  assert.equal(notice.title, 'Деньги получены, доступ ещё не выдан')
  assert.match(notice.followUp, /Не создавайте повторную оплату/)
  assert.match(notice.followUp, /не продлевайте доступ вручную/)
})

test('Ожидающий платёж не считается полученными деньгами', () => {
  const result = payment()

  assert.equal(getPaymentMoneySummary(result).confirmed, false)
  assert.equal(getPaymentMoneySummary(result).remaining, null)
  assert.equal(getPaymentNotice(result).title, 'Ожидается подтверждение оплаты')
})

test('Выданный период по покупке не утверждает, что доступ действует сейчас', () => {
  const result = payment({
    paidAt: '2026-10-25T10:01:00Z',
    appliedAt: '2026-10-25T10:02:00Z',
    applicationStatus: 'applied',
    grantedFrom: '2026-10-25T00:00:00Z',
    grantedUntil: '2026-11-25T00:00:00Z',
  })

  assert.equal(getPaymentAccessStatus(result), 'Период по покупке выдан')
  assert.match(getPaymentNotice(result).message, /не подтверждает, что доступ действует сейчас/)
})

test('Конфликт без возврата требует проверки и не отмечается как успешно оплаченный', () => {
  const result = payment({
    providerStatus: 'Succeeded',
    applicationStatus: 'needs_attention',
    refundedAmount: 0,
  })
  const notice = getPaymentNotice(result)

  assert.equal(notice.title, 'Платёж требует проверки')
  assert.equal(notice.tone, 'warning')
  assert.match(notice.followUp, /Не просите клиента платить повторно/)
})

test('Подтверждённая оплата с NeedsAttention остаётся подтверждённой, а проверка относится к выдаче доступа', () => {
  const result = payment({
    providerStatus: 'Succeeded',
    applicationStatus: 'needs_attention',
  })
  const notice = getPaymentNotice(result)

  assert.equal(getPaymentMoneySummary(result).confirmed, true)
  assert.equal(getPaymentMoneyStatus(result), 'Оплата подтверждена')
  assert.equal(notice.title, 'Платёж требует проверки')
  assert.match(notice.message, /Платёжная система подтвердила оплату/)
})

test('Полный возврат до выдачи не утверждает, что период доступа выдан или его нужно останавливать', () => {
  const notice = getPaymentNotice(payment({
    providerStatus: 'Succeeded',
    refundedAmount: 990,
    applicationStatus: 'activation_pending',
  }))

  assert.match(notice.message, /Доступ по этой покупке не выдавался/)
  assert.doesNotMatch(notice.followUp, /оставить ли доступ|изменить его вручную/)
})

test('Частичный возврат до выдачи предлагает вручную разобрать условия покупки', () => {
  const notice = getPaymentNotice(payment({
    providerStatus: 'Succeeded',
    refundedAmount: 250,
    applicationStatus: 'activation_pending',
  }))

  assert.match(notice.message, /Выдача доступа не подтверждена/)
  assert.match(notice.followUp, /вручную разберите условия покупки/)
})

test('Состояние applied в verified snapshot не выдаёт ложное событие о подписке', () => {
  const event = { stage: 'state', code: 'verified_snapshot_applied', refundedAmountMinor: 0, amountMinor: 99000, currency: 'RUB' }

  assert.equal(formatPaymentEvent(event).title, 'Состояние платежа проверено')
  assert.notEqual(formatPaymentEvent(event).title, 'Подписка выдана')
})

test('Ошибка или отмена возврата не показывается как успешный возврат', () => {
  for (const code of ['refund_failed', 'refund_canceled']) {
    const event = { stage: 'refund', code, refundedAmountMinor: 0, amountMinor: 99000, currency: 'RUB' }
    assert.equal(formatPaymentEvent(event).title, 'Возврат не подтверждён')
  }
})
test('Полный возврат до выдачи исключает ожидание выдачи доступа', () => {
  const result = payment({ providerStatus: 'Succeeded', paidAt: '2026-10-25T10:01:00Z', refundedAmount: 990, applicationStatus: 'refunded', appliedAt: null })
  assert.equal(getPaymentAccessStatus(result), 'Не выдан: средства возвращены')
})

test('Выданный ранее период остаётся историческим фактом при возврате с требованием проверки', () => {
  const result = payment({ refundedAmount: 990, applicationStatus: 'needs_attention', appliedAt: '2026-10-25T10:02:00Z' })
  assert.equal(getPaymentAccessStatus(result), 'Период по покупке выдан')
})

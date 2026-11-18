import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { i18n, translate, formattingCulture, setFormattingCulture } from '@opencrm/ui/i18n'
import '../Admin/admin-client/src/adminBillingMessages.ts'

const billingFiles = [
  '../Admin/admin-client/src/views/BillingView.vue',
  '../Admin/admin-client/src/components/PaymentServiceDetails.vue',
  '../Admin/admin-client/src/components/PaymentServiceActions.vue',
  '../Admin/admin-client/src/paymentDetails.ts'
]
const billingSource = billingFiles.map(path => readFileSync(new URL(path, import.meta.url), 'utf8')).join('\n')

function sortedKeys(value) {
  return Object.fromEntries(Object.entries(value).sort(([left], [right]) => left.localeCompare(right)).map(([key, child]) => [
    key,
    child && typeof child === 'object' ? sortedKeys(child) : true
  ]))
}

test('Платежи и подписки имеют полные словари RU/EN/zh-Hans', () => {
  const russian = i18n.global.getLocaleMessage('ru').adminBilling
  for (const locale of ['ru', 'en', 'zh-Hans']) {
    assert.deepEqual(sortedKeys(i18n.global.getLocaleMessage(locale).adminBilling), sortedKeys(russian), `${locale} has full coverage`)
  }

  for (const key of new Set([...billingSource.matchAll(/t\(['"](adminBilling(?:\.[A-Za-z0-9]+){2,})['"]/g)].map(match => match[1]))) {
    assert.ok(translate(key), `${key} resolves`)
  }
})

test('Перевод форматирует текст, не меняя валюту операций и состояния платёжных действий', () => {
  const previousLocale = i18n.global.locale.value
  const previousCulture = formattingCulture.value
  try {
    i18n.global.locale.value = 'en'
    assert.equal(translate('adminBilling.view.purchaseNumber', { id: 42 }), 'Purchase #42')
    assert.match(translate('adminBilling.actions.pendingOperation', { action: 'Refund', amount: ' · RUB 12.00' }), /Do not create a new operation yet/)
    i18n.global.locale.value = 'zh-Hans'
    assert.match(translate('adminBilling.view.apiRequestFailed', { status: 503 }), /503/)
  } finally {
    i18n.global.locale.value = previousLocale
    setFormattingCulture(previousCulture)
  }

  assert.match(billingSource, /formattingCulture\.value/)
  assert.match(billingSource, /currency: 'RUB'/)
  assert.match(billingSource, /pendingDraft\.value/)
  assert.match(billingSource, /!canSubmit/)
  assert.match(billingSource, /sessionStorage\.setItem\(draftKey\.value, JSON\.stringify\(draft\)\)/)
})

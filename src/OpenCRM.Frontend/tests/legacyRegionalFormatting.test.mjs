import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import vm from 'node:vm'
import { legacyClientText } from './legacyClientTestMessages.mjs'

const source = readFileSync(new URL('../../OpenCRM.WebApp/wwwroot/app/regional-formatting.js', import.meta.url), 'utf8')

function initialize(fetch, metadata = {}, now) {
  const window = { OpenCRMText: legacyClientText('en') }
  vm.runInNewContext(source, {
    window, fetch, Intl,
    Date: now ? class extends Date { constructor(...args) { super(...(args.length ? args : [now])) } } : Date,
    document: {
      documentElement: { lang: 'en-US' },
      querySelector: selector => ({ content: metadata[selector] ?? '' })
    }
  })
  return window.OpenCRMRegional
}

test('Старые страницы форматируют даты по выбранной культуре и переданному поясу', () => {
  const english = initialize(() => { throw new Error('Unexpected fetch') }, {
    'meta[name="opencrm-formatting-culture"]': 'en-GB'
  })
  assert.equal(english.formatDate('2026-11-16T09:30:00Z', { timeZone: 'UTC' }), '16/11/2026')
  assert.match(english.formatDate('2026-11-16T09:30:00Z', {
    timeZone: 'Asia/Shanghai', dateStyle: 'short', timeStyle: 'short'
  }), /16\/11\/2026, 17:30/)
  const chinese = initialize(() => { throw new Error('Unexpected fetch') }, {
    'meta[name="opencrm-formatting-culture"]': 'zh-CN'
  })
  assert.equal(chinese.formatDate('2026-11-16T09:30:00Z', { timeZone: 'UTC' }), '2026/11/16')
  assert.equal(chinese.formatDate('invalid'), '—')
})

test('Пояс организации меняет момент события, а календарные даты и часы остаются прежними', () => {
  const regional = initialize(() => { throw new Error('Unexpected fetch') }, {
    'meta[name="opencrm-formatting-culture"]': 'en-GB',
    'meta[name="opencrm-time-zone"]': 'America/New_York'
  })
  assert.equal(regional.formatDate('2026-11-16'), '16/11/2026')
  assert.equal(regional.formatDate('2026-11-16T00:00:00'), '16/11/2026')
  assert.equal(regional.formatDate('2026-11-16T00:00:00Z'), '15/11/2026')
  assert.equal(regional.formatDate('2026-11-16T00:30:00', { timeStyle: 'short' }), '00:30')
  assert.equal(regional.formatDate('2026-11-16T00:30:00Z', { timeStyle: 'short' }), '19:30')
  assert.equal(regional.formatDate(null), '—')
})

test('Сегодня определяется в зоне организации независимо от языка и границы суток устройства', () => {
  const fetch = () => { throw new Error('Unexpected fetch') }
  const metadata = { 'meta[name="opencrm-formatting-culture"]': 'zh-CN', 'meta[name="opencrm-time-zone"]': 'Asia/Shanghai' }
  assert.equal(initialize(fetch, metadata, '2026-11-15T16:30:00Z').today(), '2026-11-16')
  assert.equal(initialize(fetch, { ...metadata, 'meta[name="opencrm-time-zone"]': 'America/Los_Angeles' }, '2026-11-15T16:30:00Z').today(), '2026-11-15')
})

test('Старая форма получает валюту организации один раз и сохраняет валюту заказа', async () => {
  const requests = []
  const regional = initialize(async url => {
    requests.push(url)
    return { ok: true, json: async () => ({ currencyCode: 'CNY' }) }
  }, { 'meta[name="opencrm-formatting-culture"]': 'en-US', 'meta[name="opencrm-path-base"]': '/crm/' })
  const first = { organizationId: 'example' }
  const second = { vehicle: { organizationId: 'example' } }
  await Promise.all([regional.initializeLegacyVm(first), regional.initializeLegacyVm(second)])
  assert.equal(first.regionalCurrencyCode, 'CNY')
  assert.equal(second.regionalCurrencyCode, 'CNY')
  assert.deepEqual(requests, ['/crm/api/v1/organizations/example/currency'])
  assert.equal(regional.formatMoney(10, 'RUB'), new Intl.NumberFormat('en-US', {
    style: 'currency', currency: 'RUB', minimumFractionDigits: 0, maximumFractionDigits: 2
  }).format(10))
  assert.equal(regional.formatMoney(10, null), '—')
})

test('Ошибка валюты не подменяется рублями, а повторный запрос может восстановиться', async () => {
  let attempts = 0
  const regional = initialize(async () => {
    attempts++
    return { ok: attempts > 1, json: async () => ({ currencyCode: 'USD' }) }
  })
  const form = { organizationId: 'example', regionalCurrencyCode: null }
  await regional.initializeLegacyVm(form)
  assert.equal(form.regionalCurrencyCode, null)
  assert.match(form.errorMessage, /Could not load/)
  await regional.initializeLegacyVm(form)
  assert.equal(form.regionalCurrencyCode, 'USD')
  assert.equal(attempts, 2)
})

test('Личная форма использует личную валюту независимо от языка', async () => {
  const regional = initialize(() => { throw new Error('Unexpected fetch') }, {
    'meta[name="opencrm-personal-currency"]': 'EUR'
  })
  const form = {}
  await regional.initializeLegacyVm(form)
  assert.equal(form.regionalCurrencyCode, 'EUR')
})

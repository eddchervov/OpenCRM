import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '../packages/workspace/src/maintenanceCardMessages.ts'
import '../packages/workspace/src/workdayPreviewMessages.ts'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { parseDateOnly } from '../packages/ui/src/dateOnly.ts'
import { formattingCulture, i18n } from '@opencrm/ui/i18n'

const filename = new URL('../packages/workspace/src/components/WorkspaceWorkdayPreview.vue', import.meta.url)
const { descriptor } = parse(readFileSync(filename, 'utf8'))
const script = compileScript(descriptor, { id: 'workday-preview-test' })
const code = ts.transpileModule(script.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function preview(api = {}, organizationCurrencyCode = 'RUB') {
  const hooks = {}
  const module = { exports: {} }
  const document = { body: { style: { overflow: 'auto' } } }
  const resolve = id => {
    if (Object.hasOwn(regionalTestDependencies, id)) return regionalTestDependencies[id]
    if (id === 'vue') return {
      ...vue,
      onMounted: hook => { hooks.mount = hook },
      onBeforeUnmount: hook => { hooks.unmount = hook }
    }
    if (id === '@opencrm/ui') return { parseDateOnly }
    if (id === '../maintenanceCardMessages') return {}
    if (id === '../workdayPreviewMessages') return {}
    if (id === '../utils/vehicleBrandLogo') return { getVehicleBrandLogo: () => null }
    if (id === '../api/maintenance') return api
    throw new Error('Не задана зависимость: ' + id)
  }
  new Function('require', 'module', 'exports', 'document', code)(resolve, module, module.exports, document)
  const props = {
    context: { organization: { id: 'org-1', currencyCode: organizationCurrencyCode } },
    item: { id: 'order-1', workTotalCount: 2, workCompletedCount: 1 }
  }
  const events = []
  const state = module.exports.default.setup(props, { expose() {}, emit: (...args) => events.push(args) })
  return { state, hooks, document, events }
}

function deferred() {
  let resolve
  const promise = new Promise(complete => { resolve = complete })
  return { promise, resolve }
}

test('Предпросмотр отличает услуги от материалов и считает итог с учётом скидки', async () => {
  const { state } = preview({ loadMaintenance: async () => ({
    grossAmount: 2000, discountAmount: 100, paidAmount: 500, debtAmount: 1400,
    maintenanceDetails: [
      { name: 'Полировка', quantity: 2, price: 2000 },
      { name: 'Материал', materialName: 'Химия', materialQuantity: 1 }
    ]
  }) })
  await state.load()
  assert.equal(state.services.value.length, 1)
  assert.equal(state.services.value[0].price, 2000)
  assert.equal(state.totalAmount.value, 1900)
  assert.equal(state.balanceAmount.value, 1400)
})

test('Переплата отображается отдельно от долга с учётом скидки', async () => {
  const { state } = preview({ loadMaintenance: async () => ({
    grossAmount: 5000, discountAmount: 1000, paidAmount: 4500,
    debtAmount: 0, paymentStatus: 'overpaid', maintenanceDetails: []
  }) })
  await state.load()
  assert.equal(state.balanceLabel.value, 'Переплата')
  assert.equal(state.balanceAmount.value, 500)
})

test('Предпросмотр сохраняет валюту заказа и предупреждает при отличии валюты организации', async () => {
  const { state } = preview({ loadMaintenance: async () => ({
    currencyCode: 'RUB', grossAmount: 2000, discountAmount: 100, paidAmount: 500,
    debtAmount: 1400, maintenanceDetails: []
  }) }, 'CNY')
  await state.load()

  assert.equal(state.workspaceCurrency.value, 'RUB')
  assert.equal(state.isHistoricalCurrencyReadOnly.value, true)
  assert.match(state.historicalCurrencyReadOnlyMessage.value, /RUB.*CNY/)
})

test('Ответ закрытого окна не меняет данные и восстанавливает прокрутку страницы', async () => {
  const pending = deferred()
  const { state, hooks, document } = preview({ loadMaintenance: () => pending.promise, loadMaintenancePhotos: async () => [] })
  hooks.mount()
  assert.equal(document.body.style.overflow, 'hidden')
  hooks.unmount()
  assert.equal(document.body.style.overflow, 'auto')
  pending.resolve({ id: 'stale' })
  await new Promise(resolve => setImmediate(resolve))
  assert.equal(state.maintenance.value, null)
})

test('Ошибка фото не скрывает заказ и доступен повтор загрузки заказа', async () => {
  let attempts = 0
  const { state } = preview({
    loadMaintenance: async () => {
      if (++attempts === 1) throw new Error('Нет доступа')
      return { id: 'order-1', maintenanceDetails: [] }
    },
    loadMaintenancePhotos: async () => { throw new Error('Фото недоступны') }
  })
  await state.load()
  assert.equal(state.error.value, 'Нет доступа')
  assert.equal(state.isLoading.value, false)
  await state.load()
  await state.loadPhotos()
  assert.equal(state.error.value, '')
  assert.equal(state.maintenance.value.id, 'order-1')
  assert.ok(state.photoError.value)
})

test('Предпросмотр переводит статусы и использует выбранный формат даты', () => {
  const originalLocale = i18n.global.locale.value
  const originalCulture = formattingCulture.value
  try {
    for (const [locale, culture, expected] of [['en', 'en-GB', 'Planned'], ['zh-Hans', 'zh-CN', '已计划']]) {
      i18n.global.locale.value = locale
      formattingCulture.value = culture
      const { state } = preview()
      assert.equal(state.status.value, expected)
      assert.equal(state.formatDate('2026-11-18'), parseDateOnly('2026-11-18').toLocaleDateString(culture, {
        year: 'numeric', month: '2-digit', day: '2-digit'
      }))
    }
  } finally {
    i18n.global.locale.value = originalLocale
    formattingCulture.value = originalCulture
  }
})

test('Клик внутри окна не закрывает его, клик за границами закрывает', () => {
  const { state, events } = preview()
  const target = { getBoundingClientRect: () => ({ left: 100, right: 800, top: 100, bottom: 600 }) }
  state.dialog.value = target
  state.closeFromBackdrop({ target: state.dialog.value, clientX: 200, clientY: 200 })
  assert.equal(events.length, 0)
  state.closeFromBackdrop({ target: state.dialog.value, clientX: 50, clientY: 200 })
  assert.deepEqual(events, [['close']])
})

import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/MaintenanceJournalSection.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'journal-test' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function setup(load, initialState = null) {
  const storage = new Map(initialState ? [['opencrm:user:org:maintenance-journal:v1', JSON.stringify(initialState)]] : [])
  const module = { exports: {} }
  const dateOnly = date => (date ?? new Date()).toISOString().slice(0, 10)
  const resolve = id => {
    if (id === 'vue') return { ...vue, onMounted() {}, onBeforeUnmount() {}, watch() {} }
    if (id === '@opencrm/ui') return { formatDateOnly: dateOnly, parseDateOnly: value => /^\d{4}-\d{2}-\d{2}$/.test(value) ? new Date(value+'T00:00:00') : null }
    if (id === '../api/maintenanceJournal') return { loadMaintenanceJournal: load }
    return {}
  }
  const window = { clearTimeout() {}, setTimeout: () => 1, sessionStorage: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) } }
  new Function('require', 'module', 'exports', 'window', code)(resolve, module, module.exports, window)
  const props = vue.reactive({ context: { user: { id: 'user' }, organization: { id: 'org' }, shell: { scenario: {code:'detailing'}, quickActions:[] }, workspaceAccess: {isReadOnly:false} } })
  const state = module.exports.default.setup(props, { expose() {} })
  return { state, props, storage }
}
const result = items => ({ items, totalCount: items.length, page: 1, pageSize: 25, performers: [] })

test('Журнал передаёт фильтры исполнителя и выдачи до серверной пагинации', async () => {
  const calls = []
  const { state } = setup(async (...args) => { calls.push(args); return result([]) })
  state.status.value = 'ready'
  state.employeeId.value = 'employee'
  state.period.value = 'custom'
  state.dateFrom.value = '2026-10-01'
  state.dateTo.value = '2026-10-18'
  state.page.value = 3
  state.search.value = '  полировка  '
  await state.loadItems()
  assert.deepEqual(calls[0][1], { status:3, handover:'ready', dateFrom:'2026-10-01', dateTo:'2026-10-18', employeeId:'employee', search:'полировка', page:3, pageSize:25 })
})

test('Обратный или неполный интервал не отправляет запрос и показывает ошибку', async () => {
  let calls = 0
  const { state } = setup(async () => { calls++; return result([]) })
  state.period.value = 'custom'
  state.dateFrom.value = '2026-10-20'
  state.dateTo.value = '2026-10-01'
  await state.loadItems()
  state.dateFrom.value = ''
  await state.loadItems()
  assert.equal(calls, 0)
  assert.match(state.errorMessage.value, /интервал/)
  assert.equal(state.isLoading.value, false)
})

test('Поздний ответ журнала не заменяет актуальный поиск', async () => {
  let finish
  const old = new Promise(resolve => { finish = resolve })
  const { state } = setup(async (org, options) => options.search === 'old' ? old : result([{id:'new'}]))
  state.search.value = 'old'
  const request = state.loadItems()
  state.search.value = 'new'
  await state.loadItems()
  finish(result([{id:'old'}]))
  await request
  assert.equal(state.data.value.items[0].id, 'new')
  assert.equal(state.isLoading.value, false)
})

test('Возврат восстанавливает страницу и фильтры, новая организация начинает с чистого состояния', () => {
  const { state, props } = setup(async () => result([]), {status:'2',period:'all',search:'Audi',employeeId:'employee',page:3,pageSize:'50'})
  assert.equal(state.page.value, 3)
  assert.equal(state.search.value, 'Audi')
  assert.equal(state.employeeId.value, 'employee')
  props.context.organization.id = 'another-org'
  state.restoreState()
  assert.equal(state.page.value, 1)
  assert.equal(state.period.value, 'month')
  assert.equal(state.status.value, '0')
  assert.equal(state.pageSize.value, '25')
  assert.equal(state.employeeId.value, '')
  assert.equal(state.search.value, '')
})

test('Скрытые финансовые данные не превращаются в нулевые оплаты; переплата не становится долгом', () => {
  const { state } = setup(async () => result([]))
  state.data.value = result([{id:'private', amount:1000, paidAmount:null}])
  assert.equal(state.showPayments.value, false)
  state.data.value = result([{id:'zero', amount:0, paidAmount:0, paymentStatus:'no_payment_required'}])
  assert.equal(state.showPayments.value, true)
  state.data.value = result([{id:'hidden', amount:0, paidAmount:null, paymentStatus:null}])
  assert.equal(state.showPayments.value, false)
  state.data.value = result([{id:'partial', amount:1000, paidAmount:400}])
  assert.equal(state.showPayments.value, true)
  assert.equal(state.debt(state.data.value.items[0]), 600)
  assert.equal(state.debt({amount:1000,paidAmount:1200}), 0)
})

test('Ошибка сохраняет фильтры для повтора, состав работ раскрывается независимо по строкам', async () => {
  const { state } = setup(async () => { throw new Error('Недоступно') })
  state.search.value = 'BMW'
  state.toggleDetails('one')
  state.toggleDetails('two')
  state.toggleDetails('one')
  assert.deepEqual(state.expandedIds.value, ['two'])
  await state.loadItems()
  assert.equal(state.search.value, 'BMW')
  assert.equal(state.errorMessage.value, 'Недоступно')
})

test('Неоценённый нулевой заказ не считается оплаченным; бесплатная работа и переплата различаются', () => {
  const { state } = setup(async () => result([]))
  assert.equal(state.paymentLabel({ amount:0, paidAmount:0, paymentStatus:'unpaid' }), 'Не оплачено')
  assert.equal(state.paymentLabel({ amount:0, paidAmount:0, paymentStatus:'no_payment_required' }), 'Оплата не требуется')
  assert.equal(state.paymentLabel({ amount:0, paidAmount:null, paymentStatus:null }), '')
  assert.equal(state.paymentLabel({ amount:100, paidAmount:100, paymentStatus:'paid' }), 'Оплачено')
  assert.match(state.paymentLabel({ amount:100, paidAmount:40, paymentStatus:'partial' }), /^Оплачено:/)
  assert.match(state.paymentLabel({ amount:100, paidAmount:120, paymentStatus:'overpaid' }), /^Переплата:/)
})

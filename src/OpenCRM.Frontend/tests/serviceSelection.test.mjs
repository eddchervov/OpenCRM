import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { calculateServiceDuration, formatServiceDuration } from '../packages/workspace/src/serviceDuration.ts'

const filename = new URL('../packages/workspace/src/components/MaintenanceServicesEditor.vue', import.meta.url)
const { descriptor } = parse(readFileSync(filename, 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'service-selection' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function setup(rows = [], disabled = false, defaultVatRateCode) {
  const module = { exports: {} }
  const resolve = id => {
    if (id === 'vue') return { ...vue, useId: () => 'service-selection', onMounted() {}, watch() {} }
    if (id === '../serviceDuration') return { calculateServiceDuration, formatServiceDuration }
    return {}
  }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const events = []
  const props = vue.reactive({ organizationId: 'org', modelValue: rows, disabled, defaultVatRateCode })
  const state = module.exports.default.setup(props, { expose() {}, emit: (...args) => events.push(args) })
  state.services.value = [
    { id: 'a', kindCode: 'service', name: 'Мойка', unitPrice: 2000, durationMinutes: 60, isActive: true },
    { id: 'b', kindCode: 'service', name: 'Полировка', unitPrice: 12000, durationMinutes: 180, isActive: true },
    { id: 'c', kindCode: 'service', name: 'Защита', unitPrice: 10000, durationMinutes: 120, isActive: true }
  ]
  state.bundles.value = [{ id: 'bundle', kindCode: 'service_bundle', name: 'Комплекс', serviceIds: ['a', 'b'], durationMinutes: 180, isActive: true }]
  state.loading.value = false
  return { state, props, events }
}

test('Итог выбора совпадает с добавленными строками и учитывает длительность набора', () => {
  const { state, events } = setup()
  state.toggle(state.bundles.value[0])
  state.toggle(state.services.value[2])
  assert.equal(state.selectedPrice.value, 24000)
  assert.equal(state.selectedDuration.value, 300)
  state.addSelected()
  assert.deepEqual(events[0][1].map(row => row.catalogItemId), ['a', 'b', 'c'])
  assert.equal(state.appliedBundles.value[0].durationMinutes, 180)
})

test('Частично добавленный набор не дублирует строки и не подменяет длительность оставшейся услуги', () => {
  const { state, events } = setup([{ catalogItemId: 'a', name: 'Мойка', quantity: 1 }])
  state.toggle(state.bundles.value[0])
  assert.equal(state.isSelected(state.bundles.value[0]), true)
  assert.deepEqual(state.selectedServices.value.map(item => item.id), ['b'])
  assert.equal(state.selectedPrice.value, 12000)
  assert.equal(state.selectedDuration.value, 180)
  state.addSelected()
  assert.deepEqual(events[0][1].map(row => row.catalogItemId), ['a', 'b'])
  assert.equal(state.appliedBundles.value.length, 0)
})

test('Пересекающиеся наборы остаются выбранными и добавляют общую услугу один раз', () => {
  const { state, events } = setup()
  state.bundles.value.push({ id: 'other', kindCode: 'service_bundle', name: 'Другой набор', serviceIds: ['a', 'c'], durationMinutes: 100, isActive: true })
  state.toggle(state.bundles.value[0])
  state.toggle(state.bundles.value[1])
  assert.equal(state.isSelected(state.bundles.value[0]), true)
  assert.equal(state.isSelected(state.bundles.value[1]), true)
  assert.equal(state.sharedServices.value.get('a'), 2)
  assert.equal(state.selectedDuration.value, 360)
  assert.equal(state.selectedPrice.value, 24000)
  assert.equal(state.selectedServices.value.length, 3)
  state.addSelected()
  assert.deepEqual(events[0][1].map(row => [row.catalogItemId, row.quantity]), [['a', 1], ['b', 1], ['c', 1]])
  assert.equal(state.appliedBundles.value.length, 0)
})

test('Снятие одного набора сохраняет общие услуги другого и возвращает его длительность', () => {
  const { state } = setup()
  state.bundles.value.push({ id: 'other', kindCode: 'service_bundle', name: 'Другой набор', serviceIds: ['a', 'c'], durationMinutes: 100, isActive: true })
  state.toggle(state.bundles.value[0])
  state.toggle(state.bundles.value[1])
  state.toggle(state.bundles.value[0])
  assert.equal(state.isSelected(state.bundles.value[1]), true)
  assert.deepEqual(state.selectedServices.value.map(item => item.id), ['a', 'c'])
  assert.equal(state.selectedDuration.value, 100)
  assert.equal(state.sharedServices.value.size, 0)
})

test('Снятие набора не убирает отдельно выбранную услугу', () => {
  const { state } = setup()
  state.toggle(state.services.value[0])
  state.toggle(state.bundles.value[0])
  state.toggle(state.bundles.value[0])
  assert.deepEqual(state.selectedServices.value.map(item => item.id), ['a'])
  assert.equal(state.selectedDuration.value, 60)
})

test('Удаление общей услуги разбирает затронутые наборы, сохраняя остальные работы', () => {
  const { state } = setup()
  state.bundles.value.push({ id: 'other', kindCode: 'service_bundle', name: 'Другой набор', serviceIds: ['a', 'c'], durationMinutes: 100, isActive: true })
  state.toggle(state.bundles.value[0])
  state.toggle(state.bundles.value[1])
  state.toggle(state.services.value[0])
  assert.deepEqual(state.selectedServices.value.map(item => item.id), ['b', 'c'])
  assert.equal(state.selectedBundleIds.value.length, 0)
  assert.equal(state.selectedDuration.value, 300)
})

test('Удаление услуги из набора пересчитывает стоимость и отменяет его общую длительность', () => {
  const { state } = setup()
  state.toggle(state.bundles.value[0])
  state.toggle(state.services.value[0])
  assert.deepEqual(state.selectedServices.value.map(item => item.id), ['b'])
  assert.equal(state.selectedDuration.value, 180)
  assert.equal(state.selectedPrice.value, 12000)
  assert.equal(state.selectedBundles.value.length, 0)
})

test('Неизвестные цены и длительности не превращаются в нулевые; поиск учитывает состав набора', () => {
  const { state } = setup()
  state.services.value[0].unitPrice = null
  state.services.value[0].durationMinutes = null
  state.toggle(state.services.value[0])
  assert.equal(state.selectedPrice.value, null)
  assert.equal(state.selectedDuration.value, null)
  assert.equal(state.bundlePrice(state.bundles.value[0]), null)
  state.tab.value = 'bundles'
  state.search.value = 'ПОЛИРОВКА'
  assert.equal(state.filtered.value.length, 1)
})

test('Недоступные услуги и режим чтения не добавляют строки', () => {
  const { state, events } = setup()
  state.services.value[0].isActive = false
  state.toggle(state.bundles.value[0])
  assert.equal(state.selectedServices.value.length, 0)
  state.addSelected()
  assert.equal(events.length, 0)
  const readonly = setup([], true)
  readonly.state.toggle(readonly.state.services.value[0])
  readonly.state.addSelected()
  assert.equal(readonly.events.length, 0)
})

test('Новая запись подставляет «Без НДС», сохраняя явно выбранную ставку 0 %', () => {
  const { state, events } = setup([
    { name: 'Мойка', price: 2000, quantity: 1, vatRateCode: null },
    { name: 'Полировка', price: 12000, quantity: 1, vatRateCode: '0' }
  ], false, 'without_vat')

  assert.equal(state.validate(), null)
  assert.deepEqual(events[0], ['update:modelValue', [
    { name: 'Мойка', price: 2000, quantity: 1, vatRateCode: 'without_vat' },
    { name: 'Полировка', price: 12000, quantity: 1, vatRateCode: '0' }
  ]])
})

test('Редактирование без default не подменяет отсутствующую историческую ставку НДС', () => {
  const original = { name: 'Работа из истории', price: 2000, quantity: 1, vatRateCode: null }
  const { state, props, events } = setup([original])

  assert.equal(state.validate(), 'Выберите ставку НДС для услуги «Работа из истории».')
  assert.deepEqual(props.modelValue, [original])
  assert.equal(events.length, 0)
})

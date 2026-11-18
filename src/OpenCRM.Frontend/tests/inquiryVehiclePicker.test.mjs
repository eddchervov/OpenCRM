import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import './inquiryTestMessages.mjs'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/InquiryVehiclePicker.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'inquiry-vehicle-picker-test' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const vehicle = { id: 'vehicle-1', brandName: 'Toyota', modelName: 'Camry', registrationPlate: 'М777ММ' }
function picker({ modelValue = '', pageResult, loadSelected = async () => vehicle } = {}) {
  const calls = [], events = [], module = { exports: {} }
  const scope = vue.effectScope()
  const props = vue.reactive({ organizationId: 'org', clientId: 'client', modelValue, disabled: false })
  new Function('require', 'module', 'exports', code)(id => {
    if (regionalTestDependencies[id]) return regionalTestDependencies[id]
    if (id === 'vue') return { ...vue, onBeforeUnmount() {} }
    if (id === '../api/vehicles') return {
      loadVehicle: loadSelected,
      loadVehiclesPage: async (...args) => {
        calls.push(args)
        return pageResult ? pageResult(...args) : { items: [vehicle], totalCount: 7 }
      }
    }
    return {}
  }, module, module.exports)
  const state = scope.run(() => module.exports.default.setup(props, { expose() {}, emit: (event, value) => {
    events.push([event, value])
    props.modelValue = value
  } }))
  return { state, props, calls, events, dispose: () => scope.stop() }
}

test('Поиск показывает результаты и общее число, связь задаётся явным выбором', async () => {
  const { state, props, calls, events, dispose } = picker()
  try {
    let focusCount = 0
    state.resultsElement.value = { querySelector: () => ({ focus: () => { focusCount++ } }) }
    state.query.value = ' М777 '
    await state.searchVehicles()
    assert.deepEqual(calls[0], ['org', 1, 5, { searchQuery: 'М777', organizationClientId: 'client' }])
    assert.equal(state.searched.value, true)
    assert.equal(state.total.value, 7)
    assert.equal(focusCount, 1)
    assert.deepEqual(state.items.value, [vehicle])
    assert.equal(props.modelValue, '')
    await state.chooseVehicle(state.items.value[0])
    assert.deepEqual(events, [['selected', vehicle], ['update:modelValue', 'vehicle-1']])
    assert.equal(state.selected.value.id, 'vehicle-1')
    assert.equal(state.searched.value, false)
    assert.equal(state.query.value, '')
    state.clearSelection()
    assert.equal(props.modelValue, '')
  } finally { dispose() }
})

test('Новый запрос и смена клиента скрывают устаревшие результаты', async () => {
  let resolveResult
  const { state, props, dispose } = picker({ pageResult: () => new Promise(resolve => { resolveResult = resolve }) })
  try {
    state.query.value = 'М777'
    const request = state.searchVehicles()
    state.query.value = 'А123'
    resolveResult({ items: [vehicle], totalCount: 1 })
    await request
    assert.equal(state.searched.value, false)
    assert.deepEqual(state.items.value, [])
    const otherRequest = state.searchVehicles()
    props.clientId = 'other-client'
    resolveResult({ items: [vehicle], totalCount: 1 })
    await otherRequest
    assert.equal(state.query.value, '')
    assert.equal(state.searched.value, false)
    assert.deepEqual(state.items.value, [])
    assert.equal(state.searching.value, false)
  } finally { dispose() }
})

test('Пустой поиск сохраняет выбранный автомобиль; страница сохраняет условия', async () => {
  const { state, props, calls, dispose } = picker({ modelValue: 'vehicle-1', pageResult: async () => ({ items: [], totalCount: 0 }) })
  try {
    await vue.nextTick()
    state.query.value = 'Неизвестный номер'
    await state.searchVehicles(2)
    assert.equal(state.searched.value, true)
    assert.equal(state.total.value, 0)
    assert.equal(state.selected.value.id, 'vehicle-1')
    assert.equal(props.modelValue, 'vehicle-1')
    assert.equal(calls[0][1], 2)
    assert.equal(calls[0][3].searchQuery, 'Неизвестный номер')
  } finally { dispose() }
})

test('Ошибка не сбрасывает связь; недоступный редактор не отправляет поиск и выбор', async () => {
  const { state, props, calls, events, dispose } = picker({ modelValue: 'vehicle-1', pageResult: async () => { throw new Error('Нет соединения') } })
  try {
    await vue.nextTick()
    await state.searchVehicles()
    assert.equal(state.searchError.value, 'Нет соединения')
    assert.equal(state.searching.value, false)
    assert.equal(props.modelValue, 'vehicle-1')
    props.disabled = true
    await state.searchVehicles()
    await state.chooseVehicle(vehicle)
    state.clearSelection()
    assert.equal(calls.length, 1)
    assert.deepEqual(events, [])
    assert.equal(props.modelValue, 'vehicle-1')
  } finally { dispose() }
})

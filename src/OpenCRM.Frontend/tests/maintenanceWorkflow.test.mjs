import '../packages/workspace/src/workflowMessages.ts'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { isSimpleMaintenanceWorkflow, supportsMaintenanceWorkflow } from '../packages/workspace/src/utils/maintenanceWorkflow.ts'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/MaintenanceCardActions.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'workflow-actions-test' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function actions({ simpleWorkflow = true, status = 1, disabled = false, failure } = {}) {
  const calls = [], events = [], module = { exports: {} }
  const props = vue.reactive({ organizationId: 'org', simpleWorkflow, disabled,
    maintenance: { id: 'order', status, version: 17, debtAmount: 3000 } })
  new Function('require', 'module', 'exports', code)(id => {
    if (id in regionalTestDependencies) return regionalTestDependencies[id]
    if (id === 'vue') return { ...vue, onMounted() {}, onBeforeUnmount() {} }
    if (id === '../api/maintenance') return { changeMaintenanceWorkflow: async (...args) => {
      calls.push(args)
      if (failure) throw failure
    } }
    return {}
  }, module, module.exports)
  const state = module.exports.default.setup(props, { expose() {}, emit: (...args) => events.push(args) })
  return { state, props, calls, events }
}

test('Простой режим общий для детейлинга и СТО; автопарк сохраняет прежний процесс', () => {
  for (const primaryScenarioCode of ['detailing', 'service-station']) {
    assert.equal(supportsMaintenanceWorkflow({ primaryScenarioCode }), true)
    assert.equal(isSimpleMaintenanceWorkflow({ primaryScenarioCode, maintenanceWorkflowMode: 'simple' }), true)
    assert.equal(isSimpleMaintenanceWorkflow({ primaryScenarioCode, maintenanceWorkflowMode: 'staged' }), false)
  }
  assert.equal(isSimpleMaintenanceWorkflow({ primaryScenarioCode: 'fleet', maintenanceWorkflowMode: 'simple' }), false)
  assert.equal(supportsMaintenanceWorkflow({ primaryScenarioCode: 'fleet' }), false)
})

test('Простая карточка предлагает выдачу без промежуточных этапов, сохраняя перенос и отмену', () => {
  for (const status of [1, 2, 3]) {
    const { state } = actions({ status })
    assert.equal(state.primary.value.code, 'simple_handover')
    assert.equal(state.primary.value.label, 'Выдать')
    assert.deepEqual(state.available.value.map(action => action.code), status === 3
      ? ['simple_handover'] : ['simple_handover', 'reschedule', 'cancel'])
  }
  const { state, props } = actions({ status: 3 })
  props.maintenance.handedOverAt = '2026-10-23T10:00:00Z'
  assert.deepEqual(state.available.value.map(action => action.code), ['undo_handover'])
  assert.equal(state.primary.value, undefined)
  props.maintenance.handedOverAt = null
  props.maintenance.status = 4
  assert.deepEqual(state.available.value.map(action => action.code), ['reopen'])
})

test('Поэтапный режим сохраняет запуск, завершение и готовность', () => {
  for (const [status, action] of [[1, 'start'], [2, 'complete'], [3, 'mark_ready']]) {
    assert.equal(actions({ simpleWorkflow: false, status }).state.primary.value.code, action)
  }
})

test('Выдача отправляет один запрос с версией и не создаёт оплату', async () => {
  const { state, calls, events } = actions()
  state.open.value = true
  state.selected.value = state.primary.value
  await state.submit()
  assert.deepEqual(calls, [['org', 'order', { action: 'simple_handover', version: 17, reason: null }]])
  assert.deepEqual(events, [['changed']])
  assert.equal(state.open.value, false)
})

test('Ошибка выдачи оставляет подтверждение открытым, а запрет записи блокирует запрос', async () => {
  const { state, calls, events } = actions({ failure: new Error('Запись уже изменена') })
  state.open.value = true
  state.selected.value = state.primary.value
  await state.submit()
  assert.equal(state.error.value, 'Запись уже изменена')
  assert.equal(state.open.value, true)
  assert.equal(events.length, 0)
  assert.equal(calls.length, 1)
  const blocked = actions({ disabled: true })
  blocked.state.selected.value = blocked.state.primary.value
  await blocked.state.submit()
  assert.equal(blocked.calls.length, 0)
})

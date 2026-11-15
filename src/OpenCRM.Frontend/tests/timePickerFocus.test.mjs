import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const { descriptor } = parse(readFileSync(new URL('../packages/ui/src/components/UiTimePicker.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'time-picker-focus-test' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function createPicker() {
  const model = vue.ref('09:00')
  const module = { exports: {} }
  const resolve = id => id === 'vue'
    ? { ...vue, useId: () => 'time-test', useModel: () => model, watch() {}, onBeforeUnmount() {} }
    : {}
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const state = module.exports.default.setup({ step: 60 }, { expose() {} })
  const inside = {}
  state.root.value = { contains: target => target === inside }
  state.isOpen.value = true

  return { state, inside, model }
}

test('Переход фокуса внутри выбора времени сохраняет открытый picker', () => {
  const { state, inside } = createPicker()
  state.handleFocusOut({ relatedTarget: inside })

  assert.equal(state.isOpen.value, true)
})

test('Переход клавиатурой в другое поле закрывает предыдущий picker и сохраняет время', () => {
  const { state, model } = createPicker()
  state.inputValue.value = '11:15'
  state.handleFocusOut({ relatedTarget: {} })

  assert.equal(state.isOpen.value, false)
  assert.equal(model.value, '11:15')
})

test('Потеря фокуса окна закрывает picker без подстановки времени в очищенное поле', () => {
  const { state, model } = createPicker()
  state.clearValue()
  state.handleFocusOut({ relatedTarget: null })

  assert.equal(state.isOpen.value, false)
  assert.equal(model.value, '')
})

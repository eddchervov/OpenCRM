import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

function compile(name) {
  const source = readFileSync(new URL(`../packages/ui/src/components/${name}.vue`, import.meta.url), 'utf8')
  const { descriptor } = parse(source)
  const code = ts.transpileModule(compileScript(descriptor, { id: 'numeric-input-test' }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  return { descriptor, code }
}
const { descriptor, code } = compile('UiMoneyInput')

function setup(value, min = 0.01, max = 700, componentCode = code) {
  const props = vue.reactive({ modelValue: value, min, max, step: 0.01, integer: true })
  const module = { exports: {} }
  new Function('require', 'module', 'exports', componentCode)(id => id === 'vue' ? {
    ...vue,
    useModel: () => ({ get value() { return props.modelValue }, set value(value) { props.modelValue = value } })
  } : regionalTestDependencies[id] ?? {}, module, module.exports)
  const state = module.exports.default.setup(props, { expose() {} })
  return { state, props }
}

for (const value of ['0', '-10', '700.01', '18000']) {
  test(`Денежное поле сохраняет введённое ${value} вместо подмены допустимой суммой`, () => {
    const { state, props } = setup(value)
    const input = { value }
    state.handleInput?.({ target: input })
    assert.equal(input.value, value)
    assert.equal(props.modelValue, value)
    state.handleBlur()
    assert.equal(props.modelValue, value)
  })
}

test('Денежное поле нормализует запятую и пробелы без округления или ограничения суммы', () => {
  const { state, props } = setup(' 1700,25 ')
  state.handleBlur()
  assert.equal(props.modelValue, '1700.25')
  props.modelValue = '1.234'
  state.handleBlur()
  assert.equal(props.modelValue, '1.234')
  props.modelValue = ''
  state.handleBlur()
  assert.equal(props.modelValue, '')
})

test('Ограничения суммы и дробности остаются на поле для проверки формы', () => {
  for (const binding of ['min', 'max', 'step', 'required', 'disabled']) {
    assert.ok(descriptor.template.content.includes(`:${binding}="${binding}"`))
  }
  assert.match(descriptor.template.content, /type="number"/)
})

for (const value of ['0', '-10', '701', '1.5']) {
  test(`Числовое поле сохраняет ${value} для проверки диапазона и целого значения`, () => {
    const { code } = compile('UiNumberInput')
    const { state, props } = setup(value, 1, 700, code)
    state.handleBlur()
    assert.equal(props.modelValue, value)
  })
}

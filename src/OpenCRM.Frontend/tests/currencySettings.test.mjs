import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const filename = new URL('../packages/workspace/src/components/CurrencySettings.vue', import.meta.url)
const { descriptor } = parse(readFileSync(filename, 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'currency-settings' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

async function settings({ canManage = true, canChange = true, disabled = false, save, load } = {}) {
  const calls = []
  const events = []
  const current = { currencyCode: 'RUB', canManage, canChange, supportedCurrencies: ['RUB', 'USD', 'EUR', 'CNY'] }
  const dependencies = {
    vue: { ...vue, onBeforeUnmount() {} },
    '@opencrm/ui': { useLocalization: () => ({ t: key => key, locale: vue.ref('en') }) },
    '../currencyMessages': {},
    '../api/currency': {
      loadCurrencySettings: load ?? (async () => current),
      saveCurrencySettings: async (...args) => {
        calls.push(args)
        return save ? save(...args) : { ...current, currencyCode: args[1] }
      }
    }
  }
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => dependencies[id], module, module.exports)
  const props = vue.reactive({ organizationId: 'org-1', disabled })
  const state = module.exports.default.setup(props, { expose() {}, emit: (...args) => events.push(args) })
  await new Promise(resolve => setImmediate(resolve))

  return { state, props, calls, events }
}

test('Сохранение валюты отправляет организацию и ожидаемое старое значение', async () => {
  const { state, calls, events } = await settings()
  state.startEdit()
  state.draft.value = 'CNY'
  await state.save()
  assert.deepEqual(calls, [['org-1', 'CNY', 'RUB']])
  assert.equal(state.settings.value.currencyCode, 'CNY')
  assert.equal(state.editing.value, false)
  assert.ok(events.some(event => event[0] === 'saved' && event[1] === 'CNY'))
})

test('Режим чтения и денежная история запрещают изменение валюты', async () => {
  for (const options of [{ canManage: false }, { canChange: false }, { disabled: true }]) {
    const { state, calls } = await settings(options)
    state.startEdit()
    state.draft.value = 'USD'
    await state.save()
    assert.equal(state.editing.value, false)
    assert.equal(calls.length, 0)
  }
})

test('Конфликт сохраняет черновик и не сообщает об успешном изменении', async () => {
  const { state, events } = await settings({ save: async () => { throw new Error('conflict') } })
  state.startEdit()
  state.draft.value = 'EUR'
  await state.save()
  assert.equal(state.draft.value, 'EUR')
  assert.equal(state.settings.value.currencyCode, 'RUB')
  assert.equal(state.editing.value, true)
  assert.equal(state.error.value, 'conflict')
  assert.equal(events.some(event => event[0] === 'saved'), false)
})

test('Ответ сохранения прежней организации не меняет открытую организацию', async () => {
  let finish
  const { state, props, events } = await settings({ save: () => new Promise(resolve => { finish = resolve }) })
  state.startEdit()
  state.draft.value = 'CNY'
  const saving = state.save()
  props.organizationId = 'org-2'
  await vue.nextTick()
  await new Promise(resolve => setImmediate(resolve))
  finish({ currencyCode: 'CNY', canManage: true, canChange: true, supportedCurrencies: ['CNY'] })
  await saving
  assert.equal(state.settings.value.currencyCode, 'RUB')
  assert.equal(events.some(event => event[0] === 'saved'), false)
})

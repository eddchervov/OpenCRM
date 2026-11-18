import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import ts from 'typescript'
import * as localization from '@opencrm/ui/i18n'
import '../packages/workspace/src/localization.ts'
import * as international from '../packages/workspace/src/internationalRegistrationPlate.ts'

const source = readFileSync(new URL('../packages/workspace/src/legacyVehicleRegistrationPlate.ts', import.meta.url), 'utf8')
const code = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText

function legacy(vehicle, loadFormats = async () => ({ maxLength: 12 })) {
  let definition
  let masks = 0
  const element = () => ({
    children: [], listeners: {}, beforeNodes: [], afterNodes: [], labels: [], disabled: false, value: '',
    classList: { toggle() {} },
    append(...nodes) { this.children.push(...nodes) },
    before(...nodes) { this.beforeNodes.push(...nodes) },
    after(...nodes) { this.afterNodes.push(...nodes) },
    setAttribute() {}, removeAttribute() {},
    setCustomValidity(message) { this.validityMessage = message },
    getClientRects: () => [1], reportValidity() {},
    remove() { this.removed = true },
    addEventListener(type, callback) { this.listeners[type] = callback }
  })
  const input = element()
  input.value = vehicle.registrationPlate
  const document = { createElement: element, contains: node => node === input }
  const module = { exports: {} }
  new Function('require', 'module', 'exports', 'document', code)(id => {
    if (id === 'maska') return { MaskInput: class { constructor() { masks++ } destroy() {} } }
    if (id === '@opencrm/ui/i18n') return localization
    if (id === './localization') return {}
    if (id === './internationalRegistrationPlate') return international
    if (id === './utils/registrationCountries') return { registrationCountryOptions: () => [
      { value: 'RU', label: 'Russia' }, { value: 'CN', label: 'China' }
    ] }
    if (id === './vehicleRegistrationPlate') return {
      loadVehicleRegistrationPlateFormats: loadFormats,
      getVehicleRegistrationPlateFormat: () => ({ example: 'А123АА123' }),
      createVehicleRegistrationPlateMask: () => ({}),
      validateRegistrationPlate: value => value.includes('京') ? 'Russian format error' : '',
      normalizeRegistrationPlate: value => value.toUpperCase(),
      prepareRegistrationPlatePaste: value => ({ value, error: '' })
    }
    throw new Error('Не задана зависимость: ' + id)
  }, module, module.exports, document)
  module.exports.installVue({ directive(_name, value) { definition = value } })
  definition.inserted(input, { value: vehicle }, { context: { $set(target, key, value) { target[key] = value } } })

  return {
    input, vehicle, country: input.beforeNodes[0].children[1], masks: () => masks,
    validate: () => module.exports.validate(document),
    unbind: () => definition.unbind(input)
  }
}

test('Личная форма сохраняет китайский номер без русской маски и загрузки форматов', async () => {
  const state = legacy({ kind: 1, registrationCountryCode: 'CN', registrationPlate: '京B·ABC123' }, async () => {
    throw new Error('Russian formats unavailable')
  })
  state.input.value = ' 粤B·abc123 '
  state.input.listeners.input()
  await new Promise(resolve => setImmediate(resolve))

  assert.equal(state.vehicle.registrationPlate, '粤B·abc123')
  assert.equal(state.input.maxLength, 24)
  assert.equal(state.masks(), 0)
  assert.equal(state.validate(), true)
  assert.equal(state.input.validityMessage, '')
  state.unbind()
  assert.equal(state.input.beforeNodes[0].removed, true)
})

test('Смена страны при прежнем номере не обходит повторную проверку', async () => {
  const state = legacy({ id: 'existing', kind: 1, registrationCountryCode: 'CN', registrationPlate: '京B·12345' })
  await new Promise(resolve => setImmediate(resolve))
  assert.equal(state.validate(), true)
  state.country.value = 'RU'
  state.country.listeners.change()

  assert.equal(state.vehicle.registrationCountryCode, 'RU')
  assert.equal(state.validate(), false)
  assert.equal(state.input.validityMessage, 'Russian format error')
  state.unbind()
})

test('Российские номера продолжают использовать маску', async () => {
  const state = legacy({ kind: 1, registrationCountryCode: 'RU', registrationPlate: 'А123АА123' })
  await new Promise(resolve => setImmediate(resolve))
  assert.ok(state.masks() > 0)
  assert.equal(state.validate(), true)
  state.unbind()
})

test('Явно неизвестная страна сохраняет Unicode и регистр, прежнее отсутствие страны сохраняет RU', async () => {
  const unknown = legacy({ kind: 1, registrationCountryCode: 'ZZ', registrationPlate: '京B·abc123' })
  await new Promise(resolve => setImmediate(resolve))
  assert.equal(unknown.country.value, 'ZZ')
  assert.equal(unknown.masks(), 0)
  assert.equal(unknown.validate(), true)
  assert.equal(unknown.vehicle.registrationPlate, '京B·abc123')
  unknown.unbind()
  const previous = legacy({ kind: 1, registrationCountryCode: null, registrationPlate: 'А123АА123' })
  await new Promise(resolve => setImmediate(resolve))
  assert.equal(previous.country.value, 'RU')
  assert.ok(previous.masks() > 0)
  assert.equal(previous.vehicle.registrationCountryCode, null)
  previous.unbind()
})

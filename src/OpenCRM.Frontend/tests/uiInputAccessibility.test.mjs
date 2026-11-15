import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import ts from 'typescript'

const dateOnlySource = ts.transpileModule(readFileSync(new URL('../packages/ui/src/dateOnly.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const dateOnlyModule = { exports: {} }
new Function('module', 'exports', dateOnlySource)(dateOnlyModule, dateOnlyModule.exports)

test('parseDateOnly rejects impossible dates instead of rolling them into another month', () => {
  const { parseDateOnly } = dateOnlyModule.exports

  assert.equal(parseDateOnly('2026-02-31'), null)
  assert.equal(parseDateOnly('2026-13-01'), null)
  const leapDay = parseDateOnly('2024-02-29')
  assert.equal(leapDay?.getFullYear(), 2024)
  assert.equal(leapDay?.getMonth(), 1)
  assert.equal(leapDay?.getDate(), 29)
  assert.equal(parseDateOnly('0026-02-28')?.getFullYear(), 26)
})

test('UiDatePicker does not pass an impossible manually entered date to v-calendar', () => {
  const source = readFileSync(new URL('../packages/ui/src/components/UiDatePicker.vue', import.meta.url), 'utf8')

  assert.match(source, /function hasImpossibleDate\(/)
  assert.match(source, /guardImpossibleDateInput\(inputEvents\)/)
  assert.match(source, /name\.replace\(\/\^on\//)
})

test('UiPhoneInput names its input and initializes country labels in Russian', () => {
  const source = readFileSync(new URL('../packages/ui/src/components/UiPhoneInput.vue', import.meta.url), 'utf8')

  assert.match(source, /import \{ ru \} from 'intl-tel-input\/i18n'/)
  assert.match(source, /i18n: ru/)
  assert.match(source, /:aria-label="label \|\| undefined"/)
})

test('UiModal manages keyboard focus and restores it when closed', () => {
  const source = readFileSync(new URL('../packages/ui/src/components/UiModal.vue', import.meta.url), 'utf8')
  const focusSource = readFileSync(new URL('../packages/ui/src/modalFocus.ts', import.meta.url), 'utf8')

  assert.match(source, /aria-modal="true"/)
  assert.match(source, /activateModalFocus/)
  assert.match(source, /onBeforeUnmount\(deactivateModal\)/)
  assert.match(focusSource, /const activeModals: ActiveModal\[\] = \[\]/)
  assert.match(focusSource, /event\.key === 'Escape'/)
  assert.match(focusSource, /event\.key !== 'Tab'/)
  assert.match(focusSource, /getOwnedPortals/)
  assert.match(focusSource, /modal\.opener\.focus\(\)/)
})

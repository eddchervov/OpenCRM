import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import ts from 'typescript'
import * as vue from 'vue'

const source = ts.transpileModule(readFileSync(new URL('../packages/ui/src/razorDateTimeFields.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

async function fields() {
  const mounted = []
  const components = { date: {}, time: {} }
  const hosts = ['date', 'time'].map(kind => {
    const field = {
      value: kind === 'date' ? '2026-10-27' : '14:35', disabled: true,
      listeners: new Map(), changes: 0,
      addEventListener(name, handler) { this.listeners.set(name, handler) },
      dispatchEvent(event) {
        if (event.type === 'change') this.changes++
        this.listeners.get(event.type)?.(event)
      }
    }
    return {
      field, dataset: { label: kind === 'date' ? 'С' : 'Время', inputId: kind },
      querySelector: () => field, hasAttribute: name => name === `data-ui-${kind}-picker`,
      appendChild() {}
    }
  })
  const dependencies = {
    vue: { ...vue, createApp: component => ({ mount() { mounted.push(component.render) } }) },
    './components/UiDatePicker.vue': { default: components.date },
    './components/UiTimePicker.vue': { default: components.time },
    './workspaceTheme': { initializeWorkspaceTheme() {}, syncWorkspaceThemeFromDocument() {} },
    './i18n': { initializeLocalization: async () => {} },
    './styles.css': {}
  }
  new Function('require', 'exports', 'document', 'Event', 'MutationObserver', source)(
    name => dependencies[name], {}, { querySelectorAll: () => hosts, createElement: () => ({}), documentElement: {} },
    Event, class { observe() {} }
  )
  await Promise.resolve()

  return { mounted, hosts, components }
}

test('Razor использует общие компоненты даты и времени и сохраняет исходные форматы', async () => {
  const { mounted, components } = await fields()
  assert.equal(mounted[0]().type, components.date)
  assert.equal(mounted[1]().type, components.time)
  assert.equal(mounted[0]().props.modelValue, '2026-10-27')
  assert.equal(mounted[1]().props.modelValue, '14:35')
})

test('Выбор и очистка в picker обновляют поле API и событие изменения', async () => {
  const { mounted, hosts } = await fields()
  mounted[0]().props['onUpdate:modelValue']('2026-11-01')
  mounted[1]().props['onUpdate:modelValue']('09:05')
  assert.equal(hosts[0].field.value, '2026-11-01')
  assert.equal(hosts[1].field.value, '09:05')
  assert.equal(hosts[0].field.changes, 1)
  mounted[0]().props['onUpdate:modelValue']('')
  assert.equal(hosts[0].field.value, '')
})

test('Загрузка значения и блокировка с сервера синхронизируются с picker', async () => {
  const { mounted, hosts } = await fields()
  assert.equal(mounted[0]().props.disabled, true)
  hosts[0].field.value = '2026-10-01'
  hosts[0].field.disabled = false
  hosts[0].field.dispatchEvent(new Event('opencrm:field-sync'))
  assert.equal(mounted[0]().props.modelValue, '2026-10-01')
  assert.equal(mounted[0]().props.disabled, false)
  hosts[0].field.disabled = true
  hosts[0].field.dispatchEvent(new Event('opencrm:field-sync'))
  assert.equal(mounted[0]().props.disabled, true)
})

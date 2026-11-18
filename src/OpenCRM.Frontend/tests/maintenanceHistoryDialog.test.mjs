import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const filename = new URL('../packages/workspace/src/components/MaintenanceHistoryDialog.vue', import.meta.url)
const { descriptor } = parse(readFileSync(filename, 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'maintenance-history-dialog-test' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function createDialog({ loadMaintenanceHistory = async () => [] } = {}) {
  const module = { exports: {} }
  const locale = vue.ref('en-GB')
  const translate = key => ({ 'maintenanceHistory.loadError': 'Could not load maintenance history.' })[key] ?? key
  const resolve = id => {
    if (id === 'vue') return { ...vue, onBeforeUnmount() {}, onMounted() {} }
    if (id === '@opencrm/ui') return { UiButton: {}, UiLoader: {}, useLocalization: () => ({ t: translate }) }
    if (id === '@opencrm/ui/i18n') return { formattingCulture: locale }
    if (id === '../maintenanceMediaMessages') return {}
    if (id === '../api/maintenance') return { loadMaintenanceHistory }
    throw new Error(`Unexpected dependency: ${id}`)
  }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const state = module.exports.default.setup({ organizationId: 'org-1', maintenanceId: 'maintenance-1', title: 'Record title' }, { expose() {}, emit() {} })
  return { state, locale }
}

test('История загружается для организации и обслуживания, часовой пояс события сохраняется', async () => {
  const calls = []
  const { state } = createDialog({
    loadMaintenanceHistory: async (...args) => {
      calls.push(args)
      return [{ id: 'event-1', createdAt: '2024-01-01T00:00:00Z', timeZoneId: 'America/Los_Angeles', title: 'Updated', eventType: 'updated' }]
    }
  })

  await state.load()

  assert.equal(calls.length, 1)
  assert.equal(calls[0][0], 'org-1')
  assert.equal(calls[0][1], 'maintenance-1')
  assert.equal(state.events.value.length, 1)
  assert.match(state.formatTime(state.events.value[0]), /31 Dec 2023/)
})

test('Ошибка без сообщения получает локализованный fallback', async () => {
  const { state } = createDialog({ loadMaintenanceHistory: async () => { throw null } })

  await state.load()

  assert.equal(state.error.value, 'Could not load maintenance history.')
})

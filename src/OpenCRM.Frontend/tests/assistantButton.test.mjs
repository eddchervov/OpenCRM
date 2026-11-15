import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import { getAssistantPreviewUrl, getAssistantUrl, isAssistantScenario } from '../packages/workspace/src/api/assistantAccess.mjs'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceAssistantButton.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'assistant-button-test' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function setup(props, reader) {
  const hooks = {}
  const module = { exports: {} }
  const resolve = id => {
    if (id === 'vue') return {
      defineComponent: value => value,
      ref: value => ({ value }),
      computed: getter => ({ get value() { return getter() } }),
      onMounted: callback => { hooks.mounted = callback },
      onBeforeUnmount: callback => { hooks.unmounted = callback },
      watch: (_source, callback) => { hooks.changed = callback }
    }
    if (id === '../api/assistant') return { getAssistantPreviewUrl, getAssistantUrl, isAssistantScenario, loadAssistantAvailability: reader }
    throw new Error(`Не задана зависимость: ${id}`)
  }
  const window = { location: { origin: 'https://example.test', pathname: props.workspaceUrl }, addEventListener: (_event, callback) => { hooks.focus = callback }, removeEventListener() {} }
  new Function('require', 'module', 'exports', 'window', code)(resolve, module, module.exports, window)
  const state = module.exports.default.setup(props, { expose() {} })
  return { state, hooks }
}

test('Кнопка Free не опрашивает ИИ при открытии страницы и возвращении фокуса', async () => {
  let reads = 0
  const { state, hooks } = setup({ organizationId: 'org-1', scenarioCode: 'detailing', workspaceUrl: '/detailing/org-1', isLocked: true }, async () => { reads++ })
  hooks.mounted()
  hooks.focus()
  await state.refresh()
  assert.equal(reads, 0)
  assert.equal(state.assistantUrl.value, null)
  assert.match(descriptor.template.content, /href="#assistant"/)
  hooks.unmounted()
})

test('Запоздавший ответ PRO не возвращает рабочий URL после перехода в Free', async () => {
  let complete
  const pending = new Promise(resolve => { complete = resolve })
  const props = { organizationId: 'org-1', scenarioCode: 'detailing', workspaceUrl: '/opencrm/detailing/org-1', isLocked: false }
  const { state, hooks } = setup(props, () => pending)
  const request = state.refresh()
  props.isLocked = true
  hooks.changed()
  complete({ visible: true, enabled: true, organizationId: 'org-1', url: '/opencrm/assistant/org-1', pollingSeconds: 0 })
  await request
  assert.equal(state.assistantUrl.value, null)
})

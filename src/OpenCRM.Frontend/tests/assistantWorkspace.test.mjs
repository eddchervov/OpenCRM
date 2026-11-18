import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { getAssistantFrameUrl } from '../packages/workspace/src/api/assistantAccess.mjs'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '../packages/workspace/src/sectionPagesMessages.ts'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceAssistantSection.vue', import.meta.url), 'utf8'))
const source = ts.transpileModule(compileScript(descriptor, { id: 'assistant-workspace' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const available = { visible: true, enabled: true, organizationId: 'org-1', url: '/assistant/org-1' }

function assistant(loadAvailability = async () => available, isLocked = false) {
  const mounted = []
  const cleanup = []
  const events = []
  const listeners = new Map()
  const dependencies = {
    ...regionalTestDependencies,
    '../sectionPagesMessages': {},
    vue: { ...vue, watch() {}, onMounted: callback => mounted.push(callback), onBeforeUnmount: callback => cleanup.push(callback) },
    '@opencrm/ui': {}, '../api/assistant': { getAssistantFrameUrl, loadAssistantAvailability: loadAvailability },
    './WorkspaceProLock.vue': {}, './WorkspaceProFeaturePreview.vue': {}
  }
  const module = { exports: {} }
  const window = {
    innerHeight: 900, location: { origin: 'https://example.test', pathname: '/detailing/org-1' },
    addEventListener: (name, handler) => listeners.set(name, handler),
    removeEventListener: name => listeners.delete(name)
  }
  new Function('require', 'module', 'exports', 'window', source)(name => dependencies[name], module, module.exports, window)
  const context = vue.reactive({ organization: { id: 'org-1' }, shell: { scenario: { modules: { assistant: !isLocked } } } })
  const state = module.exports.default.setup({ context }, {
    expose() {}, emit: name => events.push(name)
  })
  mounted.forEach(callback => callback())
  return { state, context, events, listeners, dispose: () => cleanup.forEach(callback => callback()) }
}

test('Раздел не открывает встроенную форму при отключённом доступе', async () => {
  const { state } = assistant(async () => ({ ...available, visible: false }))
  await state.load()
  assert.equal(state.frameUrl.value, '')
  assert.equal(state.loading.value, false)
  assert.match(state.error.value, /недоступен/)
})

test('Общий выключатель скрывает предпросмотр Free и не загружает помощника по прямому адресу', async () => {
  let reads = 0
  const { state, context } = assistant(async () => { reads++; return available }, true)
  context.shell.scenario.modules.assistantVisible = false
  await state.load()
  assert.equal(state.previewOnly.value, false)
  assert.equal(state.frameUrl.value, '')
  assert.equal(state.loading.value, false)
  assert.equal(reads, 0)
  assert.match(state.error.value, /отключён администратором/)
})

test('Сообщение обновления CRM принимается только от своей формы и организации', async () => {
  const { state, events, listeners, dispose } = assistant()
  state.frame.value = { contentWindow: {}, contentDocument: { querySelector: () => ({}) }, getBoundingClientRect: () => ({ top: 180 }), closest: () => null }
  await state.load()
  assert.equal(state.frameUrl.value, '/assistant/org-1/embedded')
  const message = { origin: 'https://example.test', source: state.frame.value.contentWindow, data: { type: 'opencrm:assistant-updated', organizationId: 'org-1' } }
  state.receiveUpdate({ ...message, origin: 'https://other.test' })
  state.receiveUpdate({ ...message, source: {} })
  state.receiveUpdate({ ...message, data: { ...message.data, organizationId: 'org-2' } })
  assert.equal(events.length, 0)
  state.receiveUpdate(message)
  assert.deepEqual(events, ['changed'])
  dispose()
  assert.equal(listeners.size, 0)
})

test('Поздний ответ доступности не возвращает форму после ухода из раздела', async () => {
  let complete
  const { state, dispose } = assistant(() => new Promise(resolve => { complete = resolve }))
  const loading = state.load()
  dispose()
  complete(available)
  await loading
  assert.equal(state.frameUrl.value, '')
})

test('Ошибка загрузки документа не показывает страницу входа внутри CRM', async () => {
  const { state } = assistant()
  await state.load()
  state.frame.value = { contentDocument: { querySelector: () => null } }
  state.loaded()
  state.frame.value = null
  await vue.nextTick()
  assert.equal(state.frameUrl.value, '')
  assert.equal(state.loading.value, false)
  assert.match(state.error.value, /Проверьте доступ/)
})

test('Free не загружает availability или iframe помощника', async () => {
  let calls = 0
  const { state } = assistant(async () => { calls++; return available }, true)
  await state.load()
  assert.equal(calls, 0)
  assert.equal(state.frameUrl.value, '')
  assert.equal(state.loading.value, false)
  assert.equal(state.error.value, '')
})

test('Переход PRO в Free отбрасывает поздний ответ availability', async () => {
  let complete
  const { state, context } = assistant(() => new Promise(resolve => { complete = resolve }))
  const pending = state.load()
  context.shell.scenario.modules.assistant = false
  await state.load()
  complete(available)
  await pending
  assert.equal(state.frameUrl.value, '')
  assert.equal(state.loading.value, false)
})

import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '../packages/workspace/src/workdayBoardMessages.ts'
import '../packages/workspace/src/setupMessages.ts'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import { renderToString } from '@vue/server-renderer'
import ts from 'typescript'
import * as vue from 'vue'
import * as workingHours from '../packages/workspace/src/workingHours.ts'

function compile(path, dependencies, inlineTemplate = false) {
  const { descriptor } = parse(readFileSync(new URL(path, import.meta.url), 'utf8'))
  const script = compileScript(descriptor, { id: 'setup-wizard-test', inlineTemplate })
  const code = ts.transpileModule(script.content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const module = { exports: {} }
  new Function('require', 'module', 'exports', 'localStorage', 'window', 'setInterval', code)(id => {
    if (id === '../workdayBoardMessages') return {}
    if (id === '../setupMessages') return {}
    if (Object.hasOwn(regionalTestDependencies, id)) return regionalTestDependencies[id]
    if (id in dependencies) return dependencies[id]
    throw new Error('Не задана зависимость: ' + id)
  }, module, module.exports, { getItem: () => null, setItem() {} }, { localStorage: { getItem: () => null } }, () => 0)
  return module.exports.default
}

function wizard(overrides = {}) {
  let pending = true
  let openCalls = 0
  let shown = 0
  const props = { organizationId: 'org-1', scenarioCode: 'service-station', canWrite: true, canCreate: true, ...overrides }
  const setup = { step: 0, revision: 1, completedAt: null, canManage: true, workingDays: [], weeklySchedule: [], ...overrides.setup }
  const component = compile('../packages/workspace/src/components/WorkspaceSetupWizard.vue', {
    vue: { ...vue, onMounted() {}, onBeforeUnmount() {}, watch() {} },
    '@opencrm/ui': {},
    './OrganizationWorkingHoursEditor.vue': {},
    '../workingHours': workingHours,
    '../api/scenarioCatalog': { loadScenarioCatalog: overrides.loadCatalog ?? (async () => ({ workPosts: [], services: [] })) },
    '../api/setup': {
      loadWorkspaceSetup: async () => ({ ...setup, shouldAutoOpen: pending }),
      openWorkspaceSetup: async () => { openCalls++; const allowed = pending; pending = false; return allowed }
    }
  })
  const state = component.setup(props, { expose() {} })
  state.dialog.value = { showModal() { shown++ }, close() {} }
  return { state, calls: () => ({ openCalls, shown }) }
}

test('Первый вход открывает мастер СТО один раз, после «Позже» доступно ручное продолжение', async () => {
  const { state, calls } = wizard()
  await state.refreshState()
  assert.equal(state.open.value, true)
  assert.equal(state.isAutoService.value, true)
  assert.deepEqual(calls(), { openCalls: 1, shown: 1 })
  state.close()
  await state.refreshState()
  assert.equal(state.open.value, false)
  assert.deepEqual(calls(), { openCalls: 1, shown: 1 })
  await state.show()
  assert.equal(state.open.value, true)
  assert.deepEqual(calls(), { openCalls: 2, shown: 2 })
})

for (const [name, props] of [
  ['режим чтения', { canWrite: false }],
  ['нет права настройки', { setup: { canManage: false } }],
  ['ручной запуск из настроек', { placement: 'settings' }]
]) {
  test('Мастер не открывается автоматически: ' + name, async () => {
    const { state, calls } = wizard(props)
    await state.refreshState()
    assert.equal(state.open.value, false)
    assert.deepEqual(calls(), { openCalls: 0, shown: 0 })
  })
}

test('Ошибка загрузки справочников не расходует первое открытие', async () => {
  const { state, calls } = wizard({ loadCatalog: async () => { throw new Error('Справочники недоступны') } })
  await state.refreshState()
  assert.equal(state.open.value, false)
  assert.equal(state.error.value, 'Справочники недоступны')
  assert.deepEqual(calls(), { openCalls: 0, shown: 0 })
})

for (const scenario of ['detailing', 'autoservice', 'fleet']) {
  test(scenario + ': рабочий день подключает мастер только для детейлинга и СТО', async () => {
    const overview = compile('../Scenarios/' + scenario + '-client/src/components/OverviewSection.vue', {
      vue,
      '@opencrm/workspace': {
        WorkspaceWorkdayBoard: vue.defineComponent({
          props: ['context', 'showSetup'],
          setup: props => () => vue.h('div', props.showSetup === '' || props.showSetup === true ? 'Мастер подключён' : 'Без мастера')
        })
      }
    }, true)
    const html = await renderToString(vue.createSSRApp(overview, { context: {} }))
    assert.ok(html.includes(scenario === 'fleet' ? 'Без мастера' : 'Мастер подключён'))
  })
}

test('Рабочий день передаёт сценарию мастера код оболочки без primaryScenarioCode в организации', async () => {
  const passthrough = (props, { slots }) => vue.h('div', slots.default?.())
  const board = compile('../packages/workspace/src/components/WorkspaceWorkdayBoard.vue', {
    vue: { ...vue, watch() {}, onBeforeUnmount() {} },
    '@opencrm/ui': { UiCard: passthrough, UiButton: passthrough, formatDateOnly: () => '2026-10-21', parseDateOnly: () => null },
    './WorkspaceAccessNotice.vue': {},
    '../api/workday': {},
    './WorkspaceWorkdayCard.vue': {},
    './WorkdayInquiryActions.vue': {},
    './WorkdayRepeatVisitActions.vue': {},
    './WorkspaceSetupWizard.vue': { default: vue.defineComponent({
      props: ['scenarioCode'],
      setup: props => () => vue.h('div', 'Сценарий мастера: ' + props.scenarioCode)
    }) }
  }, true)
  const html = await renderToString(vue.createSSRApp(board, {
    showSetup: true,
    context: {
      organization: { id: 'org-1', canManageSettings: true },
      shell: { scenario: { code: 'service-station' } },
      workspaceAccess: { isReadOnly: false }
    }
  }))
  assert.ok(html.includes('Сценарий мастера: service-station'))
})

test('Действия повторных визитов доступны через меню записей детейлинга и СТО и скрыты у автопарка', async () => {
  const passthrough = (props, { slots }) => vue.h('div', slots.default?.())
  const board = compile('../packages/workspace/src/components/WorkspaceWorkdayBoard.vue', {
    vue: { ...vue, watch() {}, onBeforeUnmount() {} },
    '@opencrm/ui': { UiCard: passthrough, UiButton: passthrough, formatDateOnly: () => '2026-10-25', parseDateOnly: () => null },
    './WorkspaceAccessNotice.vue': {}, '../api/workday': {}, './WorkspaceWorkdayCard.vue': {}, './WorkdayInquiryActions.vue': {},
    './WorkspaceSetupWizard.vue': {},
    './WorkdayRepeatVisitActions.vue': { default: vue.defineComponent({ setup: () => () => vue.h('div', 'Действия повторных визитов') }) }
  }, true)
  for (const scenario of ['detailing', 'service-station', 'fleet']) {
    const html = await renderToString(vue.createSSRApp(board, {
      context: { user: { id: 'user' }, organization: { id: 'org', primaryScenarioCode: scenario }, workspaceAccess: { isReadOnly: false },
        shell: { scenario: { code: scenario }, menu: ['clients', 'vehicles', 'scheduler'].map(code => ({ code, isVisible: true })) } }
    }))
    assert.equal(html.includes('Действия повторных визитов'), scenario !== 'fleet')
  }
})

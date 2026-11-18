import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '../packages/workspace/src/proOverviewMessages.ts'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceProOverview.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'workspace-pro-overview-test' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function setup(plan, overrides = {}) {
  const events = []
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => {
    if (regionalTestDependencies[id]) return regionalTestDependencies[id]
    if (id === '../proOverviewMessages') return {}
    if (id === 'vue') return { ...vue, watch() {}, onMounted() {}, onBeforeUnmount() {} }
    if (id === '@opencrm/ui') return { UiButton: {} }
    if (id.endsWith('/intake.png')) return 'intake.png'
    if (id.endsWith('/statistics.png')) return 'statistics.png'
    if (id.endsWith('/team.png')) return 'team.png'
    if (id === './WorkspaceProFeaturePreview.vue') return {}
    if (id === './WorkspaceProFeatureDialog.vue') return {}
    throw new Error(`Не задана зависимость: ${id}`)
  }, module, module.exports)
  const state = module.exports.default.setup({ open: false, plan, canPurchase: true, isSaving: false, ...overrides }, {
    emit: (...event) => events.push(event),
    expose() {}
  })
  return { state, events }
}

test('Любой пример обзора открывает общий увеличенный просмотр, статистика использует свой ключ', () => {
  const { state } = setup({ features: [] })
  for (const code of ['intake', 'albums', 'messages', 'assistant', 'team']) {
    state.openVisual(code)
    assert.equal(state.expandedFeature.value, code)
  }
  state.openVisual('analytics')
  assert.equal(state.expandedFeature.value, 'statistics')
  state.closeDialog()
  assert.equal(state.expandedFeature.value, null)
})

test('Обзор показывает только явно включённые возможности и реальные квоты тарифа', () => {
  const { state } = setup({
    name: 'PRO',
    price: 1250,
    durationDays: 90,
    vehicleLimit: 80,
    features: [
      { code: 'workspace.maintenance.intake', boolValue: false },
      { code: 'vehicle.photo_albums', boolValue: true },
      { code: 'workspace.module.members', boolValue: undefined },
      { code: 'workspace.module.analytics', boolValue: true },
      { code: 'workspace.assistant', boolValue: false },
      { code: 'workspace.client_messaging', boolValue: true },
      { code: 'workspace.documents.styling', boolValue: undefined },
      { code: 'workspace.data.import', boolValue: false },
      { code: 'workspace.clients.limit', intValue: 300 }
    ]
  })

  assert.deepEqual(state.screenshotSections.value.map(section => section.code), ['albums', 'messages', 'analytics'])
  assert.deepEqual(state.compactCards.value.map(card => card.code), [])
  assert.deepEqual(state.quotaItems.value, [
    { label: 'Клиенты', value: '300' },
    { label: 'Автомобили', value: '80' }
  ])
  assert.equal(state.formattedPrice.value, '1 250 RUB')
  assert.equal(state.durationText.value, 'за 90 дн.')
})

test('Приёмка, альбомы, сообщения и ИИ имеют отдельные подробные блоки', () => {
  const { state } = setup({ features: [
    { code: 'workspace.maintenance.intake', boolValue: true },
    { code: 'vehicle.photo_albums', boolValue: true },
    { code: 'workspace.assistant', boolValue: true },
    { code: 'workspace.client_messaging', boolValue: true },
    { code: 'workspace.module.members', boolValue: true }
  ] })
  assert.deepEqual(state.screenshotSections.value.map(section => section.code), ['intake', 'albums', 'messages', 'assistant', 'team'])
  assert.ok(state.screenshotSections.value.every(section => section.points.length >= 2 && section.outcome))
  assert.deepEqual(state.screenshotSections.value.filter(section => section.preview).map(section => section.preview), ['albums', 'messages', 'assistant'])
  assert.match(state.screenshotSections.value.find(section => section.code === 'team').title, /смены/)
})

test('Кнопка покупки передаёт план только при разрешённой доступности', () => {
  const plan = { id: 7, name: 'PRO', effectivePrice: 990 }
  const { state, events } = setup(plan)

  state.purchasePlan()
  assert.deepEqual(events, [['purchasePlan', plan]])
})

test('Кнопка покупки блокируется без разрешения или во время сохранения', () => {
  const plan = { id: 7, name: 'PRO' }
  const unavailable = setup(plan, { canPurchase: false })
  unavailable.state.purchasePlan()
  assert.deepEqual(unavailable.events, [])

  const saving = setup(plan, { isSaving: true })
  saving.state.purchasePlan()
  assert.deepEqual(saving.events, [])
})

import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { renderToString } from '@vue/server-renderer'
import { getProjectBrand } from '../packages/ui/src/projectBrand.ts'
import { proFeatureDetails } from '../packages/workspace/src/proFeatureDetails.ts'
import { proFeatureLandingContent } from '../packages/workspace/src/proFeatureLandingContent.ts'

const filename = new URL('../packages/workspace/src/components/WorkspaceProFeatureDialog.vue', import.meta.url)
const { descriptor } = parse(readFileSync(filename, 'utf8'))
const script = ts.transpileModule(compileScript(descriptor, { id: 'pro-feature-dialog-test', inlineTemplate: true }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const module = { exports: {} }
const button = vue.defineComponent({
  props: ['href'],
  setup: (props, { slots }) => () => vue.h(props.href ? 'a' : 'button', props.href ? { href: props.href } : {}, slots.default?.())
})
const resolve = id => {
  if (id === 'vue') return vue
  if (id === '@opencrm/ui') return { UiButton: button, getProjectBrand }
  if (id === '@opencrm/ui/projectBrand') return { getProjectBrand }
  if (id === '../proFeatureDetails') return { proFeatureDetails }
  if (id === '../proFeatureLandingContent') return { proFeatureLandingContent }
  if (id === './WorkspaceProFeatureLanding.vue') return { default: featureLanding }
  if (id === './WorkspaceProFeatureVisual.vue') return { default: featureVisual }
  if (id === './WorkspaceProWorkflowPreview.vue') return { default: workflowPreview }
  if (/\.(jpg|webp|png|svg)$/.test(id)) return { default: 'demo-image.jpg' }
  if (id === './WorkspaceProTeamLanding.vue') return { default: teamLanding }
  if (id === './WorkspaceProFeaturePreview.vue') return { default: vue.defineComponent(() => () => vue.h('div', 'Демонстрация')) }
  throw new Error(`Неизвестная зависимость ${id}`)
}
function loadComponent(name) {
  const componentDescriptor = parse(readFileSync(new URL(`../packages/workspace/src/components/${name}.vue`, import.meta.url), 'utf8')).descriptor
  const code = ts.transpileModule(compileScript(componentDescriptor, { id: name, inlineTemplate: true }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const componentModule = { exports: {} }
  new Function('require', 'module', 'exports', code)(resolve, componentModule, componentModule.exports)
  return componentModule.exports.default
}
const workflowPreview = loadComponent('WorkspaceProWorkflowPreview')
const featureVisual = loadComponent('WorkspaceProFeatureVisual')
const featureLanding = loadComponent('WorkspaceProFeatureLanding')
const teamDescriptor = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceProTeamLanding.vue', import.meta.url), 'utf8')).descriptor
const teamCode = ts.transpileModule(compileScript(teamDescriptor, { id: 'team-landing-test', inlineTemplate: true }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const teamModule = { exports: {} }
new Function('require', 'module', 'exports', teamCode)(resolve, teamModule, teamModule.exports)
const teamLanding = teamModule.exports.default
new Function('require', 'module', 'exports', script)(resolve, module, module.exports)

async function render(feature, canManageBilling) {
  const context = {}
  await renderToString(vue.h(module.exports.default, { open: false, feature, canManageBilling }), context)
  return context.teleports.body
}

test('Описание каждой платной возможности доступно без права покупки', async () => {
  for (const feature of Object.keys(proFeatureDetails)) {
    const html = await render(feature, false)
    assert.ok(html.includes(`${proFeatureDetails[feature].title} в PRO`))
    assert.match(html, /Демонстрационный пример/)
    assert.match(html, /Увеличить пример/)
    assert.doesNotMatch(html, /href="#billing"/)
  }
})

test('Переход к тарифам показывается только при соответствующем праве', async () => {
  assert.match(await render('team', true), /href="#billing"/)
  const restricted = await render('team', false)
  assert.doesNotMatch(restricted, /Перейти к тарифам/)
  assert.match(restricted, /администратору тарифов/)
})

test('Tab и Shift+Tab удерживают фокус между первым и последним элементами диалога', () => {
  const code = ts.transpileModule(compileScript(descriptor, { id: 'pro-dialog-focus' }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const localModule = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => id === 'vue'
    ? { ...vue, onMounted() {}, onBeforeUnmount() {}, watch() {}, useId: () => 'focus-test' }
    : resolve(id), localModule, localModule.exports)
  const state = localModule.exports.default.setup({ open: false, feature: 'team', canManageBilling: true }, { expose() {}, emit() {} })
  let focused = ''
  let prevented = 0
  const first = { getClientRects: () => [1], focus: () => { focused = 'first' } }
  const last = { getClientRects: () => [1], focus: () => { focused = 'last' } }
  const ownerDocument = { activeElement: last }
  state.dialog.value = vue.markRaw({ ownerDocument, querySelectorAll: () => [first, last] })
  state.trapFocus({ key: 'Tab', shiftKey: false, preventDefault: () => prevented++ })
  assert.equal(focused, 'first')
  ownerDocument.activeElement = first
  state.trapFocus({ key: 'Tab', shiftKey: true, preventDefault: () => prevented++ })
  assert.equal(focused, 'last')
  assert.equal(prevented, 2)
})

test('Все виды примеров можно увеличить, вернуть к описанию и сбросить масштаб', async () => {
  const code = ts.transpileModule(compileScript(descriptor, { id: 'pro-dialog-zoom' }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const localModule = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => id === 'vue'
    ? { ...vue, onMounted() {}, onBeforeUnmount() {}, watch() {}, useId: () => 'zoom-test' }
    : resolve(id), localModule, localModule.exports)
  const events = []
  const state = localModule.exports.default.setup({ open: false, feature: 'team', canManageBilling: true }, {
    expose() {},
    emit: (...event) => events.push(event)
  })

  for (const visualType of ['screenshot', 'preview', 'demo']) {
    assert.ok(Object.values(proFeatureDetails).some(detail => detail.visual.type === visualType))
  }

  state.expandVisual()
  assert.equal(state.isExpanded.value, true)
  state.increaseZoom()
  state.increaseZoom()
  assert.equal(state.zoomScale.value, 1.5)
  state.resetZoom()
  assert.equal(state.zoomScale.value, 1)
  state.increaseZoom()
  state.increaseZoom()
  const firstEscape = { preventDefault() { this.prevented = true } }
  state.onDialogCancel(firstEscape)
  assert.equal(firstEscape.prevented, true)
  assert.equal(state.isExpanded.value, false)
  assert.equal(state.zoomScale.value, 1)
  assert.deepEqual(events, [])

  state.expandVisual()
  state.onDialogCancel({ preventDefault() {} })
  state.onDialogCancel({ preventDefault() {} })
  assert.deepEqual(events, [['close']])
})

test('Увеличение из общего обзора по Escape возвращает в обзор без второго описания', () => {
  const code = ts.transpileModule(compileScript(descriptor, { id: 'overview-visual-test' }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const localModule = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => id === 'vue'
    ? { ...vue, onMounted() {}, onBeforeUnmount() {}, watch() {}, useId: () => 'overview-visual-test' }
    : resolve(id), localModule, localModule.exports)
  const events = []
  const state = localModule.exports.default.setup({ open: true, feature: 'team', canManageBilling: false, visualOnly: true }, {
    expose() {}, emit: (...event) => events.push(event)
  })
  assert.equal(state.isExpanded.value, true)
  state.onDialogCancel({ preventDefault() {} })
  assert.deepEqual(events, [['close']])
})


test('Пример роли переключает права без изменения настроек организации', () => {
  const code = ts.transpileModule(compileScript(teamDescriptor, { id: 'team-role-state' }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const local = { exports: {} }
  new Function('require', 'module', 'exports', code)(resolve, local, local.exports)
  const events = []
  const state = local.exports.default.setup({ titleId: 'team', title: 'Команда', canManageBilling: false }, { expose() {}, emit: (...event) => events.push(event) })
  assert.equal(state.accessExample.value[3], 'Закрыто')
  state.selectedRole.value = 'admin'
  assert.equal(state.accessExample.value[3], 'Просмотр')
  assert.equal(state.accessExample.value[1], 'Просмотр и изменение')
  assert.deepEqual(events, [])
})

test('Увеличение картинки лендинга возвращает фокус на исходную кнопку', async () => {
  const code = ts.transpileModule(compileScript(descriptor, { id: 'landing-image-focus' }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const local = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => id === 'vue'
    ? { ...vue, onMounted() {}, onBeforeUnmount() {}, watch() {}, useId: () => 'landing-image-focus' }
    : resolve(id), local, local.exports)
  const state = local.exports.default.setup({ open: false, feature: 'team', canManageBilling: true }, { expose() {}, emit() {} })
  let focused = false
  state.landingPreviewOpener.value = vue.markRaw({ focus: () => { focused = true } })
  state.landingPreview.value = { src: 'team-image.webp', alt: 'Команда' }
  state.expandVisual()
  state.increaseZoom()
  state.returnToDescription()
  await vue.nextTick()
  assert.equal(state.isExpanded.value, false)
  assert.equal(state.landingPreview.value, null)
  assert.equal(state.zoomScale.value, 1)
  assert.equal(focused, true)
})


test('Каждая новая модалка показывает свой сценарий и сохраняет границы покупки', async () => {
  for (const [feature, content] of Object.entries(proFeatureLandingContent)) {
    const html = await render(feature, true)
    assert.ok(content.headline.every(line => html.includes(line)), feature)
    assert.ok(content.steps.every(step => html.includes(step.title)), feature)
    assert.equal(content.benefitTitles.length, proFeatureDetails[feature].points.length, feature)
    assert.match(html, /href="#billing"/)
    assert.doesNotMatch(html, /undefined/)
    if (proFeatureDetails[feature].beta) assert.match(html, /БЕТА/)
  }
})

test('Для любой возможности просмотр без описания остаётся увеличенным примером', async () => {
  for (const feature of Object.keys(proFeatureDetails)) {
    const context = {}
    await renderToString(vue.h(module.exports.default, { open: false, feature, canManageBilling: true, visualOnly: true }), context)
    assert.match(context.teleports.body, /Увеличенный пример/)
    assert.doesNotMatch(context.teleports.body, /href="#billing"/)
    assert.doesNotMatch(context.teleports.body, /От первого шага/)
  }
})

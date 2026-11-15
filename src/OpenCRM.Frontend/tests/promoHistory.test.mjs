import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { renderToString } from '@vue/server-renderer'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspacePromoCodeSection.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'promo-history' }).content, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
function setup(load) {
  const props = vue.reactive({ organizationId: 'org', disabled: false, historyOnly: false })
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => id === 'vue' ? vue : id.startsWith('../api/') ? { loadWorkspacePromoHistory: load } : {}, module, module.exports)
  const state = module.exports.default.setup(props, { expose() {}, emit() {} })
  return { props, state }
}
async function flush() { await vue.nextTick(); await Promise.resolve(); await vue.nextTick() }

test('Страница тарифов не загружает историю; отдельная страница запрашивает только выбранную порцию', async () => {
  const calls = []
  const { state, props } = setup(async (organizationId, page) => { calls.push({ organizationId, page }); return { items: [{ id: page, code: 'CODE' }], totalCount: 41, page, pageSize: 20 } })
  await flush()
  assert.equal(calls.length, 0)
  props.historyOnly = true
  await flush()
  assert.deepEqual(calls, [{ organizationId: 'org', page: 1 }])
  state.historyPage.value = 2
  await flush()
  assert.equal(calls[1].page, 2)
  assert.equal(state.history.value[0].id, 2)
  assert.equal(state.totalCount.value, 41)
})

test('Медленный ответ истории прежней организации не заменяет новую историю', async () => {
  const pending = []
  const { state, props } = setup((organizationId, page) => new Promise(resolve => pending.push({ organizationId, page, resolve })))
  props.historyOnly = true
  await flush()
  props.organizationId = 'another-org'
  await flush()
  pending[1].resolve({ items: [{ id: 2 }], totalCount: 1, page: 1, pageSize: 20 })
  await flush()
  pending[0].resolve({ items: [{ id: 1 }], totalCount: 50, page: 1, pageSize: 20 })
  await flush()
  assert.deepEqual(state.history.value.map(item => item.id), [2])
  assert.equal(state.totalCount.value, 1)
})

test('История доступна кнопкой справа в шапке промокода и не запрашивается при показе формы', async () => {
  const compiled = ts.transpileModule(compileScript(descriptor, { id: 'promo-header', inlineTemplate: true }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  let historyReads = 0
  const module = { exports: {} }
  const button = vue.defineComponent({
    props: ['href', 'disabled'],
    setup: (props, { slots }) => () => vue.h(props.href ? 'a' : 'button', { href: props.href, disabled: props.disabled }, slots.default?.())
  })
  new Function('require', 'module', 'exports', compiled)(id => {
    if (id === 'vue') return vue
    if (id === '@opencrm/ui') return { UiButton: button, UiInput: vue.defineComponent({ setup: () => () => vue.h('input') }) }
    if (id.startsWith('../api/')) return { loadWorkspacePromoHistory: () => { historyReads++; throw new Error('История не должна загружаться вместе с формой.') } }
    return {}
  }, module, module.exports)
  const html = await renderToString(vue.createSSRApp({
    render: () => vue.h(module.exports.default, { organizationId: 'org', disabled: false })
  }))
  assert.match(html, /<header[^>]*>[\s\S]*Промокод[\s\S]*<a href="#billing\?tab=promocodes"[^>]*>История<\/a><\/header>/)
  assert.doesNotMatch(html, /История промокодов →/)
  assert.equal(historyReads, 0)
})

import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceCommonSection.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'workspace-actions' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function setup(create) {
  const module = { exports: {} }
  const catalog = { services: [], serviceBundles: [], workPosts: [], materials: [] }
  const resolve = id => {
    if (id === 'vue') return { ...vue, onMounted() {}, onUnmounted() {}, watch() {} }
    if (id === '../api/scenarioCatalog') return {
      createScenarioCatalogItem: create,
      loadScenarioCatalog: async () => catalog
    }
    if (id === '../api/workspaceAdmin') return { WorkspaceActionError: class extends Error {} }
    return {}
  }
  new Function('require', 'module', 'exports', 'window', code)(resolve, module, module.exports, { location: { hash: '#catalog' } })
  const state = module.exports.default.setup({
    context: { organization: { id: 'org' }, workspaceAccess: { isReadOnly: false }, shell: { menu: [] } },
    sectionCode: 'catalog'
  }, { expose() {}, emit() {} })
  state.settings.value = { canManageProfile: true, scenarioCatalog: catalog }
  return state
}

function deferred() {
  let resolve
  const promise = new Promise(complete => { resolve = complete })
  return { promise, resolve }
}

test('Повторная отправка создания услуги во время запроса не создаёт дубль', async () => {
  const pending = deferred()
  const calls = []
  const state = setup(async (...args) => { calls.push(args); await pending.promise })
  Object.assign(state.catalogCreateForms.service, { name: 'QA Мойка', unitPrice: '500' })

  state.addCatalogItem('service')
  state.addCatalogItem('service')
  assert.equal(calls.length, 1)
  assert.equal(state.isSaving.value, true)
  assert.equal(state.catalogCreateForms.service.name, 'QA Мойка')
  pending.resolve()
  await new Promise(resolve => setImmediate(resolve))
  assert.equal(state.isSaving.value, false)
  assert.equal(state.catalogCreateForms.service.name, '')
})

test('Отклонённое повторное действие не снимает блокировку первого запроса', async () => {
  const pending = deferred()
  const state = setup()
  const first = state.runAction(async () => { await pending.promise; return {} }, { refreshCatalogOnSuccess: true })
  let repeated = false
  const second = await state.runAction(async () => { repeated = true; return {} })

  assert.equal(second, false)
  assert.equal(repeated, false)
  assert.equal(state.isSaving.value, true)
  pending.resolve()
  assert.equal(await first, true)
  assert.equal(state.isSaving.value, false)
})

test('Ошибка создания сохраняет поля и разрешает повторную попытку после завершения запроса', async () => {
  let calls = 0
  const state = setup(async () => { if (++calls === 1) throw new Error('QA Ошибка связи') })
  Object.assign(state.catalogCreateForms.service, { name: 'QA Мойка', unitPrice: '0', description: 'Черновик' })

  state.addCatalogItem('service')
  await new Promise(resolve => setImmediate(resolve))
  assert.match(state.actionErrorMessage.value, /Ошибка связи/)
  assert.equal(state.isSaving.value, false)
  assert.equal(state.catalogCreateForms.service.name, 'QA Мойка')
  assert.equal(state.catalogCreateForms.service.unitPrice, '0')
  state.addCatalogItem('service')
  await new Promise(resolve => setImmediate(resolve))
  assert.equal(calls, 2)
  assert.equal(state.isSaving.value, false)
  assert.equal(state.catalogCreateForms.service.name, '')
})

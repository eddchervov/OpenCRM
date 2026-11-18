import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '../packages/workspace/src/commonSectionMessages.ts'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { formattingCulture, i18n } from '@opencrm/ui/i18n'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceCommonSection.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'workspace-actions' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function setup(create, scenarioCode = 'autoservice') {
  const module = { exports: {} }
  const catalog = { services: [], serviceBundles: [], workPosts: [], materials: [] }
  const resolve = id => {
    if (id === 'vue') return { ...vue, onMounted() {}, onUnmounted() {}, watch() {} }
    if (id === '@opencrm/ui') return regionalTestDependencies['@opencrm/ui/i18n']
    if (id === '@opencrm/ui/i18n') return regionalTestDependencies['@opencrm/ui/i18n']
    if (id === '../commonSectionMessages') return {}
    if (id === '../api/scenarioCatalog') return {
      createScenarioCatalogItem: create,
      loadScenarioCatalog: async () => catalog
    }
    if (id === '../api/workspaceAdmin') return { WorkspaceActionError: class extends Error {} }
    return {}
  }
  new Function('require', 'module', 'exports', 'window', code)(resolve, module, module.exports, { location: { hash: '#catalog' } })
  const state = module.exports.default.setup({
    context: { organization: { id: 'org', primaryScenarioCode: scenarioCode, timeZoneId: 'America/Los_Angeles' }, workspaceAccess: { isReadOnly: false }, shell: { scenario: { code: scenarioCode }, menu: [] } },
    sectionCode: 'catalog'
  }, { expose() {}, emit() {} })
  state.settings.value = { canManageProfile: true, scenarioCatalog: catalog }
  return state
}

test('Заголовок раздела и системные метаданные каталога переводятся по стабильным кодам', () => {
  const previousLocale = i18n.global.locale.value
  const previousCulture = formattingCulture.value
  try {
    i18n.global.locale.value = 'en'
    formattingCulture.value = 'en-GB'
    const state = setup()
    assert.equal(state.sectionTitle.value, 'Catalogs')
    assert.equal(state.scenarioTitle.value, 'Auto service')
    assert.equal(state.catalogGroups.value[0].title, 'Services')
    assert.equal(state.catalogGroups.value[2].emptyText, 'No work bays have been set up yet.')
    assert.equal(state.settingsTitle.value, 'Organization settings')
    assert.equal(setup(undefined, 'detailing').settingsTitle.value, 'Detailing center settings')
    assert.equal(state.formatPostDate('2026-04-02'), '2 Apr 2026')
    assert.equal(state.formatPostDate('2026-04-02T01:30:00Z'), '1 Apr 2026')

    i18n.global.locale.value = 'zh-Hans'
    assert.equal(state.sectionTitle.value, '目录')
    assert.equal(state.catalogGroups.value[0].title, '服务')
  } finally {
    i18n.global.locale.value = previousLocale
    formattingCulture.value = previousCulture
  }
})

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

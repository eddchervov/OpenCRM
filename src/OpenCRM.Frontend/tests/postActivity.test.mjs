import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceCommonSection.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'post-activity' }).content, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
function setup(load) {
  const updates = []
  const props = vue.reactive({ context: { organization: { id: 'org' }, workspaceAccess: { isReadOnly: false }, shell: { menu: [] } }, sectionCode: 'catalog' })
  const catalogApi = {
    loadWorkPostAffectedMaintenances: load,
    updateScenarioCatalogItem: async (...args) => updates.push(args),
    loadScenarioCatalog: async () => ({ services: [], serviceBundles: [], workPosts: [], materials: [] })
  }
  const module = { exports: {} }
  const resolve = id => id === 'vue' ? { ...vue, onMounted() {}, onUnmounted() {} } : id === '../api/scenarioCatalog' ? catalogApi : {}
  new Function('require', 'module', 'exports', 'window', code)(resolve, module, module.exports, { location: { hash: '#catalog' } })
  const state = module.exports.default.setup(props, { expose() {}, emit() {} })
  state.settings.value = { canManageProfile: true }
  state.postActivityDialog.value = { showModal() {}, close() {} }
  const post = { id: 'post', kindCode: 'work_post', name: 'Пост 1', sortOrder: 10, isActive: true, isUsed: true, serviceIds: [] }
  return { state, props, updates, post }
}

test('Подтверждённое закрытие сохраняет данные поста и меняет только доступность', async () => {
  const { state, updates, post } = setup(async () => ({ totalCount: 1, items: [{ maintenanceId: 'record' }] }))
  await state.changePostActivity(post)
  assert.equal(updates.length, 0)
  assert.equal(state.affectedMaintenances.value.totalCount, 1)
  await state.confirmPostActivity()
  assert.equal(updates.length, 1)
  assert.equal(updates[0][2].name, post.name)
  assert.equal(updates[0][2].sortOrder, post.sortOrder)
  assert.equal(updates[0][2].isActive, false)
})

test('Ошибка чтения назначений не позволяет подтвердить закрытие', async () => {
  const { state, updates, post } = setup(async () => { throw Error('Нет соединения') })
  await state.changePostActivity(post)
  await state.confirmPostActivity()
  assert.equal(updates.length, 0)
  assert.equal(state.postActivityError.value, 'Нет соединения')
})

test('Управляющий постами без просмотра работ может закрыть пост без раскрытия назначений', async () => {
  const { state, updates, post } = setup(async () => ({ totalCount: 0, items: [], canViewMaintenances: false }))
  await state.changePostActivity(post)
  await state.confirmPostActivity()
  assert.equal(updates.length, 1)
  assert.equal(updates[0][2].isActive, false)
})


test('API скрывает назначения при отказе в просмотре и не маскирует сбой сервера', async () => {
  const apiSource = readFileSync(new URL('../packages/workspace/src/api/scenarioCatalog.ts', import.meta.url), 'utf8')
  const apiCode = ts.transpileModule(apiSource, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  for (const status of [403, 500]) {
    const module = { exports: {} }
    new Function('module', 'exports', 'fetch', apiCode)(module, module.exports, async () => ({ ok: false, status }))
    if (status === 403) {
      assert.deepEqual(await module.exports.loadWorkPostAffectedMaintenances('org', 'post'), { totalCount: 0, items: [], canViewMaintenances: false })
    } else {
      await assert.rejects(() => module.exports.loadWorkPostAffectedMaintenances('org', 'post'), /Не удалось проверить назначения/)
    }
  }
})

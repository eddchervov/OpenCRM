import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { createMemberSelectOptions } from '../packages/workspace/src/utils/memberSelectOptions.ts'

const source = readFileSync(new URL('../packages/workspace/src/components/ClientsSection.vue', import.meta.url), 'utf8')
const { descriptor } = parse(source)
const code = ts.transpileModule(compileScript(descriptor, { id: 'clients-section-test' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function setup(organizationId, userId) {
  const module = { exports: {} }
  const watchers = []
  const hooks = {}
  const pageRequests = []
  const componentVue = {
    ...vue,
    onMounted(callback) { hooks.mounted = callback },
    onBeforeUnmount(callback) { hooks.unmounted = callback },
    watch(source, callback, options) { watchers.push({ source, callback, options }) }
  }
  const resolve = id => {
    if (id === '../utils/memberSelectOptions') return { createMemberSelectOptions }
    if (id === 'vue') return componentVue
    if (id === '@opencrm/ui') return { useLocalization: () => ({ t: key => ({ 'clients.noResponsible': 'Без ответственного' }[key] ?? key) }) }
    if (id === '../clientMessages') return {}
    if (id.endsWith('.vue')) return {}
    if (id === '../api/clients') return {
      loadClientsPage: async (...args) => {
        pageRequests.push(args)
        return { items: [], tags: [], segments: [], page: 1, pageSize: 10, totalCount: 0, allCount: 0, totalVehiclesCount: 0, totalMaintenanceCount: 0 }
      }
    }
    if (id === '../types') return {}
    throw new Error(`Не задана зависимость: ${id}`)
  }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const props = vue.reactive({
    context: {
      organization: { id: organizationId, members: [] },
      user: { id: userId },
      shell: { scenario: { code: 'autoservice', terms: {} } }
    }
  })
  const state = module.exports.default.setup(props, { expose() {}, emit() {} })
  return { state, props, watchers, hooks, pageRequests }
}

test('Фильтр ответственных обновляет имена и убирает почту после исчезновения совпадения', () => {
  const { state, props } = setup('org', 'user')
  props.context.organization.members = [
    { userId: 'one', employeeName: 'Иван', email: 'contact@example.invalid' },
    { userId: 'two', employeeName: 'Иван', email: 'contact@example.invalid' }
  ]

  assert.deepEqual(state.responsibleFilterOptions.value.slice(2), [
    { value: 'one', label: 'Иван (contact@example.invalid)' },
    { value: 'two', label: 'Иван (contact@example.invalid)' }
  ])
  props.context.organization.members[1].employeeName = 'Пётр'
  assert.deepEqual(state.responsibleFilterOptions.value.slice(2), [
    { value: 'one', label: 'Иван' },
    { value: 'two', label: 'Пётр' }
  ])
})

test('Список клиентов восстанавливает безопасные фильтры из хранилища текущих организации и пользователя', () => {
  const previousWindow = globalThis.window
  const states = new Map([
    ['opencrm-workspace:org-a:user-a:clients:list-state', JSON.stringify({
      searchQuery: ' BMW ', sort: 'unexpected', segmentCode: 'all', statusCode: 'broken',
      partyTypeCode: 'company', responsibleUserId: 'user-2', tagId: 'tag-2',
      isArchiveMode: true, page: 4, pageSize: 25
    })]
  ])
  globalThis.window = {
    sessionStorage: {
      getItem: key => states.get(key) ?? null,
      setItem: (key, value) => states.set(key, value)
    }
  }

  try {
    const restored = setup('org-a', 'user-a').state
    assert.equal(restored.searchQuery.value, ' BMW ')
    assert.equal(restored.selectedPartyTypeCode.value, 'company')
    assert.equal(restored.selectedSort.value, 'name_asc')
    assert.equal(restored.selectedStatusCode.value, '')
    assert.equal(restored.isArchiveMode.value, true)
    assert.equal(restored.page.value, 4)
    assert.equal(restored.pageSize.value, 25)

    const isolated = setup('org-b', 'user-a').state
    assert.equal(isolated.searchQuery.value, '')
    assert.equal(isolated.page.value, 1)
  } finally {
    globalThis.window = previousWindow
  }
})

test('Смена организации сбрасывает фильтры, если для новой области нет сохранённого состояния', async () => {
  const previousWindow = globalThis.window
  const states = new Map([
    ['opencrm-workspace:org-a:user-a:clients:list-state', JSON.stringify({
      searchQuery: 'Старый поиск', sort: 'created_desc', segmentCode: 'repeat', statusCode: 'active',
      partyTypeCode: 'company', responsibleUserId: 'user-2', tagId: 'tag-2',
      isArchiveMode: true, page: 4, pageSize: 25
    })]
  ])
  const clearedTimers = []
  globalThis.window = {
    sessionStorage: {
      getItem: key => states.get(key) ?? null,
      setItem: (key, value) => states.set(key, value)
    },
    localStorage: { getItem: () => null, setItem() {} },
    setTimeout: () => 42,
    clearTimeout: timer => clearedTimers.push(timer)
  }

  try {
    const fixture = setup('org-a', 'user-a')
    const scopeWatcher = fixture.watchers.find(watcher => Array.isArray(watcher.source)
      && watcher.source.length === 2
      && watcher.source.every(source => typeof source === 'function'))

    fixture.props.context.organization.id = 'org-b'
    scopeWatcher.callback()
    await new Promise(resolve => setImmediate(resolve))

    assert.equal(fixture.state.searchQuery.value, '')
    assert.equal(fixture.state.selectedSort.value, 'name_asc')
    assert.equal(fixture.state.selectedPartyTypeCode.value, '')
    assert.equal(fixture.state.selectedResponsibleUserId.value, '')
    assert.equal(fixture.state.selectedStatusCode.value, '')
    assert.equal(fixture.state.selectedTagId.value, '')
    assert.equal(fixture.state.selectedSegmentCode.value, 'all')
    assert.equal(fixture.state.isArchiveMode.value, false)
    assert.equal(fixture.state.page.value, 1)
    assert.equal(fixture.state.pageSize.value, 10)
    assert.deepEqual(fixture.pageRequests.map(([organizationId]) => organizationId), ['org-b'])
    assert.equal(JSON.parse(states.get('opencrm-workspace:org-a:user-a:clients:list-state')).searchQuery, 'Старый поиск')

    fixture.state.searchQuery.value = 'Запрос'
    const searchWatcher = fixture.watchers.find(watcher => watcher.source === fixture.state.searchQuery)
    searchWatcher.callback()
    fixture.hooks.unmounted()
    assert.ok(clearedTimers.includes(42))
    assert.equal(JSON.parse(states.get('opencrm-workspace:org-b:user-a:clients:list-state')).searchQuery, 'Запрос')
  } finally {
    globalThis.window = previousWindow
  }
})

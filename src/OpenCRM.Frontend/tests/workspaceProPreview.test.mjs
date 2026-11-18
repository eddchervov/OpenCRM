import { regionalTestDependencies, localizationTestContext } from './regionalTestDependencies.mjs'
import * as workspaceChromeMessages from '../packages/workspace/src/workspaceChromeMessages.ts'
import '../packages/workspace/src/permissionMessages.ts'
import '../packages/workspace/src/employeeMessages.ts'
import '../packages/workspace/src/interactionMessages.ts'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { renderToString } from '@vue/server-renderer'
import { getProjectBrand } from '../packages/ui/src/projectBrand.ts'

function compile(name, inlineTemplate = false) {
  const filename = new URL(`../packages/workspace/src/components/${name}.vue`, import.meta.url)
  const { descriptor } = parse(readFileSync(filename, 'utf8'))
  return ts.transpileModule(compileScript(descriptor, { id: name, inlineTemplate }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
}

function preview(name) {
  const mounted = []
  const reads = []
  const timers = []
  const props = vue.reactive({
    previewOnly: true,
    organizationId: 'org',
    context: {
      user: { id: 'owner' }, organization: { id: 'org', timeZoneId: 'UTC', allowWorkSelfAssignment: true },
      shell: { menu: [{ code: 'members', isVisible: true }] }, workspaceAccess: { isReadOnly: false }
    }
  })
  const resolve = id => {
    if (id in regionalTestDependencies) return regionalTestDependencies[id]
    if (id === 'vue') return {
      ...vue, onMounted: callback => mounted.push(callback), onBeforeUnmount() {}, onUnmounted() {}
    }
    if (id === '@opencrm/ui') return { ...localizationTestContext, formatDateOnly: () => '2026-10-29', parseDateOnly: value => value, getProjectBrand }
    if (id === '@opencrm/ui/projectBrand') return { getProjectBrand }
    if (id === '../utils/organizationTimeZone') return { organizationTimeZoneName: value => value }
    if (id === '../composables/conversationUpdates') return { useConversationUpdates: () => ({ revision: vue.ref(''), unreadCount: vue.ref(0), connected: vue.ref(false), refresh() {} }) }
    if (id.startsWith('../api/')) return new Proxy({}, {
      get: (_target, method) => async (...args) => { reads.push({ method, args }); throw new Error('Предпросмотр не должен вызывать API.') }
    })
    return {}
  }
  const module = { exports: {} }
  new Function('require', 'module', 'exports', 'setInterval', 'window', 'setTimeout', compile(name))(
    resolve, module, module.exports, (...args) => { timers.push(args); return 1 },
    { location: { hash: '#messages' }, addEventListener() {}, removeEventListener() {} },
    (...args) => { timers.push(args); return 1 }
  )
  const state = module.exports.default.setup(props, { expose() {}, emit() {} })
  return { props, state, reads, timers, async mount() { for (const callback of mounted) await callback() } }
}

for (const name of ['WorkspaceEmployeesSection', 'WorkspaceShiftsSection', 'WorkspaceWorkSection', 'MessagesSection', 'MessageTemplateCatalog']) {
  test(`${name}: закрытая форма не загружает данные при открытии, повторе и смене организации`, async () => {
    const screen = preview(name)
    await screen.mount()
    await screen.state.load()
    screen.props.context.organization.id = 'other-org'
    screen.props.organizationId = 'other-org'
    if (screen.state.weekStart) screen.state.weekStart.value = '2026-11-02'
    if (screen.state.search) screen.state.search.value = 'Поиск'
    await vue.nextTick()
    assert.deepEqual(screen.reads, [])
    assert.deepEqual(screen.timers, [])
  })
}

test('Накладка блокирует содержимое, а возможности открываются отдельной кнопкой без перехода к тарифам', async () => {
  const module = { exports: {} }
  new Function('require', 'module', 'exports', compile('WorkspaceProLock', true))(
    id => regionalTestDependencies[id] ?? (id === 'vue' ? { ...vue, inject: (_key, fallback) => vue.computed(() => true) } : localizationTestContext), module, module.exports
  )
  const component = module.exports.default
  const slot = () => vue.h('button', { type: 'button' }, 'Добавить смену')
  const locked = await renderToString(vue.h(component, { locked: true, message: 'Смены доступны в PRO', feature: 'shifts' }, slot))
  assert.match(locked, /inert/)
  assert.match(locked, /aria-hidden="true"/)
  assert.match(locked, /Добавить смену/)
  assert.match(locked, /Посмотреть возможности/)
  assert.doesNotMatch(locked, /href="#billing"/)
  assert.match(locked, /Смены доступны в PRO/)
  const unlocked = await renderToString(vue.h(component, { locked: false, message: 'Смены доступны в PRO' }, slot))
  assert.doesNotMatch(unlocked, /inert|aria-hidden|href="#billing"/)
  assert.match(unlocked, /Добавить смену/)
  assert.doesNotMatch(unlocked, /Посмотреть возможности/)
})

test('Метка PRO отображается только на Free и только у закрытого платного пункта', async () => {
  const module = { exports: {} }
  new Function('require', 'module', 'exports', compile('WorkspaceCrmNavigation', true))(
    id => id === 'vue' ? vue : id === '../workspaceChromeMessages' ? workspaceChromeMessages : localizationTestContext, module, module.exports
  )
  const sections = [
    { code: 'clients', title: 'Клиенты', isAvailable: true },
    { code: 'statistics', title: 'Статистика', isAvailable: false },
    { code: 'wiki', title: 'Вики', isAvailable: true }
  ]
  const free = await renderToString(vue.h(module.exports.default, { sections, activeSection: 'statistics', showProBadges: true }))
  assert.equal([...free.matchAll(/class="crm-navigation__pro"/g)].length, 1)
  const paid = await renderToString(vue.h(module.exports.default, { sections, activeSection: 'statistics', showProBadges: false }))
  assert.doesNotMatch(paid, /class="crm-navigation__pro"/)
})

test('История сообщений открывается явно, сбрасывается при переходах, а Free не запускает обновления переписки', async () => {
  let updatesOrganization
  const module = { exports: {} }
  const resolve = id => {
    if (id in regionalTestDependencies) return regionalTestDependencies[id]
    if (id === 'vue') return { ...vue, provide() {}, onMounted() {}, onBeforeUnmount() {} }
    if (id === '@opencrm/ui') return { ...localizationTestContext, pageBreadcrumbsKey: Symbol('breadcrumbs'), workspaceTheme: vue.ref('light'), getProjectBrand }
    if (id === '@opencrm/ui/projectBrand') return { getProjectBrand }
    if (id === '../composables/conversationUpdates') return {
      provideConversationUpdates: organization => { updatesOrganization = organization; return { unreadCount: vue.ref(0) } }
    }
    return {}
  }
  new Function('require', 'module', 'exports', 'document', compile('WorkspaceCrmShell'))(
    resolve, module, module.exports, { querySelector: () => null }
  )
  const props = vue.reactive({
    context: { organization: { id: 'org' }, workspaceAccess: { isFreePlan: true, commercialEnabled: true }, shell: {
      scenario: { modules: { messaging: false } },
      menu: [{ code: 'messages', title: 'Сообщения', isVisible: true, isAvailable: false }]
    } },
    sections: [{ code: 'messages', title: 'Сообщения' }], activeSection: 'messages',
    breadcrumbs: [], profileLinks: [], displayName: 'Владелец'
  })
  const state = module.exports.default.setup(props, { expose() {}, emit() {} })
  assert.equal(state.showMessageHistory.value, false)
  assert.equal(state.lockedSectionMessage.value, 'Сообщения доступны в PRO')
  assert.equal(updatesOrganization.value, null)
  state.showMessageHistory.value = true
  props.activeSection = 'statistics'
  await vue.nextTick()
  assert.equal(state.showMessageHistory.value, false)
  assert.equal(state.lockedSectionMessage.value, 'Статистика доступна в PRO')
  state.showMessageHistory.value = true
  props.context.organization.id = 'other-org'
  await vue.nextTick()
  assert.equal(state.showMessageHistory.value, false)
  props.context.shell.scenario.modules.messaging = true
  assert.equal(updatesOrganization.value, 'other-org')
})

test('Предпросмотр ролей показывает матрицу прав с заблокированными переключателями без сообщения об отсутствии ролей', async () => {
  const module = { exports: {} }
  const ui = {
    getProjectBrand,
    UiInput: { render: () => vue.h('input', { disabled: true }) },
    UiButton: { render() { return vue.h('button', { disabled: true }, this.$slots.default?.()) } }
  }
  new Function('require', 'module', 'exports', compile('WorkspaceRolePermissions', true))(
    id => regionalTestDependencies[id] ?? (id === 'vue' ? vue : id === '@opencrm/ui' ? ui : {}), module, module.exports
  )
  const html = await renderToString(vue.h(module.exports.default, {
    roles: [], previewOnly: true, canManage: false, isEditing: false, isSaving: false, drafts: {}
  }))
  assert.match(html, /Права роли/)
  assert.match(html, /Настроить права/)
  assert.match(html, /Клиенты/)
  assert.equal([...html.matchAll(/type="checkbox" disabled/g)].length, 9)
  assert.doesNotMatch(html, /Роли не найдены/)
})


test('Базовый режим сохраняет операционный запрет без рекламы PRO и переходов к оплате', async () => {
  const module = { exports: {} }
  new Function('require', 'module', 'exports', compile('WorkspaceProLock', true))(
    id => regionalTestDependencies[id] ?? (id === 'vue' ? { ...vue, inject: (_key, fallback) => fallback } : {}), module, module.exports
  )
  const html = await renderToString(vue.h(module.exports.default,
    { locked: true, message: 'Смены доступны в PRO', feature: 'shifts' },
    () => vue.h('button', {}, 'Добавить смену')))
  assert.match(html, /inert/)
  assert.match(html, /Функция отключена в настройках установки/)
  assert.doesNotMatch(html, /PRO|Посмотреть возможности|href="#billing"/)
})

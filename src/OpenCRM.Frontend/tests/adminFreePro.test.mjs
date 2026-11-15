import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const root = new URL('../Admin/admin-client/src/', import.meta.url)

function evaluate(source, dependencies = {}) {
  const module = { exports: {} }
  const compiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  new Function('require', 'module', 'exports', compiled)(id => {
    if (id === 'vue') return vue
    if (id in dependencies) return dependencies[id]
    throw new Error(`Не задана зависимость ${id}`)
  }, module, module.exports)
  return module.exports
}

const policy = evaluate(readFileSync(new URL('utils/subscriptionPolicy.ts', root), 'utf8'))
const ApiError = class extends Error {}

function setup(view, apis = {}) {
  const { descriptor } = parse(readFileSync(new URL(`views/${view}.vue`, root), 'utf8'))
  const dependencies = {
    '@/utils/subscriptionPolicy': policy,
    '@/api/ApiError': { ApiError },
    '@/stores/adminCurrentUserStore': { useAdminCurrentUserStore: () => ({ isSystem: true, currentUser: { commercialEnabled: true, permissions: [] } }) },
    'vue-router': { useRoute: () => ({ params: {}, query: {} }), useRouter: () => ({ replace() {} }) },
    '@/components/PaymentServiceDetails.vue': {},
    ...apis
  }
  const component = evaluate(compileScript(descriptor, { id: view }).content, dependencies).default
  let state
  const app = vue.createSSRApp({ setup() {
    state = component.setup({}, { expose() {} })
    return () => null
  } })
  return import('@vue/server-renderer').then(async ({ renderToString }) => {
    await renderToString(app)
    return state
  })
}

const free = { id: 1, name: 'Free', description: '', order: 0, planTypeCode: 'business', group: 'workspace.free.v1', isActive: true,
  isDefaultPlan: false, isDemoPlan: false, price: 0, discountPrice: null, durationDays: 30, vehicleLimit: 300 }
const paid = { ...free, id: 2, name: 'PRO', group: null, price: 1000 }
const limits = [{ featureId: 1, code: 'workspace.clients.limit', isAssigned: true, intValue: 300 }]

test('Neutral user card blocks commercial actions and does not request plans', async () => {
  let planRequests = 0
  const currentUser = { isSystem: false, currentUser: { commercialEnabled: false, permissions: ['Admin.Billing.Manage'] } }
  const state = await setup('UserOverviewView', {
    '@/stores/adminCurrentUserStore': { useAdminCurrentUserStore: () => currentUser },
    '@/api/adminUsersApi': { getAdminUserOverview: async () => ({ profile: { id: 'owner' }, organizations: [], subscriptions: { users: [], organizations: [], history: [] } }) },
    '@/api/adminBillingApi': { getAdminAvailablePlans: async () => { ++planRequests; return [] } }
  })
  assert.equal(state.canManageBilling.value, false)
  assert.deepEqual(state.visibleTabs.value.map(tab => tab.value), ['overview', 'vehicles', 'organizations', 'support'])
  assert.equal(state.visibleTabs.value.find(tab => tab.value === 'organizations').label, 'Организации')
  state.openManagementModal()
  await Promise.resolve()
  assert.equal(planRequests, 0)
})

test('Commercial routes are guarded before their views can request data', async () => {
  const source = readFileSync(new URL('router/index.ts', root), 'utf8')
  const profile = { isSystem: true, commercialEnabled: false, permissions: [] }
  let routes, guard
  const dependencies = Object.fromEntries([...source.matchAll(/from '([^']+)'/g)].map(match => [match[1], {}]))
  dependencies['vue-router'] = {
    createWebHashHistory: () => ({}),
    createRouter: options => { routes = options.routes; return { beforeEach: callback => { guard = callback } } }
  }
  dependencies['@/stores/adminCurrentUserStore'] = { useAdminCurrentUserStore: () => ({ load: async () => profile }) }
  evaluate(source, dependencies)
  const commercialRoutes = routes.filter(route => route.meta?.requiresCommercial)
  assert.deepEqual(commercialRoutes.map(route => route.path), [
    '/finance/billing', '/finance/billing/:recordType/:recordId', '/finance/subscription-plans', '/finance/promo-codes'
  ])
  for (const isSystem of [false, true]) {
    profile.isSystem = isSystem
    for (const route of commercialRoutes) {
      profile.permissions = [route.meta.requiresPermission]
      profile.commercialEnabled = false
      assert.deepEqual(await guard(route), { name: 'not-found' })
      profile.commercialEnabled = true
      assert.equal(await guard(route), true)
      profile.isSystem = false
      profile.permissions = []
      assert.equal(await guard(route), false)
      profile.isSystem = isSystem
    }
  }
  profile.commercialEnabled = false
  profile.permissions = ['Admin.Users.View']
  assert.equal(await guard(routes.find(route => route.path === '/users')), true)
})

test('Free/PRO определяется типом организации, системный Free — группой', () => {
  assert.equal(policy.usesFreePro('detailing'), true)
  assert.equal(policy.usesFreePro('service-station'), true)
  for (const code of ['fleet', 'personal', '', null, undefined]) assert.equal(policy.usesFreePro(code), false)
  assert.equal(policy.isFreePlan(free), true)
  assert.equal(policy.isFreePlan({ group: null, name: 'Free', price: 0 }), false)
  assert.equal(policy.isRequiredFreeLimit(free, 'workspace.clients.limit'), true)
  assert.equal(policy.isRequiredFreeLimit(free, 'workspace.vehicles.limit'), true)
  assert.equal(policy.isRequiredFreeLimit(free, 'workspace.assistant'), false)
  assert.equal(policy.isRequiredFreeLimit(paid, 'workspace.clients.limit'), false)
  assert.match(policy.subscriptionFallback('detailing'), /продолжит базовую работу на Free/)
  assert.doesNotMatch(policy.subscriptionFallback('fleet'), /на Free/)
})

test('Редактор отделяет Free и PRO от прежних тарифов и перечитывает сохранённый Free', async () => {
  const plans = [paid, { ...free, id: 3, group: null, isDemoPlan: true }, free,
    { ...free, id: 4, group: null, isDefaultPlan: true }, { ...paid, id: 5, planTypeCode: 'personal' }]
  let updated
  const state = await setup('SubscriptionPlansView', { '@/api/adminSubscriptionPlansApi': {
    getAdminSubscriptionPlans: async () => plans,
    getAdminPlanFeatures: async () => limits,
    updateAdminSubscriptionPlan: async (id, form) => { updated = form; return { ...free, id } }
  } })
  await state.loadPlans()
  assert.deepEqual(state.businessPlans.value.map(plan => plan.id), [1, 2])
  assert.deepEqual(state.historicalPlans.value.map(plan => plan.id), [3, 4, 5])
  await state.selectPlan(free)
  state.form.value.vehicleLimit = 450
  assert.equal(state.isSystemFree.value, true)
  assert.equal(state.isReadOnly.value, false)
  await state.savePlan()
  assert.equal(updated.vehicleLimit, 450)
  assert.equal(updated.group, 'workspace.free.v1')
  assert.equal(updated.isDefaultPlan, false)
  assert.equal(updated.isDemoPlan, false)
  assert.equal(state.selectedPlan.value.id, 1)
  assert.equal(state.features.value[0].intValue, 300)
})

test('Поздний ответ возможностей другого тарифа не перезаписывает текущий редактор', async () => {
  const pending = new Map()
  let saves = 0
  const state = await setup('SubscriptionPlansView', { '@/api/adminSubscriptionPlansApi': {
    getAdminPlanFeatures: id => new Promise(resolve => pending.set(id, resolve)),
    saveAdminPlanFeatures: async () => { ++saves }
  } })
  const first = state.selectPlan(free)
  await state.saveFeatures()
  assert.equal(saves, 0)
  const second = state.selectPlan(paid)
  pending.get(2)([{ ...limits[0], intValue: 900 }])
  await second
  pending.get(1)(limits)
  await first
  assert.equal(state.selectedPlan.value.id, 2)
  assert.equal(state.features.value[0].intValue, 900)
  assert.equal(state.isLoadingFeatures.value, false)
})

test('Карточка пользователя блокирует выдачу прежнего демо детейлингу и СТО', async () => {
  let grants = 0
  const state = await setup('UserOverviewView', {
    '@/api/adminUsersApi': {},
    '@/api/adminBillingApi': { getAdminAvailablePlans: async () => [], grantAdminOrganizationDemo: async () => { ++grants } }
  })
  state.overview.value = { profile: { id: 'owner' } }
  for (const primaryScenarioCode of ['detailing', 'service-station']) {
    const organization = { organizationId: 'org', primaryScenarioCode }
    state.openOrganizationConfirmation(organization, 'grant')
    assert.equal(state.organizationConfirmation.value, null)
    state.organizationConfirmation.value = { organization, action: 'grant' }
    state.organizationActionReason.value = 'Проверка'
    await state.confirmOrganizationAction()
    state.organizationConfirmation.value = null
  }
  assert.equal(grants, 0)
})

test('Биллинг открывает организации первым и не выдаёт им старое демо', async () => {
  let grants = 0
  const state = await setup('BillingView', { '@/api/adminBillingApi': {
    grantAdminOrganizationDemo: async () => { ++grants }
  } })
  assert.equal(state.subscriptionScope.value, 'organizations')
  state.selectedSubscription.value = { organizationId: 'org', primaryScenarioCode: 'detailing' }
  state.subscriptionAction.value = 'grantDemo'
  state.subscriptionReason.value = 'Проверка'
  await state.changeDemo()
  assert.equal(grants, 0)
})

test('Список организаций применяет последний поиск и очищает устаревшие данные при ошибке', async () => {
  const pending = []
  const state = await setup('OrganizationsView', { '@/api/adminOrganizationsApi': {
    getAdminOrganizations: (...args) => new Promise((resolve, reject) => pending.push({ args, resolve, reject }))
  } })
  const first = state.load()
  state.search.value = ' Студия '
  const second = state.load(true)
  assert.deepEqual(pending[1].args, ['Студия', '', 1])
  pending[1].resolve({ totalCount: 1, organizations: [{ id: 'new' }] })
  await second
  pending[0].resolve({ totalCount: 0, organizations: [] })
  await first
  assert.equal(state.result.value.organizations[0].id, 'new')
  const failed = state.load()
  pending[2].reject(new Error('Ошибка'))
  await failed
  assert.equal(state.result.value, null)
  assert.match(state.errorMessage.value, /Не удалось/)
})

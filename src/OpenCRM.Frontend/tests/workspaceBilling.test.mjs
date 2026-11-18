import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '@opencrm/workspace/billingMessages'
import { i18n, formattingCulture, setFormattingCulture } from '@opencrm/ui/i18n'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { renderToString } from '@vue/server-renderer'

function compile(inlineTemplate = false) {
  const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceBillingSection.vue', import.meta.url), 'utf8'))
  return ts.transpileModule(compileScript(descriptor, { id: 'billing', inlineTemplate }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
}

const passthrough = vue.defineComponent({ setup: (_, { slots }) => () => vue.h('div', slots.default?.()) })
const button = vue.defineComponent({
  props: ['disabled', 'href'],
  setup: (props, { slots, attrs }) => () => vue.h(props.href ? 'a' : 'button', { ...attrs, href: props.href, disabled: props.disabled }, slots.default?.())
})

function component(inlineTemplate = false) {
  const module = { exports: {} }
  new Function('require', 'module', 'exports', compile(inlineTemplate))(id => {
    if (regionalTestDependencies[id]) return regionalTestDependencies[id]
    if (id === '../billingMessages') return {}
    if (id === 'vue') return vue
    if (id === '@opencrm/ui') return { UiButton: button, UiTooltip: passthrough }
    return { default: vue.defineComponent({ setup: () => () => null }) }
  }, module, module.exports)
  return module.exports.default
}

function setup() {
  const props = vue.reactive({
    isSaving: false, historyOnly: false,
    billing: {
      organization: { id: 'org' }, effectivePlan: { id: 1, name: 'Free', group: 'workspace.free.v1' },
      currentSubscription: null, canManageBilling: true, canUseTestSubscriptions: false,
      workspaceFeatures: [{ code: 'workspace.clients.limit', intValue: 300 }],
      workspaceVehicleLimit: 300, workspaceMembersLimit: 1, availableBusinessPlans: [
        { id: 2, name: 'PRO', price: 1250, durationDays: 30, features: [
          { code: 'workspace.maintenance.intake', boolValue: true },
          { code: 'workspace.assistant', boolValue: false }
        ] }
      ]
    }
  })
  const events = []
  const state = component().setup(props, { expose() {}, emit: (...args) => events.push(args) })
  return { props, state, events }
}

test('Обзор тарифа открывается явно и не обещает выключенные возможности', () => {
  const { props, state } = setup()
  assert.equal(state.overviewOpen.value, false)
  const plan = props.billing.availableBusinessPlans[0]
  state.showPlanOverview(plan)
  assert.equal(state.overviewOpen.value, true)
  assert.equal(state.overviewPlan.value.id, plan.id)
  assert.deepEqual(state.getPlanHighlights(plan), ['Приёмка со схемой повреждений'])
  assert.deepEqual(state.getPlanHighlights({ name: 'Без состава' }), [])
})

test('Кнопка подключения из обзора соблюдает права, занятость и действующий тариф', async () => {
  const { props, state, events } = setup()
  const plan = props.billing.availableBusinessPlans[0]
  state.showPlanOverview(plan)
  props.billing.canManageBilling = false
  state.purchaseFromOverview(plan)
  props.billing.canManageBilling = true
  props.isSaving = true
  state.purchaseFromOverview(plan)
  props.isSaving = false
  state.purchaseFromOverview({ ...plan, id: 99 })
  props.billing.currentSubscription = { sourceCode: 'paid', endDate: '2099-01-01' }
  props.billing.effectivePlan = { id: 3, name: 'Действующий PRO' }
  state.purchaseFromOverview(plan)
  assert.deepEqual(events, [])
  props.billing.currentSubscription = null
  state.purchaseFromOverview(plan)
  assert.equal(state.overviewOpen.value, false)
  assert.deepEqual(events, [['purchasePlan', plan]])
})

test('Смена организации закрывает обзор и сбрасывает выбранный тариф', async () => {
  const { props, state } = setup()
  state.showPlanOverview(props.billing.availableBusinessPlans[0])
  props.billing.organization.id = 'other-org'
  await vue.nextTick()
  assert.equal(state.overviewOpen.value, false)
  assert.equal(state.overviewPlan.value, null)
})

test('Страница тарифов показывает понятные заголовки без постоянной рекламы промокода и сырого состава', async () => {
  const { props } = setup()
  const html = await renderToString(vue.createSSRApp({ render: () => vue.h(component(true), props) }))
  assert.match(html, /Что даёт PRO/)
  assert.match(html, /Подробнее о тарифе/)
  assert.match(html, />Тарифы<\/h3>/)
  assert.match(html, /Участников: 1/)
  assert.doesNotMatch(html, /30days|Что включено|Без ограничения срока\. Расширенная/)
})

test('Перевод тарифов сохраняет валюту подписки RUB при валюте организации CNY', async () => {
  const originalLocale = i18n.global.locale.value
  const originalCulture = formattingCulture.value
  try {
    const { props } = setup()
    props.billing.organization.currencyCode = 'CNY'
    for (const [locale, label] of [['en', 'Members: 1'], ['zh-Hans', '成员：1']]) {
      i18n.global.locale.value = locale
      setFormattingCulture('en-GB')
      const html = await renderToString(vue.createSSRApp({ render: () => vue.h(component(true), props) }))
      assert.ok(html.includes(label))
      assert.match(html, /RUB/)
      assert.doesNotMatch(html, /CNY|руб\./)
    }
  } finally { i18n.global.locale.value = originalLocale; setFormattingCulture(originalCulture) }
})

test('Срок продления переводится по данным тарифа без разбора русского текста сервера', () => {
  const originalLocale = i18n.global.locale.value
  try {
    const { props, state } = setup()
    props.billing.effectivePlan = { id: 2, name: 'PRO' }
    props.billing.currentSubscription = { endDate: '2026-12-01T00:00:00Z' }
    props.billing.businessDaysUntilExpiry = 20
    props.billing.canExtendBusinessSubscription = false
    props.billing.businessExtensionMessage = 'Продление откроется за 7 дней до окончания. Осталось дней: 20.'
    i18n.global.locale.value = 'en'
    assert.equal(state.extensionUnavailableMessage.value, 'Renewal becomes available 7 days before expiry. Days remaining: 20.')
    i18n.global.locale.value = 'zh-Hans'
    assert.equal(state.extensionUnavailableMessage.value, '到期前 7 天可续订。剩余天数：20。')
    props.billing.canExtendBusinessSubscription = true
    assert.equal(state.extensionUnavailableMessage.value, '')
  } finally { i18n.global.locale.value = originalLocale }
})

import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { i18n, formattingCulture, setFormattingCulture, translate } from '@opencrm/ui/i18n'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '../Admin/admin-client/src/adminCatalogMessages.ts'

function setup(name) {
  const filename = new URL(`../Admin/admin-client/src/views/${name}.vue`, import.meta.url)
  const { descriptor } = parse(readFileSync(filename, 'utf8'))
  const code = ts.transpileModule(compileScript(descriptor, { id: name }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS }
  }).outputText
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => regionalTestDependencies[id] ?? (id === 'vue' ? vue : {}), module, module.exports)
  return module.exports.default.setup({}, { expose() {} })
}

test('Промокоды меняют подписи без изменения кодов, условий и пользовательских данных', () => {
  const originalLocale = i18n.global.locale.value
  const originalCulture = formattingCulture.value
  try {
    i18n.global.locale.value = 'en'
    setFormattingCulture('en-GB')
    const state = setup('PromoCodesView')
    assert.equal(state.benefitOptions.value[2].value, 'fixed_amount')
    assert.equal(state.benefitOptions.value[2].label, 'Discount in RUB')
    assert.equal(state.getLegacyBenefitText({ discountTypeCode: 'fixed_amount', discountValue: 1234.5 }), 'Discount: RUB\u00a01,234.50')
    assert.equal(state.getLegacyBenefitText({ discountTypeCode: 'percentage', discountValue: 15 }), 'Discount: 15%')
    assert.equal(state.getPlanLabel({ name: '自定义 PRO', durationDays: 30 }), '自定义 PRO (30 days)')
    state.form.value.bonusDays = 31
    assert.equal(state.validateForm(), 'Select a plan for gift days.')
    i18n.global.locale.value = 'zh-Hans'
    assert.equal(state.benefitOptions.value[2].label, '卢布折扣')
    assert.deepEqual(state.scenarioOptions.value.map(item => item.value), ['detailing', 'service-station', 'fleet'])
    assert.equal(state.getPlanLabel({ name: '自定义 PRO', durationDays: 30 }), '自定义 PRO（30 天）')
    assert.equal(state.getUsageStatus({ applicationStatus: 'consumed' }), '优惠已获得')
    assert.equal(state.getUsageDiscountText({ applicationStatus: 'claimed' }), '已用于付款')
    assert.equal(state.form.value.bonusDays, 31)
    assert.equal(state.formatDate(null), '不限')
  } finally {
    i18n.global.locale.value = originalLocale
    setFormattingCulture(originalCulture)
  }
})

test('Цены тарифов используют формат региона и явный RUB при любом языке', () => {
  const originalLocale = i18n.global.locale.value
  const originalCulture = formattingCulture.value
  try {
    i18n.global.locale.value = 'zh-Hans'
    setFormattingCulture('en-GB')
    const state = setup('SubscriptionPlansView')
    assert.equal(state.formatMoney(1234.5), 'RUB\u00a01,234.50')
    assert.equal(translate('adminCatalog.deletePlanConfirm', { name: 'Мой план' }), '删除套餐“Мой план”？')
    assert.equal(translate('adminCatalog.usageCount', { count: 42 }), '使用次数：42')
  } finally {
    i18n.global.locale.value = originalLocale
    setFormattingCulture(originalCulture)
  }
})

import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { i18n, translate } from '@opencrm/ui/i18n'
import { formattingCulture, setFormattingCulture } from '@opencrm/ui/i18n'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '../Admin/admin-client/src/adminUsersMessages.ts'

const viewPaths = [
  '../Admin/admin-client/src/views/DashboardView.vue',
  '../Admin/admin-client/src/views/UsersView.vue',
  '../Admin/admin-client/src/views/UserOverviewView.vue',
  '../Admin/admin-client/src/components/DashboardActivityChart.vue',
  '../Admin/admin-client/src/components/DashboardPaymentsChart.vue'
]
const source = viewPaths.map(path => readFileSync(new URL(path, import.meta.url), 'utf8')).join('\n')

test('График платежей сохраняет часы для суток и даты для многодневного ряда', () => {
  const filename = new URL('../Admin/admin-client/src/components/DashboardPaymentsChart.vue', import.meta.url)
  const { descriptor } = parse(readFileSync(filename, 'utf8'))
  const code = ts.transpileModule(compileScript(descriptor, { id: 'payments-chart' }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS }
  }).outputText
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => regionalTestDependencies[id] ?? (id === 'vue' ? vue : {}), module, module.exports)
  const props = vue.reactive({ periodDays: 1, points: [] })
  const state = module.exports.default.setup(props, { expose() {} })
  const originalCulture = formattingCulture.value
  try {
    setFormattingCulture('en-GB')
    assert.equal(state.formatPointDate('2026-11-16T09:00:00Z'), '09:00')
    assert.equal(state.formatPointDate('2026-11-16T15:00:00Z'), '15:00')
    props.periodDays = 7
    assert.equal(state.formatPointDate('2026-11-16T09:00:00Z'), '16 Nov')
    assert.equal(state.formatPointDate('2026-11-17T15:00:00Z'), '17 Nov')
  } finally { setFormattingCulture(originalCulture) }
})

function sortedKeys(value) {
  return Object.fromEntries(Object.entries(value).sort(([left], [right]) => left.localeCompare(right)).map(([key, child]) => [
    key,
    child && typeof child === 'object' ? sortedKeys(child) : true
  ]))
}

test('Админские экраны пользователей имеют полный словарь RU/EN/zh-Hans', () => {
  const russian = i18n.global.getLocaleMessage('ru').adminUsers
  for (const locale of ['ru', 'en', 'zh-Hans']) {
    const messages = i18n.global.getLocaleMessage(locale).adminUsers
    assert.deepEqual(sortedKeys(messages), sortedKeys(russian), `${locale} has full coverage`)
  }

  for (const key of new Set([...source.matchAll(/adminUsers(?:\.[A-Za-z0-9]+){2,}/g)].map(match => match[0]))) {
    assert.ok(translate(key), `${key} resolves`)
  }
})

test('Локализуются целые фразы с параметрами, даты и валюты независимы от UI языка', () => {
  const previousLocale = i18n.global.locale.value
  try {
    i18n.global.locale.value = 'en'
    assert.equal(translate('adminUsers.users.range', { first: 1, last: 25, total: 48 }), 'Showing 1–25 of 48')
    assert.match(translate('adminUsers.overview.stopOrgWarning'), /Unused paid time will not be restored automatically/)
    i18n.global.locale.value = 'zh-Hans'
    assert.match(translate('adminUsers.dashboard.updatedAt', { time: '10:30' }), /10:30/)
    assert.match(translate('adminUsers.api.requestFailed', { status: 503 }), /503/)
  } finally {
    i18n.global.locale.value = previousLocale
  }

  assert.match(source, /formattingCulture\.value/)
  assert.match(source, /currency: 'RUB'/)
  assert.match(source, /currencyDisplay: 'code'/)
})

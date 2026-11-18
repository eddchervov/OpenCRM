import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { formattingCulture, i18n, setFormattingCulture } from '@opencrm/ui/i18n'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '../packages/workspace/src/defectMessages.ts'

for (const scenario of ['autoservice', 'fleet']) {
  test(`${scenario}: подписи кодов переводятся, календарная дата не сдвигается в часовом поясе`, () => {
    const source = readFileSync(new URL(`../Scenarios/${scenario}-client/src/components/DefectsSection.vue`, import.meta.url), 'utf8')
    const { descriptor } = parse(source)
    const code = ts.transpileModule(compileScript(descriptor, { id: 'defects' }).content, {
      compilerOptions: { module: ts.ModuleKind.CommonJS }
    }).outputText
    const module = { exports: {} }
    new Function('require', 'module', 'exports', code)(id => regionalTestDependencies[id] ?? (id === 'vue' ? { ...vue, onMounted() {} } : {}), module, module.exports)
    const originalLocale = i18n.global.locale.value
    const originalCulture = formattingCulture.value
    try {
      i18n.global.locale.value = 'en'
      setFormattingCulture('en-GB')
      const state = module.exports.default.setup({ context: { organization: { timeZoneId: 'America/New_York' } } }, { expose() {} })
      assert.equal(state.statusName({ statusCode: 'in_progress', statusName: 'В работе' }), 'In progress')
      assert.equal(state.severityName({ severityCode: 'critical', severityName: 'Критичная' }), 'Critical')
      assert.equal(state.statusName({ statusCode: 'custom', statusName: 'Особый статус' }), 'Особый статус')
      assert.equal(state.formatDate('2026-11-16T00:00:00Z', true), '16/11/2026')
      assert.equal(state.formatDate('2026-11-16T00:00:00Z'), '15/11/2026')
      i18n.global.locale.value = 'zh-Hans'
      assert.equal(state.statusName({ statusCode: 'in_progress' }), '处理中')
      assert.equal(state.severityName({ severityCode: 'critical' }), '严重')
    } finally { i18n.global.locale.value = originalLocale; setFormattingCulture(originalCulture) }
  })
}

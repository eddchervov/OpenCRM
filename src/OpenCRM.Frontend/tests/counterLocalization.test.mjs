import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { formattingCulture, i18n, setFormattingCulture } from '@opencrm/ui/i18n'
import { formatDateOnly } from '../packages/ui/src/dateOnly.ts'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '../packages/workspace/src/counterMessages.ts'

for (const scenario of ['autoservice', 'fleet']) {
  test(`${scenario}: показания, единицы и источники следуют языку и формату без сдвига календарной даты`, () => {
    const source = readFileSync(new URL(`../Scenarios/${scenario}-client/src/components/CountersSection.vue`, import.meta.url), 'utf8')
    const { descriptor } = parse(source)
    const code = ts.transpileModule(compileScript(descriptor, { id: 'counters' }).content, {
      compilerOptions: { module: ts.ModuleKind.CommonJS }
    }).outputText
    const module = { exports: {} }
    new Function('require', 'module', 'exports', code)(id => regionalTestDependencies[id]
      ?? (id === 'vue' ? { ...vue, onMounted() {} } : id === '@opencrm/ui' ? { formatDateOnly } : {}), module, module.exports)
    const originalLocale = i18n.global.locale.value
    const originalCulture = formattingCulture.value
    try {
      const state = module.exports.default.setup({ context: { organization: { timeZoneId: 'America/New_York' } } }, { expose() {} })
      i18n.global.locale.value = 'en'
      setFormattingCulture('en-GB')
      assert.equal(state.unitLabel(1), 'engine h')
      assert.equal(state.sourceName({ sourceType: 'manual', sourceName: 'Вручную' }), 'Manual')
      assert.equal(state.sourceName({ sourceType: 'custom', sourceName: 'Свой источник' }), 'Свой источник')
      assert.equal(state.formatValue(1234.5), '1,234.5')
      assert.equal(state.formatDate('2026-11-16T00:00:00Z', true), '16/11/2026')
      assert.equal(state.formatDate('2026-11-16T00:00:00Z'), '15/11/2026')
      i18n.global.locale.value = 'zh-Hans'
      assert.equal(state.unitLabel(0), '公里')
      assert.equal(state.sourceName({ sourceType: 'maintenance' }), '保养')
    } finally { i18n.global.locale.value = originalLocale; setFormattingCulture(originalCulture) }
  })
}

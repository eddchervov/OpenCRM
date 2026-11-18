import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { formattingCulture, i18n, setFormattingCulture } from '@opencrm/ui/i18n'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '../packages/workspace/src/sectionPagesMessages.ts'

test('Статистика связывает ряды по датам, а подписи и деньги форматирует независимо', () => {
  const source = readFileSync(new URL('../packages/workspace/src/components/WorkspaceStatisticsSection.vue', import.meta.url), 'utf8')
  const { descriptor } = parse(source)
  const code = ts.transpileModule(compileScript(descriptor, { id: 'statistics' }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS }
  }).outputText
  const charts = []
  class Chart { constructor(_canvas, configuration) { charts.push(configuration) } destroy() {} }
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => {
    if (id === 'vue') return { ...vue, watch() {}, onMounted() {}, onBeforeUnmount() {} }
    if (id === 'chart.js/auto') return { default: Chart }
    if (id === '@opencrm/ui') return { workspaceTheme: vue.ref('light'), workspaceChartColors: vue.ref({ text: '#000', grid: '#ddd' }) }
    if (id === '../workspaceCurrency') return { useWorkspaceCurrency: () => vue.ref('CNY') }
    return regionalTestDependencies[id] ?? {}
  }, module, module.exports)
  const analytics = {
    kpis: { plannedWorks: 1, inProgressWorks: 2, completedWorks14Days: 12, revenue14Days: 1250.5, clients: 2, activeVehicles: 3, openDefects: 0, materialsLowStock: 0, materialsOutOfStock: 0 },
    maintenanceCreatedSeries: [{ date: '2026-12-31', label: 'old 1', value: 4 }, { date: '2027-01-01', label: 'old 2', value: 8 }],
    maintenanceCompletedSeries: [{ date: '2027-01-01', label: 'different 2', value: 9 }, { date: '2026-12-31', label: 'different 1', value: 3 }],
    revenueSeries: [{ date: '2026-12-31', label: 'old 1', value: 1250.5 }], statusBreakdown: [], materialRiskBreakdown: []
  }
  const originalLocale = i18n.global.locale.value
  const originalCulture = formattingCulture.value
  try {
    i18n.global.locale.value = 'en'
    setFormattingCulture('en-GB')
    const state = module.exports.default.setup({ statistics: { analytics } }, { expose() {} })
    state.maintenanceChartCanvas.value = {}
    state.revenueChartCanvas.value = {}
    state.renderCharts()
    assert.deepEqual(charts[0].data.labels, ['31 Dec', '1 Jan'])
    assert.deepEqual(charts[0].data.datasets[1].data, [3, 9])
    assert.equal(charts[0].data.datasets[0].label, 'Created')
    assert.match(state.formatMoney(1250.5), /CNY.*1,250\.50/)
    i18n.global.locale.value = 'zh-Hans'
    assert.equal(state.breakdownName({ code: 'planned', label: 'Запланировано' }), '已计划')
    assert.equal(state.breakdownName({ code: 'custom', label: 'Свой статус' }), 'Свой статус')
  } finally { i18n.global.locale.value = originalLocale; setFormattingCulture(originalCulture) }
})

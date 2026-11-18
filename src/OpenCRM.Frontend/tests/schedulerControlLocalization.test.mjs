import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const path = '../packages/workspace/src/components/SchedulerList.vue'
const { descriptor } = parse(readFileSync(new URL(path, import.meta.url), 'utf8'))
assert.deepEqual(compileTemplate({ source: descriptor.template?.content ?? '', id: 'scheduler-controls', filename: path }).errors, [])
const code = ts.transpileModule(compileScript(descriptor, { id: 'scheduler-controls' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function setup() {
  const culture = vue.ref('en-US')
  const language = vue.ref('en')
  const translations = {
    en: { noPost: 'No bay', scheduled: 'Scheduled', statusNotSpecified: 'Status not specified' },
    'zh-Hans': { noPost: '无工位', scheduled: '已计划', statusNotSpecified: '未指定状态' }
  }
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => {
    if (id === 'vue') return { ...vue, onMounted() {} }
    if (id === '@opencrm/ui/i18n') return { formattingCulture: culture }
    if (id === '../workspaceCurrency') return { useWorkspaceCurrency: () => vue.computed(() => 'RUB') }
    if (id === '@opencrm/ui') return { UiSelect: {}, useLocalization: () => ({ t: (key, params = {}) => {
      const shortKey = key.replace('schedulerControl.', '')
      return (translations[language.value]?.[shortKey] ?? key).replace(/\{([^}]+)\}/g, (_, name) => params[name] ?? `{${name}}`)
    } }) }
    if (id === '../schedulerControlMessages') return {}
    if (id === '../vehicleRegistrationPlate') return { loadVehicleRegistrationPlateFormats: async () => null, matchesRegistrationPlateSearch: () => false }
    if (id === './MaintenanceWorkflowActions.vue') return {}
    if (id === '../utils/vehicleBrandLogo') return { getVehicleBrandLogo: () => null }
    throw new Error('Не задана зависимость: ' + id)
  }, module, module.exports)
  const state = module.exports.default.setup({ items: [], organizationId: 'org', canWrite: false }, { expose() {} })

  return { state, culture, language }
}

test('Общие компоненты управления расписанием компилируют свои шаблоны', () => {
  for (const component of ['SchedulerList', 'SchedulerToolbar', 'SchedulerClosedDay', 'SchedulerBookingDialog']) {
    const componentPath = `../packages/workspace/src/components/${component}.vue`
    const { descriptor: componentDescriptor } = parse(readFileSync(new URL(componentPath, import.meta.url), 'utf8'))
    compileScript(componentDescriptor, { id: 'scheduler-controls-' + component })
    const template = compileTemplate({
      source: componentDescriptor.template?.content ?? '',
      id: 'scheduler-controls-' + component,
      filename: componentPath
    })
    assert.deepEqual(template.errors, [], component)
  }
})

test('Список локализует статусы и пустую зону, а формат даты реагирует на formattingCulture отдельно от языка', () => {
  const { state, culture, language } = setup()
  const item = { status: 1, statusName: 'Запланировано' }
  assert.equal(state.statusText(item), 'Scheduled')
  assert.equal(state.statusText({ status: 99, statusName: 'Серверная русская подпись' }), 'Status not specified')
  assert.equal(state.locationLabel('__no-location__'), 'No bay')
  assert.equal(state.formatDate('2026-10-21'), 'Wednesday, October 21, 2026')

  language.value = 'zh-Hans'
  assert.equal(state.statusText(item), '已计划')
  assert.equal(state.locationLabel('__no-location__'), '无工位')
  assert.equal(state.formatDate('2026-10-21'), 'Wednesday, October 21, 2026')

  culture.value = 'zh-CN'
  assert.equal(state.formatDate('2026-10-21'), '2026年10月21日星期三')
  assert.equal(state.formatMoney(123, 'CNY'), new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY', maximumFractionDigits: 2 }).format(123))
})

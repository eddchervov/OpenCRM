import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import * as localization from '@opencrm/ui/i18n'

const messageSource = readFileSync(new URL('../packages/workspace/src/exportMessages.ts', import.meta.url), 'utf8')
const messageCode = ts.transpileModule(messageSource, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText
new Function('require', 'exports', messageCode)(() => localization, {})

const componentPath = new URL('../packages/workspace/src/components/Export1CSection.vue', import.meta.url)
const source = readFileSync(componentPath, 'utf8')
const { descriptor, errors } = parse(source, { filename: componentPath.pathname })
assert.deepEqual(errors, [])
const template = compileTemplate({ source: descriptor.template.content, filename: componentPath.pathname, id: 'finance-export' })
assert.deepEqual(template.errors, [])
const financePath = new URL('../packages/workspace/src/components/FinanceSection.vue', import.meta.url)
const financeDescriptor = parse(readFileSync(financePath, 'utf8'), { filename: financePath.pathname }).descriptor
const financeTemplate = compileTemplate({ source: financeDescriptor.template.content, filename: financePath.pathname, id: 'finance-section' })
assert.deepEqual(financeTemplate.errors, [])
const script = compileScript(descriptor, { id: 'finance-export' })
const compiled = ts.transpileModule(script.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function exportSection(props, api) {
  const module = { exports: {} }
  let unmount
  const onMounted = () => {}
  const onBeforeUnmount = callback => { unmount = callback }
  const resolve = id => {
    if (regionalTestDependencies[id]) return regionalTestDependencies[id]
    if (id === '../exportMessages') return {}
    if (id === 'vue') return { ...vue, onMounted, onBeforeUnmount }
    if (id === '@opencrm/ui') return { UiButton: {}, UiLoader: {}, formatDateOnly: () => '2026-10-29' }
    if (id === '../api/export1C') return api
    if (id === '../types') return {}
    if (id === './WorkspaceProLock.vue') return {}
    throw new Error(`Не задана зависимость компонента: ${id}`)
  }
  const evaluate = new Function('require', 'module', 'exports', compiled)
  evaluate(resolve, module, module.exports)
  return {
    state: module.exports.default.setup(vue.reactive(props), { expose() {}, emit() {} }),
    unmount: () => unmount?.()
  }
}

function context(id = 'org-1', accountingExport = false) {
  return {
    organization: { id, timeZoneId: 'UTC' },
    shell: { scenario: { modules: { accountingExport } } }
  }
}

function capabilities(overrides = {}) {
  return {
    canExport: true,
    canExportBp3: false,
    canExportOrders: true,
    canExportClients: true,
    canExportVehicles: true,
    canExportCatalog: true,
    canExportMaterials: true,
    ...overrides
  }
}

test('Free не запрашивает предпросмотр или скачивание 1С, но оставляет доступным JSON', async () => {
  const previewCalls = []
  const downloadCalls = []
  const { state } = exportSection({ context: context() }, {
    loadExport1CCapabilities: async () => capabilities(),
    loadExport1CPreview: async (...args) => { previewCalls.push(args); return { validationIssues: [] } },
    downloadExport1C: async (...args) => { downloadCalls.push(args); return 0 }
  })

  await state.loadCapabilities()
  assert.equal(state.exportMode.value, 'full')
  assert.equal(state.isAccountingExportLocked.value, false)
  state.exportMode.value = 'bp3'
  await vue.nextTick()
  assert.equal(state.isAccountingExportLocked.value, true)
  await state.checkData()
  await state.exportData()
  assert.equal(previewCalls.length, 0)
  assert.equal(downloadCalls.length, 0)

  state.exportMode.value = 'full'
  await vue.nextTick()
  assert.equal(state.isAccountingExportLocked.value, false)
  await state.checkData()
  await state.exportData()
  assert.equal(previewCalls.length, 1)
  assert.equal(downloadCalls.length, 1)
  assert.equal(previewCalls[0][1].destinationCode, 'full')
  assert.equal(downloadCalls[0][1].destinationCode, 'full')
})

test('PRO выполняет предпросмотр и скачивание для 1С с выбранными датами', async () => {
  const previewCalls = []
  const downloadCalls = []
  const { state } = exportSection({
    context: context('org-pro', true),
    initialDateFrom: '2026-10-01',
    initialDateTo: '2026-10-29'
  }, {
    loadExport1CCapabilities: async () => capabilities({ canExportBp3: true }),
    loadExport1CPreview: async (...args) => { previewCalls.push(args); return { validationIssues: [] } },
    downloadExport1C: async (...args) => { downloadCalls.push(args); return 0 }
  })

  state.exportMode.value = 'bp3'
  await state.loadCapabilities()
  await state.checkData()
  await state.exportData()
  assert.equal(state.isAccountingExportLocked.value, false)
  assert.equal(previewCalls[0][1].destinationCode, 'bp3')
  assert.equal(previewCalls[0][1].dateFrom, '2026-10-01')
  assert.equal(previewCalls[0][1].dateTo, '2026-10-29')
  assert.equal(downloadCalls[0][1].destinationCode, 'bp3')
})

test('Замечания универсального JSON не предлагают загружать данные в 1С', async () => {
  const { state } = exportSection({ context: context('org-pro', true) }, {
    loadExport1CCapabilities: async () => capabilities({ canExportBp3: true }),
    downloadExport1C: async () => 2
  })
  await state.loadCapabilities()
  await state.exportData()
  assert.match(state.exportMessage.value, /2 замечаний/)
  assert.doesNotMatch(state.exportMessage.value, /1С/)
  state.exportMode.value = 'bp3'
  await vue.nextTick()
  await state.exportData()
  assert.match(state.exportMessage.value, /перед загрузкой в 1С/)
})

test('Неверный период блокирует предпросмотр и скачивание', async () => {
  let apiCalls = 0
  const { state } = exportSection({
    context: context('org-pro', true),
    initialDateFrom: '2026-10-30',
    initialDateTo: '2026-10-01'
  }, {
    loadExport1CCapabilities: async () => capabilities({ canExportBp3: true }),
    loadExport1CPreview: async () => { apiCalls++; return { validationIssues: [] } },
    downloadExport1C: async () => { apiCalls++; return 0 }
  })

  await state.loadCapabilities()
  await state.checkData()
  await state.exportData()
  assert.equal(apiCalls, 0)
  assert.equal(state.previewError.value, 'Проверьте даты периода.')
  assert.equal(state.exportMessage.value, 'Проверьте даты периода.')
})

test('Устаревший ответ capabilities не перезаписывает доступ новой организации', async () => {
  let resolveOld
  const props = { context: context('org-old', true) }
  const { state } = exportSection(props, {
    loadExport1CCapabilities: organizationId => organizationId === 'org-old'
      ? new Promise(resolve => { resolveOld = resolve })
      : Promise.resolve(capabilities({ canExportBp3: true })),
    loadExport1CPreview: async () => ({ validationIssues: [] }),
    downloadExport1C: async () => 0
  })

  const oldRequest = state.loadCapabilities()
  props.context.organization.id = 'org-new'
  const newRequest = state.loadCapabilities()
  resolveOld(capabilities({ canExport: false }))
  await Promise.all([oldRequest, newRequest])
  assert.equal(state.capabilities.value.canExport, true)
  assert.equal(state.isLoading.value, false)
})

test('Закрытое окно отменяет запрос и игнорирует его результат', async () => {
  let resolvePreview
  let previewSignal
  const { state, unmount } = exportSection({ context: context('org-pro', true) }, {
    loadExport1CCapabilities: async () => capabilities({ canExportBp3: true }),
    loadExport1CPreview: (_organizationId, _options, signal) => {
      previewSignal = signal
      return new Promise(resolve => { resolvePreview = resolve })
    },
    downloadExport1C: async () => 0
  })

  await state.loadCapabilities()
  const request = state.checkData()
  unmount()
  resolvePreview({ ordersCount: 1, validationIssues: [] })
  await request
  assert.equal(previewSignal.aborted, true)
  assert.equal(state.preview.value, null)
})

test('Страница сохраняет прямой маршрут выгрузки как модалку и закрывается на финансы с теми же датами', () => {
  const finance = readFileSync(new URL('../packages/workspace/src/components/FinanceSection.vue', import.meta.url), 'utf8')
  for (const scenario of ['detailing-client', 'autoservice-client', 'fleet-client']) {
    const app = readFileSync(new URL(`../Scenarios/${scenario}/src/App.vue`, import.meta.url), 'utf8')
    assert.match(app, /:initial-export-open="activeEntityId === 'export'"/)
    assert.doesNotMatch(app, /<Export1CSection\s/)
  }
  assert.match(finance, /<dialog[^>]+aria-labelledby/)
  assert.match(finance, /window\.location\.hash = `#finance\?dateFrom=\$\{exportDateFrom\.value\}&dateTo=\$\{exportDateTo\.value\}`/)
  assert.match(finance, /@close="handleExportDialogClose"/)
})

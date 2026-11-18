import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { formatDateOnly, parseDateOnly } from '../packages/ui/src/dateOnly.ts'
import { getWorkdayProgress, shouldOpenRecord } from '../packages/workspace/src/utils/workdayCard.ts'
import { formattingCulture, i18n } from '@opencrm/ui/i18n'
import '../packages/workspace/src/workMessages.ts'

const compiled = new Map()

function setupComponent(name, props, dependencies, emit = () => {}) {
  if (!compiled.has(name)) {
    const filename = new URL('../packages/workspace/src/components/' + name + '.vue', import.meta.url)
    const { descriptor } = parse(readFileSync(filename, 'utf8'), { filename: filename.pathname })
    const script = compileScript(descriptor, { id: name })
    compiled.set(name, ts.transpileModule(script.content, {
      compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
    }).outputText)
  }
  const module = { exports: {} }
  const storage = new Map()
  const lifecycle = { onMounted() {}, onUnmounted() {}, onBeforeUnmount() {}, watch() {} }
  const resolve = id => {
    if (Object.hasOwn(regionalTestDependencies, id)) return regionalTestDependencies[id]
    if (id === 'vue') return { ...vue, ...lifecycle }
    if (Object.hasOwn(dependencies, id)) return dependencies[id]
    throw new Error('Не задана зависимость компонента: ' + id)
  }
  const window = { sessionStorage: {
    getItem: key => storage.get(key) ?? null,
    setItem: (key, value) => storage.set(key, value),
    removeItem: key => storage.delete(key)
  } }
  const evaluate = new Function('require', 'module', 'exports', 'window', 'document', 'HTMLInputElement', 'setTimeout', 'clearTimeout', compiled.get(name))
  evaluate(resolve, module, module.exports, window, { activeElement: null }, class {}, () => 1, () => {})
  return module.exports.default.setup(props, { expose() {}, emit })
}

function workdayCard(props, emit) {
  return setupComponent('WorkspaceWorkdayCard', props, {
    '@opencrm/ui': { formatDateOnly() {}, parseDateOnly() {} },
    '../utils/vehicleBrandLogo': { getVehicleBrandLogo: () => null },
    '../workdayCardMessages': {},
    '../utils/workdayCard': { getWorkdayProgress, shouldOpenRecord }
  }, emit)
}

const work = { detailId: 'line-1', maintenanceId: 'order-1', status: 2 }
function workComponent(api = {}, shiftApi = {}) {
  return setupComponent('WorkspaceWorkSection', {
    context: { organization: { id: 'org-1', allowWorkSelfAssignment: false }, workspaceAccess: { isReadOnly: false } }
  }, {
    '@opencrm/ui': { formatDateOnly, parseDateOnly },
    '../workMessages': {},
    '../api/work': { loadAssignedWork: async () => [work], loadWorkReports: async () => [], ...api },
    '../api/shifts': { loadOwnShifts: async () => [], ...shiftApi },
    '../formatWorkDuration': { formatWorkDuration() {} }
  })
}

function deferred() {
  let resolve
  const promise = new Promise(complete => { resolve = complete })
  return { promise, resolve }
}

const settled = () => new Promise(resolve => setImmediate(resolve))

test('Цена работы использует валюту снимка вместо валюты открытой организации', () => {
  const state = workComponent()
  assert.match(state.formatPrice(100, 'CNY'), /CNY|CN¥|¥|￥/)
  assert.doesNotMatch(state.formatPrice(100, 'CNY'), /₽|RUB/)
  assert.match(state.formatPrice(100), /₽|RUB/)
})

test('Историческая валюта блокирует выдачу и возврат материалов до API-вызова', async () => {
  let calls = 0
  const state = workComponent({
    issueOperationMaterial: async () => { calls++ },
    returnOperationMaterial: async () => { calls++ }
  })
  state.materialStocks.value = [{ id: 'stock', name: 'Test', unit: 'л', availableQuantity: 2 }]
  state.selectedStockId.value = 'stock'
  state.materialQuantity.value = 1
  state.materialReturnReason.value = 'Test reason'

  await state.issueMaterial({ ...work, currencyCode: 'CNY' })
  await state.returnMaterial({ ...work, currencyCode: 'CNY' }, { id: 'issue' })

  assert.equal(calls, 0)
  assert.equal(state.canManageWorkMaterials({ ...work, currencyCode: 'RUB' }), true)
})

test('Фоновый опрос обновляет согласование и сохраняет черновик замечания', async () => {
  let reports = [{ id: 'report-1', decision: null }]
  const state = workComponent({ loadWorkReports: async () => reports })
  await state.load()
  await state.toggleReports(work)
  state.reportText.value = 'Незавершённый комментарий'
  reports = [{ id: 'report-1', decision: 'rejected', decisionReason: 'Отказ клиента' }]
  await state.load(true)
  await settled()
  assert.equal(state.reports.value[0].decision, 'rejected')
  assert.equal(state.reportText.value, 'Незавершённый комментарий')
  assert.equal(state.reportPanelId.value, work.detailId)
  assert.equal(state.reportLoading.value, false)
})

test('Запоздалый ответ закрытой панели не заменяет замечания другой работы', async () => {
  const old = deferred()
  const state = workComponent({ loadWorkReports: async (_org, _order, detail) => detail === 'line-1' ? old.promise : [{ id: 'other' }] })
  const opening = state.toggleReports(work)
  await state.toggleReports(work)
  await state.toggleReports({ ...work, detailId: 'line-2' })
  old.resolve([{ id: 'stale' }])
  await opening
  assert.equal(state.reports.value[0].id, 'other')
  assert.equal(state.reportLoading.value, false)
})

test('Ответ фоновой загрузки не удаляет только что отправленное замечание', async () => {
  const old = deferred()
  let initial = true
  const state = workComponent({
    loadWorkReports: async () => initial ? [] : old.promise,
    addWorkReport: async () => ({ id: 'new-report' })
  })
  await state.toggleReports(work)
  initial = false
  const refreshing = state.refreshReports(work)
  state.reportText.value = 'Новое замечание'
  await state.sendReport(work)
  old.resolve([])
  await refreshing
  assert.equal(state.reports.value[0].id, 'new-report')
  assert.equal(state.reportText.value, '')
})

test('Состояния и типы сообщений переводятся по коду, даты используют formattingCulture', () => {
  const state = workComponent()
  i18n.global.locale.value = 'en'
  formattingCulture.value = 'en-US'

  assert.equal(state.statusName(3), 'Paused')
  assert.equal(state.reportKindName('extra'), 'Extra work')
  assert.equal(state.emptyState.value.title, 'No work for today')
  assert.equal(state.formatInstant('2026-11-16T09:05:00Z'), new Intl.DateTimeFormat('en-US', {
    dateStyle: 'short', timeStyle: 'short', timeZone: 'UTC'
  }).format(new Date('2026-11-16T09:05:00Z')))
  assert.equal(state.formatPlan({ plannedDate: '2026-11-16', plannedStartTime: '09:00' }),
    parseDateOnly('2026-11-16').toLocaleDateString('en-US', { day: 'numeric', month: 'short' }) + ' · 09:00')

  const source = readFileSync(new URL('../packages/workspace/src/components/WorkspaceWorkSection.vue', import.meta.url), 'utf8')
  const keys = [...new Set([...source.matchAll(/\bt\(['"]([^'"]+)['"]\)/g)].map(match => match[1]))]
  const dynamicKeys = [
    ...['today', 'active', 'assigned', 'available', 'completed'].flatMap(code => [
      `work.tabs.${code}`, `work.empty.${code}Title`, `work.empty.${code}Description`
    ]),
    ...['assigned', 'active', 'paused', 'completed'].map(code => `work.status.${code}`),
    ...['comment', 'defect', 'extra', 'time'].map(code => `work.reportKind.${code}`),
    ...['g', 'kg', 'ml', 'l', 'package'].map(code => `work.units.${code}`)
  ]
  for (const locale of ['ru', 'en', 'zh-Hans']) {
    i18n.global.locale.value = locale
    for (const key of [...keys, ...dynamicKeys]) assert.notEqual(i18n.global.t(key), key, `${key} отсутствует для ${locale}`)
  }

  i18n.global.locale.value = 'ru'
  formattingCulture.value = 'ru-RU'
})

function importComponent(api = {}) {
  return setupComponent('WorkspaceImportSection', { organizationId: 'org-1', userId: 'user-1' }, {
    '@opencrm/ui': {},
    '../importRecoveryReport': {},
    '../importProfileStructure.mjs': {},
    '../importMessages': { importMessage: key => key },
    '../importRowMessage': { importRowMessage: row => row.message ?? '' },
    '../api/imports': api
  })
}

function importDraft() {
  return {
    id: 'draft-1', revision: 4, storageVersion: 2, state: 'ready', totalRows: 1,
    data: { mappings: [], tables: [], rows: [{ id: 'row-1', name: 'Клиент', kind: 'client', status: 'create', matches: [] }] }
  }
}

test('Сохранение импорта без изменений не отправляет пустую команду', async () => {
  let requests = 0
  const state = importComponent({ patchOrganizationImportBatchRows: async () => { requests++ } })
  state.setDraft(importDraft())
  await state.saveRows()
  assert.equal(requests, 0)
  assert.equal(state.rowSaveStatus.value, 'saved')
  assert.equal(state.rowSaveError.value, '')
  assert.equal(state.isSavingRows.value, false)
})

test('Возврат ячейки к исходному значению снимает признак несохранённых правок', async () => {
  const state = importComponent()
  state.setDraft(importDraft())
  const row = state.draft.value.data.rows[0]
  state.updateRow(row, 'name', 'Новое имя')
  state.updateRow(row, 'name', 'Клиент')
  assert.equal(state.rowsDirty.value, true)
  await state.saveRows()
  assert.equal(state.rowsDirty.value, false)
  assert.equal(state.rowSaveStatus.value, 'saved')
})

test('Изменённая строка импорта отправляется и принимает новую ревизию сервера', async () => {
  let sent
  const state = importComponent({
    patchOrganizationImportBatchRows: async (_org, _id, body) => {
      sent = body
      return { ...importDraft(), revision: 5, data: { ...importDraft().data, rows: body.rows } }
    },
    loadOrganizationImports: async () => []
  })
  state.setDraft(importDraft())
  state.updateRow(state.draft.value.data.rows[0], 'name', 'Новое имя')
  await state.saveRows()
  assert.equal(sent.rows.length, 1)
  assert.equal(sent.rows[0].name, 'Новое имя')
  assert.equal(state.draft.value.revision, 5)
  assert.equal(state.rowsDirty.value, false)
  assert.equal(state.rowSaveError.value, '')
})

test('Опрос не вытесняет медленное первоначальное открытие замечаний', async () => {
  const pending = deferred()
  let requests = 0
  const state = workComponent({ loadWorkReports: async () => { requests++; return pending.promise } })
  const opening = state.toggleReports(work)
  await state.load(true)
  await state.load(true)
  assert.equal(requests, 1)
  assert.equal(state.reportLoading.value, true)
  pending.resolve([{ id: 'initial' }])
  await opening
  assert.equal(state.reports.value[0].id, 'initial')
  assert.equal(state.reportLoading.value, false)
})

test('Медленный фоновый ответ завершается до следующего запроса замечаний', async () => {
  const pending = deferred()
  let requests = 0
  const state = workComponent({ loadWorkReports: async () => ++requests === 2 ? pending.promise : [{ id: 'initial' }] })
  await state.toggleReports(work)
  await state.load(true)
  await state.load(true)
  assert.equal(requests, 2)
  pending.resolve([{ id: 'updated' }])
  await settled()
  assert.equal(state.reports.value[0].id, 'updated')
  await state.load(true)
  await settled()
  assert.equal(requests, 3)
})

test('Сводка смены сохраняется при медленном фоновом обновлении', async () => {
  const pending = deferred()
  const previousShift = { id: 'shift-1', startLocal: '2026-10-28T08:00:00', endLocal: '2026-10-28T17:00:00' }
  const updatedShift = { ...previousShift, endLocal: '2026-10-28T18:00:00' }
  const state = workComponent({ loadAssignedWork: () => pending.promise }, { loadOwnShifts: async () => [updatedShift] })
  state.ownShifts.value = [previousShift]
  const refreshing = state.load(true)
  assert.deepEqual(state.ownShifts.value, [previousShift])
  pending.resolve([work])
  await refreshing
  await settled()
  assert.deepEqual(state.ownShifts.value, [updatedShift])
})

test('Карточка отправляет open только для обычного клика и доступной записи', () => {
  const emitted = []
  const props = {
    item: {
      id: 'order-1', vehicleName: 'Ford Focus', date: '2026-10-17', services: [],
      workTotalCount: 3, workCompletedCount: 1, workPausedCount: 1
    },
    stage: 'inProgress', today: '2026-10-17', canOpen: true
  }
  const state = workdayCard(props, (...event) => emitted.push(event))
  const event = { button: 0, ctrlKey: false, metaKey: false, shiftKey: false, altKey: false, preventDefault() { this.defaultPrevented = true } }
  state.openRecord(event)
  assert.deepEqual(emitted, [['open', 'order-1']])
  assert.equal(event.defaultPrevented, true)
  assert.deepEqual(state.progress.value, { total: 3, completed: 1, percent: 33, isComplete: false })

  const modifiedEvent = { ...event, defaultPrevented: false, metaKey: true, preventDefault() { this.defaultPrevented = true } }
  state.openRecord(modifiedEvent)
  assert.equal(emitted.length, 1)
  assert.equal(modifiedEvent.defaultPrevented, false)

  const restricted = workdayCard({ ...props, canOpen: false }, (...event) => emitted.push(event))
  const plainEvent = { ...event, defaultPrevented: false }
  restricted.openRecord(plainEvent)
  assert.equal(emitted.length, 1)
  assert.equal(plainEvent.defaultPrevented, false)
})

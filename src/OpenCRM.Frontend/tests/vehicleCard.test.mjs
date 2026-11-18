import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { getWorkdayProgress } from '../packages/workspace/src/utils/workdayCard.ts'

const filename = new URL('../packages/workspace/src/components/VehicleCardSection.vue', import.meta.url)
const source = readFileSync(filename, 'utf8')
const { descriptor } = parse(source, { filename: filename.pathname })
const script = compileScript(descriptor, { id: 'vehicle-card-test' })
const code = ts.transpileModule(script.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

const messages = {
  'vehicleOverview.linkedOrganization': 'Связь с организацией',
  'vehicleOverview.status3': 'Выполнено',
  'vehicleOverview.status4': 'Отменено',
  'vehicleOverview.readyStatus': 'Готово к выдаче',
  'vehicleOverview.handedOver': 'Выдано'
}

function translate(key, parameters = {}) {
  return (messages[key] ?? key).replace(/\{([^}]+)\}/g, (_, name) => parameters[name] ?? `{${name}}`)
}

function vehicle(id) {
  return { id, brandName: 'BMW', modelName: 'X3', isArchived: false, maintenanceCount: 0 }
}

function page(items = [], values = {}) {
  return { items, totalCount: items.length, page: 1, pageSize: 4, hasMore: false, ...values }
}

function vehicleCard(api = {}, workApi = {}) {
  const watchers = []
  const calls = []
  const currency = vue.ref('RUB')
  const culture = vue.ref('ru-RU')
  const module = { exports: {} }
  const resolve = id => {
    if (id === 'vue') return {
      ...vue,
      inject: () => undefined,
      provide() {},
      onMounted() {},
      onUnmounted() {},
      watch: (source, callback) => watchers.push({ source, callback })
    }
    if (id === '@opencrm/ui') return { UiButton: {}, UiCard: {}, UiConfirmDialog: {}, UiImagePreview: {}, UiLoader: {}, useLocalization: () => ({ t: translate }) }
    if (id === '@opencrm/ui/i18n') return { formattingCulture: culture }
    if (id === '../vehicleOverviewMessages') return {}
    if (id === '../workspaceCurrency') return { useWorkspaceCurrency: () => currency }
    if (id === './WorkspaceProLock.vue') return {}
    if (id === '../api/vehicles') return {
      getVehicleDisplayName: item => `${item.brandName} ${item.modelName}`,
      loadVehicle: async (organizationId, vehicleId) => { calls.push(['vehicle', organizationId, vehicleId]); return vehicle(vehicleId) },
      loadVehicleAlbums: async (...args) => { calls.push(['albums', ...args]); return { albums: [] } },
      loadVehicleHistory: async () => [],
      loadVehicleMaintenancePage: async (...args) => {
        calls.push(['maintenance', ...args])
        return page([], { page: args[2], pageSize: args[3] })
      },
      loadVehiclePhotos: async (...args) => { calls.push(['photos', ...args]); return {} },
      ...api
    }
    if (id === '../api/work') return { loadWorkProgress: async () => [], ...workApi }
    if (id === '../utils/vehicleBrandLogo') return { getVehicleBrandLogo: () => null }
    if (id === '../utils/workdayCard') return { getWorkdayProgress }
    throw new Error('Не задана зависимость: ' + id)
  }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const props = vue.reactive({
    context: {
      organization: { id: 'org-1' },
      shell: { scenario: { code: 'autoservice' }, menu: [{ code: 'clients', isVisible: true }] },
      workspaceAccess: {}
    },
    vehicleId: 'vehicle-1'
  })
  const state = module.exports.default.setup(props, { expose() {}, emit() {} })
  return { state, props, watchers, calls, currency, culture }
}

test('Исторические цены и оплаты сохраняют валюту заказа после переноса в другую организацию', () => {
  const { state, currency, culture } = vehicleCard()
  currency.value = 'CNY'
  culture.value = 'en-GB'
  assert.equal(state.formatMoney(1234.5, 'RUB'), 'RUB\u00a01,234.50')
  assert.equal(state.formatMoney(1234.5), 'CNY\u00a01,234.50')
  assert.equal(state.formatMoney(1234.5, 'USD'), 'USD\u00a01,234.50')
  const moneyExpressions = [...descriptor.template.content.matchAll(/\{\{[^}]*?formatMoney\((.*?)\)\s*\}\}/g)].map(match => match[1])
  assert.equal(moneyExpressions.length, 3)
  assert.ok(moneyExpressions.every(expression => expression.endsWith(', item.currencyCode')))
})

test('Фото автомобиля доступны Free сразу, альбомы читаются только при открытии истории', async () => {
  const { state, props, calls } = vehicleCard()
  props.context.shell.scenario.modules = { photos: false }

  await state.loadCard()

  assert.equal(calls.some(call => call[0] === 'photos'), true)
  assert.equal(calls.some(call => call[0] === 'albums'), false)
  assert.equal(state.isAlbumPreview.value, true)

  await state.toggleSavedMediaHistory()

  assert.deepEqual(calls.filter(call => call[0] === 'albums').map(call => call[0]), ['albums'])
  assert.equal(state.isAlbumPreview.value, false)
})

function deferred() {
  let resolve
  const promise = new Promise(complete => { resolve = complete })
  return { promise, resolve }
}

test('Смена организации и техники игнорирует устаревшую загрузку карточки', async () => {
  const firstVehicle = deferred()
  const { state, props, watchers, calls } = vehicleCard({
    loadVehicle: async (organizationId, vehicleId) => {
      calls.push(['vehicle', organizationId, vehicleId])
      return vehicleId === 'vehicle-1' ? firstVehicle.promise : vehicle(vehicleId)
    }
  })
  const staleLoad = state.loadCard()
  props.context.organization.id = 'org-2'
  props.vehicleId = 'vehicle-2'
  await watchers[0].callback()
  firstVehicle.resolve(vehicle('vehicle-1'))
  await staleLoad

  assert.equal(state.vehicle.value.id, 'vehicle-2')
  assert.deepEqual(calls.filter(call => call[0] === 'vehicle'), [
    ['vehicle', 'org-1', 'vehicle-1'],
    ['vehicle', 'org-2', 'vehicle-2']
  ])
})

test('Подгрузка страницы передаёт номер страницы и сохраняет записи при ответе', async () => {
  const loadedPage = page([{ id: 'record-5', status: 3 }], { page: 2, pageSize: 4, totalCount: 5, hasMore: false })
  const { state, calls } = vehicleCard({ loadVehicleMaintenancePage: async (...args) => {
    calls.push(['maintenance', ...args])
    return loadedPage
  } })
  state.maintenancePage.value = 1
  state.maintenanceItems.value = [{ id: 'record-1', status: 3 }]
  await state.loadHistory(true)

  assert.deepEqual(calls.at(-1), ['maintenance', 'org-1', 'vehicle-1', 2, 4, undefined])
  assert.deepEqual(state.maintenanceItems.value.map(item => item.id), ['record-1', 'record-5'])
  assert.equal(state.maintenanceHasMore.value, false)
  assert.equal(state.maintenanceTotalCount.value, 5)
})

test('Поздний ответ прежней страницы не заменяет результат нового фильтра', async () => {
  const append = deferred()
  const filtered = deferred()
  const calls = []
  let requestNumber = 0
  const { state } = vehicleCard({ loadVehicleMaintenancePage: (...args) => {
    calls.push(args)
    return ++requestNumber === 1 ? append.promise : filtered.promise
  } })
  state.maintenancePage.value = 1
  state.maintenanceItems.value = [{ id: 'record-1', status: 2 }]
  const oldPage = state.loadHistory(true)
  state.maintenanceStatus.value = '3'
  const newFilter = state.loadHistory()
  filtered.resolve(page([{ id: 'completed-1', status: 3 }], { totalCount: 7, hasMore: true }))
  await newFilter
  append.resolve(page([{ id: 'stale-next-page', status: 2 }], { page: 2, hasMore: false }))
  await oldPage

  assert.equal(calls[0][2], 2)
  assert.equal(calls[1][2], 1)
  assert.equal(calls[1][4], 3)
  assert.deepEqual(state.maintenanceItems.value.map(item => item.id), ['completed-1'])
  assert.equal(state.maintenanceHasMore.value, true)
})

test('Статусы показывают выдачу и готовность, а старое завершение остаётся выполненным', () => {
  const { state } = vehicleCard()
  assert.equal(state.getRecordStatus({ status: 3 }), 'Выполнено')
  assert.equal(state.getRecordStatus({ status: 3, readyForHandoverAt: '2026-10-17T12:00:00Z' }), 'Готово к выдаче')
  assert.equal(state.getRecordStatus({ status: 3, handedOverAt: '2026-10-17T13:00:00Z' }), 'Выдано')
  assert.equal(state.getRecordStatus({ status: 4, handedOverAt: '2026-10-17T13:00:00Z' }), 'Отменено')
})

test('Ход работ сопоставляется по detailId услуг и исключает материалы', () => {
  const { state } = vehicleCard()
  const record = {
    id: 'record-1',
    maintenanceDetails: [
      { detailId: 'service-1', name: 'Полировка' },
      { detailId: 'service-2', name: 'Керамика' },
      { detailId: 'material-1', name: 'Состав', materialName: 'Состав' }
    ]
  }
  state.progressByMaintenance.value = {
    'record-1': [
      { detailId: 'service-1', status: 4 },
      { detailId: 'unrelated-service', status: 4 },
      { detailId: 'material-1', status: 4 }
    ]
  }

  assert.deepEqual(state.getRecordProgress(record), { completed: 1, total: 2, percent: 50, isComplete: false })
})

test('Загрузка страницы истории подгружает прогресс активной записи за пределами первой выборки', async () => {
  const activeRecord = {
    id: 'record-5', status: 2,
    maintenanceDetails: [{ detailId: 'service-5', name: 'Полировка' }]
  }
  const { state } = vehicleCard({
    loadVehicleMaintenancePage: async () => page([activeRecord])
  }, {
    loadWorkProgress: async (_organizationId, maintenanceId) => {
      assert.equal(maintenanceId, 'record-5')
      return [{ detailId: 'service-5', status: 4 }]
    }
  })
  await state.loadHistory()
  await new Promise(resolve => setImmediate(resolve))

  assert.deepEqual(state.getRecordProgress(activeRecord), { completed: 1, total: 1, percent: 100, isComplete: true })
})

test('Ошибка прогресса одной записи не мешает загрузить остальные статусы работ', async () => {
  const firstRecord = { id: 'record-a', status: 2, maintenanceDetails: [{ detailId: 'service-a', name: 'Диагностика' }] }
  const secondRecord = { id: 'record-b', status: 2, maintenanceDetails: [{ detailId: 'service-b', name: 'Ремонт' }] }
  const { state } = vehicleCard({
    loadVehicleMaintenancePage: async () => page([firstRecord, secondRecord])
  }, {
    loadWorkProgress: async (_organizationId, maintenanceId) => {
      if (maintenanceId === 'record-a') throw new Error('Временно недоступно')
      return [{ detailId: 'service-b', status: 4 }]
    }
  })
  await state.loadHistory()
  await new Promise(resolve => setImmediate(resolve))

  assert.deepEqual(Object.keys(state.progressByMaintenance.value), ['record-b'])
  assert.equal(state.getRecordProgress(secondRecord).completed, 1)
})

test('Поздний ответ прогресса старой техники не попадает в карточку новой техники', async () => {
  const pendingProgress = deferred()
  const oldRecord = {
    id: 'old-record', status: 2,
    maintenanceDetails: [{ detailId: 'old-service', name: 'Диагностика' }]
  }
  const { state, props } = vehicleCard({
    loadVehicleMaintenancePage: async (_organizationId, vehicleId) => vehicleId === 'vehicle-1'
      ? page([oldRecord])
      : page([])
  }, {
    loadWorkProgress: async () => pendingProgress.promise
  })
  await state.loadHistory()
  props.vehicleId = 'vehicle-2'
  await state.loadCard()
  pendingProgress.resolve([{ detailId: 'old-service', status: 4 }])
  await new Promise(resolve => setImmediate(resolve))

  assert.equal(state.vehicle.value.id, 'vehicle-2')
  assert.deepEqual(state.progressByMaintenance.value, {})
})

test('Сценарий автопарка использует подпись клиента из terms.clients', () => {
  const { state, props } = vehicleCard()
  props.context.shell.scenario = { code: 'fleet', terms: { clients: 'Подразделение' } }
  assert.equal(state.clientLabel.value, 'Подразделение')
  props.context.shell.scenario.terms.clients = ''
  assert.equal(state.clientLabel.value, 'Связь с организацией')
})

test('Режим чтения блокирует архивирование, а ссылка клиента зависит от доступного раздела', async () => {
  const { state, props } = vehicleCard()
  state.vehicle.value = { ...vehicle('vehicle-1'), organizationClientId: 'client-1' }
  props.context.workspaceAccess.isReadOnly = true
  await state.archiveVehicle()
  assert.equal(state.deleteConfirmOpen.value, false)
  assert.equal(state.canWrite.value, false)

  props.context.workspaceAccess.isReadOnly = false
  assert.equal(state.clientCardUrl.value, '#clients/client-1')
  props.context.shell.menu[0].isVisible = false
  assert.equal(state.clientCardUrl.value, '')
  props.context.shell.menu = []
  assert.equal(state.clientCardUrl.value, '')
})

test('Создание обслуживания и действия с техникой используют отдельные права', () => {
  const template = compileTemplate({ source: descriptor.template.content, filename: filename.pathname, id: 'vehicle-card-test' })
  assert.deepEqual(template.errors, [])
  assert.match(source, /v-if="canCreateMaintenance && !vehicle\.isArchived" :href="maintenanceCreateUrl"/)
  assert.match(source, /v-if="canEditVehicles" variant="secondary" :href="editVehicleUrl"/)
  assert.match(source, /<details v-if="canArchiveVehicles" class="vehicle-profile__more">/)
  assert.match(source, /v-if="vehicle\.isArchived"[^>]*@click="restoreVehicle"/)
  assert.match(source, /v-else[^>]*@click="archiveVehicle"/)
  assert.match(source, /item\.debtAmount > 0 \? item\.debtAmount : item\.paidAmount/)
})

test('Карточка разделяет права редактирования, архивации и создания обслуживания', () => {
  const cases = [
    { name: 'Free владелец', readOnly: false, edit: true, archive: true, maintenance: true, expected: [true, true, true] },
    { name: 'Free участник', readOnly: true, edit: true, archive: true, maintenance: true, expected: [false, false, false] },
    { name: 'PRO только редактирование', readOnly: false, edit: true, archive: false, maintenance: false, expected: [true, false, false] },
    { name: 'PRO только архивирование', readOnly: false, edit: false, archive: true, maintenance: false, expected: [false, true, false] },
    { name: 'PRO без прав техники', readOnly: false, edit: false, archive: false, maintenance: false, expected: [false, false, false] }
  ]

  for (const scenario of cases) {
    const { state, props } = vehicleCard()
    props.context.workspaceAccess = {
      isReadOnly: scenario.readOnly,
      canEditVehicles: scenario.edit,
      canArchiveVehicles: scenario.archive
    }
    props.context.shell.quickActions = [{ code: 'create-maintenance', isVisible: scenario.maintenance }]

    assert.deepEqual([
      state.canEditVehicles.value,
      state.canArchiveVehicles.value,
      state.canCreateMaintenance.value
    ], scenario.expected, scenario.name)
  }
})

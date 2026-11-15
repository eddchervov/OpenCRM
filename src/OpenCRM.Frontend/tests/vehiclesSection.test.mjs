import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const filename = new URL('../packages/workspace/src/components/VehiclesSection.vue', import.meta.url)
const { descriptor } = parse(readFileSync(filename, 'utf8'), { filename: filename.pathname })
const script = compileScript(descriptor, { id: 'vehicles-section-test' })
const compiled = ts.transpileModule(script.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function component(api, workspaceAccess = { isReadOnly: false }, quickActions = []) {
  const module = { exports: {} }
  const dependencies = {
    '../api/vehicles': {
      getVehicleDisplayName: vehicle => `${vehicle.brandName} ${vehicle.modelName}`,
      ...api
    },
    '../utils/vehicleBrandLogo': { getVehicleBrandLogo: () => null },
    './WorkspaceAccessNotice.vue': {},
    './WorkspacePageHelpButton.vue': {},
    '@opencrm/ui': {}
  }
  const resolve = id => {
    if (id === 'vue') {
      return { ...vue, onMounted() {}, onBeforeUnmount() {}, watch() {} }
    }
    return dependencies[id]
  }
  const window = { localStorage: { getItem: () => null, setItem() {} } }
  const evaluate = new Function('require', 'module', 'exports', 'window', 'document', 'HTMLInputElement', 'setTimeout', 'clearTimeout', compiled)
  evaluate(resolve, module, module.exports, window, {}, class {}, () => 1, () => {})
  return module.exports.default.setup({
    context: {
      user: { id: 'user-1' },
      organization: { id: 'org-1' },
      shell: { quickActions },
      workspaceAccess
    }
  }, { expose() {}, emit() {} })
}

test('Создание техники зависит от быстрого действия', () => {
  const cases = [
    { name: 'Free владелец', readOnly: false, create: true, expectedCreate: true },
    { name: 'Free участник', readOnly: true, create: true, expectedCreate: false },
    { name: 'PRO без права создания', readOnly: false, create: false, expectedCreate: false }
  ]

  for (const scenario of cases) {
    const state = component({}, {
      isReadOnly: scenario.readOnly
    }, [{ code: 'create-vehicle', isVisible: scenario.create }])

    assert.equal(state.canCreateVehicle.value, scenario.expectedCreate, scenario.name)
  }
})

function deferred() {
  let resolve
  const promise = new Promise(complete => { resolve = complete })
  return { promise, resolve }
}

test('Поздний ответ списка техники не перезаписывает результат нового запроса', async () => {
  const oldResponse = deferred()
  const vehicle = { id: 'vehicle-new', maintenanceCount: 0 }
  const state = component({
    loadVehiclesPage: (organizationId, page, pageSize, options) => options.searchQuery === 'old'
      ? oldResponse.promise
      : Promise.resolve({ items: [vehicle], userBrands: [], userKinds: [], page, pageSize, totalCount: 1, allCount: 1 }),
    loadVehiclePhotos: async () => ({})
  })

  state.searchQuery.value = 'old'
  const oldRequest = state.loadVehicles()
  state.searchQuery.value = 'new'
  await state.loadVehicles()
  oldResponse.resolve({ items: [{ id: 'vehicle-old' }], userBrands: [], userKinds: [], page: 1, pageSize: 10, totalCount: 1, allCount: 1 })
  await oldRequest

  assert.deepEqual(state.vehicles.value, [vehicle])
  assert.equal(state.isLoading.value, false)
})

test('Поздний ответ фотографий не перезаписывает фотографии актуального списка', async () => {
  const oldPhotos = deferred()
  const oldVehicle = { id: 'vehicle-old', maintenanceCount: 0 }
  const newVehicle = { id: 'vehicle-new', maintenanceCount: 0 }
  const state = component({
    loadVehiclesPage: (organizationId, page, pageSize, options) => Promise.resolve({
      items: [options.searchQuery === 'old' ? oldVehicle : newVehicle],
      userBrands: [],
      userKinds: [],
      page,
      pageSize,
      totalCount: 1,
      allCount: 1
    }),
    loadVehiclePhotos: async (organizationId, vehicleIds) => vehicleIds[0] === oldVehicle.id
      ? oldPhotos.promise
      : { [newVehicle.id]: [{ url: 'new-photo.jpg' }] }
  })

  state.searchQuery.value = 'old'
  const oldRequest = state.loadVehicles()
  await Promise.resolve()
  await Promise.resolve()
  state.searchQuery.value = 'new'
  await state.loadVehicles()
  oldPhotos.resolve({ [oldVehicle.id]: [{ url: 'old-photo.jpg' }] })
  await oldRequest

  assert.deepEqual(state.photosByVehicleId.value, { [newVehicle.id]: 'new-photo.jpg' })
})

test('Восстановление без сохранённого состояния сбрасывает все фильтры и параметры списка', () => {
  const state = component({ loadVehiclesPage: async () => ({}), loadVehiclePhotos: async () => ({}) })
  state.searchQuery.value = 'старый поиск'
  state.selectedBrandFilter.value = 'catalog:5'
  state.selectedKind.value = '9'
  state.isArchiveMode.value = true
  state.page.value = 8
  state.pageSize.value = 50
  state.selectedPageSize.value = '50'
  state.viewMode.value = 'cards'

  state.restoreListState()

  assert.equal(state.searchQuery.value, '')
  assert.equal(state.selectedBrandFilter.value, 'all')
  assert.equal(state.selectedKind.value, 'all')
  assert.equal(state.isArchiveMode.value, false)
  assert.equal(state.page.value, 1)
  assert.equal(state.pageSize.value, 10)
  assert.equal(state.selectedPageSize.value, '10')
  assert.equal(state.viewMode.value, 'list')
})

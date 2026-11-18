import '../packages/workspace/src/bookingFormMessages.ts'
import '../packages/workspace/src/maintenanceCardMessages.ts'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { isSimpleMaintenanceWorkflow } from '../packages/workspace/src/utils/maintenanceWorkflow.ts'

const filename = new URL('../packages/workspace/src/components/MaintenanceFormSection.vue', import.meta.url)
const { descriptor } = parse(readFileSync(filename, 'utf8'))
const script = compileScript(descriptor, { id: 'maintenance-form-test' })
const code = ts.transpileModule(script.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function form({ photos = false, readOnly = false, edit = false, currencyCode, canChangeFinancialData, organizationCurrencyCode = 'RUB' } = {}) {
  const module = { exports: {} }
  const calls = []
  const resolve = id => {
    if (Object.hasOwn(regionalTestDependencies, id)) return regionalTestDependencies[id]
    if (id === 'vue') return { ...vue, onMounted() {}, watch() {}, provide() {}, inject() {} }
    if (id === '@opencrm/ui') return { formatDateOnly: () => '2026-10-29' }
    if (id === '../utils/maintenanceWorkflow') return { isSimpleMaintenanceWorkflow }
    if (id === '../analytics') return { trackWorkspaceGoal() {} }
    if (id === '../api/scenarioCatalog') return { loadScenarioCatalog: async () => ({ services: [], workPosts: [], materials: [] }) }
    if (id === '../api/vehicles') return {
      loadVehiclesPage: async () => ({ items: [] }),
      loadVehicle: async () => ({ id: 'vehicle-1' })
    }
    if (id === '../api/clients') return { loadClientOptions: async () => [] }
    if (id === '../api/maintenance') return {
      loadMaintenance: async () => ({ vehicleId: 'vehicle-1', status: 1, type: 1, date: '2026-10-29', currencyCode, canChangeFinancialData, maintenanceDetails: [] }),
      createMaintenance: async (...args) => { calls.push(['create', ...args]); return 'new-order' },
      updateMaintenance: async (...args) => { calls.push(['update', ...args]) },
      uploadMaintenancePhotos: async () => calls.push(['upload']),
      deleteMaintenancePhoto: async () => calls.push(['delete']),
      loadMaintenancePhotos: async () => { calls.push(['photos']); return [{ photoId: 'saved-photo' }] }
    }
    return {}
  }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const props = vue.reactive({
    context: {
      organization: { id: 'org-1', currencyCode: organizationCurrencyCode, primaryScenarioCode: 'detailing' },
      shell: { scenario: { code: 'detailing', modules: { photos } } },
      workspaceAccess: { isReadOnly: readOnly }
    },
    initialVehicleId: 'vehicle-1', fromVehicle: true,
    maintenanceId: edit ? 'existing-order' : undefined
  })
  const state = module.exports.default.setup(props, { expose() {}, emit() {} })
  state.form.vehicleId = 'vehicle-1'
  state.isFormReady.value = true
  state.servicesEditor.value = { validate: () => '' }
  state.serviceLines.value = [state.createServiceLine({ name: 'Мойка', price: 0, quantity: 1 })]
  return { state, props, calls }
}

test('Free сохраняет запись из автомобиля и сразу открывает её без предложения фото', async () => {
  const { state, calls } = form()
  const previousWindow = globalThis.window
  globalThis.window = { location: { hash: '' } }
  try {
    await state.saveMaintenance()
    assert.equal(calls[0]?.[0], 'create')
    assert.equal(state.photoPromptMaintenanceId.value, '')
    assert.equal(window.location.hash, '#maintenance/new-order?from=vehicle')
  } finally {
    globalThis.window = previousWindow
  }
})

test('PRO сохраняет предложение фото после создания записи из автомобиля', async () => {
  const { state, calls } = form({ photos: true })
  await state.saveMaintenance()
  assert.equal(calls[0]?.[0], 'create')
  assert.equal(state.photoPromptMaintenanceId.value, 'new-order')
})

test('Free не отправляет загрузку и удаление фото, сохранённые фото читаются', async () => {
  const { state, calls } = form({ edit: true })
  await state.loadForm()
  assert.equal(state.isFormReady.value, true)
  assert.deepEqual(calls, [])
  await state.loadPhotoHistory({ target: { open: false } })
  assert.deepEqual(calls, [])
  await state.loadPhotoHistory({ target: { open: true } })
  assert.equal(state.photos.value[0].photoId, 'saved-photo')
  await state.loadPhotoHistory({ target: { open: true } })
  await state.uploadPhotos({ target: { files: [{ type: 'image/jpeg', size: 100 }], value: 'file' } })
  state.removePhoto({ photoId: 'saved-photo' })
  assert.equal(state.photoDeleteConfirm.value, null)
  state.photoDeleteConfirm.value = { photoId: 'saved-photo' }
  await state.confirmRemovePhoto()
  assert.deepEqual(calls, [['photos']])
})

test('PRO сохраняет загрузку и удаление фото в форме редактирования', async () => {
  const { state, calls } = form({ photos: true, edit: true })
  await state.uploadPhotos({ target: { files: [{ type: 'image/jpeg', size: 100 }], value: 'file' } })
  state.removePhoto({ photoId: 'saved-photo' })
  assert.equal(state.photoDeleteConfirm.value.photoId, 'saved-photo')
  await state.confirmRemovePhoto()
  assert.deepEqual(calls, [['upload'], ['photos'], ['delete'], ['photos']])
})

test('Фото в PRO остаются недоступны в режиме чтения', () => {
  const { state } = form({ photos: true, readOnly: true })
  assert.equal(state.canEditPhotos.value, false)
})

test('Историческая валюта переводит форму обслуживания в режим чтения до обновления', async () => {
  const { state, calls } = form({ edit: true, currencyCode: 'RUB', organizationCurrencyCode: 'CNY' })
  await state.loadForm()

  assert.equal(state.isReadOnly.value, true)
  assert.match(state.readOnlyMessage.value, /RUB.*CNY/)

  await state.saveMaintenance()

  assert.equal(calls.some(call => call[0] === 'update'), false)
})

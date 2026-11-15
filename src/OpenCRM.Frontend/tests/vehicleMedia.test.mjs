import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const filename = new URL('../packages/workspace/src/components/VehicleMediaSection.vue', import.meta.url)
const source = readFileSync(filename, 'utf8')
const { descriptor } = parse(source, { filename: filename.pathname })
const script = compileScript(descriptor, { id: 'vehicle-media-test' })
const template = compileTemplate({ source: descriptor.template.content, filename: filename.pathname, id: 'vehicle-media-test' })
const code = ts.transpileModule(script.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function vehicleMedia(mode = 'photos') {
  const calls = []
  const api = new Proxy({}, {
    get: (_target, name) => {
      if (name === 'getVehicleDisplayName') return item => `${item.brandName} ${item.modelName}`
      return async (...args) => {
        calls.push([name, ...args])
        if (name === 'loadVehiclePhotoList') return []
        return name === 'loadVehicle' ? { id: 'vehicle-1', brandName: 'BMW', modelName: 'X3' } : { albums: [] }
      }
    }
  })
  const resolve = id => {
    if (id === 'vue') return { ...vue, inject: () => undefined, provide() {}, onMounted() {} }
    if (id === '@opencrm/ui') return {
      pageBreadcrumbsKey: Symbol('pageBreadcrumbs'), UiButton: {}, UiCard: {}, UiConfirmDialog: {},
      UiImagePreview: {}, UiInput: {}, UiLoader: {}
    }
    if (id === '../api/vehicles') return api
    if (id === './WorkspaceProLock.vue') return {}
    throw new Error('Не задана зависимость: ' + id)
  }
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const props = vue.reactive({
    context: { organization: { id: 'org-1' }, shell: { scenario: { modules: { photos: false } } }, workspaceAccess: {} },
    vehicleId: 'vehicle-1',
    mode
  })
  const state = module.exports.default.setup(props, { expose() {}, emit() {} })

  return { state, props, calls }
}

test('Обычная галерея Free загружается и доступна для изменения без PRO', async () => {
  const { state, calls } = vehicleMedia()

  await state.loadMedia()

  assert.equal(calls.some(([name]) => name === 'loadVehiclePhotoList'), true)
  assert.equal(calls.some(([name]) => name === 'loadVehicleAlbums'), false)
  assert.equal(state.isPhotoPreview.value, false)
  assert.equal(state.canChangePhotos.value, true)
})

test('Режим чтения по-прежнему запрещает изменение обычной галереи', () => {
  const { state, props } = vehicleMedia()
  props.context.workspaceAccess.isReadOnly = true
  assert.equal(state.canChangePhotos.value, false)
})

test('Free может загрузить фото автомобиля, выбрать основное и удалить', async () => {
  const { state, calls } = vehicleMedia()
  await state.loadMedia()
  await state.uploadPhotos({ target: { files: [{ type: 'image/jpeg', size: 100 }], value: 'photo' } })
  await state.makePrimary({ photoId: 'ordinary-photo' })
  await state.removePhoto({ photoId: 'ordinary-photo' })
  await state.confirmAction()
  assert.equal(calls.filter(([name]) => name === 'uploadVehiclePhotos').length, 1)
  assert.equal(calls.filter(([name]) => name === 'setPrimaryVehiclePhoto').length, 1)
  assert.equal(calls.filter(([name]) => name === 'deleteVehiclePhoto').length, 1)
})

test('Ограничение галереи в 10 фото сохраняется на Free', async () => {
  const { state, calls } = vehicleMedia()
  state.photos.value = Array.from({ length: 10 }, (_, index) => ({ photoId: String(index) }))
  await state.uploadPhotos({ target: { files: [{ type: 'image/jpeg', size: 100 }], value: 'photo' } })
  assert.equal(calls.some(([name]) => name === 'uploadVehiclePhotos'), false)
  assert.match(state.errorMessage.value, /10 фото/)
})

test('Шаблон раздела фото собирается без ошибок', () => {
  assert.deepEqual(template.errors, [])
})

test('Альбомы Free не загружаются до явного открытия сохранённой истории', async () => {
  const { state, calls } = vehicleMedia('albums')

  await state.loadMedia()

  assert.equal(calls.some(([name]) => name === 'loadVehicleAlbums'), false)
  assert.equal(state.canChangePhotos.value, false)
  state.albumTitle.value = 'Новый альбом'
  await state.createAlbum()
  assert.equal(calls.some(([name]) => name === 'createVehicleAlbum'), false)

  await state.toggleSavedMediaHistory()

  assert.equal(calls.some(([name]) => name === 'loadVehicleAlbums'), true)
  assert.equal(state.canChangePhotos.value, false)
})

import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import * as registrationPlates from '../packages/workspace/src/vehicleRegistrationPlate.ts'
import * as internationalPlates from '../packages/workspace/src/internationalRegistrationPlate.ts'
import { registrationCountryOptions } from '../packages/workspace/src/utils/registrationCountries.ts'

const vehicleMessages = {}
const messagesSource = readFileSync(new URL('../packages/workspace/src/vehicleMessages.ts', import.meta.url), 'utf8')
new Function('require', 'exports', ts.transpileModule(messagesSource, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText)(() => ({
  registerMessages(locale, messages) { if (locale === 'ru') Object.assign(vehicleMessages, messages.vehicle) }
}), {})
const translate = (key, parameters = {}) => (vehicleMessages[key.replace(/^vehicle\./, '')] ?? key)
  .replace(/\{(\w+)\}/g, (_, name) => String(parameters[name] ?? ''))

const filename = new URL('../packages/workspace/src/components/VehicleFormSection.vue', import.meta.url)
const { descriptor } = parse(readFileSync(filename, 'utf8'))
const script = compileScript(descriptor, { id: 'vehicle-form-test' })
const code = ts.transpileModule(script.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function setup(api = {}, values = {}, registrationApi = {}, clientsApi = {}) {
  const module = { exports: {} }
  const calls = []
  const mountedHooks = []
  const unmountedHooks = []
  const watchers = []
  const props = vue.reactive({
    context: { organization: { id: 'org' }, shell: { scenario: { code: 'detailing' }, menu: [{ code: 'clients', isVisible: true }], quickActions: [{ code: 'create-vehicle', isVisible: true }] }, workspaceAccess: { canEditVehicles: true } },
    vehicleId: '1', ...values
  })
  const resolve = id => {
    if (id === 'vue') return {
      ...vue,
      onMounted: hook => mountedHooks.push(hook),
      onBeforeUnmount: hook => unmountedHooks.push(hook),
      watch: (_, callback) => watchers.push(callback)
    }
    if (id === '@opencrm/ui') return { useLocalization: () => ({ t: translate, locale: vue.ref('ru') }) }
    if (id.endsWith('.vue') || id === '../localization' || id === '../vehicleMessages') return {}
    if (id === '../internationalRegistrationPlate') return internationalPlates
    if (id === '../utils/registrationCountries') return { registrationCountryOptions }
    if (id === '../analytics') return { trackWorkspaceGoal() {} }
    if (id === '../vehicleRegistrationPlate') return { ...registrationPlates, ...registrationApi }
    if (id === '../utils/vehicleBrandLogo') return { getVehicleBrandLogo: key => key ? { name: key, src: key } : null }
    if (id === '../api/clients') return {
      loadClientOptions: clientsApi.loadClientOptions ?? (async () => [{ id: 'client', name: 'Клиент' }])
    }
    if (id === '../api/vehicles') return {
      loadVehicle: async (_, vehicleId) => ({
        id: vehicleId, kind: 1, brandId: 1, modelId: 2, brandLogoKey: 'bmw', brandName: 'BMW', modelName: 'X3',
        registrationPlate: 'А001АА 154', vin: 'VIN', yearIssue: 2021, organizationClientId: 'client', photoUrl: '/cover.jpg'
      }),
      loadVehicleBrandOptions: async () => [{ id: 1, name: 'BMW' }],
      loadVehicleModelOptions: async () => [{ id: 2, name: 'X3' }],
      loadVehiclePhotoList: async (...args) => { calls.push(['photos', ...args]); return [] },
      updateVehicle: async () => calls.push(['update']), createVehicle: async () => calls.push(['create']),
      ...api
    }
    throw new Error('Не задана зависимость: ' + id)
  }
  const navigation = { location: { hash: '#vehicles/1/edit' } }
  new Function('require', 'module', 'exports', 'window', code)(resolve, module, module.exports, navigation)
  const state = module.exports.default.setup(props, { expose() {}, emit() {} })
  return { state, props, calls, navigation, mountedHooks, unmountedHooks, watchers }
}

test('Неизвестная страна использует свободный номер, а редактирование прежней машины не заменяет null на RU', async () => {
  const { state } = setup()
  await state.loadForm()
  assert.equal(state.form.registrationCountryCode, 'RU')
  assert.equal(state.buildPayload().registrationCountryCode, null)
  state.form.registrationCountryCode = 'ZZ'
  state.form.registrationPlate = ' 沪A·abc123 '
  assert.equal(state.isInternationalPlate.value, true)
  const payload = state.buildPayload()
  assert.equal(payload.registrationCountryCode, 'ZZ')
  assert.equal(payload.registrationPlate, '沪A·abc123')
})

test('Новая форма не запрашивает фото и сохраняет привязку к клиенту', async () => {
  const { state, calls } = setup({}, { vehicleId: undefined, initialClientId: 'client' })
  await state.loadForm()
  assert.equal(state.form.organizationClientId, 'client')
  assert.equal(state.isClientLocked.value, true)
  assert.equal(state.photosHref.value, '')
  assert.equal(state.currentPhotoUrl.value, '')
  assert.deepEqual(calls, [])
})

test('Начальная форма ждёт форматы и актуальную организацию до появления полей', async () => {
  let finishFormats
  const clientOrganizations = []
  const { state, props, mountedHooks, watchers } = setup({}, { vehicleId: undefined }, {
    loadVehicleRegistrationPlateFormats: () => new Promise(resolve => { finishFormats = resolve })
  }, {
    loadClientOptions: async organizationId => {
      clientOrganizations.push(organizationId)
      return []
    }
  })

  assert.equal(state.isLoading.value, true)
  assert.match(descriptor.template.content, /<form v-if="!isLoading/)
  const initialization = mountedHooks[0]()
  await Promise.resolve()
  props.context.organization.id = 'org-2'
  await watchers[0]()
  assert.equal(state.isLoading.value, true)
  assert.deepEqual(clientOrganizations, [])

  finishFormats({ maxLength: 32, formats: [], characterMappings: {} })
  await initialization

  assert.equal(state.isLoading.value, false)
  assert.deepEqual(clientOrganizations, ['org-2'])
  state.form.registrationPlate = 'А123АА 154'
  state.form.kind = '2'
  state.setManualIdentity(true)
  await vue.nextTick()
  assert.equal(state.form.registrationPlate, 'А123АА 154')
  assert.equal(state.form.kind, '2')
  assert.equal(state.isManualIdentity.value, true)
})

test('Ошибка загрузки форматов не оставляет начальную форму в состоянии загрузки', async () => {
  const { state, mountedHooks } = setup({}, { vehicleId: undefined }, {
    loadVehicleRegistrationPlateFormats: async () => { throw new Error('Нет связи с сервисом форматов') }
  })

  await mountedHooks[0]()

  assert.equal(state.isLoading.value, false)
  assert.equal(state.errorMessage.value, 'Нет связи с сервисом форматов')
})

test('Смена организации не ждёт старые данные и не даёт им перезаписать новую форму', async () => {
  let finishOldClients
  const clientRequests = []
  const { state, props, mountedHooks, watchers } = setup({}, { vehicleId: undefined }, {
    loadVehicleRegistrationPlateFormats: async () => ({ maxLength: 32, formats: [], characterMappings: {} })
  }, {
    loadClientOptions: organizationId => {
      clientRequests.push(organizationId)
      if (organizationId === 'org') {
        return new Promise(resolve => { finishOldClients = resolve })
      }

      return Promise.resolve([{ id: 'new-client', name: 'Новый клиент' }])
    }
  })

  const initialization = mountedHooks[0]()
  await Promise.resolve()
  assert.deepEqual(clientRequests, ['org'])

  props.context.organization.id = 'org-2'
  await watchers[0]()

  assert.deepEqual(clientRequests, ['org', 'org-2'])
  assert.equal(state.isLoading.value, false)
  assert.equal(state.clientOptions.value[0].id, 'new-client')

  finishOldClients([{ id: 'old-client', name: 'Старый клиент' }])
  await initialization
  await vue.nextTick()

  assert.equal(state.clientOptions.value[0].id, 'new-client')
  assert.equal(state.isLoading.value, false)
})

test('Размонтирование до завершения форматов не запускает загрузку данных', async () => {
  let finishFormats
  const clientRequests = []
  const { state, mountedHooks, unmountedHooks } = setup({}, { vehicleId: undefined }, {
    loadVehicleRegistrationPlateFormats: () => new Promise(resolve => { finishFormats = resolve })
  }, {
    loadClientOptions: async organizationId => {
      clientRequests.push(organizationId)
      return []
    }
  })

  const initialization = mountedHooks[0]()
  unmountedHooks[0]()
  finishFormats({ maxLength: 32, formats: [], characterMappings: {} })
  await initialization

  assert.deepEqual(clientRequests, [])
  assert.equal(state.isLoading.value, true)
})

test('Фото и сводка не меняют существующий payload редактирования', async () => {
  const { state } = setup({ loadVehiclePhotoList: async () => [
    { photoId: 'side', url: '/side.jpg', isPrimary: false },
    { photoId: 'main', url: '/main.jpg', isPrimary: true }
  ] })
  await state.loadForm()
  assert.equal(state.vehicleName.value, 'BMW X3')
  assert.equal(state.currentPhotoUrl.value, '/main.jpg')
  state.selectedPhotoId.value = 'side'
  assert.equal(state.currentPhotoUrl.value, '/side.jpg')
  state.failedPhotos.value.add('/side.jpg')
  assert.equal(state.currentPhotoUrl.value, '/main.jpg')
  const payload = state.buildPayload()
  assert.equal(payload.modelId, 2)
  assert.equal(payload.organizationClientId, 'client')
  assert.equal(payload.registrationPlate, 'А001АА 154')
  assert.equal('photos' in payload, false)
  state.setManualIdentity(true)
  state.form.manualBrandName = 'Другая марка'
  assert.equal(state.brandLogo.value, null)
  assert.equal(state.buildPayload().manualBrandName, 'Другая марка')
})

test('Ошибка фото не мешает редактированию автомобиля', async () => {
  const { state } = setup({ loadVehiclePhotoList: async () => { throw new Error('network') } })
  await state.loadForm()
  assert.equal(state.form.registrationPlate, 'А001АА 154')
  assert.equal(state.errorMessage.value, '')
  assert.equal(state.photoError.value, 'Не удалось загрузить фотографии.')
  assert.equal(state.currentPhotoUrl.value, '/cover.jpg')
})

test('Ошибка соседней миниатюры не меняет выбранное фото', async () => {
  const { state } = setup({ loadVehiclePhotoList: async () => [
    { photoId: 'first', url: '/first.jpg' },
    { photoId: 'selected', url: '/selected.jpg' },
    { photoId: 'last', url: '/last.jpg' }
  ] })
  await state.loadForm()
  state.selectedPhotoId.value = 'selected'
  state.failedPhotos.value.add('/first.jpg')
  assert.equal(state.currentPhotoUrl.value, '/selected.jpg')
})

test('Устаревший ответ фото не попадает в форму другого автомобиля', async () => {
  let finish
  const pending = new Promise(resolve => { finish = resolve })
  const { state, props } = setup({ loadVehiclePhotoList: async (_, id) => id === '1' ? pending : [{ photoId: 'two', url: '/two.jpg' }] })
  await state.loadForm()
  props.vehicleId = '2'
  await state.loadForm()
  finish([{ photoId: 'old', url: '/old.jpg' }])
  await pending
  await vue.nextTick()
  assert.equal(state.currentPhotoUrl.value, '/two.jpg')
})

test('Режим чтения не выполняет сохранение', async () => {
  const { state, props, calls } = setup()
  await state.loadForm()
  props.context.workspaceAccess.isReadOnly = true
  await state.saveVehicle()
  assert.equal(calls.some(call => call[0] === 'update'), false)
  assert.ok(state.errorDialogMessage.value)
})

test('Права формы техники различают создание, редактирование и архивирование', async () => {
  const cases = [
    { name: 'Free владелец', readOnly: false, create: true, edit: true, expectedEditable: true },
    { name: 'Free участник', readOnly: true, create: true, edit: true, expectedEditable: false },
    { name: 'PRO только редактирование', readOnly: false, create: false, edit: true, expectedEditable: true },
    { name: 'PRO только архивирование', readOnly: false, create: false, edit: false, expectedEditable: false },
    { name: 'PRO без прав техники', readOnly: false, create: false, edit: false, expectedEditable: false }
  ]

  for (const scenario of cases) {
    const { state, props, calls } = setup({
      updateVehicle: async () => calls.push(['update']),
      createVehicle: async () => calls.push(['create'])
    })
    props.context.workspaceAccess = {
      isReadOnly: scenario.readOnly,
      canEditVehicles: scenario.edit,
      canArchiveVehicles: scenario.name === 'PRO только архивирование'
    }
    props.context.shell.quickActions = [{ code: 'create-vehicle', isVisible: scenario.create }]
    props.vehicleId = scenario.name === 'Free владелец' || scenario.name === 'Free участник' ? undefined : '1'

    assert.equal(state.isReadOnly.value, !scenario.expectedEditable, scenario.name)
    await state.saveVehicle()
    assert.equal(calls.some(call => call[0] === 'update' || call[0] === 'create'), false, scenario.name)
  }
})


test('Позднее сохранение автомобиля не закрывает форму другого автомобиля', async () => {
  let finish
  const {state,props,navigation} = setup({updateVehicle: async () => new Promise(resolve => {finish=resolve})})
  await state.loadForm()
  state.registrationPlateFormats.value = {maxLength:32,formats:[],characterMappings:{}}
  const pending = state.saveVehicle()
  props.vehicleId = '2'
  await state.loadForm()
  navigation.location.hash = '#vehicles/2/edit'
  finish()
  await pending
  assert.equal(navigation.location.hash,'#vehicles/2/edit')
  assert.equal(state.isSaving.value,false)
})

test('Ошибка прежнего автомобиля не попадает в новую форму', async () => {
  let fail
  const {state,props} = setup({updateVehicle: async () => new Promise((_,reject) => {fail=reject})})
  await state.loadForm()
  state.registrationPlateFormats.value = {maxLength:32,formats:[],characterMappings:{}}
  const pending = state.saveVehicle()
  props.vehicleId = '2'
  await state.loadForm()
  fail(new Error('Ошибка автомобиля 1'))
  await pending
  assert.equal(state.errorDialogMessage.value,'')
  assert.equal(state.isSaving.value,false)
})


test('Переход к другой машине во время загрузки форматов не отправляет обновление новой машины', async () => {
  let finish
  const updates = []
  const {state,props} = setup({updateVehicle: async (...args) => updates.push(args)}, {}, {
    loadVehicleRegistrationPlateFormats: () => new Promise(resolve => {finish=resolve})
  })
  await state.loadForm()
  const pending = state.saveVehicle()
  props.vehicleId = '2'
  await state.loadForm()
  finish({maxLength:32,formats:[],characterMappings:{}})
  await pending
  assert.deepEqual(updates,[])
  assert.equal(state.loadedVehicle.value.id,'2')
  assert.equal(state.isSaving.value,false)
})

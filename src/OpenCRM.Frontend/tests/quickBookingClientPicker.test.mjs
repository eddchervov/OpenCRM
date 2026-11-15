import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { loadClientLookup } from '../packages/workspace/src/api/clients.ts'

function compile(relativePath) {
  const { descriptor } = parse(readFileSync(new URL(relativePath, import.meta.url), 'utf8'))
  return ts.transpileModule(compileScript(descriptor, { id: 'quick-client-test' }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
}
const pickerCode = compile('../packages/workspace/src/components/QuickBookingClientPicker.vue')
const bookingCode = {
  detailing: compile('../Scenarios/detailing-client/src/components/QuickBookingSection.vue'),
  autoservice: compile('../Scenarios/autoservice-client/src/components/QuickBookingSection.vue')
}

function picker(load = async () => ({ items: [], totalCount: 0 }), overrides = {}) {
  const props = vue.reactive({ organizationId:'org', modelValue:'', clientName:'', clientPhone:'', selectedName:'', disabled:false, canCreate:true, ...overrides })
  let unmount
  const module = { exports: {} }
  new Function('require','module','exports', pickerCode)(id => {
    if (id === 'vue') return { ...vue, onBeforeUnmount: callback => { unmount = callback } }
    if (id === '@opencrm/ui') return {}
    if (id === '../api/clients') return { loadClientLookup: load }
    throw new Error(id)
  }, module, module.exports)
  const events = []
  const state = module.exports.default.setup(props, { expose() {}, emit(name, value) {
    events.push([name,value]); props[name.slice('update:'.length)] = value
  } })
  return { state, props, events, unmount: () => unmount?.() }
}

test('Поиск клиента обращается к bounded lookup с запросом и следующей страницей', async () => {
  const calls=[]
  const subject=picker(async (...args) => { calls.push(args); return {items:[{id:String(args[2]),name:'Анна',phone:'+7 000 000-00-01'}],totalCount:10} })
  subject.state.query.value='  +7 900  '
  await vue.nextTick()
  await subject.state.search()
  await subject.state.search(true)
  assert.deepEqual(calls,[['org','+7 900',1],['org','+7 900',2]])
  assert.equal(subject.state.items.value.length,2)
  subject.unmount()
})

test('Поздний ответ прежнего поиска не заменяет нового клиента', async () => {
  let finish
  const subject=picker(async (_,query) => query==='old' ? new Promise(resolve => {finish=resolve}) : {items:[{id:'new',name:'Новый'}],totalCount:1})
  subject.state.query.value='old'; await vue.nextTick()
  const old=subject.state.search()
  subject.state.query.value='new'; await vue.nextTick()
  await subject.state.search()
  finish({items:[{id:'old',name:'Старый'}],totalCount:1}); await old
  assert.equal(subject.state.items.value[0].id,'new')
  subject.unmount()
})

test('Новый телефон становится черновиком без имени и не создаёт клиента отдельным запросом', () => {
  let calls=0
  const subject=picker(async () => { calls++; return {items:[],totalCount:0} })
  subject.state.query.value='+7 000 000-00-00'
  assert.match(subject.state.validate(), /Выберите клиента/)
  subject.state.startNewClient()
  assert.equal(subject.props.clientPhone,'+7 000 000-00-00')
  assert.equal(subject.props.clientName,'')
  assert.equal(subject.props.modelValue,'')
  assert.equal(subject.state.validate(),null)
  assert.equal(calls,0)
  subject.unmount()
})

test('Выбор существующего клиента исключает поля нового, очистка возвращает запись без клиента', async () => {
  const subject=picker(undefined,{clientName:'Новый',clientPhone:'+7 000 000-00-02'})
  subject.state.selectClient({id:'known',name:'Иван',phone:'+7 000 000-00-03'})
  await vue.nextTick()
  assert.equal(subject.props.modelValue,'known')
  assert.equal(subject.props.clientName,'')
  assert.equal(subject.props.clientPhone,'')
  assert.equal(subject.state.selectedLabel.value,'Иван')
  subject.state.clearClient()
  assert.equal(subject.props.modelValue,'')
  assert.equal(subject.state.validate(),null)
  subject.unmount()
})

test('Пустой новый клиент и невалидный телефон не проходят проверку', () => {
  const subject=picker()
  subject.state.startNewClient()
  assert.match(subject.state.validate(),/Укажите телефон или имя/)
  subject.props.clientPhone='123'
  subject.state.phoneValidity.value=false
  assert.match(subject.state.validate(),/Проверьте номер/)
  subject.unmount()
})

test('Смена организации и закрытие формы исключают старые результаты', async () => {
  let finish
  const subject=picker(() => new Promise(resolve => {finish=resolve}))
  subject.state.query.value='Анна'
  await vue.nextTick()
  const pending=subject.state.search()
  subject.props.organizationId='other'; await vue.nextTick()
  finish({items:[{id:'foreign',name:'Другой'}],totalCount:1}); await pending
  assert.deepEqual(subject.state.items.value,[])
  assert.equal(subject.props.modelValue,'')
  subject.unmount()
})

function booking(api = {}, workspaceApi = {}, scenario = 'detailing') {
  const sent=[]
  const props=vue.reactive({context:{organization:{id:'org',timeZoneId:'UTC'},workspaceAccess:{isReadOnly:false},shell:{scenario:{code:scenario}},links:{}},modal:true})
  const module={exports:{}}
  const workspace={
    formatDateOnly:()=> '2026-10-30', getVehicleBrandLogo:()=>null,
    getVehicleDisplayName:vehicle=>vehicle.name || vehicle.registrationPlate,
    getVehicleRegistrationPlateFormat:()=>undefined, loadVehicleRegistrationPlateFormats:async()=>({}),
    normalizeRegistrationPlate:value=>value, validateRegistrationPlate:()=>'',trackWorkspaceGoal(){},
    loadVehicleBrandOptions:async()=>[],loadVehicleModelOptions:async()=>[],...workspaceApi
  }
  new Function('require','module','exports','window',bookingCode[scenario])(id=> {
    if(id==='vue')return {...vue,onMounted(){},onBeforeUnmount(){},useId:()=> 'quick-test'}
    if(id==='@opencrm/ui')return {formatDateOnly:()=> '2026-10-30'}
    if(id==='@opencrm/workspace')return workspace
    if(id==='@/api/detailingBff')return {createDetailingQuickBooking:async (_,body)=>{sent.push(body);return {maintenanceId:'created'}},...api}
    if(id==='@/api/autoserviceBff')return {createAutoServiceQuickBooking:async (_,body)=>{sent.push(body);return {maintenanceId:'created'}},...api}
    throw new Error(id)
  },module,module.exports,{clearTimeout(){},setTimeout:()=>1,location:{hash:''}})
  const state=module.exports.default.setup(props,{expose(){},emit(){}})
  state.form.registrationPlate='А123ВС54'
  state.registrationPlateFormats.value={}
  state.selectedServices.value=[{catalogItemId:'service',name:'Мойка',quantity:1,price:2500}]
  state.servicesEditor.value={validate:()=>null}
  return {state,props,sent}
}

test('Детейлинг передаёт выбранный ID бесхозной машины и новый телефон вместе с записью', async () => {
  const {state,sent}=booking()
  const vehicle={id:'car',registrationPlate:'А123ВС54',kind:1}
  state.foundVehicles.value=[vehicle]; state.applyVehicle(vehicle)
  state.form.clientPhone='+7 000 000-00-01'
  await state.saveBooking()
  assert.equal(sent[0].vehicleId,'car')
  assert.equal(sent[0].clientPhone,'+7 000 000-00-01')
  assert.equal(sent[0].clientName,null)
  assert.equal(sent[0].organizationClientId,null)
})

test('Машина другого клиента не стирает введённый новый контакт или выбранного клиента', () => {
  const {state}=booking()
  state.form.clientPhone='+7 000 000-00-01'
  state.applyVehicle({id:'other',registrationPlate:'В456ВС54',organizationClientId:'owner',organizationClientName:'Владелец'})
  assert.equal(state.selectedVehicleId.value,'')
  assert.equal(state.form.clientPhone,'+7 000 000-00-01')
  assert.match(state.errorMessage.value,/другой клиент/)
  state.form.clientPhone=''; state.form.organizationClientId='chosen'
  state.applyVehicle({id:'other',registrationPlate:'В456ВС54',organizationClientId:'owner'})
  assert.equal(state.form.organizationClientId,'chosen')
})

for (const scenario of ['detailing', 'autoservice']) {
  test(`Правка черновика очищает ошибку предыдущей отправки в сценарии ${scenario}`, async () => {
    const apiName = scenario === 'detailing' ? 'createDetailingQuickBooking' : 'createAutoServiceQuickBooking'
    const { state } = booking({ [apiName]: async () => { throw new Error('Пост занят после проверки.') } }, {}, scenario)
    state.form.plannedStartTime = '17:45'
    state.form.plannedEndTime = '18:15'
    await vue.nextTick()

    await state.saveBooking()

    assert.equal(state.errorMessage.value, 'Пост занят после проверки.')
    assert.equal(state.formErrorSource.value, 'submission')
    state.form.plannedStartTime = '18:00'
    await vue.nextTick()

    assert.equal(state.errorMessage.value, '')
    assert.equal(state.form.registrationPlate, 'А123ВС54')
    assert.equal(state.selectedServices.value.length, 1)
    assert.equal(state.form.plannedEndTime, '18:15')

    await state.saveBooking()
    assert.equal(state.formErrorSource.value, 'submission')
    state.allowOtherRegistrationPlateFormat.value = true
    await vue.nextTick()

    assert.equal(state.errorMessage.value, '')
    assert.equal(state.form.plannedStartTime, '18:00')
  })

  test(`Исправление времени очищает только его ошибку в сценарии ${scenario}`, async () => {
    const { state, sent } = booking({}, {}, scenario)
    state.form.plannedStartTime = '10:00'
    state.form.plannedEndTime = '11:00'
    state.bookingTimes.value = {
      validate: async () => false,
      getValidationError: () => 'Время уже занято.'
    }

    await state.saveBooking()

    assert.equal(state.errorMessage.value, 'Время уже занято.')
    assert.equal(state.formErrorSource.value, 'time')
    assert.equal(state.form.plannedStartTime, '10:00')
    assert.equal(state.form.plannedEndTime, '11:00')
    assert.equal(state.selectedServices.value.length, 1)
    assert.deepEqual(sent, [])

    state.form.plannedStartTime = '12:00'
    await vue.nextTick()

    assert.equal(state.errorMessage.value, '')
    assert.equal(state.errorDialogMessage.value, '')
    assert.equal(state.form.plannedStartTime, '12:00')
    assert.equal(state.form.plannedEndTime, '11:00')

    state.showFormError('Ошибка другого поля.', false)
    state.form.location = 'Пост 2'
    await vue.nextTick()

    assert.equal(state.errorMessage.value, 'Ошибка другого поля.')
    assert.equal(state.formErrorSource.value, 'other')
  })
}

test('Ошибка сохранения оставляет контакт, машину и услугу в черновике', async () => {
  const {state}=booking({createDetailingQuickBooking:async()=>{throw new Error('Нет связи')}})
  state.form.clientPhone='+7 000 000-00-01'
  await state.saveBooking()
  assert.equal(state.form.clientPhone,'+7 000 000-00-01')
  assert.equal(state.form.registrationPlate,'А123ВС54')
  assert.equal(state.selectedServices.value.length,1)
  assert.equal(state.errorMessage.value,'Нет связи')
})

test('Новый ввод после изменения клиента не сохраняет прежний ID', async () => {
  const subject=picker()
  subject.state.selectClient({id:'old-client',name:'Иван'})
  await vue.nextTick()
  subject.state.clearClient()
  subject.state.query.value='+7 000 000-00-04'
  subject.state.startNewClient()
  await vue.nextTick()
  assert.equal(subject.props.modelValue,'')
  assert.equal(subject.props.clientPhone,'+7 000 000-00-04')
  assert.equal(subject.state.isOpen.value,false)
  subject.unmount()
})

test('Lookup передаёт телефон и страницу без загрузки полного списка клиентов', async t => {
  const oldFetch=globalThis.fetch
  const oldWindow=globalThis.window
  const calls=[]
  globalThis.window={location:{origin:'https://example.test'}}
  globalThis.fetch=async (...args)=>{calls.push(args);return Response.json({items:[],totalCount:0})}
  t.after(()=>{globalThis.fetch=oldFetch;if(oldWindow===undefined)delete globalThis.window;else globalThis.window=oldWindow})
  await loadClientLookup('org','+7 000 000-00-00',2)
  const url=new URL(calls[0][0])
  assert.equal(url.pathname,'/api/v1/organizations/org/clients/lookup')
  assert.equal(url.searchParams.get('searchQuery'),'+7 000 000-00-00')
  assert.equal(url.searchParams.get('page'),'2')
  assert.equal(calls[0][1].credentials,'same-origin')
})

test('Пустой и однобуквенный поиск не загружает список клиентов', async () => {
  let calls = 0
  const subject = picker(async () => { calls++; return { items: [], totalCount: 0 } })
  await subject.state.search()
  subject.state.query.value = 'А'
  await vue.nextTick()
  await subject.state.search()
  assert.equal(calls, 0)
  assert.deepEqual(subject.state.items.value, [])
  subject.unmount()
})


test('Медленный ответ моделей прежней марки не подменяет модели новой марки', async () => {
  const responses = new Map()
  const {state} = booking({}, {loadVehicleModelOptions: brand => new Promise(resolve => responses.set(brand,resolve))})
  state.selectedBrandId.value = 'old'
  await vue.nextTick()
  state.selectedBrandId.value = 'new'
  await vue.nextTick()
  responses.get('new')([{id:22,name:'Новая модель'}])
  await vue.nextTick()
  responses.get('old')([{id:11,name:'Прежняя модель'}])
  await vue.nextTick()
  assert.equal(state.models.value[0].id,22)
})

test('Очистка марки отменяет влияние её незавершённого поиска моделей', async () => {
  let finish
  const {state} = booking({}, {loadVehicleModelOptions: () => new Promise(resolve => {finish=resolve})})
  state.selectedBrandId.value = 'old'
  await vue.nextTick()
  state.selectedBrandId.value = ''
  await vue.nextTick()
  finish([{id:11,name:'Прежняя модель'}])
  await vue.nextTick()
  assert.deepEqual(state.models.value,[])
  assert.equal(state.isLoadingModels.value,false)
})

test('Поздние марки грузовика не подменяют марки выбранного мотоцикла', async () => {
  const responses = new Map()
  const {state} = booking({}, {loadVehicleBrandOptions: kind => new Promise(resolve => responses.set(kind,resolve))})
  state.form.kind = '2'
  await vue.nextTick()
  state.form.kind = '3'
  await vue.nextTick()
  responses.get('3')([{id:33,name:'Мотоцикл'}])
  await vue.nextTick()
  responses.get('2')([{id:22,name:'Грузовик'}])
  await vue.nextTick()
  assert.equal(state.brands.value[0].id,33)
})


test('Переход на ручной ввод не восстанавливает модели из прежнего запроса', async () => {
  let finish
  const {state} = booking({}, {loadVehicleModelOptions: () => new Promise(resolve => {finish=resolve})})
  state.selectedBrandId.value = 'old'
  await vue.nextTick()
  state.isManualIdentity.value = true
  state.form.manualBrandName = 'Ручная марка'
  await vue.nextTick()
  finish([{id:11,name:'Прежняя модель'}])
  await vue.nextTick()
  assert.deepEqual(state.models.value,[])
  assert.equal(state.form.manualBrandName,'Ручная марка')
  assert.equal(state.isLoadingModels.value,false)
})

test('Ошибка старой марки не очищает модели новой и не завершает её загрузку', async () => {
  const responses = new Map()
  const {state} = booking({}, {loadVehicleModelOptions: brand => new Promise((resolve,reject) => responses.set(brand,{resolve,reject}))})
  state.selectedBrandId.value = 'old'
  await vue.nextTick()
  state.selectedBrandId.value = 'new'
  await vue.nextTick()
  responses.get('old').reject(new Error('Медленный отказ'))
  await vue.nextTick()
  assert.equal(state.isLoadingModels.value,true)
  responses.get('new').resolve([{id:22,name:'Новая модель'}])
  await vue.nextTick()
  assert.equal(state.models.value[0].id,22)
  assert.equal(state.isLoadingModels.value,false)
})


test('Выбор существующей машины во время загрузки не теряет справочник её типа', async () => {
  let finish
  const {state} = booking({}, {loadVehicleBrandOptions: () => new Promise(resolve=>{finish=resolve})})
  state.form.kind = '2'
  await vue.nextTick()
  const vehicle = {id:'car',kind:2,registrationPlate:'А123ВС54',brandId:22,brandName:'Марка'}
  state.foundVehicles.value = [vehicle]
  state.applyVehicle(vehicle)
  await vue.nextTick()
  finish([{id:22,name:'Марка'}])
  await vue.nextTick()
  assert.equal(state.brands.value[0].id,22)
})

test('Выбрать другой после машины иного типа загружает марки текущего типа', async () => {
  const kinds = []
  const {state} = booking({}, {loadVehicleBrandOptions: async kind => {kinds.push(kind);return [{id:33,name:'Yamaha'}]}})
  const vehicle = {id:'bike',kind:3,registrationPlate:'1234АА54',brandId:33,brandName:'Yamaha'}
  state.foundVehicles.value = [vehicle]
  state.applyVehicle(vehicle)
  await vue.nextTick()
  state.clearVehicleSelection()
  await vue.nextTick()
  assert.deepEqual(kinds,['3'])
  assert.equal(state.brands.value[0].id,33)
})

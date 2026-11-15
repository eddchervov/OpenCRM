import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { formatServiceDuration } from '../packages/workspace/src/serviceDuration.ts'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceCatalogSection.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'catalog-editor' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function setup(kind = 'service_bundle', canManage = true) {
  const services = vue.reactive([
    { id: 'a', kindCode: 'service', name: 'Мойка', unitPrice: 2000, durationMinutes: 60, isActive: true, isUsed: true, sortOrder: 1 },
    { id: 'b', kindCode: 'service', name: 'Полировка', unitPrice: 12000, durationMinutes: 180, isActive: true, isUsed: true, sortOrder: 2 },
    { id: 'archived', kindCode: 'service', name: 'Старая услуга', unitPrice: 500, durationMinutes: 15, isActive: false, sortOrder: 3 }
  ])
  const bundle = { id: 'bundle', kindCode: 'service_bundle', name: 'Комплекс', serviceIds: ['a', 'b'], durationMinutes: 180, isActive: true, isUsed: true, sortOrder: 1 }
  const props = vue.reactive({
    selectedCatalogKind: kind, selectedCatalogGroup: { title: 'Услуги' }, catalogGroups: [],
    canManageCatalog: canManage, isSaving: false, actionErrorMessage: '', isEditingCatalog: false,
    catalogCreateForms: Object.fromEntries(['service', 'service_bundle', 'work_post', 'material'].map(kind => [kind, { name: '', description: '', unitPrice: '', durationMinutes: '', serviceIds: [] }])),
    catalogDrafts: {}, getCatalogItems: kind => kind === 'service' ? services : [bundle], getActiveCatalogCount: () => 2
  })
  const module = { exports: {} }
  const events = []
  const resolve = id => {
    if (id === 'vue') return { ...vue, onBeforeUnmount() {}, watch() {} }
    if (id === '../serviceDuration') return { formatServiceDuration }
    return {}
  }
  new Function('require', 'module', 'exports', 'document', code)(resolve, module, module.exports, { activeElement: null })
  const state = module.exports.default.setup(props, { expose() {}, emit: (...args) => events.push(args) })
  return { state, props, services, bundle, events }
}

test('Поиск состава не меняет выбор и итог, индивидуальная длительность набора перекрывает сумму услуг', () => {
  const { state, bundle } = setup()
  state.openItem(bundle, true)
  assert.equal(state.bundlePrice.value, 14000)
  assert.equal(state.bundleDuration.value, 180)
  state.serviceSearch.value = 'ПОЛИРОВКА'
  assert.deepEqual(state.filteredServices.value.map(item => item.id), ['b'])
  assert.deepEqual(state.form.value.serviceIds, ['a', 'b'])
  assert.equal(state.bundlePrice.value, 14000)
  state.form.value.durationMinutes = ''
  assert.equal(state.bundleDuration.value, 240)
  state.serviceSearch.value = 'Ничего нет'
  assert.equal(state.filteredServices.value.length, 0)
  assert.equal(state.form.value.serviceIds.length, 2)
})

test('Посты и материалы используют свои заголовки, сохраняют общий контракт и ограничения использованных элементов', () => {
  for (const [kind, title, label] of [['work_post', 'Новый пост / бокс', 'Добавить пост / бокс'], ['material', 'Новый материал', 'Добавить материал']]) {
    const { state, events } = setup(kind)
    state.mode.value = 'create'
    state.form.value.name = 'Новая позиция'
    assert.equal(state.title.value, title)
    assert.equal(state.saveLabel.value, label)
    state.submit()
    assert.deepEqual(events.at(-1), ['addCatalogItem', kind])
    assert.equal(state.canEdit({ kindCode: kind, isUsed: true }), false)
  }
})

test('Поиск справочника учитывает описание и не изменяет исходный список', () => {
  const { state, services } = setup('service')
  services[0].description = 'Подготовка кузова'
  state.catalogSearch.value = 'ПОДГОТОВКА'
  assert.deepEqual(state.filteredCatalogItems.value.map(item => item.id), ['a'])
  assert.equal(services.length, 3)
  state.catalogSearch.value = 'Ничего не найдено'
  assert.equal(state.filteredCatalogItems.value.length, 0)
})

test('Нулевая цена сохраняется; неизвестная цена, время или услуга не подменяются нулём', () => {
  const { state, bundle, services } = setup()
  state.openItem(bundle, true)
  services[0].unitPrice = 0
  assert.equal(state.bundlePrice.value, 12000)
  services[0].unitPrice = null
  services[0].durationMinutes = null
  state.form.value.durationMinutes = ''
  assert.equal(state.bundlePrice.value, null)
  assert.equal(state.bundleDuration.value, null)
  state.form.value.serviceIds.push('missing')
  assert.equal(state.hasUnavailableServices.value, true)
  assert.equal(state.form.value.serviceIds.includes('missing'), true)
})

test('Выбранная архивная услуга доступна для удаления, новые наборы её не предлагают', () => {
  const { state, bundle } = setup()
  state.mode.value = 'create'
  assert.equal(state.availableServices.value.some(item => item.id === 'archived'), false)
  bundle.serviceIds.push('archived')
  state.openItem(bundle, true)
  assert.equal(state.availableServices.value.some(item => item.id === 'archived'), true)
  assert.equal(state.hasUnavailableServices.value, true)
  state.form.value.serviceIds = ['a', 'b']
  assert.equal(state.availableServices.value.some(item => item.id === 'archived'), false)
})

test('Изменение черновика не меняет каталог; отмена восстанавливает исходные значения', () => {
  const { state, bundle } = setup()
  state.openItem(bundle, true)
  state.form.value.name = 'Другое имя'
  state.form.value.serviceIds.pop()
  assert.equal(bundle.name, 'Комплекс')
  assert.deepEqual(bundle.serviceIds, ['a', 'b'])
  state.closeDialog()
  state.openItem(bundle, true)
  assert.equal(state.form.value.name, 'Комплекс')
  assert.deepEqual(state.form.value.serviceIds, ['a', 'b'])
})

test('Сохранение использует существующее событие, режим чтения не разрешает изменения', () => {
  const { state, bundle, events } = setup()
  state.openItem(bundle, true)
  state.submit()
  assert.equal(events.at(-1)[0], 'saveCatalogItem')
  assert.deepEqual(events.at(-1)[1], bundle)
  const readonly = setup('service', false)
  readonly.state.openItem(readonly.services[0], true)
  readonly.state.submit()
  assert.equal(readonly.state.mode.value, null)
  assert.equal(readonly.events.length, 0)
})

test('Пустой или слишком большой набор не отправляется; исправление состава позволяет сохранить черновик', () => {
  const { state, events } = setup()
  state.mode.value = 'create'
  state.form.value.name = 'Новый набор'
  state.submit()
  assert.match(state.bundleValidationError.value, /от 1 до 100/)
  assert.equal(events.some(event => event[0] === 'addCatalogItem'), false)
  assert.equal(state.pendingMutation.value, false)
  state.form.value.serviceIds = ['a']
  assert.equal(state.bundleValidationError.value, '')
  state.submit()
  assert.deepEqual(events.at(-1), ['addCatalogItem', 'service_bundle'])
  assert.equal(state.form.value.name, 'Новый набор')
  state.form.value.serviceIds = Array.from({ length: 101 }, (_, index) => String(index))
  const count = events.length
  state.submit()
  assert.match(state.bundleValidationError.value, /от 1 до 100/)
  assert.equal(events.length, count)
})

test('Удаление всех услуг при редактировании не сохраняет пустой набор', () => {
  const { state, bundle, events } = setup()
  state.openItem(bundle, true)
  state.form.value.serviceIds = []
  state.submit()
  assert.match(state.bundleValidationError.value, /от 1 до 100/)
  assert.equal(events.some(event => event[0] === 'saveCatalogItem'), false)
  assert.deepEqual(bundle.serviceIds, ['a', 'b'])
  state.closeDialog()
  assert.equal(state.bundleValidationError.value, '')
})

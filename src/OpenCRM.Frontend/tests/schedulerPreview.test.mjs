import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import { renderToString } from '@vue/server-renderer'
import ts from 'typescript'
import * as vue from 'vue'
import * as localization from '../packages/ui/src/i18n.ts'
import '../packages/workspace/src/schedulerMessages.ts'
import '../packages/workspace/src/schedulerControlMessages.ts'
import '../packages/workspace/src/schedulerCalendarMessages.ts'

const rendererModule = { exports: {} }
const rendererCode = ts.transpileModule(readFileSync(new URL('../packages/workspace/src/utils/schedulerCalendarEvent.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
new Function('require', 'exports', rendererCode)(() => ({ getVehicleBrandLogo: () => null }), rendererModule.exports)
const { renderSchedulerCalendarEvent } = rendererModule.exports

function compile(path, dependencies, inlineTemplate = false) {
  const source = readFileSync(new URL(path, import.meta.url), 'utf8')
  const { descriptor } = parse(source)
  const script = compileScript(descriptor, { id: 'scheduler-preview-test', inlineTemplate })
  const code = ts.transpileModule(script.content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => {
    if (id in dependencies) return dependencies[id]
    throw new Error('Не задана зависимость: ' + id)
  }, module, module.exports)
  return module.exports.default
}

const record = {
  id: 'record-1', vehicleId: 'vehicle-1', vehicleDisplayName: 'Mercedes-Benz E-Класс',
  registrationPlate: 'А001АА 154', date: '2026-10-21', plannedStartTime: '09:00',
  plannedEndDate: '2026-10-22', plannedEndTime: '12:00', status: 2,
  serviceNames: ['Полировка кузова и нанесение защитного керамического покрытия'],
  employeeNames: ['Алексей Смирнов'], location: 'Пост 1'
}

const preview = compile('../packages/workspace/src/components/SchedulerRecordPreview.vue', {
  vue,
  '@opencrm/ui/i18n': localization,
  '@opencrm/ui': { UiButton: (props, { slots }) => vue.h('a', props, slots.default?.()) },
  '../schedulerMessages': {},
  '../schedulerControlMessages': {},
  '../schedulerCalendarMessages': {},
  './SchedulerBookingDialog.vue': { default: (props, { slots }) => props.open ? vue.h('div', slots.default?.()) : null },
  '../utils/vehicleBrandLogo': { getVehicleBrandLogo: () => null }
}, true)

test('Просмотр показывает полные сведения и ссылку на обслуживание', async () => {
  const html = await renderToString(vue.createSSRApp(preview, { item: record }))
  for (const text of [...record.serviceNames, ...record.employeeNames, record.location, record.registrationPlate, 'В работе', '22.10.2026', '#maintenance/record-1']) {
    assert.ok(html.includes(text), text)
  }
})

test('Просмотр сохраняет окончание многодневной записи без времени и запасной заголовок', async () => {
  const html = await renderToString(vue.createSSRApp(preview, {
    item: { ...record, vehicleDisplayName: '', plannedStartTime: null, plannedEndTime: null }
  }))
  assert.ok(html.includes('<h3>Запись</h3>'))
  assert.ok(html.includes('Без времени'))
  assert.ok(html.includes('22.10.2026'))
})

test('Стабильный код определяет статус, пользовательскую подпись неизвестного статуса оставляет без перевода', async () => {
  localization.i18n.global.locale.value = 'en'
  localization.formattingCulture.value = 'en-US'
  const knownStatus = await renderToString(vue.createSSRApp(preview, { item: { ...record, status: 2, statusName: 'Пользовательская подпись' } }))
  assert.ok(knownStatus.includes('In progress'))
  assert.ok(!knownStatus.includes('Пользовательская подпись'))

  const customStatus = await renderToString(vue.createSSRApp(preview, { item: { ...record, status: 99, statusName: 'Custom status label' } }))
  assert.ok(customStatus.includes('Custom status label'))
  assert.ok(customStatus.includes('10/22/26'))
  localization.i18n.global.locale.value = 'ru'
  localization.formattingCulture.value = 'ru-RU'
})

test('Заголовок списка дня локализует дату через formattingCulture', async () => {
  localization.i18n.global.locale.value = 'en'
  localization.formattingCulture.value = 'en-US'
  const component = compile('../packages/workspace/src/components/SchedulerDayRecords.vue', {
    vue,
    '@opencrm/ui/i18n': localization,
    '@opencrm/ui': { UiButton: (props, { slots }) => vue.h('button', props, slots.default?.()) },
    '../schedulerCalendarMessages': {},
    './SchedulerBookingDialog.vue': { default: (props, { slots }) => props.open ? vue.h('div', { title: props.title }, slots.default?.()) : null },
    './SchedulerList.vue': { default: (props) => vue.h('div', props.items.length) }
  }, true)
  const html = await renderToString(vue.createSSRApp(component, {
    open: true, date: '2026-10-22', items: [], organizationId: 'org-1', canCreate: false
  }))
  assert.ok(html.includes('Appointments for October 22, 2026'))
  localization.i18n.global.locale.value = 'ru'
  localization.formattingCulture.value = 'ru-RU'
})

test('Все статические ключи календарных компонентов доступны на трёх языках', () => {
  const componentFiles = [
    'SchedulerDayCalendar.vue', 'SchedulerSlots.vue', 'SchedulerRecordPreview.vue', 'SchedulerDayRecords.vue'
  ]
  const source = componentFiles.map(name => readFileSync(new URL(`../packages/workspace/src/components/${name}`, import.meta.url), 'utf8')).join('\n')
  const keys = [...new Set([...source.matchAll(/\bt\(['"]([^'"]+)['"]\)/g)].map(match => match[1]))]

  for (const locale of ['ru', 'en', 'zh-Hans']) {
    localization.i18n.global.locale.value = locale
    for (const key of keys) assert.notEqual(localization.translate(key), key, `${key} отсутствует для ${locale}`)
  }

  localization.i18n.global.locale.value = 'ru'
})

class Element {
  children = []
  className = ''
  textContent = ''
  classList = { add: value => { this.className += ' ' + value } }
  appendChild(child) { this.children.push(child) }
  get text() { return this.textContent + this.children.map(child => child.text).join(' ') }
}

test('В узкой сетке остаются время и автомобиль, список недели сохраняет подробности', () => {
  globalThis.document = { createElement: () => new Element() }
  try {
    const compact = renderSchedulerCalendarEvent(record, 'timeGridWeek').domNodes[0]
    assert.ok(compact.text.includes('09:00'))
    assert.ok(compact.text.includes('22.10 12:00'))
    assert.ok(compact.text.includes(record.vehicleDisplayName))
    assert.ok(!compact.text.includes(record.serviceNames[0]))
    assert.ok(!compact.text.includes(record.registrationPlate))
    assert.ok(compact.title.includes(record.serviceNames[0]))
    const list = renderSchedulerCalendarEvent(record, 'listWeek').domNodes[0]
    assert.ok(list.text.includes(record.serviceNames[0]))
    assert.ok(list.text.includes(record.registrationPlate))
  } finally {
    delete globalThis.document
  }
})

for (const scenario of ['detailing', 'autoservice', 'fleet']) {
  test(`${scenario}: клик открывает просмотр, модификаторы сохраняют ссылку, «ещё» открывает один диалог`, () => {
    let mount
    let options
    const component = compile(`../Scenarios/${scenario}-client/src/components/MaintenanceCalendar.vue`, {
      vue: { ...vue, onMounted: callback => { mount = callback }, onBeforeUnmount() {}, watch() {} },
      '@opencrm/workspace': { scheduleBusinessHours: () => false },
      '@opencrm/workspace/calendarLocale': {
        fullCalendarLocaleCode: locale => locale,
        getFullCalendarLocales: () => []
      },
      '@opencrm/workspace/schedulerMessages': {},
      '@opencrm/ui/i18n': { formatLocaleDate: () => '' },
      '@opencrm/ui': {
        formatDateOnly: () => '2026-10-21',
        formatLocalDateTime: () => '2026-10-21T12:00:00',
        useLocalization: () => ({ t: key => key, locale: vue.ref('ru') })
      },
      [`@/api/${scenario}Bff`]: {}
    })
    globalThis.window = {
      localStorage: { getItem: () => 'listWeek' },
      FullCalendar: { Calendar: class {
        constructor(element, settings) { options = settings }
        render() {}
      } }
    }
    try {
      const state = component.setup({ organizationId: 'org-1', storageKey: 'test', initialDate: '2026-10-21' }, { expose() {}, emit() {} })
      state.calendarElement.value = { querySelectorAll: () => [] }
      mount()
      assert.equal(options.initialView, 'timeGridWeek')
      assert.equal(options.headerToolbar.right, 'dayGridMonth,timeGridWeek,timeGridDay')
      const click = { preventDefault() { this.prevented = true } }
      options.eventClick({ event: { extendedProps: record }, jsEvent: click })
      assert.equal(state.previewItem.value.id, record.id)
      assert.equal(click.prevented, true)
      for (const modifier of ['ctrlKey', 'metaKey', 'shiftKey', 'altKey']) {
        state.previewItem.value = null
        const modified = { [modifier]: true, preventDefault() { this.prevented = true } }
        options.eventClick({ event: { extendedProps: record }, jsEvent: modified })
        assert.equal(state.previewItem.value, null)
        assert.equal(modified.prevented, undefined)
      }
      assert.equal(options.moreLinkClick({ date: new Date(2026, 9, 21), jsEvent: { target: { closest: () => null } } }), false)
      assert.equal(state.isDayModalOpen.value, true)
      state.isDayModalOpen.value = false
      options.dateClick({ date: new Date(2026, 9, 22), view: { type: 'dayGridMonth' } })
      assert.equal(state.selectedDate.value, '2026-10-22')
      assert.equal(state.isDayModalOpen.value, true)
      let keydown
      const cell = { setAttribute() {}, addEventListener(type, callback) { if (type === 'keydown') keydown = callback } }
      options.dayCellDidMount({ date: new Date(2026, 9, 23), el: cell })
      for (const key of ['Enter', ' ']) {
        state.isDayModalOpen.value = false
        keydown({ target: cell, key, preventDefault() {} })
        assert.equal(state.selectedDate.value, '2026-10-23')
        assert.equal(state.isDayModalOpen.value, true)
      }
    } finally {
      delete globalThis.window
    }
  })
}

import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import { renderToString } from '@vue/server-renderer'
import ts from 'typescript'
import * as vue from 'vue'
import * as workingHours from '../packages/workspace/src/workingHours.ts'
import { useSchedulerViewState } from '../packages/workspace/src/schedulerViewState.ts'

function compile(path, dependencies, inlineTemplate = false) {
  const { descriptor } = parse(readFileSync(new URL(path, import.meta.url), 'utf8'))
  const script = compileScript(descriptor, { id: 'scheduler-schedule-test', inlineTemplate })
  const template = compileTemplate({ source: descriptor.template?.content ?? '', id: 'scheduler-schedule-test', filename: path })
  assert.deepEqual(template.errors, [])
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

const today = '2026-10-21'
const emptyWeek = Array.from({ length: 7 }, (_, dayOfWeek) => ({
  dayOfWeek, isWorking: false, startTime: null, endTime: null, breaks: []
}))
const workingDay = { dayOfWeek: 4, isWorking: true, startTime: '09:00', endTime: '18:00', breaks: [] }
const schedulerMessages = {}
const schedulerTranslation = key => ({
  'scheduler.noPost': 'Без поста', 'scheduler.noZone': 'Без зоны',
  'schedulerControl.setupSchedule': 'Настройте график работы', 'schedulerControl.scheduleMissing': 'График работы не настроен',
  'schedulerControl.todayClosed': 'Сегодня выходной', 'schedulerControl.selectedDayClosed': 'В этот день выходной',
  'schedulerControl.scheduleNotConfigured': 'График работы не настроен', 'schedulerControl.closedDay': 'Выходной день',
  'schedulerControl.organizationHoursRequired': 'Укажите рабочие дни и часы организации, чтобы планировать записи по графику.',
  'schedulerControl.askAdministrator': 'Попросите администратора организации указать рабочие дни и часы.',
  'schedulerControl.closedOnDate': '{date} — выходной по графику организации.', 'schedulerControl.nearestWorkDay': 'К ближайшему рабочему дню',
  'schedulerControl.nextWorkDayMissing': 'Ближайший рабочий день в графике не указан.', 'schedulerControl.configureSchedule': 'Настроить график'
})[key] ?? key
const closedDay = compile('../packages/workspace/src/components/SchedulerClosedDay.vue', {
  vue,
  '@opencrm/ui': { formatDateOnly: () => today, useLocalization: () => ({ t: schedulerTranslation }), UiButton: (props, { slots }) => vue.h('button', props, slots.default?.()) },
  '@opencrm/ui/i18n': { formattingCulture: vue.ref('ru-RU') },
  '../schedulerControlMessages': schedulerMessages,
  '../workingHours': workingHours
}, true)

for (const schedule of [[], emptyWeek]) {
  test('Пустой график предлагает настройку вместо выходного: ' + schedule.length + ' дней', async () => {
    const html = await renderToString(vue.createSSRApp(closedDay, { date: today, schedule, canManage: true }))
    assert.ok(html.includes('Настройте график работы'))
    assert.ok(html.includes('#settings?tab=workflow'))
    assert.ok(!html.includes('Сегодня выходной'))
    assert.ok(!html.includes('Ближайший рабочий день'))
  })
}

test('Сотрудник без права настройки видит обращение к администратору без ссылки', async () => {
  const html = await renderToString(vue.createSSRApp(closedDay, { date: today, schedule: emptyWeek, canManage: false }))
  assert.ok(html.includes('График работы не настроен'))
  assert.ok(html.includes('Попросите администратора'))
  assert.ok(!html.includes('#settings'))
})

test('Выходной заданного графика сохраняет дату и переход к рабочему дню', async () => {
  const html = await renderToString(vue.createSSRApp(closedDay, { date: today, schedule: [workingDay], canManage: true }))
  assert.ok(html.includes('Сегодня выходной'))
  assert.ok(html.includes('выходной по графику организации'))
  assert.ok(html.includes('К ближайшему рабочему дню'))
  assert.ok(html.includes('09:00–18:00'))
  assert.ok(!html.includes('Настройте график работы'))
})

test('Выбранный будущий выходной не называется сегодняшним', async () => {
  const html = await renderToString(vue.createSSRApp(closedDay, { date: '2026-10-23', schedule: [workingDay], canManage: false }))
  assert.ok(html.includes('В этот день выходной'))
  assert.ok(!html.includes('Сегодня выходной'))
})

for (const scenario of ['detailing', 'autoservice', 'fleet']) {
  test(scenario + ': отсутствие графика отличается от выходного и ошибки загрузки', () => {
    const component = compile('../Scenarios/' + scenario + '-client/src/components/SchedulerSection.vue', {
      vue: { ...vue, onMounted() {}, watch() {} },
      '@opencrm/workspace': { ...workingHours, useSchedulerViewState },
      '@opencrm/workspace/api/setup': {},
      '@opencrm/ui': { formatDateOnly: () => today, useLocalization: () => ({ t: schedulerTranslation }) },
      '@opencrm/workspace/schedulerMessages': schedulerMessages,
      ['@/api/' + scenario + 'Bff']: {},
      './MaintenanceCalendar.vue': {},
      './MaintenanceMoveDialog.vue': {},
      './QuickBookingSection.vue': {}
    })
    const state = component.setup({ context: { user: { id: 'user-1' }, organization: { id: 'org-1' } } }, { expose() {}, emit() {} })
    assert.equal(state.isScheduleMissing.value, false)
    assert.equal(state.isClosedDay.value, false)
    for (const weeklySchedule of [[], emptyWeek]) {
      state.schedule.value = { weeklySchedule }
      assert.equal(state.isScheduleMissing.value, true)
      assert.equal(state.isClosedDay.value, false)
    }
    state.schedule.value = { weeklySchedule: [...emptyWeek.filter(day => day.dayOfWeek !== 4), workingDay] }
    assert.equal(state.isScheduleMissing.value, false)
    assert.equal(state.isClosedDay.value, true)
    state.selectedDate.value = '2026-10-22'
    assert.equal(state.isClosedDay.value, false)
    state.schedule.value = null
    assert.equal(state.isScheduleMissing.value, false)
  })
}

for (const scenario of ['detailing', 'autoservice', 'fleet']) {
  test(scenario + ': записи планировщика показывают локализованные статусы и сохраняют валюту снимка', async () => {
    const loadedItems = [
      { id: 'ready', date: today, status: 3, statusName: 'Готово к выдаче', readyForHandoverAt: today, currencyCode: 'CNY' },
      { id: 'handed-over', date: today, status: 3, statusName: 'Выдано', handedOverAt: today, currencyCode: 'USD' }
    ]
    const api = { loadMaintenanceCalendar: async () => loadedItems }
    const component = compile('../Scenarios/' + scenario + '-client/src/components/SchedulerSection.vue', {
      vue: { ...vue, onMounted() {}, watch() {} },
      '@opencrm/workspace': { ...workingHours, useSchedulerViewState },
      '@opencrm/workspace/api/setup': {},
      '@opencrm/ui': { formatDateOnly: () => today, useLocalization: () => ({ t: schedulerTranslation }) },
      '@opencrm/workspace/schedulerMessages': schedulerMessages,
      ['@/api/' + scenario + 'Bff']: api,
      './MaintenanceCalendar.vue': {},
      './MaintenanceMoveDialog.vue': {},
      './QuickBookingSection.vue': {}
    })
    const context = { user: { id: 'user-1' }, organization: { id: 'org-1', timeZoneId: 'UTC' }, links: { api: { calendar: '/calendar' } } }
    const state = component.setup({ context }, { expose() {}, emit() {} })
    await state.loadItems()
    assert.deepEqual(state.items.value.map(item => [item.statusName, item.currencyCode]), [
      ['scheduler.ready', 'CNY'], ['scheduler.handedOver', 'USD']
    ])
  })
}

for (const scenario of ['detailing', 'autoservice']) {
  test(scenario + ': ошибка каталога сбрасывает сохранённый пост и не скрывает расписание', async () => {
    const component = compile('../Scenarios/' + scenario + '-client/src/components/SchedulerSection.vue', {
      vue: { ...vue, onMounted() {}, watch() {} },
      '@opencrm/workspace': { ...workingHours, useSchedulerViewState },
      '@opencrm/workspace/api/setup': {},
      '@opencrm/ui': { formatDateOnly: () => today, useLocalization: () => ({ t: schedulerTranslation }) },
      '@opencrm/workspace/schedulerMessages': schedulerMessages,
      ['@/api/' + scenario + 'Bff']: { loadScenarioCatalog: async () => { throw new Error('Каталог недоступен.') } },
      './MaintenanceCalendar.vue': {},
      './MaintenanceMoveDialog.vue': {},
      './QuickBookingSection.vue': {}
    })
    const state = component.setup({ context: { user: { id: 'user-1' }, organization: { id: 'org-1' } } }, { expose() {}, emit() {} })
    state.postFilter.value = 'Пост 3'
    await state.loadWorkPosts()
    assert.equal(state.postFilter.value, '')
    assert.equal(state.arePostsLoaded.value, true)
    assert.deepEqual(state.postColumns.value, ['Без поста'])
  })
}

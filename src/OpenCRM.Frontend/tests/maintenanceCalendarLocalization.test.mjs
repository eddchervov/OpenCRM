import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const adapterPath = new URL('../packages/workspace/src/calendarLocale.ts', import.meta.url)
const adapterSource = readFileSync(adapterPath, 'utf8')
const adapterJs = ts.transpileModule(adapterSource, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const adapterModule = { exports: {} }
new Function('exports', 'module', adapterJs)(adapterModule.exports, adapterModule)

test('FullCalendar uses UI locales with localized controls and Monday as the first day', () => {
  const { createFullCalendarLocale, getFullCalendarLocales } = adapterModule.exports
  const locales = getFullCalendarLocales()

  assert.deepEqual(locales.map(locale => locale.code), ['ru', 'en', 'zh-cn'])
  assert.equal(createFullCalendarLocale('ru').buttonText.today, 'Сегодня')
  assert.equal(createFullCalendarLocale('en').buttonText.today, 'Today')
  assert.equal(createFullCalendarLocale('zh-Hans').buttonText.today, '今天')
  assert.ok(locales.every(locale => locale.week.dow === 1 && locale.week.doy === 4))

  const supportedFields = [
    'buttonText', 'buttonHints', 'weekText', 'weekTextLong', 'closeHint', 'timeHint',
    'eventHint', 'allDayText', 'moreLinkText', 'noEventsText', 'viewHint', 'navLinkHint', 'moreLinkHint'
  ]
  for (const locale of locales) {
    for (const field of supportedFields) assert.ok(locale[field], `${locale.code} defines ${field}`)
  }

  assert.equal(createFullCalendarLocale('ru').moreLinkHint(2), 'Показать еще 2 события')
  assert.equal(createFullCalendarLocale('en').moreLinkHint(2), 'Show 2 more events')
  assert.equal(createFullCalendarLocale('zh-Hans').moreLinkHint(2), '显示另外 2 个预约')
  assert.equal(createFullCalendarLocale('zh-Hans').buttonHints.prev, '上一个$0')
  assert.equal(createFullCalendarLocale('zh-Hans').noEventsText, '没有可显示的事件')
})

test('calendar and move-dialog messages exist in Russian, English, and Simplified Chinese', () => {
  const source = readFileSync(new URL('../packages/workspace/src/schedulerMessages.ts', import.meta.url), 'utf8')
  const compiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const registered = {}
  const mockRequire = id => id === '@opencrm/ui/i18n'
    ? { registerMessages: (locale, messages) => { registered[locale] = messages } }
    : (() => { throw new Error(`Unexpected dependency: ${id}`) })()
  const registerModule = { exports: {} }
  new Function('exports', 'require', 'module', compiled)(registerModule.exports, mockRequire, registerModule)

  for (const locale of ['ru', 'en', 'zh-Hans']) {
    const messages = registered[locale]?.maintenanceCalendar
    assert.ok(messages)
    assert.ok(messages.moveTitle)
    assert.ok(messages.invalidPeriod)
    assert.ok(messages.limitWarning.includes('{count}'))
    assert.ok(messages.logoAlt.includes('{name}'))
  }
})

test('locale fields used by the adapter exist in the bundled FullCalendar version', () => {
  const source = readFileSync(new URL('../../OpenCRM.WebApp/wwwroot/lib/fullcalendar/index.global.min.js', import.meta.url), 'utf8')

  for (const field of [
    'buttonText', 'buttonHints', 'weekText', 'weekTextLong', 'closeHint', 'timeHint', 'eventHint',
    'allDayText', 'moreLinkText', 'noEventsText', 'viewHint', 'navLinkHint', 'moreLinkHint'
  ]) {
    assert.ok(source.includes(field), `bundled FullCalendar supports ${field}`)
  }
})

test('scenario calendars follow UI locale and keep calendar data in place', () => {
  const paths = [
    '../Scenarios/autoservice-client/src/components/MaintenanceCalendar.vue',
    '../Scenarios/fleet-client/src/components/MaintenanceCalendar.vue',
    '../Scenarios/detailing-client/src/components/MaintenanceCalendar.vue'
  ]

  for (const path of paths) {
    const source = readFileSync(new URL(path, import.meta.url), 'utf8')
    assert.match(source, /fullCalendarLocaleCode\(locale\.value\)/)
    assert.match(source, /locales: getFullCalendarLocales\(\)/)
    assert.match(source, /watch\(locale, value =>/)
    assert.match(source, /dayMaxEvents: 2/)
    assert.match(source, /moreLinkContent: \(arg: \{ num: number \}\) => t\('maintenanceCalendar\.moreLink', \{ count: arg\.num \}\)/)
    assert.match(source, /moreLinkDidMount:[\s\S]*?setAttribute\('role', 'button'\)/)
    assert.match(source, /formatLocaleDate\(arg\.date, \{ dateStyle: 'full' \}\)/)
    assert.doesNotMatch(source, /locale:\s*'ru'/)
    assert.doesNotMatch(source, /ru-RU/)
  }
})

test('detailing move dialog localizes controls without changing the scheduling action', () => {
  const source = readFileSync(new URL('../Scenarios/detailing-client/src/components/MaintenanceMoveDialog.vue', import.meta.url), 'utf8')

  for (const key of ['moveTitle', 'startDate', 'endDate', 'postLabel', 'invalidPeriod', 'moveError', 'confirmMove']) {
    assert.ok(source.includes(`maintenanceCalendar.${key}`))
  }

  assert.match(source, /action: 'reschedule'/)
  assert.match(source, /plannedDate: date\.value, plannedEndDate: endDate\.value/)
  assert.match(source, /if \(saving\.value \|\| !props\.canWrite \|\| !props\.move\.item\.canReschedule\) return/)
})

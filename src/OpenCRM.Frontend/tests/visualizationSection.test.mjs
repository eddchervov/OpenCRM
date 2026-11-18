import test from 'node:test'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import * as localization from '@opencrm/ui/i18n'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { getWorkdayProgress, shouldOpenRecord } from '../packages/workspace/src/utils/workdayCard.ts'
import { buildVisualWorkPosts, isBookingOverdue } from '../packages/workspace/src/components/visualization/workPosts.ts'

const filename = new URL('../packages/workspace/src/components/WorkspaceVisualizationSection.vue', import.meta.url)
const source = readFileSync(filename, 'utf8')
const { descriptor } = parse(source, { filename: filename.pathname })
const script = compileScript(descriptor, { id: 'visualization-section-test' })
const code = ts.transpileModule(script.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const timers = { intervals: [], timeouts: [] }
globalThis.window = {
  setInterval(callback) { timers.intervals.push(callback); return timers.intervals.length },
  clearInterval() {},
  setTimeout(callback) { timers.timeouts.push(callback); return timers.timeouts.length },
  clearTimeout() {}
}

function group(items = []) {
  return { totalCount: items.length, items }
}

function workday(values = {}) {
  return {
    hasAnyWork: true,
    planned: group(),
    inProgress: group(),
    ready: group(),
    handedOver: group(),
    ...values
  }
}

function item(id, values = {}) {
  return {
    id,
    vehicleName: `Автомобиль ${id}`,
    services: ['Уход'],
    workTotalCount: 3,
    workCompletedCount: 1,
    workPausedCount: 0,
    date: new Date().toISOString().slice(0, 10),
    location: null,
    ...values
  }
}

function deferred() {
  let resolve
  const promise = new Promise(complete => { resolve = complete })
  return { promise, resolve }
}

const messageSource = readFileSync(new URL('../packages/workspace/src/visualizationMessages.ts', import.meta.url), 'utf8')
const messageCode = ts.transpileModule(messageSource, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText
new Function('require', 'exports', messageCode)(() => localization, {})

function visualization({ loadDay, loadCatalog } = {}) {
  const watchers = []
  const lifecycle = []
  const calls = []
  const module = { exports: {} }
  const resolve = id => {
    if (regionalTestDependencies[id]) return regionalTestDependencies[id]
    if (id === '../visualizationMessages') return {}
    if (id === 'vue') return {
      ...vue,
      defineAsyncComponent: loader => loader,
      watch(source, callback, options) {
        watchers.push({ source, callback, options })
        if (options?.immediate) callback()
      },
      onBeforeUnmount(callback) { lifecycle.push(callback) }
    }
    if (id === '@opencrm/ui') return {
      UiButton: {}, UiCard: {}, UiLoader: {},
      formatDateOnly: date => date.toISOString().slice(0, 10)
    }
    if (id === '../api/scenarioCatalog') return {
      loadScenarioCatalog: async organizationId => {
        calls.push(['catalog', organizationId])
        return loadCatalog ? loadCatalog(organizationId) : { workPosts: [{ id: 'post-1', name: 'Пост 1', isActive: true, sortOrder: 1 }] }
      }
    }
    if (id === '../api/visualization') return {
      loadWorkspaceVisualization: async (organizationId, date, signal) => {
        calls.push(['day', organizationId, date, signal])
        return loadDay ? loadDay(organizationId, date, signal) : workday()
      }
    }
    if (id === '../utils/vehicleBrandLogo') return { getVehicleBrandLogo: () => null }
    if (id === '../utils/workdayCard') return { getWorkdayProgress, shouldOpenRecord }
    if (id === './visualization/workPosts') return { buildVisualWorkPosts, isBookingOverdue }
    if (id === './WorkspaceWorkdayPreview.vue') return { default: {} }
    throw new Error('Не задана зависимость: ' + id)
  }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const props = vue.reactive({
    context: {
      organization: { id: 'org-1' },
      shell: { menu: [{ code: 'maintenance', isVisible: true }] }
    }
  })
  const state = module.exports.default.setup(props, { expose() {}, emit() {} })
  return { state, props, watchers, lifecycle, calls, dispose: () => lifecycle.forEach(callback => callback()) }
}

function organizationWatcher(subject) {
  return subject.watchers.find(watcher => watcher.options?.immediate)
}

function clickEvent(values = {}) {
  return {
    button: 0,
    ctrlKey: false,
    metaKey: false,
    shiftKey: false,
    altKey: false,
    preventDefault() { this.prevented = true },
    ...values
  }
}

test('Паузы берутся только из работ в процессе, а подборки и пост выбирают свои записи', async () => {
  const paused = item('paused', { workPausedCount: 1, location: 'Пост 1' })
  const active = item('active', { location: 'Пост 1' })
  const overdue = item('overdue', { date: '2020-01-01', location: 'Пост 1' })
  const plannedWithPauseCount = item('future-paused', {
    date: '2999-01-01', startTime: '12:00', workPausedCount: 2, location: 'Пост 1'
  })
  const ready = item('ready', { location: null })
  const subject = visualization({ loadDay: async () => workday({
    planned: group([overdue, plannedWithPauseCount]),
    inProgress: group([paused, active, item('unassigned', { location: '' })]),
    ready: group([ready])
  }) })
  await new Promise(resolve => setImmediate(resolve))

  assert.deepEqual(subject.state.pausedEntries.value.map(entry => entry.item.id), ['paused'])
  assert.equal(isBookingOverdue(plannedWithPauseCount, subject.state.today.value, subject.state.time.value), false)

  subject.state.selectedTray.value = 'ready'
  assert.equal(subject.state.selectionTitle.value, 'Готовы к выдаче')
  assert.deepEqual(subject.state.selectedEntries.value.map(entry => entry.item.id), ['ready'])
  subject.state.selectedTray.value = 'waiting'
  assert.deepEqual(subject.state.selectedEntries.value.map(entry => entry.item.id), ['overdue'])
  subject.state.selectedTray.value = 'paused'
  assert.deepEqual(subject.state.selectedEntries.value.map(entry => entry.item.id), ['paused'])
  subject.state.selectedTray.value = 'unassigned'
  assert.deepEqual(subject.state.selectedEntries.value.map(entry => entry.item.id), ['unassigned'])
  subject.state.selectPost('post-1')
  assert.equal(subject.state.selectedTray.value, null)
  assert.deepEqual(subject.state.selectedEntries.value.map(entry => entry.item.id).sort(), ['active', 'future-paused', 'overdue', 'paused'])
  subject.dispose()
})

test('Выбор поста можно сбросить, обновление не выбирает первый пост автоматически', async () => {
  const subject = visualization()
  await new Promise(resolve => setImmediate(resolve))
  const postsWatcher = subject.watchers.find(watcher => !watcher.options?.immediate)

  postsWatcher.callback(postsWatcher.source())
  assert.equal(subject.state.selectedPostId.value, '')
  subject.state.selectPost('post-1')
  await subject.state.load()
  postsWatcher.callback(postsWatcher.source())
  assert.equal(subject.state.selectedPostId.value, 'post-1')

  subject.state.selectPost(null)
  assert.equal(subject.state.selectedPostId.value, '')
  assert.deepEqual(subject.state.selectedEntries.value, [])
  subject.state.selectedTray.value = 'ready'
  subject.state.selectPost('')
  assert.equal(subject.state.selectedTray.value, null)
  await subject.state.load()
  postsWatcher.callback(postsWatcher.source())
  assert.equal(subject.state.selectedPostId.value, '')

  subject.state.selectPost('post-1')
  postsWatcher.callback([])
  assert.equal(subject.state.selectedPostId.value, '')
  subject.dispose()
})

test('Прогресс ограничивает выполненное числом работ и скрывает шкалу без общего числа', () => {
  assert.deepEqual(getWorkdayProgress(3, 8), { completed: 3, total: 3, percent: 100, isComplete: true })
  assert.deepEqual(getWorkdayProgress(3, -2), { completed: 0, total: 3, percent: 0, isComplete: false })
  assert.deepEqual(getWorkdayProgress(0, 8), { completed: 0, total: 0, percent: 0, isComplete: false })
})

test('Открытие предпросмотра обрабатывает обычный клик, модификаторы и отсутствие доступа', async () => {
  const record = item('record-1')
  const subject = visualization({ loadDay: async () => workday({ inProgress: group([record]) }) })
  await new Promise(resolve => setImmediate(resolve))

  const ordinaryClick = clickEvent()
  subject.state.openPreview(ordinaryClick, record)
  assert.equal(subject.state.previewId.value, record.id)
  assert.equal(ordinaryClick.prevented, true)

  subject.state.previewId.value = ''
  const modifiedClick = clickEvent({ ctrlKey: true })
  subject.state.openPreview(modifiedClick, record)
  assert.equal(subject.state.previewId.value, '')
  assert.equal(modifiedClick.prevented, undefined)

  subject.props.context.shell.menu[0].isVisible = false
  const noAccessClick = clickEvent()
  subject.state.openPreview(noAccessClick, record)
  assert.equal(subject.state.previewId.value, '')
  assert.equal(noAccessClick.prevented, undefined)
  subject.dispose()
})

test('Смена организации сбрасывает выбранные данные и закрывает предпросмотр', async () => {
  const subject = visualization({ loadDay: async () => workday({ inProgress: group([item('record-1')]) }) })
  await new Promise(resolve => setImmediate(resolve))
  subject.state.previewId.value = 'record-1'
  subject.state.selectedPostId.value = 'post-1'
  subject.state.selectedTray.value = 'paused'
  subject.state.updatedAt.value = '10:15'
  subject.state.errorMessage.value = 'Старая ошибка'

  subject.props.context.organization.id = 'org-2'
  organizationWatcher(subject).callback()

  assert.equal(subject.state.data.value, null)
  assert.deepEqual(subject.state.catalog.value, [])
  assert.equal(subject.state.previewId.value, '')
  assert.equal(subject.state.selectedPostId.value, '')
  assert.equal(subject.state.selectedTray.value, null)
  assert.equal(subject.state.updatedAt.value, '')
  assert.equal(subject.state.errorMessage.value, '')
  await new Promise(resolve => setImmediate(resolve))
  subject.dispose()
})

test('Поздний ответ старой организации не заменяет актуальные данные', async () => {
  const oldResponse = deferred()
  const newResponse = deferred()
  const subject = visualization({
    loadDay: organizationId => organizationId === 'org-1' ? oldResponse.promise : newResponse.promise
  })
  subject.props.context.organization.id = 'org-2'
  organizationWatcher(subject).callback()
  newResponse.resolve(workday({ inProgress: group([item('new-org')]) }))
  await new Promise(resolve => setImmediate(resolve))
  oldResponse.resolve(workday({ inProgress: group([item('stale-org')]) }))
  await new Promise(resolve => setImmediate(resolve))

  assert.deepEqual(subject.state.data.value.inProgress.items.map(entry => entry.id), ['new-org'])
  subject.dispose()
})

test('SFC страницы визуализации и шаблон компилируются', () => {
  const template = compileTemplate({ source: descriptor.template.content, filename: filename.pathname, id: 'visualization-section-test' })
  assert.deepEqual(template.errors, [])
})

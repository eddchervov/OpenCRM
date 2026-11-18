import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import * as hours from '../packages/workspace/src/workingHours.ts'

const filename = new URL('../packages/workspace/src/components/OrganizationWorkingHoursSettings.vue', import.meta.url)
const { descriptor } = parse(readFileSync(filename, 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'hours-settings-test' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const setupData = {
  canManage: true, revision: 7, workdayStart: '08:00:00', workdayEnd: '20:00:00', workingDays: [1, 2, 3, 4, 5],
  weeklySchedule: [
    { dayOfWeek: 1, isWorking: true, startTime: '08:00:00', endTime: '20:00:00', breaks: [{ startTime: '12:00:00', endTime: '13:00:00' }] },
    { dayOfWeek: 0, isWorking: false, startTime: null, endTime: null, breaks: [] }
  ]
}

function deferred() {
  let resolve
  let reject
  const promise = new Promise((complete, fail) => { resolve = complete; reject = fail })
  return { promise, resolve, reject }
}

function settingsComponent(api = {}, overrides = {}) {
  const watchers = []
  const module = { exports: {} }
  const events = []
  const props = { organizationId: 'org-1', disabled: false, ...overrides }
  new Function('require', 'module', 'exports', code)(id => {
    if (id in regionalTestDependencies) return regionalTestDependencies[id]
    if (id === 'vue') return {
      ...vue, onBeforeUnmount() {}, onMounted() {}, watch: (source, callback) => watchers.push(callback)
    }
    if (id === '../workingHours') return hours
    if (id === '../api/setup') return {
      loadWorkspaceSetup: async () => setupData,
      saveWorkspaceSetup: async () => setupData,
      ...api
    }
    return {}
  }, module, module.exports)
  const state = module.exports.default.setup(props, { expose() {}, emit: (...args) => events.push(args) })
  const modal = { opened: 0, closed: 0 }
  state.dialog.value = { showModal() { modal.opened++ }, close() { modal.closed++ } }
  return { state, props, watchers, events, modal }
}

test('График открывается в модалке с независимым черновиком; отмена сбрасывает изменения', async () => {
  assert.deepEqual(compileTemplate({ source: descriptor.template.content, filename: filename.pathname, id: 'hours-settings-test' }).errors, [])
  const { state, modal } = settingsComponent()
  await state.edit()
  assert.equal(modal.opened, 1)
  assert.equal(state.editing.value, true)
  state.schedule.value[0].breaks[0].startTime = '12:30'
  assert.equal(setupData.weeklySchedule[0].breaks[0].startTime, '12:00:00')
  state.close()
  assert.equal(state.editing.value, false)
  assert.deepEqual(state.schedule.value, [])
  await state.edit()
  assert.equal(state.schedule.value[0].breaks[0].startTime, '12:00:00')
})

test('Сохранение передаёт текущую ревизию и перерывы; ошибка оставляет черновик', async () => {
  let fail = true
  let command
  const { state, events } = settingsComponent({ saveWorkspaceSetup: async (...args) => {
    command = args
    if (fail) throw new Error('График уже изменён')
    return setupData
  } })
  await state.edit()
  state.schedule.value[0].startTime = '09:15'
  await state.save()
  assert.equal(state.error.value, 'График уже изменён')
  assert.equal(state.editing.value, true)
  assert.equal(state.schedule.value[0].startTime, '09:15')
  assert.deepEqual(command, ['org-1', {
    step: 2, revision: 7, skip: false, items: [], weeklySchedule: [
      { ...setupData.weeklySchedule[0], startTime: '09:15:00' }, setupData.weeklySchedule[1]
    ]
  }])
  fail = false
  await state.save()
  assert.equal(state.editing.value, false)
  assert.deepEqual(events, [['saved']])
})

test('Повторная отправка и закрытие во время сохранения блокируются', async () => {
  const pending = deferred()
  let commands = 0
  const { state } = settingsComponent({ saveWorkspaceSetup: () => { commands++; return pending.promise } })
  await state.edit()
  const saving = state.save()
  state.close()
  await state.save()
  assert.equal(state.editing.value, true)
  assert.equal(commands, 1)
  pending.resolve(setupData)
  await saving
  assert.equal(state.editing.value, false)
})

test('Закрытие во время загрузки игнорирует запоздалый ответ; повторная загрузка доступна', async () => {
  const pending = deferred()
  const { state } = settingsComponent({ loadWorkspaceSetup: () => pending.promise })
  const opening = state.edit()
  await vue.nextTick()
  state.close()
  pending.resolve(setupData)
  await opening
  assert.equal(state.state.value, null)
  assert.equal(state.editing.value, false)
  await state.edit()
  assert.equal(state.state.value.revision, 7)
})

test('Смена организации закрывает форму и игнорирует результат старого сохранения', async () => {
  const pending = deferred()
  const { state, props, watchers, events } = settingsComponent({ saveWorkspaceSetup: () => pending.promise })
  await state.edit()
  const saving = state.save()
  props.organizationId = 'org-2'
  watchers[0]()
  pending.resolve(setupData)
  await saving
  assert.equal(state.editing.value, false)
  assert.equal(state.state.value, null)
  assert.deepEqual(events, [])
})

test('Права, занятость и некорректные перерывы запрещают сохранение', async () => {
  let commands = 0
  const { state, props } = settingsComponent({ saveWorkspaceSetup: async () => { commands++ } })
  props.disabled = true
  await state.edit()
  assert.equal(state.editing.value, false)
  props.disabled = false
  await state.edit()
  state.schedule.value[0].breaks[0].endTime = '22:00'
  await state.save()
  assert.match(state.error.value, /перерывы должны быть внутри рабочего дня/)
  state.state.value = { ...setupData, canManage: false }
  await state.save()
  assert.equal(commands, 0)
})

test('Ошибка загрузки и отсутствие прав остаются в окне; повтор не закрывает его', async () => {
  let attempt = 0
  const { state } = settingsComponent({ loadWorkspaceSetup: async () => {
    if (++attempt === 1) throw new Error('Нет соединения')
    if (attempt === 2) return { ...setupData, canManage: false }
    return setupData
  } })
  await state.edit()
  assert.equal(state.error.value, 'Нет соединения')
  assert.equal(state.editing.value, true)
  await state.load()
  assert.match(state.error.value, /Нет права/)
  assert.equal(state.state.value, null)
  await state.load()
  assert.equal(state.error.value, '')
  assert.equal(state.state.value.revision, 7)
})

test('Встроенный график работает без модального окна и сообщает о сохранении и отмене', async () => {
  const pending = deferred()
  const { state, events, modal } = settingsComponent({ saveWorkspaceSetup: () => pending.promise }, { embedded: true })
  await state.edit()
  assert.equal(modal.opened, 0)
  const saving = state.save()
  assert.deepEqual(events, [['busy-changed', true]])
  state.close()
  assert.equal(state.editing.value, true)
  pending.resolve(setupData)
  await saving
  assert.deepEqual(events, [['busy-changed', true], ['busy-changed', false], ['saved']])
  assert.equal(modal.closed, 0)
  await state.edit()
  state.close()
  assert.equal(events.at(-1)[0], 'closed')
})

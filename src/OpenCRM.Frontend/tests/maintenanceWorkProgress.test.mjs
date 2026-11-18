import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import { parse as parseTemplate } from '@vue/compiler-dom'
import ts from 'typescript'
import * as vue from 'vue'
import * as workDuration from '../packages/workspace/src/formatWorkDuration.ts'

const filename = new URL('../packages/workspace/src/components/MaintenanceWorkProgress.vue', import.meta.url)
const source = readFileSync(filename, 'utf8')
const { descriptor } = parse(source, { filename: filename.pathname })
const script = compileScript(descriptor, { id: 'maintenance-work-progress-test' })
const code = ts.transpileModule(script.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function progressComponent(api = {}) {
  const watchers = []
  const module = { exports: {} }
  const translations = {
    'maintenanceWork.historyTitle': 'История работ',
    'maintenanceWork.performersAndAdjustments': 'Исполнители и корректировки',
    'maintenanceWork.workControl': 'Контроль работ',
    'maintenanceWork.workProgress': 'Ход работ',
    'maintenanceWork.minutesRange': 'Введите целое число минут от −10 080 до 10 080.',
    'maintenanceWork.cannotReduce': 'Уменьшить время нельзя: учтено меньше минуты.',
    'maintenanceWork.canRemoveMinutes': 'Можно убрать не больше {minutes} мин.',
    'maintenanceWork.minutesShort': 'мин',
    'maintenanceWork.secondsShort': 'с',
    'maintenanceWork.hoursShort': 'ч',
    'maintenanceWork.zeroMinutes': '0 мин',
    'maintenanceWork.currentTimeUnknown': 'Не удалось определить текущее время. Закройте форму и обновите данные.',
    'maintenanceWork.timeChangeError': 'Не удалось исправить время работы.',
    'maintenanceWork.returnError': 'Не удалось вернуть работу.',
    'maintenanceWork.completionError': 'Не удалось завершить работу.',
    'maintenanceWork.completionReasonRequired': 'Укажите причину завершения (до 1000 символов).'
  }
  const translate = (key, params = {}) => (translations[key] ?? key).replace(/\{(\w+)\}/g, (_, name) => String(params[name] ?? ''))
  const resolve = id => {
    if (Object.hasOwn(regionalTestDependencies, id)) return regionalTestDependencies[id]
    if (id === 'vue') return {
      ...vue,
      onMounted() {},
      onUnmounted() {},
      watch: (source, callback) => watchers.push({ source, callback })
    }
    if (id === '@opencrm/ui') return { UiButton: {}, UiTooltip: {}, useLocalization: () => ({ t: translate }) }
    if (id === '../maintenanceWorkMessages') return {}
    if (id === '../api/work') return {
      loadWorkProgress: async () => [],
      loadWorkReports: async () => [],
      loadProductionReport: async () => ({ materials: [] }),
      adjustWorkTime: async () => {},
      completeWorkForEmployee: async () => {},
      decideWorkReport: async () => {},
      returnWorkForRework: async () => {},
      ...api
    }
    if (id === '../formatWorkDuration') return workDuration
    throw new Error('Не задана зависимость: ' + id)
  }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const props = vue.reactive({ organizationId: 'org-1', maintenanceId: 'order-1', canReturn: true, canComplete: true, maintenanceStatus: 2 })
  const events = []
  const state = module.exports.default.setup(props, { expose() {}, emit: (...event) => events.push(event) })
  return { state, props, watchers, events }
}

function deferred() {
  let resolve
  let reject
  const promise = new Promise((complete, fail) => { resolve = complete; reject = fail })
  return { promise, resolve, reject }
}

test('Free без отслеживания скрывает пустое управление исполнителями, PRO сохраняет его', () => {
  const { state, props } = progressComponent()
  state.works.value = [{ detailId: 'service', status: 1, version: 0, hasAssignedEmployee: false }]
  assert.equal(state.canOpenManagement.value, true)
  props.teamAvailable = false
  assert.equal(state.canOpenManagement.value, false)
  assert.equal(state.managementTitle.value, 'История работ')
})

test('Free сохраняет историю назначений, начатых операций и отчётов, включая завершение владельцем', () => {
  const { state, props } = progressComponent()
  props.teamAvailable = false
  for (const history of [
    { detailId: 'service', status: 1, version: 0, hasAssignedEmployee: true },
    { detailId: 'service', status: 2, version: 1, hasAssignedEmployee: true }
  ]) {
    state.works.value = [history]
    assert.equal(state.canOpenManagement.value, true)
    assert.equal(state.canCompleteWork(history), true)
  }
  state.works.value = []
  state.reports.value = [{ kind: 'extra', decision: null }]
  assert.equal(state.canOpenManagement.value, true)
})

function hasDescendant(node, predicate) {
  return node.children?.some(child => predicate(child) || hasDescendant(child, predicate)) ?? false
}

function hasClass(node, className) {
  return node.type === 1 && node.props.some(prop => prop.type === 6
    && prop.name === 'class' && prop.value?.content.split(/\s+/).includes(className))
}

test('Прогресс отдаёт фактические статусы и сбрасывается при ошибке обновления', async () => {
  const works = [{ detailId: 'line-1', name: 'Полировка', status: 4, version: 3, activeTime: '00:30:00' }]
  let attempts = 0
  const { state, events } = progressComponent({
    loadWorkProgress: async () => {
      if (++attempts > 1) throw new Error('Временная ошибка')
      return works
    }
  })
  await state.load()
  await state.load()
  assert.deepEqual(events, [['progress', works], ['progress', null]])
})

test('Смена заказа немедленно сбрасывает прогресс и игнорирует старый ответ', async () => {
  const pending = deferred()
  const { state, props, watchers, events } = progressComponent({
    loadWorkProgress: (_organizationId, maintenanceId) => maintenanceId === 'order-1'
      ? pending.promise
      : Promise.resolve([{ detailId: 'line-2', status: 2 }])
  })
  const firstLoad = state.load()
  props.maintenanceId = 'order-2'
  watchers[0].callback()
  await new Promise(resolve => setImmediate(resolve))
  pending.resolve([{ detailId: 'line-1', status: 4 }])
  await firstLoad
  assert.deepEqual(events, [['progress', null], ['progress', [{ detailId: 'line-2', status: 2 }]]])
  assert.deepEqual(state.works.value, [{ detailId: 'line-2', status: 2 }])
})

test('Успешный прогресс сохраняется, даже если не загрузились замечания', async () => {
  const works = [{ detailId: 'line-1', name: 'Полировка', status: 2, version: 1, activeTime: '00:05:00' }]
  const { state, events } = progressComponent({
    loadWorkProgress: async () => works,
    loadWorkReports: async () => { throw new Error('Не удалось загрузить замечания.') }
  })
  await state.load()
  assert.deepEqual(events, [['progress', works]])
  assert.deepEqual(state.works.value, works)
  assert.equal(state.error.value, 'Не удалось загрузить замечания.')
})

test('Сводка остаётся в карточке, а управление и замечания находятся в отдельных окнах', () => {
  const template = compileTemplate({
    source: descriptor.template.content,
    filename: filename.pathname,
    id: 'maintenance-work-progress-test'
  })
  assert.deepEqual(template.errors, [])
  assert.match(source, /compact\?: boolean/)
  assert.match(source, /compact \? t\('maintenanceWork\.workControl'\) : t\('maintenanceWork\.workProgress'\)/)
  const templateAst = parseTemplate(descriptor.template.content)
  const management = []
  const summaries = []
  const dialogs = []
  const visit = node => {
    if (hasClass(node, 'work-progress__management')) management.push(node)
    if (hasClass(node, 'work-progress__summary')) summaries.push(node)
    if (node.type === 1 && node.tag === 'dialog') dialogs.push(node)
    node.children?.forEach(visit)
  }
  visit(templateAst)
  assert.equal(management.length, 1)
  assert.equal(summaries.length, 1)
  assert.equal(hasDescendant(management[0], node => hasClass(node, 'work-progress__summary')), false)
  assert.equal(dialogs.some(dialog => hasDescendant(dialog, node => hasClass(node, 'work-progress__management'))), true)
  assert.equal(dialogs.some(dialog => hasDescendant(dialog, node => hasClass(node, 'work-progress__reports'))), true)
  assert.equal(dialogs.some(dialog => hasDescendant(dialog, node => hasClass(node, 'work-progress__audit'))), true)
  assert.equal(dialogs.some(dialog => hasDescendant(dialog, node => hasClass(node, 'work-progress__summary'))), false)
  assert.match(source, /canReturn && maintenanceStatus === 2 && work\.status === 4/)
  assert.match(source, /canReturn && report\.kind === 'extra'/)
})

test('Потеря права управления очищает формы и блокирует мутации', async () => {
  let mutations = 0
  const { state, props, watchers } = progressComponent({
    adjustWorkTime: async () => { mutations++ },
    decideWorkReport: async () => { mutations++ },
    returnWorkForRework: async () => { mutations++ }
  })
  state.timeWork.value = { detailId: 'line-1' }
  state.selected.value = { detailId: 'line-1' }
  state.decisionReport.value = { id: 'report-1' }
  props.canReturn = false
  watchers.at(-1).callback(false)
  await state.adjustTime()
  await state.returnForRework()
  await state.decide('approved')
  assert.equal(state.timeWork.value, null)
  assert.equal(state.selected.value, null)
  assert.equal(state.decisionReport.value, null)
  assert.equal(mutations, 0)
})

const assignedWork = { detailId: 'line-1', employeeName: 'Исполнитель', hasAssignedEmployee: true, status: 1, version: 0, activeTime: '00:00:00' }

test('Закрытие списков сбрасывает формы, но блокируется во время отправки', () => {
  const { state } = progressComponent()
  let closed = 0
  state.managementDialog.value = { close() { closed++ } }
  state.reportsDialog.value = { close() { closed++ } }
  state.selected.value = assignedWork
  state.reason.value = 'Повторная проверка'
  state.decisionReport.value = { id: 'extra-1' }
  state.decisionPrice.value = 500
  state.decisionVatRateCode.value = '20'
  state.decisionReason.value = 'Согласовано'
  state.busy.value = true
  state.closeManagement()
  state.closeReports()
  assert.equal(closed, 0)
  assert.ok(state.selected.value)
  assert.equal(state.decisionPrice.value, 500)
  state.busy.value = false
  state.closeManagement()
  state.closeReports()
  assert.equal(closed, 2)
  assert.equal(state.selected.value, null)
  assert.equal(state.reason.value, '')
  assert.equal(state.decisionReport.value, null)
  assert.equal(state.decisionPrice.value, null)
  assert.equal(state.decisionVatRateCode.value, '')
  assert.equal(state.decisionReason.value, '')
})

test('Смена записи закрывает окна и отменяет отложенное открытие истории', async () => {
  const { state, props, watchers } = progressComponent()
  let closed = 0
  let opened = 0
  state.managementDialog.value = { close() { closed++ } }
  state.reportsDialog.value = { close() { closed++ } }
  state.auditDialog.value = { close() { closed++ }, showModal() { opened++ } }
  const opening = state.openCompletionDetails(assignedWork)
  props.maintenanceId = 'order-2'
  watchers[0].callback()
  await opening
  assert.equal(closed, 3)
  assert.equal(opened, 0)
  assert.equal(state.auditWork.value, null)
  await state.openCompletionDetails({ ...assignedWork, detailId: 'line-2' })
  assert.equal(opened, 1)
  state.closeCompletionDetails()
  assert.equal(state.auditWork.value, null)
})

test('Ошибка решения или возврата видна после обновления и сохраняет введённые данные', async () => {
  const { state } = progressComponent({
    decideWorkReport: async () => { throw new Error('Решение не сохранено') },
    returnWorkForRework: async () => { throw new Error('Возврат не сохранён') }
  })
  state.decisionReport.value = { id: 'extra-1' }
  state.decisionPrice.value = 500
  state.decisionVatRateCode.value = '20'
  state.decisionReason.value = 'Проверено'
  await state.decide('approved')
  assert.equal(state.reportError.value, 'Решение не сохранено')
  assert.equal(state.decisionPrice.value, 500)
  assert.equal(state.decisionReason.value, 'Проверено')
  state.selected.value = assignedWork
  state.reason.value = 'Повторная проверка'
  await state.returnForRework()
  assert.equal(state.managementError.value, 'Возврат не сохранён')
  assert.ok(state.selected.value)
  assert.equal(state.reason.value, 'Повторная проверка')
})

test('Администратор завершает назначенную работу с причиной и обновляет прогресс', async () => {
  let command
  const { state, events } = progressComponent({ completeWorkForEmployee: async (...args) => { command = args } })
  await state.openCompletion(assignedWork)
  await state.completeForEmployee()
  assert.equal(command, undefined)
  assert.match(state.completionError.value, /Укажите причину/)
  state.completionReason.value = '  Проверил результат лично  '
  await state.completeForEmployee()
  assert.deepEqual(command.slice(0, 4), ['org-1', 'order-1', assignedWork, 'Проверил результат лично'])
  assert.ok(command[4])
  assert.equal(state.completionWork.value, null)
  assert.ok(events.some(event => event[0] === 'changed'))
})

test('Повтор после ошибки использует тот же commandId и сохраняет причину', async () => {
  const commandIds = []
  const { state } = progressComponent({ completeWorkForEmployee: async (...args) => {
    commandIds.push(args[4])
    throw new Error('Связь прервана')
  } })
  await state.openCompletion(assignedWork)
  state.completionReason.value = 'Проверил'
  await state.completeForEmployee()
  await state.completeForEmployee()
  assert.equal(commandIds[0], commandIds[1])
  assert.equal(state.completionReason.value, 'Проверил')
  assert.equal(state.completionError.value, 'Связь прервана')
  state.completionReason.value = 'Другая причина'
  await state.completeForEmployee()
  assert.notEqual(commandIds[1], commandIds[2])
})

test('Права, статус записи и завершённая работа ограничивают админское завершение', async () => {
  const { state, props, watchers } = progressComponent()
  for (const status of [1, 4]) {
    props.maintenanceStatus = status
    assert.equal(state.canCompleteWork(assignedWork), false)
  }
  props.maintenanceStatus = 3
  assert.equal(state.canCompleteWork(assignedWork), true)
  assert.equal(state.canCompleteWork({ ...assignedWork, status: 4 }), false)
  assert.equal(state.canCompleteWork({ ...assignedWork, hasAssignedEmployee: false }), false)
  await state.openCompletion(assignedWork)
  props.canComplete = false
  watchers[1].callback(false)
  assert.equal(state.completionWork.value, null)
  assert.equal(state.canCompleteWork(assignedWork), false)
})

test('Повторный клик и закрытие во время сохранения заблокированы', async () => {
  const pending = deferred()
  let calls = 0
  const { state } = progressComponent({ completeWorkForEmployee: async () => { calls++; await pending.promise } })
  await state.openCompletion(assignedWork)
  state.completionReason.value = 'Проверил'
  const save = state.completeForEmployee()
  state.closeCompletion()
  await state.completeForEmployee()
  assert.equal(calls, 1)
  assert.ok(state.completionWork.value)
  pending.resolve()
  await save
  assert.equal(state.completionWork.value, null)
})

test('Корректировка времени закрывается после сохранения и сохраняет черновик при ошибке', async () => {
  let fail = true
  let command
  const { state } = progressComponent({ adjustWorkTime: async (...args) => {
    command = args
    if (fail) throw new Error('Не удалось сохранить')
  } })
  const work = { ...assignedWork, status: 4 }
  await state.openTimeAdjustment(work)
  state.timeMinutes.value = 15
  state.timeReason.value = '  Уточнение времени  '
  await state.adjustTime()
  assert.equal(state.timeWork.value.detailId, work.detailId)
  assert.equal(state.timeMinutes.value, 15)
  assert.equal(state.timeError.value, 'Не удалось сохранить')
  fail = false
  await state.adjustTime()
  assert.deepEqual(command, ['org-1', 'order-1', 'line-1', 15, 'Уточнение времени'])
  assert.equal(state.timeWork.value, null)
  assert.equal(state.timeReason.value, '')
})

test('Закрытие корректировки сбрасывает черновик, а во время сохранения заблокировано', async () => {
  const pending = deferred()
  const { state } = progressComponent({ adjustWorkTime: async () => pending.promise })
  const work = { ...assignedWork, status: 4 }
  await state.openTimeAdjustment(work)
  state.timeMinutes.value = 15
  state.timeReason.value = 'Уточнение'
  state.closeTimeAdjustment()
  await state.openTimeAdjustment(work)
  assert.equal(state.timeMinutes.value, 0)
  assert.equal(state.timeReason.value, '')
  state.timeMinutes.value = 10
  state.timeReason.value = 'Проверено'
  const save = state.adjustTime()
  state.closeTimeAdjustment()
  assert.ok(state.timeWork.value)
  pending.resolve()
  await save
  assert.equal(state.timeWork.value, null)
})

test('Корректировка показывает итог и запрещает отрицательное время до отправки', async () => {
  let calls = 0
  const { state } = progressComponent({ adjustWorkTime: async () => { calls++ } })
  const work = { ...assignedWork, status: 4, activeTime: '01:20:35.5' }
  await state.openTimeAdjustment(work)
  state.timeReason.value = 'Уточнение'
  assert.equal(state.currentWorkSeconds.value, 4835.5)
  assert.equal(state.minimumTimeMinutes.value, -80)
  state.timeMinutes.value = -81
  assert.match(state.timeValidationError.value, /не больше 80 мин/)
  await state.adjustTime()
  assert.equal(calls, 0)
  state.timeMinutes.value = -80
  assert.equal(state.timeValidationError.value, '')
  assert.equal(state.adjustedWorkSeconds.value, 35.5)
  assert.equal(state.formatAdjustmentTime(state.adjustedWorkSeconds.value), '35 с')
  state.timeMinutes.value = 15
  assert.equal(state.formatAdjustmentTime(state.adjustedWorkSeconds.value), '1 ч 35 мин 35 с')
  state.timeMinutes.value = -9999999999999999999999999999
  assert.match(state.timeValidationError.value, /целое число минут/)
  await state.adjustTime()
  assert.equal(calls, 0)
})

test('Расчёт учитывает обновление времени, дни и отсутствие данных', async () => {
  const { state } = progressComponent()
  await state.openTimeAdjustment({ ...assignedWork, status: 4 })
  assert.equal(state.formatAdjustmentTime(state.currentWorkSeconds.value), '0 мин')
  state.timeMinutes.value = -1
  assert.match(state.timeValidationError.value, /Уменьшить время нельзя/)
  state.works.value = [{ ...assignedWork, status: 4, activeTime: '1.02:03:04' }]
  assert.equal(state.currentWorkSeconds.value, 93784)
  assert.equal(state.adjustedWorkSeconds.value, 93724)
  assert.equal(state.timeValidationError.value, '')
  state.works.value = [{ ...assignedWork, status: 4, activeTime: 'неизвестно' }]
  assert.equal(state.currentWorkSeconds.value, null)
  assert.equal(state.adjustedWorkSeconds.value, null)
  assert.match(state.timeValidationError.value, /Не удалось определить/)
})

test('Отключённая команда блокирует согласование старой заявки, но позволяет её отклонить', async () => {
  const decisions = []
  const { state, props } = progressComponent({ decideWorkReport: async (...args) => decisions.push(args[2]) })
  props.teamAvailable = false
  state.decisionReport.value = { id: 'old-extra' }
  state.decisionPrice.value = 500
  state.decisionVatRateCode.value = 'without_vat'
  state.decisionReason.value = 'Отказ после смены тарифа'
  await state.decide('approved')
  assert.deepEqual(decisions, [])
  assert.equal(state.decisionReport.value.id, 'old-extra')
  await state.decide('rejected')
  assert.deepEqual(decisions, ['rejected'])
  assert.equal(state.decisionReport.value, null)
})

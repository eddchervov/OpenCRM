import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import './inquiryTestMessages.mjs'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { i18n, setFormattingCulture } from '@opencrm/ui/i18n'

function componentCode(name) {
  const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/' + name + '.vue', import.meta.url), 'utf8'))
  return ts.transpileModule(compileScript(descriptor, { id: name + '-test' }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
}
const dialogCode = componentCode('InquiryHistoryDialog')
const noteCode = componentCode('InquiryNoteComposer')
const timelineCode = componentCode('InquiryTimeline')
const inquiryTime = { exports: {} }
const timeCode = ts.transpileModule(readFileSync(new URL('../packages/workspace/src/utils/inquiryTime.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS }
}).outputText
new Function('require', 'module', 'exports', timeCode)(id => regionalTestDependencies[id] ?? {}, inquiryTime, inquiryTime.exports)
class InquiryRequestError extends Error {
  constructor(message, status) { super(message); this.status = status }
}
function historyComponent(code, { saveFailure, historyFailure } = {}) {
  const module = { exports: {} }, calls = [], events = []
  const props = vue.reactive({ context: { organization: { id: 'org' } }, inquiry: { id: 'inquiry', version: 7, canManage: true }, timeZoneId: 'Asia/Shanghai' })
  new Function('require', 'module', 'exports', code)(id => {
    if (regionalTestDependencies[id]) return regionalTestDependencies[id]
    if (id === '../utils/inquiryTime') return inquiryTime.exports
    if (id === 'vue') return { ...vue, onMounted() {}, onBeforeUnmount() {} }
    if (id === '../api/inquiries') return {
      InquiryRequestError,
      inquiryStages: [{ value: 'new', label: 'Новое' }, { value: 'proposal', label: 'Предложение' }],
      loadInquiryHistory: async (...args) => {
        calls.push(['history', ...args])
        if (historyFailure) throw historyFailure
        return { items: [], totalCount: 0 }
      },
      addInquiryNote: async (...args) => {
        calls.push(['note', ...args])
        if (saveFailure) { const failure = saveFailure; saveFailure = null; throw failure }
        return { ...props.inquiry, version: props.inquiry.version + 1 }
      },
      loadInquiry: async () => ({ ...props.inquiry, version: 10 })
    }
    return {}
  }, module, module.exports)
  const state = module.exports.default.setup(props, { expose() {}, emit: (event, value) => {
    events.push([event, value])
    if (event === 'updated') props.inquiry = value
  } })
  return { state, props, calls, events }
}

test('Системные события переводятся, а неизвестные события и заметки сохраняют текст', () => {
  const originalLocale = i18n.global.locale.value
  try {
    const { state } = historyComponent(timelineCode)
    for (const [locale, expected] of [['en', 'Inquiry created'], ['zh-Hans', '咨询已创建']]) {
      i18n.global.locale.value = locale
      assert.equal(state.eventTitle({ eventType: 'created', title: 'Обращение создано' }), expected)
      assert.equal(state.eventTitle({ eventType: 'custom', title: 'Пользовательское событие' }), 'Пользовательское событие')
      assert.equal(state.eventDescription({ eventType: 'note', description: '客户希望周五到店' }), '客户希望周五到店')
    }
  } finally { i18n.global.locale.value = originalLocale }
})

test('Перенос действия форматирует дату независимо от языка и сохраняет часовой пояс организации', () => {
  const originalLocale = i18n.global.locale.value
  try {
    i18n.global.locale.value = 'zh-Hans'
    setFormattingCulture('en-GB')
    const { state } = historyComponent(timelineCode)
    const result = state.eventDescription({ eventType: 'action_rescheduled', newValue: '2026-11-16T09:30:00Z' })
    assert.match(result, /16 Nov/)
    assert.match(result, /17:30/)
  } finally { i18n.global.locale.value = originalLocale; setFormattingCulture('ru-RU') }
})

test('История загружает выбранную страницу и отменяет предыдущий запрос', async () => {
  const { state, calls } = historyComponent(dialogCode)
  await state.loadHistory()
  state.page.value = 2
  await state.loadHistory()
  assert.equal(calls[0][4].aborted, true)
  assert.equal(calls[1][3], 2)
  assert.equal(calls[1][4].aborted, false)
})

test('Ошибка истории остаётся в окне и не меняет карточку', async () => {
  const { state, events } = historyComponent(dialogCode, { historyFailure: new Error('Нет соединения') })
  await state.loadHistory()
  assert.equal(state.error.value, 'Нет соединения')
  assert.equal(state.loading.value, false)
  assert.equal(state.history.value, null)
  assert.deepEqual(events, [])
})

test('Конфликт сохраняет заметку; обновление версии позволяет сохранить её без потери текста', async () => {
  const { state, calls, props } = historyComponent(noteCode, { saveFailure: new InquiryRequestError('Данные изменены', 409) })
  state.composing.value = true
  state.note.value = ' Договорились созвониться '
  await state.saveNote()
  assert.equal(state.conflict.value, true)
  assert.equal(state.note.value, ' Договорились созвониться ')
  assert.equal(state.composing.value, true)
  await state.refreshInquiry()
  assert.equal(props.inquiry.version, 10)
  assert.equal(state.conflict.value, false)
  assert.equal(state.note.value, ' Договорились созвониться ')
  await state.saveNote()
  assert.deepEqual(calls.findLast(call => call[0] === 'note'), ['note', 'org', 'inquiry', 10, 'Договорились созвониться'])
  assert.equal(state.note.value, '')
  assert.equal(state.composing.value, false)
  assert.equal(props.inquiry.version, 11)
})

test('Выполненное действие показывает текст задачи без перехода к пустому значению', () => {
  const { state } = historyComponent(timelineCode)
  assert.equal(state.eventDescription({ eventType: 'action_completed', previousValue: 'Перезвонить клиенту', newValue: null }), 'Перезвонить клиенту')
  assert.equal(state.eventDescription({ eventType: 'action_completed', previousValue: null, newValue: null }), '')
})

test('Закрытие показывает причину без технического кода прежнего этапа', () => {
  const { state } = historyComponent(timelineCode)
  assert.equal(state.eventDescription({ eventType: 'closed', previousValue: 'proposal', newValue: 'Клиент отказался' }), 'Клиент отказался')
})

test('Смена этапа сохраняет локализованный переход между значениями', () => {
  const { state } = historyComponent(timelineCode)
  assert.equal(state.eventDescription({ eventType: 'stage_changed', previousValue: 'new', newValue: 'proposal' }), 'Новое → Предложение')
})

test('Комментарий к этапу не скрывает сам переход в общей ленте', () => {
  const { state } = historyComponent(timelineCode)
  assert.equal(state.eventDescription({ eventType: 'stage_changed', previousValue: 'new', newValue: 'proposal', description: 'Предложение отправлено клиенту' }), 'Новое → Предложение')
})

test('Сохранение заметки сообщает занятость владельцу окна', async () => {
  const { state, events } = historyComponent(noteCode)
  state.note.value = 'Клиент ждёт звонка'
  await state.saveNote()
  assert.deepEqual(events.filter(event => event[0] === 'busy'), [['busy', true], ['busy', false]])
})

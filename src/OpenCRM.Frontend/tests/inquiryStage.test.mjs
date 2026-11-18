import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import './inquiryTestMessages.mjs'
import { i18n, formattingCulture, setFormattingCulture } from '@opencrm/ui/i18n'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/InquiryCardSection.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'inquiry-stage-test' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
class InquiryRequestError extends Error { constructor(message, status) { super(message); this.status = status } }
function card(failure) {
  const scope = vue.effectScope(), module = { exports: {} }, transitions = [], historyRequests = []
  let state
  const props = vue.reactive({ inquiryId: 'inquiry', context: { organization: { id: 'org', members: [] } }, timeZoneId: null })
  new Function('require', 'module', 'exports', code)(id => {
    if (regionalTestDependencies[id]) return regionalTestDependencies[id]
    if (id === 'vue') return { ...vue, onMounted() {}, onBeforeUnmount() {} }
    if (id === '../utils/inquiryTime') return { resolveInquiryTimeZone: () => 'UTC', inquiryLocalTime: () => '' }
    if (id === '../api/inquiries') return {
      InquiryRequestError,
      changeInquiryStage: async (...args) => {
        transitions.push(args)
        if (failure) { const caught = failure; failure = undefined; throw caught }
        return { ...state.inquiry.value, stageCode: args[3], version: state.inquiry.value.version + 1 }
      },
      loadInquiry: async () => ({ ...state.inquiry.value, version: 5, contactName: 'Актуальное имя', interestText: 'Актуальный запрос' }),
      loadInquiryHistory: async (...args) => { historyRequests.push(args); return { items: [], totalCount: 0 } }
    }
    return {}
  }, module, module.exports)
  state = scope.run(() => module.exports.default.setup(props, { expose() {}, emit() {} }))
  state.inquiry.value = { id: 'inquiry', stageCode: 'new', version: 4, timeZoneId: null, canManage: true }
  return { state, transitions, historyRequests, dispose: () => scope.stop() }
}

test('Название часового пояса следует языку интерфейса отдельно от формата дат', () => {
  const originalLocale = i18n.global.locale.value
  const originalCulture = formattingCulture.value
  const { state, dispose } = card()
  try {
    i18n.global.locale.value = 'zh-Hans'
    setFormattingCulture('en-GB')
    assert.match(state.zoneLabel.value, /\p{Script=Han}/u)
    assert.equal(formattingCulture.value, 'en-GB')
    i18n.global.locale.value = 'en'
    assert.match(state.zoneLabel.value, /Universal Time/)
  } finally { dispose(); i18n.global.locale.value = originalLocale; setFormattingCulture(originalCulture) }
})

test('Комментарий отправляется вместе с переходом и очищается после сохранения', async () => {
  const { state, transitions, dispose } = card()
  try {
    state.stage.value = 'proposal'
    await vue.nextTick()
    state.stageComment.value = 'Предложение отправлено'
    await state.applyStage()
    assert.deepEqual(transitions, [['org', 'inquiry', 4, 'proposal', 'Предложение отправлено']])
    assert.equal(state.inquiry.value.stageCode, 'proposal')
    assert.equal(state.stageComment.value, '')
  } finally { dispose() }
})

test('Конфликт смены этапа сохраняет комментарий и выбранный этап', async () => {
  const { state, dispose } = card(new InquiryRequestError('Обращение изменено', 409))
  try {
    state.stage.value = 'proposal'
    await vue.nextTick()
    state.stageComment.value = 'Согласовали объём'
    await state.applyStage()
    assert.equal(state.stageComment.value, 'Согласовали объём')
    assert.equal(state.stage.value, 'proposal')
    assert.equal(state.inquiry.value.version, 4)
    assert.equal(state.conflict.value, true)
    await state.refreshAfterConflict()
    assert.equal(state.inquiry.value.version, 5)
    assert.equal(state.stage.value, 'proposal')
    assert.equal(state.stageComment.value, 'Согласовали объём')
    await state.applyStage()
    assert.equal(state.inquiry.value.stageCode, 'proposal')
    assert.equal(state.stageComment.value, '')
  } finally { dispose() }
})

test('Заметка обновляет краткую историю и версию без потери черновика перехода', async () => {
  const { state, historyRequests, dispose } = card()
  try {
    state.stage.value = 'proposal'
    await vue.nextTick()
    state.stageComment.value = 'Предложение отправлено'
    state.updateFromHistory({ ...state.inquiry.value, version: 5 })
    await vue.nextTick()
    assert.equal(state.stage.value, 'proposal')
    assert.equal(state.stageComment.value, 'Предложение отправлено')
    assert.equal(state.inquiry.value.version, 5)
    assert.equal(historyRequests[0][3].aborted, true)
    assert.equal(historyRequests.at(-1)[2], 1)
    assert.equal(historyRequests.at(-1)[4], 3)
  } finally { dispose() }
})

test('Обновление версии сохраняет поля основного редактора после конфликта', async () => {
  const { state, dispose } = card()
  try {
    state.editing.value = true
    state.form.contactName = 'Черновик имени'
    state.form.interestText = 'Черновик запроса'
    state.form.nextActionDate = '2026-10-24'
    await state.refreshAfterConflict()
    assert.equal(state.inquiry.value.version, 5)
    assert.equal(state.form.contactName, 'Черновик имени')
    assert.equal(state.form.interestText, 'Черновик запроса')
    assert.equal(state.form.nextActionDate, '2026-10-24')
  } finally { dispose() }
})

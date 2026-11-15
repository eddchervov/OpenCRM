import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const code = ts.transpileModule(readFileSync(new URL('../packages/workspace/src/api/inquiries.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

test('Сервер определяет часовой пояс организации для списка, доски и действий рабочего дня', async () => {
  const requests = [], module = { exports: {} }
  const fetch = async url => { requests.push(url); return { ok: true, status: 200, json: async () => ({}) } }
  new Function('require', 'module', 'exports', 'fetch', 'window', 'document', code)(
    () => ({ resolveInquiryTimeZone: () => 'Asia/Novosibirsk' }), module, module.exports, fetch,
    { location: { origin: 'https://localhost' } }, { querySelector: () => null }
  )
  await module.exports.loadInquiries('org', { actionDue: 'today' })
  await module.exports.loadInquiryBoard('org', { actionDue: 'today' })
  await module.exports.loadInquiries('org', { actionDue: 'through-day', date: '2026-10-24', responsibleUserId: 'employee' })
  assert.equal(requests.length, 3)
  for (const url of requests) assert.equal(url.searchParams.has('timeZoneId'), false)
  assert.equal(requests[1].pathname, '/api/v1/organizations/org/inquiries/board')
  assert.equal(requests[2].searchParams.get('date'), '2026-10-24')
  assert.equal(requests[2].searchParams.get('responsibleUserId'), 'employee')
})

test('Смена этапа передаёт комментарий, краткая история запрашивает только три события', async () => {
  const requests = [], module = { exports: {} }
  const fetch = async (url, options) => { requests.push({ url, options }); return { ok: true, status: 200, json: async () => ({}) } }
  new Function('require', 'module', 'exports', 'fetch', 'window', 'document', code)(
    () => ({ resolveInquiryTimeZone: () => 'Asia/Novosibirsk' }), module, module.exports, fetch,
    { location: { origin: 'https://localhost' } }, { querySelector: () => null }
  )
  await module.exports.changeInquiryStage('org', 'inquiry', 9, 'proposal', 'Предложение отправлено клиенту')
  assert.deepEqual(JSON.parse(requests[0].options.body), { version: 9, stageCode: 'proposal', comment: 'Предложение отправлено клиенту' })
  await module.exports.loadInquiryHistory('org', 'inquiry', 1, undefined, 3)
  assert.equal(requests[1].url.searchParams.get('pageSize'), '3')
})

test('Перенос следующего действия отправляет новую дату и текущую версию', async () => {
  const requests = [], module = { exports: {} }
  const fetch = async (url, options) => { requests.push({ url, options }); return { ok: true, status: 200, json: async () => ({}) } }
  new Function('require', 'module', 'exports', 'fetch', 'window', 'document', code)(
    () => ({ resolveInquiryTimeZone: () => 'Asia/Novosibirsk' }), module, module.exports, fetch,
    { location: { origin: 'https://localhost' } }, { querySelector: () => null }
  )
  await module.exports.rescheduleInquiryAction('org', 'inquiry', 11, '2026-10-25T02:00:00.000Z')
  assert.equal(requests[0].url.pathname, '/api/v1/organizations/org/inquiries/inquiry/action/reschedule')
  assert.equal(requests[0].options.method, 'POST')
  assert.deepEqual(JSON.parse(requests[0].options.body), { version: 11, nextActionAt: '2026-10-25T02:00:00.000Z' })
})

test('Напоминания по обращениям загружаются только для выбранной организации', async () => {
  const requests = [], module = { exports: {} }
  const fetch = async (url, options) => { requests.push({ url, options }); return { ok: true, status: 200, json: async () => [] } }
  new Function('require', 'module', 'exports', 'fetch', 'window', 'document', code)(
    () => ({ resolveInquiryTimeZone: () => 'Asia/Novosibirsk' }), module, module.exports, fetch,
    { location: { origin: 'https://localhost' } }, { querySelector: () => null }
  )
  await module.exports.loadInquiryActionReminders('org')
  assert.equal(requests[0].url.pathname, '/api/v1/organizations/org/inquiries/action-reminders')
  assert.equal(requests[0].options.credentials, 'same-origin')
})

import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const source = readFileSync(new URL('../packages/workspace/src/api/workspaceAdmin.ts', import.meta.url), 'utf8')
const code = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
const translatedMessages = {
  'workspaceApi.checkForm': 'Проверьте данные формы.',
  'workspaceApi.loadMembers': 'Не удалось загрузить участников.'
}
function apiFor(response) {
  const calls = []
  const module = { exports: {} }
  const requireModule = id => id === '@opencrm/ui/i18n'
    ? { translate: key => translatedMessages[key] ?? key }
    : {}
  new Function('module', 'exports', 'document', 'fetch', 'require', code)(module, module.exports, { querySelector: () => null }, async (url, options) => { calls.push({ url, options }); return response }, requireModule)
  return { api: module.exports, calls }
}

test('Реквизиты очищают пробелы до серверной проверки длины и email', async () => {
  const { api, calls } = apiFor({ ok: true, json: async () => ({}) })
  await api.updateWorkspaceRequisites('org', { kpp: ' 990101001 ', email: ' contact@example.invalid ', legalName: '  ООО Тест  ', phone: '   ' })
  assert.deepEqual(JSON.parse(calls[0].options.body), { kpp: '990101001', email: 'contact@example.invalid', legalName: 'ООО Тест', phone: null })
})

test('Ошибка API сохраняет конкретное поле вместо общего английского заголовка', async () => {
  const errors = { Kpp: ['Не более 9 символов.'] }
  const { api } = apiFor({ ok: false, json: async () => ({ message: 'One or more validation errors occurred.', errors }) })
  await assert.rejects(() => api.updateWorkspaceRequisites('org', { kpp: '990101001' }), error => {
    assert.ok(error instanceof api.WorkspaceActionError)
    assert.equal(error.message, 'Проверьте данные формы.')
    assert.deepEqual(error.errors, errors)
    return true
  })
})

test('Неверные российские реквизиты не отправляются, ошибки привязаны к полям', async () => {
  const { api, calls } = apiFor({ ok: true, json: async () => ({}) })
  for (const payload of [
    { inn: '123', kpp: '123', ogrn: '123' },
    { inn: '１２３４５６７８９０', kpp: '1234АБ123', ogrn: '123456789012A' }
  ]) {
    await assert.rejects(() => api.updateWorkspaceRequisites('org', payload), error => {
      assert.ok(error instanceof api.WorkspaceActionError)
      assert.deepEqual(Object.keys(error.errors), ['inn', 'kpp', 'ogrn'])
      return true
    })
  }
  assert.equal(calls.length, 0)
  await api.updateWorkspaceRequisites('org', { inn: ' 1234567890 ', kpp: ' 1234ab123 ', ogrn: '1234567890123' })
  await api.updateWorkspaceRequisites('org', { inn: '123456789012', kpp: '', ogrn: '123456789012345' })
  await api.updateWorkspaceRequisites('org', { inn: '   ', kpp: '', ogrn: null })
  assert.equal(calls.length, 3)
  assert.equal(JSON.parse(calls[0].options.body).kpp, '1234AB123')
  assert.deepEqual(JSON.parse(calls.at(-1).options.body), { inn: null, kpp: null, ogrn: null })
})

test('Fallback API сообщения локализованы, а сообщение сервера сохраняется', async () => {
  const fallback = apiFor({ ok: false, json: async () => ({}) })
  await assert.rejects(() => fallback.api.loadWorkspaceMembers('org'), error => {
    assert.equal(error.message, 'Не удалось загрузить участников.')
    return true
  })

  const serverError = apiFor({ ok: false, json: async () => ({ message: 'Server supplied error' }) })
  await assert.rejects(() => serverError.api.loadWorkspaceMembers('org'), error => {
    assert.equal(error.message, 'Server supplied error')
    return true
  })
})

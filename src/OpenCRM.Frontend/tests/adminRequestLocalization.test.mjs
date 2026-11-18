import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import ts from 'typescript'
import * as localization from '@opencrm/ui/i18n'
import '../Admin/admin-client/src/adminApiMessages.ts'

const source = readFileSync(new URL('../Admin/admin-client/src/api/httpClient.ts', import.meta.url), 'utf8')
  .replace('import.meta.env.VITE_API_BASE_URL', 'undefined')
const compilerOptions = { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
const code = ts.transpileModule(source, { compilerOptions }).outputText
const errorExports = {}
const errorSource = readFileSync(new URL('../Admin/admin-client/src/api/ApiError.ts', import.meta.url), 'utf8')
new Function('exports', ts.transpileModule(errorSource, { compilerOptions }).outputText)(errorExports)
const { ApiError } = errorExports

function createClient(fetch) {
  const module = { exports: {} }
  new Function('require', 'module', 'exports', 'fetch', 'Headers', code)(id => {
    if (id === './ApiError') return { ApiError }
    if (id === '@opencrm/ui/i18n') return localization
    if (id === '../adminApiMessages') return {}
    throw new Error(`Unexpected dependency: ${id}`)
  }, module, module.exports, fetch, Headers)

  return module.exports
}

test('Админский HTTP fallback переводится, а статус, код и серверное сообщение сохраняются', async () => {
  const originalLocale = localization.i18n.global.locale.value
  try {
    localization.i18n.global.locale.value = 'zh-Hans'
    const fallback = createClient(async () => ({ ok: false, status: 503, json: async () => ({}) }))
    await assert.rejects(fallback.request('/test-only'), error => error.status === 503 && error.message === '请求失败（HTTP 503）。')
    const specific = createClient(async () => ({ ok: false, status: 409, json: async () => ({ message: 'Сервер сообщил о конфликте', code: 'example_conflict' }) }))
    await assert.rejects(specific.request('/test-only'), error => error.status === 409 && error.code === 'example_conflict' && error.message === 'Сервер сообщил о конфликте')
  } finally { localization.i18n.global.locale.value = originalLocale }
})

test('Ошибка токена локализована, не отправляет изменение и допускает безопасный повтор', async () => {
  const originalLocale = localization.i18n.global.locale.value
  const calls = []
  let tokenRequests = 0
  try {
    localization.i18n.global.locale.value = 'en'
    const client = createClient(async (url, options) => {
      calls.push({ url, options })
      if (url.endsWith('/antiforgery/token')) {
        tokenRequests++
        return { ok: tokenRequests > 1, status: tokenRequests > 1 ? 200 : 403, json: async () => ({ requestToken: 'test-only-token' }) }
      }
      return { ok: true, status: 204, headers: new Headers() }
    })
    await assert.rejects(client.request('/test-only', { method: 'POST' }), /Could not obtain the request verification token/)
    assert.equal(calls.length, 1)
    await client.request('/test-only', { method: 'POST' })
    assert.equal(tokenRequests, 2)
    assert.equal(calls[2].options.headers.get('RequestVerificationToken'), 'test-only-token')
    assert.equal(calls[2].options.credentials, 'include')
  } finally { localization.i18n.global.locale.value = originalLocale }
})

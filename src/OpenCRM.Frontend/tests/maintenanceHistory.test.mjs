import * as localization from '@opencrm/ui/i18n'
import '../packages/workspace/src/apiMessages.ts'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const code = ts.transpileModule(readFileSync(new URL('../packages/workspace/src/api/maintenance.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function createApi(fetch) {
  const module = { exports: {} }
  new Function('require', 'module', 'exports', 'fetch', 'window', code)(id => {
    if (id === '@opencrm/ui/i18n') return localization
    if (id === '@opencrm/workspace/apiMessages') return {}
    throw new Error(id)
  }, module, module.exports, fetch, { location: { origin: 'https://localhost' } })
  return module.exports
}

test('История запрашивается в выбранной организации с отменой запроса и сохраняет источник события', async () => {
  const abort = new AbortController()
  const events = [{ id: 'event', eventType: 'created', sourceInquiryId: 'inquiry', timeZoneId: 'Asia/Novosibirsk' }]
  const api = createApi(async (url, options) => {
    const request = new URL(url)
    assert.equal(request.pathname, '/api/v1/maintenance/record/history')
    assert.equal(request.searchParams.get('organizationId'), 'organization')
    assert.equal(options.signal, abort.signal)
    assert.equal(options.credentials, 'same-origin')
    return { ok: true, json: async () => events }
  })
  assert.deepEqual(await api.loadMaintenanceHistory('organization', 'record', abort.signal), events)
})

test('Ошибка доступа к истории не превращается в пустую историю', async () => {
  const api = createApi(async () => ({
    ok: false, headers: new Headers({ 'content-type': 'application/json' }),
    json: async () => ({ message: 'Недостаточно прав для просмотра записи.' })
  }))
  await assert.rejects(api.loadMaintenanceHistory('organization', 'record'), /Недостаточно прав/)
})

test('HTML страницы ошибки не показывается как текст ошибки записи', async () => {
  for (const contentType of ['text/html', 'text/plain']) {
    const api = createApi(async () => ({
      ok: false, status: 404, headers: new Headers({ 'content-type': contentType }),
      text: async () => '<!DOCTYPE html><html><body>Ошибка 404</body></html>'
    }))
    await assert.rejects(api.loadMaintenance('organization', 'not-a-guid'), error => {
      assert.doesNotMatch(error.message, /DOCTYPE|<html>/)
      assert.ok(error.message.length < 200)
      return true
    })
  }
})

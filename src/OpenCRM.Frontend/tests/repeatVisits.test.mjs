import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'

function compileTypeScript(source) {
  return ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
}

function loadApi(fetch, getMutationHeaders = () => ({ 'X-Requested-With': 'XMLHttpRequest', RequestVerificationToken: 'shared-token' })) {
  const source = readFileSync(new URL('../packages/workspace/src/api/repeatVisits.ts', import.meta.url), 'utf8')
  const module = { exports: {} }
  new Function('require', 'module', 'exports', 'fetch', 'window', compileTypeScript(source))(
    id => {
      assert.equal(id, './maintenance')
      return { getMutationHeaders }
    },
    module,
    module.exports,
    fetch,
    { location: { origin: 'https://localhost' } }
  )
  return module.exports
}

test('Repeat visit API uses shared antiforgery headers and sends the stable create command id', async () => {
  const calls = []
  const api = loadApi(async (...args) => {
    calls.push(args)
    return { ok: true, status: 201, json: async () => ({ id: 'repeat-id' }) }
  })
  const body = { id: 'draft-id', sourceMaintenanceId: 'maintenance-id', serviceName: 'Полировка', suggestedVisitDate: '2026-12-01', responsibleUserId: null, contactByUtc: null }

  await api.saveRepeatVisit('org one', null, body)

  assert.equal(calls[0][0].pathname, '/api/v1/organizations/org%20one/repeat-visits')
  assert.equal(calls[0][1].method, 'POST')
  assert.equal(calls[0][1].headers.RequestVerificationToken, 'shared-token')
  assert.deepEqual(JSON.parse(calls[0][1].body), body)
})

test('Repeat visit API reports authentication, access and version conflicts clearly', async () => {
  const messages = new Map([[401, 'HTTP 401'], [403, 'нет доступа'], [409, 'Рекомендацию изменил другой сотрудник']])
  for (const [status, expectedMessage] of messages) {
    const api = loadApi(async () => ({ ok: false, status, json: async () => ({ message: status === 409 ? 'Рекомендацию изменил другой сотрудник' : 'server error' }) }))
    await assert.rejects(api.loadRepeatVisit('org', 'repeat-id'), new RegExp(expectedMessage))
  }
})

test('Create form keeps one command id on retry and leaves contact action off by default', async () => {
  const calls = []
  let failFirstSave = true
  const api = {
    loadRepeatVisits: async () => ({ items: [], totalCount: 0, page: 1, pageSize: 10, timeZoneId: 'Asia/Novosibirsk' }),
    loadRepeatVisitOptions: async () => ({ responsibles: [{ id: 'employee', name: 'Сотрудник' }], timeZoneId: 'Asia/Novosibirsk', canCreate: true }),
    saveRepeatVisit: async (_organizationId, _id, body) => {
      calls.push(body)
      if (failFirstSave) {
        failFirstSave = false
        throw new Error('Сетевая ошибка')
      }
      return { ...body, id: body.id }
    }
  }
  const filename = new URL('../packages/workspace/src/components/RepeatVisitRecommendations.vue', import.meta.url)
  const { descriptor } = parse(readFileSync(filename, 'utf8'), { filename: filename.pathname })
  const code = compileTypeScript(compileScript(descriptor, { id: 'repeat-visit-recommendations-test' }).content)
  const lifecycle = {
    defineComponent: options => options,
    ref: value => ({ value }),
    computed: getter => ({ get value() { return getter() } }),
    nextTick: async () => {},
    watch(_source, callback, options = {}) { if (options.immediate) lifecycle.initialLoad = callback() },
    onBeforeUnmount() {}
  }
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)((id) => {
    if (id === 'vue') return lifecycle
    if (id === '@opencrm/ui') return { UiButton: {}, UiInput: {}, UiPagination: {}, UiSelect: {}, UiTextarea: {} }
    if (id === '../api/repeatVisits') return api
    if (id === '../utils/inquiryTime') return {
      inquiryLocalTime: value => value,
      inquiryUtcTime: (value, zone) => { assert.equal(zone, 'Asia/Novosibirsk'); return `${value}:00+07:00` }
    }
    throw new Error(`Unexpected module ${id}`)
  }, module, module.exports)
  const state = module.exports.default.setup({ organizationId: 'org', sourceMaintenanceId: 'maintenance-id', canEdit: true }, { emit() {}, expose() {} })
  await new Promise(resolve => setImmediate(resolve))
  state.startCreate()
  state.draft.value.serviceName = 'Полировка'
  state.draft.value.suggestedVisitDate = '2026-12-01'
  const draftId = state.createCommandId.value

  await state.save()
  assert.equal(state.formError.value, 'Сетевая ошибка')
  assert.equal(state.createCommandId.value, draftId)
  assert.equal(state.contactEnabled.value, false)
  assert.equal(calls[0].contactByUtc, null)
  assert.equal(calls[0].responsibleUserId, null)

  await state.save()
  assert.equal(calls[1].id, draftId)
  assert.equal(calls[1].contactByUtc, null)

  state.startCreate()
  state.draft.value.serviceName = 'Защитное покрытие'
  state.draft.value.suggestedVisitDate = '2027-01-15'
  state.contactEnabled.value = true
  state.draft.value.responsibleUserId = 'employee'
  state.draft.value.contactDate = '2026-12-01'
  state.draft.value.contactTime = '09:00'
  await state.save()
  assert.equal(calls[2].suggestedVisitDate, '2027-01-15')
  assert.equal(calls[2].contactByUtc, '2026-12-01T09:00:00+07:00')
  assert.equal(calls[2].responsibleUserId, 'employee')

  state.startCreate()
  state.draft.value.serviceName = 'Общий контрольный осмотр'
  state.draft.value.suggestedVisitDate = '2027-01-15'
  state.contactEnabled.value = true
  state.draft.value.contactDate = '2026-12-01'
  state.draft.value.contactTime = '10:00'
  await state.save()
  assert.equal(calls[3].responsibleUserId, null)
  assert.equal(calls[3].contactByUtc, '2026-12-01T10:00:00+07:00')

  state.startCreate()
  state.contactEnabled.value = true
  await state.save()
  assert.equal(calls.length, 4)
  assert.equal(state.fieldErrors.value.serviceName, 'Укажите рекомендуемую услугу.')
  assert.equal(state.fieldErrors.value.contactDate, 'Выберите дату связи с клиентом.')
  assert.equal(state.fieldErrors.value.contactTime, 'Выберите время связи с клиентом.')
  assert.match(state.formError.value, /Рекомендация не сохранена/)
})

test('Organization local contact time converts to UTC without changing the suggested date', () => {
  const source = readFileSync(new URL('../packages/workspace/src/utils/inquiryTime.ts', import.meta.url), 'utf8')
  const module = { exports: {} }
  new Function('module', 'exports', compileTypeScript(source))(module, module.exports)
  assert.equal(module.exports.inquiryUtcTime('2026-10-25T09:00', 'Asia/Novosibirsk'), '2026-10-25T02:00:00.000Z')
})

import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'

const transpile = source => ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText

function loadPure(path) {
  const url = new URL(path, import.meta.url)
  const module = { exports: {} }
  new Function('require', 'module', 'exports', transpile(readFileSync(url, 'utf8')))(id => loadPure(new URL(`${id}.ts`, url)), module, module.exports)
  return module.exports
}

test('Organization today follows the configured zone across UTC midnight and preserves floating calendar dates', () => {
  const dates = loadPure('../packages/ui/src/dateOnly.ts')
  const instant = new Date('2026-10-24T18:30:00Z')
  assert.equal(dates.formatDateOnly(instant, 'Asia/Novosibirsk'), '2026-10-25')
  assert.equal(dates.formatDateOnly(instant, 'UTC'), '2026-10-24')
  assert.equal(dates.formatDateOnly(new Date('2026-10-25T02:00:00Z'), 'America/Los_Angeles'), '2026-10-24')
  assert.equal(dates.formatDateOnly(dates.parseDateOnly('2026-10-25')), '2026-10-25')
  const times = loadPure('../packages/ui/src/dateTime.ts')
  assert.equal(times.formatLocalDateTime(instant, 'Asia/Novosibirsk'), '2026-10-25T01:30:00')
})

test('Time zone API sends the prior setting and shared antiforgery token to the selected organization', async () => {
  const calls = []
  const module = { exports: {} }
  const source = readFileSync(new URL('../packages/workspace/src/api/organizationTimeZone.ts', import.meta.url), 'utf8')
  new Function('require', 'module', 'exports', 'fetch', transpile(source))(
    id => { assert.equal(id, './maintenance'); return { getMutationHeaders: () => ({ RequestVerificationToken: 'token' }) } },
    module, module.exports, async (...args) => { calls.push(args); return { ok: true, json: async () => ({ timeZoneId: 'Asia/Novosibirsk', canManage: true }) } }
  )
  await module.exports.saveOrganizationTimeZone('org one', 'Asia/Novosibirsk', null)
  assert.equal(calls[0][0], '/api/v1/organizations/org%20one/time-zone')
  assert.equal(calls[0][1].credentials, 'same-origin')
  assert.equal(calls[0][1].headers.RequestVerificationToken, 'token')
  assert.deepEqual(JSON.parse(calls[0][1].body), { timeZoneId: 'Asia/Novosibirsk', expectedTimeZoneId: null })
})

async function timeZoneEditor({ canManage = true, disabled = false } = {}) {
  const saves = []
  const events = []
  let failSave = true
  const filename = new URL('../packages/workspace/src/components/OrganizationTimeZoneSettings.vue', import.meta.url)
  const { descriptor } = parse(readFileSync(filename, 'utf8'), { filename: filename.pathname })
  const code = transpile(compileScript(descriptor, { id: 'organization-time-zone-test' }).content)
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => {
    if (id === 'vue') return {
      defineComponent: options => options, ref: value => ({ value }), computed: getter => ({ get value() { return getter() } }),
      watch(_source, callback, options) { if (options?.immediate) callback() }, onBeforeUnmount() {}
    }
    if (id === '@opencrm/ui') return { UiButton: {}, UiSelect: {} }
    if (id === '../utils/organizationTimeZone') return loadPure('../packages/workspace/src/utils/organizationTimeZone.ts')
    if (id === '../api/organizationTimeZone') return {
      loadOrganizationTimeZone: async () => ({ timeZoneId: 'Asia/Novosibirsk', canManage }),
      saveOrganizationTimeZone: async (...args) => {
        saves.push(args)
        if (failSave) { failSave = false; throw new Error('Часовой пояс уже изменил другой сотрудник.') }
        return { timeZoneId: args[1], canManage }
      }
    }
    throw new Error(`Unexpected module ${id}`)
  }, module, module.exports)
  const state = module.exports.default.setup({ organizationId: 'org', disabled }, { emit: (...args) => events.push(args), expose() {} })
  await new Promise(resolve => setImmediate(resolve))
  return { state, saves, events }
}

test('Time zone editor preserves the draft on conflict and refreshes the workspace only after success', async () => {
  const { state, saves, events } = await timeZoneEditor()
  state.startEdit()
  state.draft.value = 'Asia/Irkutsk'
  await state.save()
  assert.equal(state.editing.value, true)
  assert.equal(state.draft.value, 'Asia/Irkutsk')
  assert.equal(state.settings.value.timeZoneId, 'Asia/Novosibirsk')
  assert.match(state.error.value, /другой сотрудник/)
  assert.equal(events.some(event => event[0] === 'saved'), false)
  await state.save()
  assert.deepEqual(saves[1], ['org', 'Asia/Irkutsk', 'Asia/Novosibirsk'])
  assert.equal(state.editing.value, false)
  assert.equal(state.settings.value.timeZoneId, 'Asia/Irkutsk')
  assert.equal(events.filter(event => event[0] === 'saved').length, 1)
})

test('Time zone editor respects read-only access and cancellation never saves a setting', async () => {
  for (const options of [{ canManage: false }, { disabled: true }]) {
    const { state, saves } = await timeZoneEditor(options)
    state.startEdit()
    await state.save()
    assert.equal(state.editing.value, false)
    assert.equal(saves.length, 0)
  }
  const { state, saves } = await timeZoneEditor()
  state.startEdit()
  state.draft.value = 'Asia/Irkutsk'
  state.cancel()
  assert.equal(state.draft.value, 'Asia/Novosibirsk')
  assert.equal(saves.length, 0)
})


test('Journal month and week start from the organization day when its month differs from UTC', () => {
  class ClockDate extends Date {
    constructor(...args) { super(...(args.length > 1 ? [Date.UTC(...args)] : args.length ? args : ['2026-12-31T18:30:00Z'])) }
    getFullYear() { return this.getUTCFullYear() }
    getMonth() { return this.getUTCMonth() }
    getDate() { return this.getUTCDate() }
  }
  const dateModule = { exports: {} }
  new Function('Date', 'module', 'exports', transpile(readFileSync(new URL('../packages/ui/src/dateOnly.ts', import.meta.url), 'utf8')))(ClockDate, dateModule, dateModule.exports)
  const filename = new URL('../packages/workspace/src/components/MaintenanceJournalSection.vue', import.meta.url)
  const { descriptor } = parse(readFileSync(filename, 'utf8'), { filename: filename.pathname })
  const module = { exports: {} }
  new Function('require', 'module', 'exports', 'Date', 'window', transpile(compileScript(descriptor, { id: 'journal-time-zone-test' }).content))(id => {
    if (id === 'vue') return { defineComponent: options => options, ref: value => ({ value }), computed: getter => ({ get value() { return getter() } }), watch() {}, onMounted() {}, onBeforeUnmount() {} }
    if (id === '@opencrm/ui') return dateModule.exports
    if (id.endsWith('.vue') || id === '../api/maintenanceJournal') return {}
    throw new Error('Unexpected module ' + id)
  }, module, module.exports, ClockDate, { sessionStorage: { getItem: () => null } })
  const state = module.exports.default.setup({ context: { organization: { id: 'org', timeZoneId: 'Asia/Novosibirsk' }, user: { id: 'user' }, shell: {} } }, { expose() {} })
  assert.equal(state.dateFrom.value, '2027-01-01')
  assert.deepEqual(state.dateRange.value, { dateFrom: '2027-01-01', dateTo: '2027-01-01' })
  state.period.value = 'week'
  assert.deepEqual(state.dateRange.value, { dateFrom: '2026-12-26', dateTo: '2027-01-01' })
})

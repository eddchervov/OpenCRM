import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'

const pendingSettings = {
  hoursBefore: 24,
  channelCode: 'auto',
  template: '{organization}: запись {date} в {time}.',
  timeZoneId: 'Asia/Novosibirsk'
}
const pendingReminder = {
  enabled: true,
  version: 3,
  startsAtUtc: '2026-10-26T03:00:00Z',
  scheduledAtUtc: '2026-10-25T03:00:00Z',
  statusCode: 'scheduled',
  statusText: 'Напоминание запланировано.',
  channelCode: 'telegram',
  preview: 'Запись 26 октября в 10:00.',
  canEdit: true
}

function compileTypeScript(source, id) {
  return ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
}

function loadApi(fetch, getMutationHeaders = () => ({ 'X-Request-Token': 'token' })) {
  const source = readFileSync(new URL('../packages/workspace/src/api/visitReminders.ts', import.meta.url), 'utf8')
  const module = { exports: {} }
  new Function('require', 'module', 'exports', 'fetch', compileTypeScript(source, 'api'))(
    id => {
      assert.equal(id, './maintenance')
      return { getMutationHeaders }
    },
    module,
    module.exports,
    fetch
  )
  return module.exports
}

const compiledComponents = new Map()

function setupComponent(name, props, apiDependencies) {
  if (!compiledComponents.has(name)) {
    const filename = new URL(`../packages/workspace/src/components/${name}.vue`, import.meta.url)
    const { descriptor } = parse(readFileSync(filename, 'utf8'), { filename: filename.pathname })
    const script = compileScript(descriptor, { id: `visit-reminder-${name}` })
    compiledComponents.set(name, compileTypeScript(script.content, name))
  }

  const lifecycle = {
    defineComponent: options => options,
    onBeforeUnmount() {},
    ref: value => ({ value }),
    computed: getter => ({ get value() { return getter() } }),
    watch(source, callback, options = {}) {
      lifecycle.watcher = { source, callback }
      if (options.immediate) lifecycle.initialLoad = callback(source())
    }
  }
  const module = { exports: {} }
  const resolve = id => {
    if (id === 'vue') return lifecycle
    if (id === '@opencrm/ui') return { UiButton: {}, UiLoader: {}, UiSelect: {}, UiTextarea: {} }
    if (Object.hasOwn(apiDependencies, id)) return apiDependencies[id]
    throw new Error(`Не задана зависимость компонента: ${id}`)
  }
  new Function('require', 'module', 'exports', compiledComponents.get(name))(resolve, module, module.exports)
  const emitted = []
  const state = module.exports.default.setup(props, { emit: (...args) => emitted.push(args), expose() {} })
  return { state, emitted, lifecycle }
}

function deferred() {
  let resolve
  let reject
  const promise = new Promise((complete, fail) => { resolve = complete; reject = fail })
  return { promise, resolve, reject }
}

const settled = () => new Promise(resolve => setImmediate(resolve))

test('API передаёт общие antiforgery-заголовки и DTO сохранения', async () => {
  const calls = []
  const expectedSettings = { ...pendingSettings, hoursBefore: null }
  const api = loadApi(async (...args) => {
    calls.push(args)
    return { ok: true, status: 200, json: async () => expectedSettings }
  }, options => ({ ...options, RequestVerificationToken: 'shared-token' }))

  const saved = await api.saveVisitReminderSettings('org one', {
    hoursBefore: null,
    channelCode: 'both',
    template: 'Пример'
  })

  assert.deepEqual(saved, expectedSettings)
  assert.match(calls[0][0], /org%20one\/visit-reminder-settings/)
  assert.equal(calls[0][1].method, 'PUT')
  assert.equal(calls[0][1].headers.RequestVerificationToken, 'shared-token')
  assert.deepEqual(JSON.parse(calls[0][1].body), { hoursBefore: null, channelCode: 'both', template: 'Пример' })
})

test('API сохраняет серверную ошибку конфликта версии и корректно кодирует организацию в query', async () => {
  const api = loadApi(async url => {
    assert.match(url, /organizationId=org%20one/)
    return { ok: false, status: 409, json: async () => ({ message: 'Обновите запись.' }) }
  })

  await assert.rejects(api.saveMaintenanceVisitReminder('org one', 'maintenance', { enabled: false, version: 3 }), /Обновите запись/)
})

test('Настройки можно повторно загрузить после ошибки и нельзя изменять без права', async () => {
  let calls = 0
  const api = {
    loadVisitReminderSettings: async () => {
      if (++calls === 1) throw new Error('Сбой чтения')
      return pendingSettings
    },
    saveVisitReminderSettings: async () => { throw new Error('Не должно вызываться') }
  }
  const { state, lifecycle } = setupComponent('WorkspaceVisitReminderSettings', { organizationId: 'org', canWrite: false }, {
    '../api/visitReminders': api
  })

  await lifecycle.initialLoad
  assert.match(state.error.value, /Сбой чтения/)
  await state.load()
  assert.equal(state.settings.value, pendingSettings)
  state.template.value = 'Новый шаблон'
  await state.save()
  assert.equal(state.isSaving.value, false)
})

test('Настройки блокируют новое расписание без часового пояса, но разрешают выключить напоминания', async () => {
  let saved
  const { state, lifecycle } = setupComponent('WorkspaceVisitReminderSettings', { organizationId: 'org', canWrite: true }, {
    '../api/visitReminders': {
      loadVisitReminderSettings: async () => ({ ...pendingSettings, hoursBefore: null, timeZoneId: null }),
      saveVisitReminderSettings: async (_organizationId, settings) => {
        saved = settings
        return { ...pendingSettings, ...settings, timeZoneId: null }
      }
    }
  })
  await lifecycle.initialLoad

  state.hoursBefore.value = '24'
  assert.equal(state.canSave.value, false)
  state.hoursBefore.value = 'off'
  assert.equal(state.canSave.value, true)
  await state.save()
  assert.equal(saved.hoursBefore, null)
})

test('Предпросмотр напоминания в Free не читает и не изменяет платные данные', async () => {
  let calls = 0
  const props = { organizationId: 'org', maintenanceId: 'maintenance', previewOnly: true }
  const { state, lifecycle } = setupComponent('MaintenanceVisitReminder', props, {
    '../api/visitReminders': {
      loadMaintenanceVisitReminder: async () => { calls++; return pendingReminder },
      saveMaintenanceVisitReminder: async () => { calls++; return pendingReminder }
    }
  })
  await lifecycle.initialLoad
  state.reminder.value = pendingReminder
  await state.setEnabled(false)
  await state.load()
  assert.equal(calls, 0)
  assert.equal(state.reminder.value, null)
  assert.equal(state.error.value, '')
})

test('Переход в предпросмотр игнорирует запоздавшее напоминание PRO', async () => {
  const pending = deferred()
  const props = { organizationId: 'org', maintenanceId: 'maintenance', previewOnly: false }
  const { state, lifecycle } = setupComponent('MaintenanceVisitReminder', props, {
    '../api/visitReminders': { loadMaintenanceVisitReminder: () => pending.promise }
  })
  props.previewOnly = true
  await lifecycle.watcher.callback()
  pending.resolve(pendingReminder)
  await lifecycle.initialLoad
  assert.equal(state.reminder.value, null)
  assert.equal(state.isLoading.value, false)
})

test('Индивидуальное напоминание сохраняется с версией и показывает конфликт после обновления', async () => {
  let calls = 0
  const { state, lifecycle } = setupComponent('MaintenanceVisitReminder', { organizationId: 'org', maintenanceId: 'maintenance' }, {
    '../api/visitReminders': {
      loadMaintenanceVisitReminder: async () => pendingReminder,
      saveMaintenanceVisitReminder: async (_org, _maintenance, body) => {
        calls++
        assert.deepEqual(body, { enabled: false, version: 3 })
        throw new Error('Запись уже изменили. Обновите напоминание.')
      }
    }
  })
  await lifecycle.initialLoad
  await state.setEnabled(false)

  assert.equal(calls, 1)
  assert.equal(state.reminder.value, pendingReminder)
  assert.match(state.error.value, /Запись уже изменили/)
  assert.equal(state.isSaving.value, false)
})

test('Ответ прежней организации не затирает настройки после её смены', async () => {
  const first = deferred()
  const props = { organizationId: 'old', canWrite: true }
  const { state, lifecycle } = setupComponent('WorkspaceVisitReminderSettings', props, {
    '../api/visitReminders': {
      loadVisitReminderSettings: organizationId => organizationId === 'old' ? first.promise : Promise.resolve({ ...pendingSettings, template: 'Новая организация' }),
      saveVisitReminderSettings: async (_organizationId, value) => ({ ...pendingSettings, ...value })
    }
  })
  await settled()
  props.organizationId = 'new'
  const changedOrganization = lifecycle.watcher.callback('new', 'old')
  await changedOrganization
  first.resolve({ ...pendingSettings, template: 'Старая организация' })
  await settled()

  assert.equal(state.template.value, 'Новая организация')
  assert.equal(state.isLoading.value, false)
})

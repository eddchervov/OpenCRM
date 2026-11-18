import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import vm from 'node:vm'
import { assistantUiMessages } from './assistantUiTestMessages.mjs'

const source = await readFile(new URL('../../OpenCRM.WebApp/wwwroot/js/ai-pilot-telegram.js', import.meta.url), 'utf8')
const connected = {
  isConfigured: true, isBound: true, isConnected: true,
  telegramUserId: '7123456789', connectedAt: '2026-10-27T10:00:00Z',
  botUsername: 'test_bot', canViewOrganizationConnections: false
}
const settled = () => new Promise(resolve => setImmediate(resolve))

function telegram(request, locale = 'ru', culture = 'ru-RU') {
  const nodes = new Map()
  function element() {
    return {
      hidden: true, disabled: false, checked: false, open: false, textContent: '',
      listeners: new Map(), children: [], classes: new Set(),
      addEventListener(name, handler) { this.listeners.set(name, handler) },
      showModal() { this.open = true }, close() { this.open = false },
      append(...children) { this.children.push(...children) },
      appendChild(child) { this.children.push(child) },
      replaceChildren() { this.children = [] },
      removeAttribute(name) { delete this[name] }
    }
  }
  function node(selector) {
    if (!nodes.has(selector)) {
      const value = element()
      value.classList = { toggle: (name, active) => active ? value.classes.add(name) : value.classes.delete(name) }
      nodes.set(selector, value)
    }
    return nodes.get(selector)
  }
  const root = node('root')
  root.dataset = { organizationId: 'org-1', uiMessages: JSON.stringify(assistantUiMessages(locale)), formatCulture: culture, timeZoneId: 'Asia/Shanghai' }
  root.querySelector = node
  const sandbox = {
    window: { addEventListener() {} }, URLSearchParams, URL, Intl, Date,
    document: { addEventListener() {}, createElement: element }
  }
  vm.runInNewContext(source, sandbox)
  const manager = sandbox.window.OpenCRMAssistantTelegram.create(root, request)
  manager.setAvailability({ enabled: true, canConnect: true })
  return { root, manager, node }
}

test('Telegram показывает китайские состояния и английский региональный формат без изменения ID', async () => {
  const { root, node } = telegram(async () => connected, 'zh-Hans', 'en-GB')
  root.listeners.get('assistant:ready')()
  await settled()
  assert.equal(node('[data-telegram-connect]').textContent, 'Telegram · 已连接')
  assert.equal(node('[data-telegram-user-id]').textContent, connected.telegramUserId)
  assert.match(node('[data-telegram-connected-at]').textContent, /27\/10\/2026/)
  assert.match(node('[data-telegram-connected-at]').textContent, /18:00/)
})

test('Страница показывает существующий Telegram ID и дату подключения', async () => {
  const paths = []
  const { root, node } = telegram(async path => { paths.push(path); return connected })
  root.listeners.get('assistant:ready')()
  await settled()
  assert.equal(node('[data-telegram-connect]').textContent, 'Telegram · подключён')
  assert.equal(node('[data-telegram-user-id]').textContent, connected.telegramUserId)
  assert.notEqual(node('[data-telegram-connected-at]').textContent, '—')
  assert.equal(node('[data-telegram-binding]').hidden, false)
  assert.equal(node('[data-telegram-team]').hidden, true)
  assert.equal(paths.some(path => path.startsWith('telegram/connections?')), false)
})

test('Владелец видит список команды после открытия управления', async () => {
  const { root, node } = telegram(async path => path.startsWith('telegram/connections?')
    ? { items: [{ userName: 'Сотрудник', telegramUserId: '123456789', connectedAt: connected.connectedAt, isCurrentUser: false }], page: 1, pageSize: 20, totalCount: 1 }
    : { ...connected, canViewOrganizationConnections: true })
  root.listeners.get('assistant:ready')()
  await settled()
  node('[data-telegram-connect]').listeners.get('click')()
  await settled()
  assert.equal(node('[data-telegram-dialog]').open, true)
  assert.equal(node('[data-telegram-team]').hidden, false)
  assert.equal(node('[data-telegram-feedback]').classes.has('is-error'), false, node('[data-telegram-feedback]').textContent)
  assert.equal(node('[data-telegram-team-list]').children[0].children[0].textContent, 'Сотрудник')
  assert.equal(node('[data-telegram-team-list]').children[0].children[1].textContent, 'Telegram ID: 123456789')
})

test('Привязка к другой организации остаётся видимой владельцу аккаунта', async () => {
  const { root, node } = telegram(async () => ({ ...connected, isConnected: false, organizationId: 'another-org' }))
  root.listeners.get('assistant:ready')()
  await settled()
  assert.equal(node('[data-telegram-binding]').hidden, false)
  assert.equal(node('[data-telegram-disconnect-section]').hidden, false)
  assert.match(node('[data-telegram-selection]').textContent, /другая организация/)
})

test('Отключение требует подтверждения и отправляет только собственную команду', async () => {
  let isBound = true
  const deletions = []
  const { root, node } = telegram(async (path, options) => {
    if (options?.method === 'DELETE') {
      deletions.push({ path, options })
      isBound = false
      return {}
    }
    return { ...connected, isBound }
  })
  root.listeners.get('assistant:ready')()
  await settled()
  await node('[data-telegram-disconnect]').listeners.get('click')()
  assert.equal(deletions.length, 0)
  node('[data-telegram-disconnect-confirm]').checked = true
  await node('[data-telegram-disconnect]').listeners.get('click')()
  assert.equal(deletions.length, 1)
  assert.equal(deletions[0].path, 'telegram/binding?organizationId=org-1')
  assert.equal(deletions[0].options.body, undefined)
  assert.equal(node('[data-telegram-binding]').hidden, true)
})

test('Ссылка подключения не может вести на посторонний сайт', async () => {
  const { root, node } = telegram(async (path, options) => options?.method === 'POST'
    ? { url: 'https://example.test/steal', expiresAt: connected.connectedAt }
    : connected)
  root.listeners.get('assistant:ready')()
  await settled()
  await node('[data-telegram-create-link]').listeners.get('click')()
  assert.equal(node('[data-telegram-link-panel]').hidden, true)
  assert.equal(node('[data-telegram-link]').href, undefined)
  assert.equal(node('[data-telegram-feedback]').classes.has('is-error'), true)
})

test('Ошибка отключения не подменяет действующую привязку успешным состоянием', async () => {
  const { root, node } = telegram(async (path, options) => {
    if (options?.method === 'DELETE') throw new Error('Сервер недоступен')
    return connected
  })
  root.listeners.get('assistant:ready')()
  await settled()
  node('[data-telegram-disconnect-confirm]').checked = true
  await node('[data-telegram-disconnect]').listeners.get('click')()
  assert.equal(node('[data-telegram-binding]').hidden, false)
  assert.equal(node('[data-telegram-feedback]').textContent, 'Сервер недоступен')
  assert.equal(node('[data-telegram-feedback]').classes.has('is-error'), true)
})

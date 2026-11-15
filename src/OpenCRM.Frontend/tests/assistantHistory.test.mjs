import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import vm from 'node:vm'

const source = await readFile(new URL('../../OpenCRM.WebApp/wwwroot/js/ai-pilot-dashboard.js', import.meta.url), 'utf8')
const settled = () => new Promise(resolve => setImmediate(resolve))

function dashboard(totalConversations, totalMessages) {
  const nodes = new Map()
  const requests = []
  function element() {
    return {
      dataset: {}, hidden: false, disabled: false, value: '', children: [], listeners: new Map(),
      classList: { toggle() {} },
      addEventListener(name, handler) { this.listeners.set(name, handler) },
      dispatchEvent(event) { this.listeners.get(event.type)?.(event) },
      setAttribute(name, value) { this[name] = value },
      replaceChildren() { this.children = [] },
      append(...children) { this.children.push(...children) },
      appendChild(child) { this.children.push(child) },
      querySelectorAll() { return [] }
    }
  }
  function node(selector) {
    if (!nodes.has(selector)) nodes.set(selector, element())
    return nodes.get(selector)
  }
  const root = node('root')
  root.dataset = { apiUrl: '/api/v1/assistant', organizationId: 'org-1' }
  root.querySelector = node
  root.querySelectorAll = selector => selector === '.history-pagination button'
    ? [...nodes.values()].flatMap(value => value.children).filter(value => value.type === 'button')
    : []
  vm.runInNewContext(source, {
    document: { querySelector: () => root, createElement: element },
    URLSearchParams, Intl, Date, Event,
    fetch: async path => {
      requests.push(path)
      const url = new URL(path, 'https://localhost')
      const page = Number(url.searchParams.get('page'))
      const isConversation = url.pathname.endsWith('/conversation-1')
      return { ok: true, json: async () => isConversation
        ? { page, pageSize: 2, totalCount: totalMessages, messages: [{ role: 'user', text: 'Тест' }] }
        : { page, pageSize: 2, totalCount: totalConversations, items: [{ id: 'conversation-1', userName: 'Сотрудник' }] } }
    }
  })
  root.listeners.get('assistant:ready')()
  return { node, requests }
}

test('Навигация скрыта для единственной страницы разговоров и сообщений', async () => {
  const { node } = dashboard(1, 1)
  node('[data-history-refresh]').listeners.get('click')()
  await settled()
  assert.equal(node('[data-history-pagination]').hidden, true)
  assert.equal(node('[data-history-message-pagination]').hidden, true)
  assert.equal(node('[data-history-status]').hidden, true)
})

test('Номер страницы загружает другую страницу списка, отдельно от сообщений', async () => {
  const { node, requests } = dashboard(6, 1)
  node('[data-history-refresh]').listeners.get('click')()
  await settled()
  assert.equal(node('[data-history-pagination]').hidden, false)
  assert.equal(node('[data-history-message-pagination]').hidden, true)
  const second = node('[data-history-page]').children[1]
  assert.equal(second.textContent, '2')
  second.listeners.get('click')({ currentTarget: second })
  await settled()
  assert.ok(requests.includes('/api/v1/assistant/history?organizationId=org-1&page=2'))
  assert.equal(node('[data-history-page]').children[1]['aria-current'], 'page')
})

test('Страницы сообщений переключаются без смены страницы списка', async () => {
  const { node, requests } = dashboard(1, 5)
  node('[data-history-refresh]').listeners.get('click')()
  await settled()
  assert.equal(node('[data-history-pagination]').hidden, true)
  assert.equal(node('[data-history-message-pagination]').hidden, false)
  const third = node('[data-history-message-page]').children[2]
  third.listeners.get('click')({ currentTarget: third })
  await settled()
  assert.ok(requests.includes('/api/v1/assistant/history/conversation-1?organizationId=org-1&page=3'))
  assert.equal(requests.filter(path => path.includes('/history?')).length, 1)
})

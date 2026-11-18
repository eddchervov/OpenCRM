import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { runInNewContext, Script } from 'node:vm'
import { legacyClientText } from './legacyClientTestMessages.mjs'

const webRoot = new URL('../../OpenCRM.WebApp/', import.meta.url)

test('Общие скрипты компилируются и используют существующие переводы на всех языках', () => {
  for (const file of ['site.js', 'layout-utils.js', 'email-verification.js', 'site-notifications.js', 'email-validation-ru.js', 'organizations-entry.js', 'payment-complete.js']) {
    const source = readFileSync(new URL('wwwroot/js/' + file, webRoot), 'utf8')
    assert.doesNotThrow(() => new Script(source, { filename: file }))
    const keys = [...source.matchAll(/OpenCRMText\('([^']+)'/g)].map(match => match[1])
    for (const locale of ['ru', 'en', 'zh-Hans']) {
      const translate = legacyClientText(locale)
      for (const key of keys) assert.notEqual(translate(key), key, `${locale}: ${file}: ${key}`)
    }
  }
})

test('Проверка email переводит подсказку и сохраняет введённый адрес', () => {
  const handlers = {}
  let validation = ''
  const input = {
    value: 'fictional.@example.invalid',
    addEventListener: (event, handler) => { handlers[event] = handler },
    setCustomValidity: message => { validation = message }
  }
  runInNewContext(readFileSync(new URL('wwwroot/js/email-validation-ru.js', webRoot), 'utf8'), {
    window: { OpenCRMText: legacyClientText('zh-Hans') },
    document: { querySelectorAll: () => [input] }
  })
  handlers.invalid()
  assert.equal(validation, '@ 前不能有点。您是否想输入“fictional@example.invalid”？')
  assert.equal(input.value, 'fictional.@example.invalid')
  handlers.input()
  assert.equal(validation, '')
  input.value = 'fictional'
  handlers.invalid()
  assert.match(validation, /contact@example.invalid/)
})

test('История уведомлений переводит действия и даты, сохраняя опубликованный текст', async () => {
  const makeNode = () => ({ children: [], dataset: {}, appendChild(child) { this.children.push(child) }, replaceChildren() { this.children = [] } })
  const history = makeNode()
  const source = readFileSync(new URL('wwwroot/js/site-notifications.js', webRoot), 'utf8')
  runInNewContext(source, {
    window: { OpenCRMText: legacyClientText('zh-Hans'), setInterval() {} },
    document: {
      querySelector: selector => selector === '[data-site-notification-history]' ? history : selector.startsWith('meta') ? { content: 'en-GB' } : null,
      createElement: makeNode,
      addEventListener() {}
    },
    fetch: async () => ({ ok: true, json: async () => [{ id: 'notice-1', title: 'Сохранённое объявление', body: '用户内容', createdAt: '2026-11-16T10:30:00Z' }] })
  })
  await new Promise(resolve => setImmediate(resolve))
  const item = history.children[0]
  assert.equal(item.children[0].textContent, 'Сохранённое объявление')
  assert.equal(item.children[1].textContent, '用户内容')
  assert.match(item.children[2].children[0].textContent, /16 November 2026/)
  assert.equal(item.children[2].children[1].textContent, '关闭')
})

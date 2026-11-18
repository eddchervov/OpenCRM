import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import { legacyClientText } from './legacyClientTestMessages.mjs'

const webRoot = new URL('../../OpenCRM.WebApp/', import.meta.url)
const view = readFileSync(new URL('Views/Auth/VerifyEmail.cshtml', webRoot), 'utf8')
const siteScript = readFileSync(new URL('wwwroot/js/site.js', webRoot), 'utf8')
const verificationScript = readFileSync(new URL('wwwroot/js/email-verification.js', webRoot), 'utf8')

function createPage(retryAfter = 0, locale = 'ru') {
  const formTag = view.match(/<form[^>]*data-email-resend[^>]*>/)[0]
  const documentHandlers = []
  const formHandlers = []
  const intervals = new Set()
  let now = 0
  const button = {
    dataset: { retryAfter: String(retryAfter) },
    disabled: retryAfter > 0,
    tagName: 'BUTTON',
    innerHTML: 'Отправить письмо повторно',
    classList: { add() {}, remove() {} },
    getAttribute() { return null },
  }
  const form = {
    tagName: 'FORM',
    dataset: {},
    getAttribute(name) { return name === 'method' ? 'post' : '/auth/verify-email/resend' },
    hasAttribute(name) { return formTag.includes(name) },
    querySelector(selector) { return selector.startsWith('button') ? button : {} },
    querySelectorAll(selector) { return selector.startsWith('button') ? [button] : [] },
    checkValidity() { return true },
    addEventListener(type, handler) { if (type === 'submit') formHandlers.push(handler) },
  }
  const document = {
    querySelector(selector) { return selector === '[data-email-resend]' ? form : null },
    addEventListener(type, handler, capture) {
      if (type === 'submit') documentHandlers.push({ handler, capture })
    },
  }
  const window = {
    OpenCRMText: legacyClientText(locale),
    addEventListener() {},
    setTimeout() {},
    setInterval(handler) { intervals.add(handler); return handler },
    clearInterval(handler) { intervals.delete(handler) },
  }
  const context = { document, window, Date: { now: () => now } }
  runInNewContext(siteScript, context)
  runInNewContext(verificationScript, context)

  return {
    button,
    submit() {
      const event = { target: form, submitter: button, defaultPrevented: false,
        preventDefault() { this.defaultPrevented = true } }
      documentHandlers.filter(item => item.capture).forEach(item => item.handler(event))
      formHandlers.forEach(handler => handler(event))
      documentHandlers.filter(item => !item.capture).forEach(item => item.handler(event))
      return event
    },
    advance(seconds) {
      now += seconds * 1000
      for (const handler of intervals) handler()
    },
  }
}

test('Повторная отправка письма использует язык страницы и подставляет весь срок ожидания', () => {
  const page = createPage(60, 'zh-Hans')
  assert.equal(page.button.textContent, '60 秒后重新发送')
  page.advance(60)
  assert.equal(page.button.textContent, '重新发送邮件')
  assert.equal(page.submit().defaultPrevented, false)
  assert.equal(page.button.textContent, '正在发送…')
})

test('resend submits with the shared loader installed and blocks a second submission', () => {
  const page = createPage()
  assert.equal(page.submit().defaultPrevented, false)
  assert.equal(page.button.disabled, true)
  assert.equal(page.button.textContent, 'Отправляем…')
  assert.equal(page.submit().defaultPrevented, true)
})

test('server cooldown prevents sending until the countdown expires', () => {
  const page = createPage(60)
  assert.equal(page.button.textContent, 'Отправить повторно через 60 с')
  assert.equal(page.submit().defaultPrevented, true)
  page.advance(1)
  assert.equal(page.button.textContent, 'Отправить повторно через 59 с')
  page.advance(59)
  assert.equal(page.button.disabled, false)
  assert.equal(page.button.textContent, 'Отправить письмо повторно')
  assert.equal(page.submit().defaultPrevented, false)
})

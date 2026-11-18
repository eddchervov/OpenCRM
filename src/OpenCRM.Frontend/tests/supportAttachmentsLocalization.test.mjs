import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import vm from 'node:vm'

const webRoot = new URL('../../OpenCRM.WebApp/', import.meta.url)
const source = readFileSync(new URL('wwwroot/js/support-attachments.js', webRoot), 'utf8')
const partial = readFileSync(new URL('Views/Shared/_SupportAttachments.cshtml', webRoot), 'utf8')

function attachments(language = 'en-US', culture = 'en-GB') {
  const xml = readFileSync(new URL(`Resources/Infrastructure/Localization/LocalizationStrings.${language}.resx`, webRoot), 'utf8')
  const resources = new Map([...xml.matchAll(/<data name="([^"]+)"[^>]*>\s*<value>([\s\S]*?)<\/value>/g)]
    .map(([, key, value]) => [key, value.replaceAll('&quot;', '"').replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&amp;', '&')]))
  const dataset = { formattingCulture: culture }
  for (const [, attribute, key] of partial.matchAll(/data-([\w-]+)="@Text\["([^"]+)"\]"/g)) {
    dataset[attribute.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase())] = resources.get(key)
  }

  const element = () => ({
    children: [], listeners: {},
    append(...children) { this.children.push(...children) },
    replaceChildren() { this.children = [] },
    setAttribute() {},
    addEventListener(type, callback) { this.listeners[type] = callback }
  })
  const input = { ...element(), files: [], closest: () => ({ dataset }) }
  const list = element()
  const status = element()
  const elements = { 'support-attachments': input, 'support-attachments-list': list, 'support-attachments-status': status }
  class DataTransfer {
    files = []
    items = { add: file => this.files.push(file) }
  }
  vm.runInNewContext(source, {
    Intl, DataTransfer,
    document: { getElementById: id => elements[id], createElement: element }
  })

  return {
    input, list, status, dataset,
    select(files) { input.files = files; input.listeners.change() }
  }
}

const file = (name, size = 1) => ({ name, size, type: 'text/plain' })
const megabyte = 1024 * 1024

test('Перевод не меняет лимиты пяти вложений и двадцати мегабайт', () => {
  const count = attachments()
  count.select(Array.from({ length: 6 }, (_, index) => file(`${index}.txt`)))
  assert.equal(count.input.files.length, 5)
  assert.equal(count.status.textContent, count.dataset.errorFileCount)

  const size = attachments()
  size.select([file('1.txt', 10 * megabyte), file('2.txt', 10 * megabyte), file('3.txt')])
  assert.equal(size.input.files.length, 2)
  assert.equal(size.status.textContent, size.dataset.errorTotalSize)
})

test('Китайское имя файла подставляется в сообщение без потери символов', () => {
  const state = attachments('zh-Hans', 'zh-CN')
  const name = '车辆<test>.txt'
  state.select([file(name, 11 * megabyte)])
  assert.equal(state.input.files.length, 0)
  assert.equal(state.status.textContent, state.dataset.errorFileSize.replace('{fileName}', name))
  assert.doesNotMatch(state.status.textContent, /\{fileName\}|undefined/)
})

test('Размер использует региональный формат отдельно от языка подписей', () => {
  const state = attachments('en-US', 'ru-RU')
  state.select([file('test.txt', 1.5 * megabyte)])
  assert.equal(state.list.children[0].children[2].textContent, '1,5 MB')
})

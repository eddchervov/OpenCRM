import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

function api(channel, fetch) {
  const source = readFileSync(new URL(`../packages/workspace/src/api/${channel}Notifications.ts`, import.meta.url), 'utf8')
  const code = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  new Function('module', 'exports', 'fetch', 'document', code)(module, module.exports, fetch, { querySelector: () => null })
  return module.exports
}

test('Сетевая ошибка проверки бота понятна пользователю; следующий запрос может восстановиться', async () => {
  for (const [channel, provider] of [['telegram', 'Telegram'], ['max', 'Max']]) {
    let count = 0
    const client = api(channel, async () => {
      if (++count === 1) throw new TypeError('Failed to fetch')
      return { ok: true, status: 200, json: async () => ({ isClientConnected: true }) }
    })
    await assert.rejects(client[`load${provider}ClientStatus`]('org', 'client'), /Не удалось связаться с CRM/)
    assert.equal((await client[`load${provider}ClientStatus`]('org', 'client')).isClientConnected, true)
    assert.equal(count, 2)
  }
})

test('Создание ссылки не повторяется автоматически при потере соединения', async () => {
  for (const [channel, provider] of [['telegram', 'Telegram'], ['max', 'Max']]) {
    const calls = []
    const client = api(channel, async (...args) => { calls.push(args); throw new TypeError('Failed to fetch') })
    await assert.rejects(client[`create${provider}ClientLink`]('org', 'client'), /Не удалось связаться с CRM/)
    assert.equal(calls.length, 1)
    assert.equal(calls[0][1].method, 'POST')
  }
})

test('Ответы сервера о правах сохраняют своё сообщение', async () => {
  for (const [channel, provider] of [['telegram', 'Telegram'], ['max', 'Max']]) {
    const client = api(channel, async () => ({ ok: false, status: 403, json: async () => ({ message: 'Нет права редактировать клиента.' }) }))
    await assert.rejects(client[`load${provider}ClientStatus`]('org', 'client'), /Нет права редактировать клиента/)
  }
})

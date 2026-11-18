import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { fileURLToPath } from 'node:url'
import { i18n, translate } from '@opencrm/ui/i18n'
import '../packages/workspace/src/shiftMessages.ts'

const sectionSource = readFileSync(new URL('../packages/workspace/src/components/WorkspaceShiftsSection.vue', import.meta.url), 'utf8')
const loadSource = readFileSync(new URL('../packages/workspace/src/components/WorkspaceShiftLoadSection.vue', import.meta.url), 'utf8')
const apiSource = ts.transpileModule(readFileSync(new URL('../packages/workspace/src/api/shifts.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const apiModule = { exports: {} }
new Function('require', 'module', 'exports', apiSource)(id => {
  if (id === '@opencrm/ui/i18n') return { translate }
  if (id === '../shiftMessages') return {}
  throw new Error(`Неизвестная зависимость: ${id}`)
}, apiModule, apiModule.exports)
const { loadShiftWeek } = apiModule.exports

test('Все сообщения интерфейса смен доступны на русском, английском и китайском', () => {
  for (const namespace of ['shifts', 'shiftLoad', 'shiftsApi']) {
    const baseMessages = i18n.global.getLocaleMessage('ru')[namespace]
    for (const locale of ['ru', 'en', 'zh-Hans']) {
      assert.deepEqual(
        Object.keys(i18n.global.getLocaleMessage(locale)[namespace]).sort(),
        Object.keys(baseMessages).sort(),
        `${locale}.${namespace} has full coverage`
      )
    }
  }

  const usedKeys = new Set([...`${sectionSource}\n${loadSource}`.matchAll(/t\(['"]([^'"]+)['"]/g)].map(match => match[1]))
  for (const key of usedKeys) {
    const [namespace, name] = key.split('.')
    for (const locale of ['ru', 'en', 'zh-Hans']) {
      assert.ok(i18n.global.getLocaleMessage(locale)[namespace]?.[name], `${locale}.${key}`)
    }
  }
})

test('Смены сохраняют civil-даты и локализуют fallback API', async () => {
  assert.match(sectionSource, /formattingCulture\.value/)
  assert.match(sectionSource, /if \(props\.previewOnly\) return/)
  assert.match(sectionSource, /formatDateOnly\(undefined, props\.context\.organization\.timeZoneId \|\| 'UTC'\)/)
  assert.match(sectionSource, /combineShiftLocalDateTime\(startDate\.value, startTime\.value\)/)
  assert.match(loadSource, /localTimestamp\(value: string\)/)

  const originalFetch = globalThis.fetch
  globalThis.fetch = async () => ({ ok: false, json: async () => null })
  try {
    for (const locale of ['ru', 'en', 'zh-Hans']) {
      i18n.global.locale.value = locale
      await assert.rejects(loadShiftWeek('org', '2026-10-26'), { message: translate('shiftsApi.actionError') })
    }
  } finally {
    globalThis.fetch = originalFetch
    i18n.global.locale.value = 'ru'
  }
})

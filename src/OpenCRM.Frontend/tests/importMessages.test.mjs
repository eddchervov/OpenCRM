import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { i18n } from '../packages/ui/src/i18n.ts'
import { importMessage } from '../packages/workspace/src/importMessages.ts'

test('import messages have the same keys in Russian, English, and Simplified Chinese', () => {
  const source = readFileSync(new URL('../packages/workspace/src/components/WorkspaceImportSection.vue', import.meta.url), 'utf8')
  const usedKeys = new Set([...source.matchAll(/importMessage\(['"]([^'"]+)['"]/g)].map(match => match[1]))
  const fieldKeys = new Set([...source.matchAll(/label: '([^']+)'/g)].map(match => match[1]))
  const resource = i18n.global.getLocaleMessage('ru').importSection

  assert.ok(resource)
  for (const locale of ['ru', 'en', 'zh-Hans']) {
    const messages = i18n.global.getLocaleMessage(locale).importSection
    assert.deepEqual(Object.keys(messages).sort(), Object.keys(resource).sort(), `${locale} has full coverage`)
    for (const key of [...usedKeys, ...fieldKeys]) assert.ok(messages[key], `${locale} includes ${key}`)
  }
})

test('import status errors and dynamic confirmations are localized as complete messages', () => {
  const previousLocale = i18n.global.locale.value
  try {
    i18n.global.locale.value = 'en'
    assert.equal(importMessage('apiLoadImports'), 'Could not load organization imports.')
    assert.equal(importMessage('rowImportedLocked', { row: 17 }), 'Row 17 has already been imported and cannot be edited. The paste was cancelled.')
    assert.equal(importMessage('loseChangesBeforeUpload', { file: 'source.csv' }), 'There are unsaved changes. Upload “source.csv” and discard them?')
    i18n.global.locale.value = 'zh-Hans'
    assert.equal(importMessage('apiLoadImports'), '无法加载组织导入记录。')
  } finally {
    i18n.global.locale.value = previousLocale
  }
})

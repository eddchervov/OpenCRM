import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const source = readFileSync(new URL('../packages/ui/src/i18n.ts', import.meta.url), 'utf8')

test('shared locale registry contains Russian, English, and simplified Chinese', () => {
  assert.match(source, /ru: 'ru-RU'/)
  assert.match(source, /en: 'en-US'/)
  assert.match(source, /'zh-Hans': 'zh-CN'/)
  assert.match(source, /fallbackLocale: 'ru'/)
})

test('locale switch posts the antiforgery token and local current URL', () => {
  assert.match(source, /form\.action = `\$\{pathBase\}\/localization\/language`/)
  assert.match(source, /method = 'post'/)
  assert.match(source, /requestVerificationToken/)
  assert.match(source, /returnUrl: `\$\{window\.location\.pathname\}\$\{window\.location\.search\}\$\{window\.location\.hash\}`/)
  assert.match(source, /formattingCulture: selectedFormattingCulture/)
})

test('formatters accept an explicit display culture and currency code', () => {
  assert.match(source, /formatLocaleDate\(value: Date \| number \| string, options: Intl\.DateTimeFormatOptions = \{\}, culture = formattingCulture\.value\)/)
  assert.match(source, /formatLocaleNumber\(value: number, options: Intl\.NumberFormatOptions = \{\}, culture = formattingCulture\.value\)/)
  assert.match(source, /formatLocaleCurrency\(value: number, currency: string, options: Intl\.NumberFormatOptions = \{\}, culture = formattingCulture\.value\)/)
})

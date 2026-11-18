import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { parse } from '@vue/compiler-sfc'
import ts from 'typescript'

const source = readFileSync(new URL('../Admin/admin-client/src/views/UserOverviewView.vue', import.meta.url), 'utf8')
const { descriptor } = parse(source)
const script = ts.createSourceFile('overview.ts', descriptor.scriptSetup.content, ts.ScriptTarget.Latest, true)
const names = new Set(['formatMoney', 'formatMaintenanceDate'])
const functions = script.statements.filter(statement => ts.isFunctionDeclaration(statement) && names.has(statement.name?.text))
const code = ts.transpileModule(functions.map(statement => statement.getText(script)).join('\n') + '\nexport { formatMoney, formatMaintenanceDate }', {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function createFormatters(culture, timeZone = 'America/Los_Angeles') {
  const module = { exports: {} }
  const regionalIntl = {
    NumberFormat: Intl.NumberFormat,
    DateTimeFormat: class extends Intl.DateTimeFormat {
      constructor(locale, options) { super(locale, { timeZone, ...options }) }
    }
  }
  new Function('module', 'exports', 'formattingCulture', 'Intl', code)(module, module.exports, { value: culture }, regionalIntl)

  return module.exports
}

test('Админская карточка сохраняет код валюты и копейки обслуживания, коммерческие платежи остаются RUB', () => {
  const { formatMoney } = createFormatters('en-GB')

  assert.match(formatMoney(1234.56, 'CNY'), /CNY/)
  assert.match(formatMoney(1234.56, 'CNY'), /1,234\.56/)
  assert.match(formatMoney(10.25), /RUB/)
  assert.match(formatMoney(10.25), /10\.25/)
  assert.match(formatMoney(10), /10\.00/)
  assert.equal(formatMoney(null, 'CNY'), '—')
  assert.match(descriptor.template.content, /formatMoney\(maintenance\.paidAmount, maintenance\.currencyCode\)/)
  assert.match(descriptor.template.content, /formatMoney\(payment\.amount\)/)
})

test('Календарная дата обслуживания не сдвигается из-за пояса устройства администратора', () => {
  for (const zone of ['America/Los_Angeles', 'Asia/Shanghai']) {
    const { formatMaintenanceDate } = createFormatters('en-GB', zone)
    assert.equal(formatMaintenanceDate('2026-11-16T00:00:00Z'), '16 Nov 2026')
    assert.equal(formatMaintenanceDate('2026-11-16'), '16 Nov 2026')
    assert.equal(formatMaintenanceDate(null), '—')
  }
  assert.equal(createFormatters('zh-CN').formatMaintenanceDate('2026-11-16T00:00:00Z'), '2026年11月16日')
  assert.equal((descriptor.template.content.match(/formatMaintenanceDate\(maintenance\.date\)/g) ?? []).length, 2)
})

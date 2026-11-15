import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { getProjectBrand } from '../packages/ui/src/projectBrand.ts'

const catalogPath = new URL('../packages/workspace/src/proFeatureDetails.ts', import.meta.url)
const source = readFileSync(catalogPath, 'utf8')
const code = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const catalogModule = { exports: {} }
// Resolver понимает только чистый helper-модуль, Vue-barrel не загружается.
const requireResolver = id => {
  if (id === '@opencrm/ui/projectBrand') return { getProjectBrand }
  throw new Error(`Неизвестная зависимость ${id}`)
}
new Function('require', 'module', 'exports', code)(requireResolver, catalogModule, catalogModule.exports)
const { proFeatureDetails } = catalogModule.exports

test('Каталог содержит описание и от трёх до четырёх конкретных пунктов для каждой PRO-возможности', () => {
  assert.deepEqual(Object.keys(proFeatureDetails), [
    'team', 'shifts', 'statistics', 'messages', 'intake', 'albums', 'notifications',
    'messageTemplates', 'selfAssignment', 'documentStyling', 'dataImport', 'accountingExport',
    'assistant', 'myWork', 'roles'
  ])

  for (const [key, detail] of Object.entries(proFeatureDetails)) {
    assert.ok(detail.title, `${key}: задан заголовок`)
    assert.ok(detail.description, `${key}: задана польза`)
    assert.ok(detail.points.length >= 3 && detail.points.length <= 4, `${key}: указано от трёх до четырёх пунктов`)
    assert.ok(detail.visual, `${key}: задан пример`)
    if (detail.visual.type === 'demo') {
      assert.ok(detail.visual.rows.length >= 3, `${key}: демонстрация содержит примеры`)
    }
  }
})

test('Скриншоты каталога содержат alt-текст и область кадрирования', () => {
  for (const key of ['team', 'statistics', 'intake']) {
    const visual = proFeatureDetails[key].visual
    assert.equal(visual.type, 'screenshot')
    assert.ok(visual.alt)
    assert.ok(visual.viewBox)
    assert.ok(visual.imageWidth > 0 && visual.imageHeight > 0)
  }
})

test('Описание помощника обозначает бета-версию, голос, Telegram и условия использования', () => {
  const detail = proFeatureDetails.assistant
  assert.equal(detail.beta, true)
  assert.match(detail.description, /Telegram-бота/)
  assert.match(detail.description, /диктуйте команды и слушайте ответы/)
  assert.match(detail.note, /включается администратором/)
  assert.match(detail.note, /отдельном бюджете/)
  assert.ok(detail.points.some(point => point.includes('проверки и подтверждения')))
})

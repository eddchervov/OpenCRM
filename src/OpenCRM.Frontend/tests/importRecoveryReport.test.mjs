import assert from 'node:assert/strict'
import test from 'node:test'
import { buildImportRecoveryReport } from '../packages/workspace/src/importRecoveryReport.ts'

test('Отчёт сохраняет локальные значения, версию и сопоставление столбцов', () => {
  const csv = buildImportRecoveryReport({
    draftId: 'draft', revision: 7,
    rowEdits: [{ id: 'row-4', changes: { comment: 'Анна "Тест"\nВторая строка', excluded: false, existingClientId: null } }],
    mappings: [{ tableId: 'sheet', kind: 'combined', fields: { clientName: [0, 2], clientPhone: [] } }]
  })
  assert.ok(csv.startsWith('\uFEFF'))
  assert.ok(csv.includes('"draft";"7";"Строка";"row-4";"comment";"Анна ""Тест""\nВторая строка"'))
  assert.ok(csv.includes('"excluded";"false"'))
  assert.ok(csv.includes('"existingClientId";""'))
  assert.ok(csv.includes('"Сопоставление";"sheet";"clientName";"1, 3"'))
  assert.ok(csv.includes('"clientPhone";""'))
})

test('Значения, похожие на формулы, экспортируются как текст', () => {
  for (const value of ['=HYPERLINK("url")', ' +123', '-123', '@SUM(A1)', '\ttext', '\rtext']) {
    const csv = buildImportRecoveryReport({ draftId: 'draft', revision: 1, rowEdits: [{ id: 'row', changes: { comment: value } }] })
    assert.ok(csv.includes(`"'${value.replaceAll('"', '""')}"`))
  }
})

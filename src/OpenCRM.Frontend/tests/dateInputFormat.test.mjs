import test from 'node:test'
import assert from 'node:assert/strict'
import { getDateInputMasks, isImpossibleDateInput } from '../packages/ui/src/dateInputFormat.ts'

test('Региональный формат определяет порядок дня и месяца независимо от языка', () => {
  assert.equal(getDateInputMasks('ru-RU').input, 'DD.MM.YYYY')
  assert.equal(getDateInputMasks('en-US').input, 'MM/DD/YYYY')
  assert.equal(getDateInputMasks('en-GB').input, 'DD/MM/YYYY')
  assert.equal(getDateInputMasks('zh-CN').inputDateTime24hr, 'YYYY/MM/DD HH:mm')
  assert.equal(isImpossibleDateInput('12/31/2026', 'en-US'), false)
  assert.equal(isImpossibleDateInput('31/12/2026', 'en-GB'), false)
  assert.equal(isImpossibleDateInput('31/12/2026', 'en-US'), true)
  assert.equal(isImpossibleDateInput('12/31/2026', 'en-GB'), true)
})

test('Ручной ввод отклоняет невозможные даты, сохраняя високосные годы', () => {
  for (const [culture, invalid, valid] of [
    ['ru-RU', '29.02.2026', '29.02.2024'],
    ['en-US', '02/29/2026', '02/29/2024'],
    ['en-GB', '29/02/2026', '29/02/2024'],
    ['zh-CN', '2026/02/29', '2024/02/29']
  ]) {
    assert.equal(isImpossibleDateInput(invalid, culture), true)
    assert.equal(isImpossibleDateInput(valid, culture), false)
  }
  assert.equal(isImpossibleDateInput('2026/04/31 12:30', 'zh-CN'), true)
})

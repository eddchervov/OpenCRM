import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse } from '@vue/compiler-sfc'
import { combineShiftLocalDateTime, splitShiftLocalDateTime } from '../packages/workspace/src/shiftLocalDateTime.ts'

test('Смена раскладывает локальную дату и время без перевода часового пояса', () => {
  const parts = splitShiftLocalDateTime('2026-10-30T09:00:00')

  assert.deepEqual(parts, { date: '2026-10-30', time: '09:00' })
  assert.equal(combineShiftLocalDateTime(parts.date, parts.time), '2026-10-30T09:00')
})

test('Пустые и неожиданные значения не становятся датой смены', () => {
  assert.deepEqual(splitShiftLocalDateTime(''), { date: '', time: '' })
  assert.deepEqual(splitShiftLocalDateTime('2026-10-30'), { date: '', time: '' })
  assert.equal(combineShiftLocalDateTime('2026-10-30', ''), '')
})

test('Форма смен использует общие поля даты и времени', () => {
  const source = readFileSync(new URL('../packages/workspace/src/components/WorkspaceShiftsSection.vue', import.meta.url), 'utf8')
  const { descriptor } = parse(source)

  assert.match(descriptor.scriptSetup.content, /UiDatePicker/)
  assert.match(descriptor.scriptSetup.content, /UiTimePicker/)
  assert.equal(source.includes('type="datetime-local"'), false)
})

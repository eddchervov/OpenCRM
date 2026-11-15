import test from 'node:test'
import assert from 'node:assert/strict'
import { inquiryLocalTime, inquiryUtcTime, resolveInquiryTimeZone, formatInquiryTime } from '../packages/workspace/src/utils/inquiryTime.ts'

test('Срок обращения сохраняется в часовом поясе организации независимо от часового пояса браузера', () => {
  const utc = inquiryUtcTime('2026-10-24T10:30', 'Asia/Novosibirsk')
  assert.equal(utc, '2026-10-24T03:30:00.000Z')
  assert.equal(inquiryLocalTime(utc, 'Asia/Novosibirsk'), '2026-10-24T10:30')
  assert.equal(inquiryLocalTime(utc, 'Europe/Moscow'), '2026-10-24T06:30')
})

test('Пустое действие не создаёт срока', () => {
  assert.equal(inquiryUtcTime('', 'Asia/Novosibirsk'), null)
  assert.equal(inquiryLocalTime(null, 'Asia/Novosibirsk'), '')
})

test('Несуществующее время при переходе на летнее время отклоняется', () => {
  assert.throws(() => inquiryUtcTime('2026-03-29T02:30', 'Europe/Berlin'), /Такого времени нет/)
  assert.equal(inquiryUtcTime('2026-03-29T03:30', 'Europe/Berlin'), '2026-03-29T01:30:00.000Z')
})

test('Настроенный пояс организации имеет приоритет, для старых организаций без настройки используется UTC', () => {
  assert.equal(resolveInquiryTimeZone(null), 'UTC')
  assert.equal(resolveInquiryTimeZone('Europe/Moscow'), 'Europe/Moscow')
  assert.equal(formatInquiryTime('2026-10-24T03:30:00Z', null), formatInquiryTime('2026-10-24T03:30:00Z', resolveInquiryTimeZone()))
})

test('Сохранение неизменённого срока сохраняет исходный момент при осеннем переводе часов', () => {
  for (const original of ['2026-10-25T00:30:00Z', '2026-10-25T01:30:00Z']) {
    assert.equal(inquiryUtcTime(inquiryLocalTime(original, 'Europe/Berlin'), 'Europe/Berlin', original), original)
  }
  assert.equal(inquiryUtcTime('2026-10-25T04:30', 'Europe/Berlin', '2026-10-25T00:30:00Z'), '2026-10-25T03:30:00.000Z')
})

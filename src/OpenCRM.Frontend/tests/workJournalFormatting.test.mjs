import assert from 'node:assert/strict'
import test from 'node:test'
import { formatWorkJournalDate } from '../packages/workspace/src/workJournalFormatting.ts'

test('Локальное время журнала сохраняется, момент UTC переводится в пояс организации', () => {
  assert.equal(formatWorkJournalDate('2026-11-18T09:30:00', 'Asia/Shanghai', 'en-GB'), '18/11, 09:30')
  assert.equal(formatWorkJournalDate('2026-11-18T09:30:00Z', 'Asia/Shanghai', 'en-GB'), '18/11, 17:30')
  assert.equal(formatWorkJournalDate('2026-11-18T09:30:00+03:00', 'Asia/Shanghai', 'en-GB'), '18/11, 14:30')
})

test('Календарная дата журнала не сдвигается из-за пояса браузера или организации', () => {
  const originalTimeZone = process.env.TZ
  try {
    for (const zone of ['Pacific/Honolulu', 'Asia/Shanghai']) {
      process.env.TZ = zone
      assert.equal(formatWorkJournalDate('2026-11-18', 'America/Los_Angeles', 'en-GB'), '18/11')
      assert.equal(formatWorkJournalDate('2026-11-18T00:00:00', 'Asia/Shanghai', 'en-GB'), '18/11')
    }
  } finally {
    if (originalTimeZone === undefined) delete process.env.TZ
    else process.env.TZ = originalTimeZone
  }
})

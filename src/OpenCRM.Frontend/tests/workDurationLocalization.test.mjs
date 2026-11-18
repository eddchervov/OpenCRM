import assert from 'node:assert/strict'
import test from 'node:test'
import { formatWorkDuration } from '../packages/workspace/src/formatWorkDuration.ts'
import { formatServiceDuration } from '../packages/workspace/src/serviceDuration.ts'
import { describeOccupiedSlot } from '../packages/workspace/src/api/bookingSlots.ts'
import { formattingCulture, i18n } from '@opencrm/ui/i18n'

test('Длительность сохраняет русские склонения и поддерживает английский и китайский', () => {
  assert.equal(formatWorkDuration('1.02:05:00', 'ru'), '1 день 2 часа 5 минут')
  assert.equal(formatWorkDuration('1.02:05:00', 'en'), '1 day 2 hours 5 minutes')
  const chinese = formatWorkDuration('1.02:05:00', 'zh-Hans')
  assert.match(chinese, /1.*天.*2.*小时.*5.*分钟/)
  assert.doesNotMatch(chinese, /день|часа|минут/)
})

test('Длительность корректно выводит секунды и сохраняет неизвестный формат', () => {
  assert.equal(formatWorkDuration('-00:00:03', 'en'), '−3 seconds')
  assert.equal(formatWorkDuration('00:00:00', 'en'), '0 seconds')
  assert.equal(formatWorkDuration('invalid', 'zh-Hans'), 'invalid')
})

test('Занятость сохраняет местные дату и время, а единицы следуют языку', () => {
  const previousLocale = i18n.global.locale.value
  const previousCulture = formattingCulture.value
  try {
    i18n.global.locale.value = 'en'
    formattingCulture.value = 'en-GB'
    assert.match(formatServiceDuration(90), /1.*hr.*30.*min/)
    const slot = { id: 'example', startMinute: 30, endMinute: 1500, vehicle: 'Example' }
    assert.match(describeOccupiedSlot(slot), /01:00 \(\+1 day\)/)
    const dated = describeOccupiedSlot({ ...slot, startAt: '2026-11-16T00:30:00', endAt: '2026-11-17T01:00:00' })
    assert.match(dated, /16\/11\/2026 00:30/)
    assert.match(dated, /17\/11\/2026 01:00/)
  } finally {
    i18n.global.locale.value = previousLocale
    formattingCulture.value = previousCulture
  }
})

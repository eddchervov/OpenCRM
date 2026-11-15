import assert from 'node:assert/strict'
import test from 'node:test'
import { nextWorkingDay, scheduleBusinessHours, workingIntervals } from '../packages/workspace/src/workingHours.ts'

const day = (dayOfWeek, startTime = '09:00', endTime = '19:00', breaks = []) => ({
  dayOfWeek, isWorking: true, startTime, endTime, breaks
})

test('После выходного находит ближайший рабочий день с учётом перерывов', () => {
  assert.deepEqual(nextWorkingDay('2026-10-18', [day(1, '09:00', '19:00', [{ startTime: '13:00', endTime: '14:00' }])]), {
    date: '2026-10-19', intervals: [{ startTime: '09:00', endTime: '13:00' }, { startTime: '14:00', endTime: '19:00' }]
  })
})

test('Поиск рабочего дня пересекает границу года и пропускает дни без интервалов', () => {
  assert.equal(nextWorkingDay('2026-12-31', [day(5, '09:00', '09:00'), day(6)])?.date, '2027-01-02')
})

test('Для полностью закрытого графика и пустого расписания перехода нет', () => {
  assert.equal(nextWorkingDay('2026-10-18', []), null)
  assert.equal(nextWorkingDay('2026-10-18', [{ ...day(1), isWorking: false }]), null)
})

test('Следующая неделя доступна, когда рабочий день только один', () => {
  assert.equal(nextWorkingDay('2026-10-19', [day(1)])?.date, '2026-10-26')
})

test('Рабочие интервалы исключают короткие перерывы и дни отдыха из календарного фона', () => {
  const monday = day(1, '09:00:00', '19:00:00', [
    { startTime: '12:00:00', endTime: '13:00:00' },
    { startTime: '16:00:00', endTime: '16:15:00' }
  ])
  const saturday = { ...day(6), isWorking: false }
  const schedule = [monday, saturday]

  assert.deepEqual(workingIntervals(monday), [
    { startTime: '09:00', endTime: '12:00' },
    { startTime: '13:00', endTime: '16:00' },
    { startTime: '16:15', endTime: '19:00' }
  ])
  assert.deepEqual(scheduleBusinessHours(schedule), [
    { daysOfWeek: [1], startTime: '09:00', endTime: '12:00' },
    { daysOfWeek: [1], startTime: '13:00', endTime: '16:00' },
    { daysOfWeek: [1], startTime: '16:15', endTime: '19:00' }
  ])
})

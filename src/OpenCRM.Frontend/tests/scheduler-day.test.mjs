import assert from 'node:assert/strict'
import test from 'node:test'
import {
  getSchedulerDayInterval,
  getSchedulerDayLayout,
  getSchedulerFreeStarts,
  isSchedulerSlotAvailable
} from '../packages/workspace/src/utils/schedulerDay.ts'

const day = '2026-10-17'
const item = (id, overrides = {}) => ({
  id,
  vehicleId: `vehicle-${id}`,
  vehicleDisplayName: `Автомобиль ${id}`,
  status: 1,
  date: day,
  plannedStartTime: '10:00',
  plannedEndTime: '11:00',
  location: 'Пост 1',
  ...overrides
})

test('Свободный слот целиком помещается в рабочий интервал и не пересекает перерыв', () => {
  const intervals = [{ startTime: '09:00', endTime: '12:00' }, { startTime: '13:00', endTime: '15:00' }]
  const starts = getSchedulerFreeStarts([item('busy')], day, 'Пост 1', intervals, 90)

  assert.deepEqual(starts, [780, 810])
  assert.deepEqual(getSchedulerFreeStarts([], day, 'Пост 1', [], 60), [])
})

test('Завершённые работы не занимают свободные слоты, длительная работа с прошлого дня занимает', () => {
  const intervals = [{ startTime: '09:00', endTime: '12:00' }]
  const previousDay = item('previous', { date: '2026-10-16', plannedStartTime: '23:00', plannedEndDate: day, plannedEndTime: '10:00' })
  const completed = item('done', { status: 3 })

  assert.deepEqual(getSchedulerFreeStarts([previousDay, completed], day, 'Пост 1', intervals, 60), [600, 630, 660])
  assert.deepEqual(getSchedulerFreeStarts([], day, 'Пост 1', intervals, 0), [])
})

test('Календарь обрезает многодневную работу границами выбранного дня', () => {
  const booking = item('overnight', { plannedStartTime: '23:00', plannedEndDate: '2026-10-18', plannedEndTime: '01:30' })

  assert.deepEqual(getSchedulerDayInterval(booking, day), { start: 1380, end: 1440 })
  assert.deepEqual(getSchedulerDayInterval(booking, '2026-10-18'), { start: 0, end: 90 })
  assert.equal(getSchedulerDayInterval(booking, '2026-10-19'), null)
})

test('Пересекающиеся записи получают разные дорожки, а соприкасающиеся используют одну', () => {
  const first = item('first', { plannedStartTime: '10:00', plannedEndTime: '11:00' })
  const overlapping = item('overlap', { plannedStartTime: '10:30', plannedEndTime: '11:30' })
  const touching = item('touching', { plannedStartTime: '11:30', plannedEndTime: '12:00' })
  const layout = getSchedulerDayLayout([first, overlapping, touching], day, 8 * 60, 14 * 60)

  assert.equal(layout.find(entry => entry.item.id === 'first')?.lane, 0)
  assert.equal(layout.find(entry => entry.item.id === 'overlap')?.lane, 1)
  assert.equal(layout.find(entry => entry.item.id === 'touching')?.lane, 0)
})

test('Отменённые и завершённые записи остаются в календаре, но не блокируют создание', () => {
  const cancelled = item('cancelled', { status: 4 })
  const completed = item('completed', { status: 3 })

  assert.equal(getSchedulerDayLayout([cancelled, completed], day, 8 * 60, 14 * 60).length, 2)
  assert.equal(isSchedulerSlotAvailable([cancelled, completed], day, 'Пост 1', 10 * 60), true)
})

test('Активная запись блокирует всё пересекающееся время своего поста', () => {
  const active = item('active', { status: 2 })

  assert.equal(isSchedulerSlotAvailable([active], day, 'Пост 1', 10 * 60 + 30), false)
  assert.equal(isSchedulerSlotAvailable([active], day, 'Пост 2', 10 * 60), true)
  assert.equal(isSchedulerSlotAvailable([active], day, 'Пост 1', 11 * 60), true)
})

test('При создании записи через полночь учитывается занятость следующего дня', () => {
  const nextDay = item('next-day', {
    date: '2026-10-18',
    plannedStartTime: '00:00',
    plannedEndTime: '00:30'
  })

  assert.equal(isSchedulerSlotAvailable([nextDay], day, 'Пост 1', 23 * 60 + 30), false)
})

test('Карточка у полуночи остаётся внутри дня и сохраняет ссылку на запись', () => {
  const booking = item('late', { plannedStartTime: '23:45', plannedEndDate: '2026-10-18', plannedEndTime: '00:30' })
  const [entry] = getSchedulerDayLayout([booking], day, 9 * 60, 1440)
  assert.equal(entry.item.id, 'late')
  assert.ok(entry.top + entry.height <= (1440 - 9 * 60) * 1.2)
})

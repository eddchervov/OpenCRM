import assert from 'node:assert/strict'
import test from 'node:test'
import { buildVisualWorkPosts, isBookingOverdue } from '../packages/workspace/src/components/visualization/workPosts.ts'

const today = '2026-10-09'
const post = { id: 'post', name: 'Бокс 1', sortOrder: 1, isActive: true }
const work = (overrides = {}) => ({
  id: 'order', vehicleName: 'Автомобиль', location: 'Бокс 1', date: today, startTime: '10:00',
  services: [], workTotalCount: 1, workCompletedCount: 0, workPausedCount: 0, ...overrides
})
const group = items => ({ items, totalCount: items.length })
const day = (planned = [], inProgress = [], ready = []) => ({
  planned: group(planned), inProgress: group(inProgress), ready: group(ready), handedOver: group([]), hasAnyWork: true
})
const build = value => buildVisualWorkPosts([post], value, today, '10:00')

test('Запись становится красной ровно к началу; будущая и без времени сегодня не занимают пост', () => {
  assert.equal(isBookingOverdue(work(), today, '09:59'), false)
  assert.equal(build(day([work()])).posts[0].state, 'overdue')
  assert.equal(build(day([work({ startTime: '11:00' })])).posts[0].state, 'free')
  assert.equal(build(day([work({ startTime: null })])).posts[0].count, 0)
  assert.equal(build(day([work({ date: '2026-10-08', startTime: null })])).posts[0].state, 'overdue')
})

test('Начатый заказ занимает пост независимо от плановой даты, завершённый уходит в готовые', () => {
  assert.equal(build(day([], [work({ date: '2026-10-01' })])).posts[0].state, 'inProgress')
  const result = build(day([], [], [work()]))
  assert.equal(result.posts[0].state, 'free')
  assert.equal(result.ready.length, 1)
})

test('Несколько заказов не скрывают друг друга; первой показана начатая работа', () => {
  const result = build(day([work({ id: 'waiting' })], [work({ id: 'active' })]))
  assert.equal(result.posts[0].state, 'conflict')
  assert.equal(result.posts[0].count, 2)
  assert.deepEqual(result.posts[0].entries.map(entry => entry.item.id), ['active', 'waiting'])
})

test('Пауза, старое имя поста и записи без поста сохраняются в представлении', () => {
  const result = build(day([], [work({ workPausedCount: 1 }), work({ id: 'old', location: 'Старый бокс' }), work({ id: 'none', location: null })]))
  assert.equal(result.posts[0].state, 'paused')
  assert.equal(result.posts[1].isLegacy, true)
  assert.equal(result.unassigned[0].item.id, 'none')
})

test('Пробелы и регистр имени не создают второй пост; отключённый занятый пост не исчезает', () => {
  const result = buildVisualWorkPosts([{ ...post, isActive: false }], day([], [work({ location: ' бОкС 1 ' })]), today, '10:00')
  assert.equal(result.posts.length, 1)
  assert.equal(result.posts[0].id, 'post')
  assert.equal(result.posts[0].isLegacy, true)
})

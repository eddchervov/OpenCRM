import assert from 'node:assert/strict'
import test from 'node:test'
import { layoutPostMarkers } from '../packages/workspace/src/components/visualization/postMarkers.ts'

const post = (id, left, top, occupied = false) => ({ id, left, top, occupied, depth: 0 })

test('Выбранный пост остаётся виден при наложении подписей, остальные не перекрывают его', () => {
  const layout = layoutPostMarkers([post('first', 100, 100), post('selected', 110, 110), post('last', 290, 220)], 400, 300, 'selected')
  assert.equal(layout.selected.visible, true)
  assert.equal(layout.first.visible, false)
  assert.equal(layout.last.visible, true)
})

test('На 100 постах компактные номера не пересекаются и остаются внутри области', () => {
  const posts = Array.from({ length: 100 }, (_, index) => post(String(index), 20 + index % 10 * 35, 10 + Math.floor(index / 10) * 24))
  const layout = layoutPostMarkers(posts, 360, 280, '99')
  const visible = Object.values(layout).filter(marker => marker.visible)
  assert.equal(layout['99'].visible, true)
  assert.ok(visible.length > 10)
  for (let index = 0; index < visible.length; index++) {
    const marker = visible[index]
    assert.equal(marker.compact, true)
    assert.ok(marker.left >= marker.width / 2 && marker.left + marker.width / 2 <= 360)
    assert.ok(marker.top >= 0 && marker.top + 28 <= 280)
    for (const other of visible.slice(index + 1)) {
      assert.ok(Math.abs(marker.left - other.left) >= 32 || Math.abs(marker.top - other.top) >= 32)
    }
  }
})

test('Занятый пост имеет приоритет, невидимая проекция не вытесняет видимые подписи', () => {
  const layout = layoutPostMarkers([post('free', 120, 100), post('busy', 125, 100, true), { ...post('behind', 290, 120), depth: 2 }], 400, 300)
  assert.equal(layout.busy.visible, true)
  assert.equal(layout.free.visible, false)
  assert.equal(layout.behind.visible, false)
})

test('Мобильные подписи используют фактическую ширину кнопки, не скрывая соседний пост', () => {
  const posts = [post('first', 100, 100), post('second', 200, 100)]
  const layout = layoutPostMarkers(posts, 360, 280, 'first', 76)
  assert.equal(layout.first.width, 76)
  assert.equal(layout.second.visible, true)
})

import test from 'node:test'
import assert from 'node:assert/strict'
import { computed, effectScope, ref, toValue } from 'vue'
import { useSchedulerViewState } from '../packages/workspace/src/schedulerViewState.ts'

const today = '2026-10-23'
const key = 'opencrm-workspace:user-1:org-1:scheduler:view-state:v1'

function storage(values = new Map()) {
  return {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value)
  }
}

function openState(storageKey = key) {
  const scope = effectScope()
  const state = scope.run(() => useSchedulerViewState(storageKey, today, computed(() => toValue(storageKey) + ':preferred-mode')))
  return { ...state, close: () => scope.stop() }
}

test('Возврат к записям восстанавливает дату, период, представление и параметры слотов', () => {
  globalThis.window = { sessionStorage: storage(), localStorage: storage() }
  const first = openState()
  first.selectedDate.value = '2026-11-02'
  first.listDays.value = 14
  first.viewMode.value = 'slots'
  first.slotDuration.value = 180
  first.postFilter.value = 'Пост 3'
  first.daySearch.value = 'BMW'
  first.close()

  const returned = openState()
  assert.equal(returned.selectedDate.value, '2026-11-02')
  assert.equal(returned.listDays.value, 14)
  assert.equal(returned.viewMode.value, 'slots')
  assert.equal(returned.slotDuration.value, 180)
  assert.equal(returned.postFilter.value, 'Пост 3')
  assert.equal(returned.daySearch.value, 'BMW')
  returned.close()
})

test('Смена организации и пользователя не переносит их настройки друг другу', () => {
  globalThis.window = { sessionStorage: storage(), localStorage: storage() }
  const currentKey = ref(key)
  const state = openState(currentKey)
  state.selectedDate.value = '2026-11-02'
  state.postFilter.value = 'Пост 2'

  for (const otherKey of ['opencrm-workspace:user-1:org-2:scheduler:view-state:v1', 'opencrm-workspace:user-2:org-1:scheduler:view-state:v1']) {
    currentKey.value = otherKey
    assert.equal(state.selectedDate.value, today)
    assert.equal(state.postFilter.value, '')
    state.selectedDate.value = '2026-12-01'
  }

  currentKey.value = key
  assert.equal(state.selectedDate.value, '2026-11-02')
  assert.equal(state.postFilter.value, 'Пост 2')
  state.close()
})

for (const saved of ['invalid JSON', 'null', '[]', '{"selectedDate":"2026-02-30","listDays":-1,"slotDuration":1,"postFilter":{},"viewMode":"unknown"}']) {
  test('Некорректные сохранённые значения заменяются начальными: ' + saved, () => {
    globalThis.window = { sessionStorage: storage(new Map([[key, saved]])), localStorage: storage() }
    const state = openState()
    assert.equal(state.selectedDate.value, today)
    assert.equal(state.listDays.value, 7)
    assert.equal(state.viewMode.value, 'day')
    assert.equal(state.slotDuration.value, 60)
    assert.equal(state.postFilter.value, '')
    state.close()
  })
}

test('В новой вкладке используется предпочтение вида текущего пользователя', () => {
  globalThis.window = { sessionStorage: storage(), localStorage: storage(new Map([[key + ':preferred-mode', 'calendar']])) }
  const state = openState()
  assert.equal(state.viewMode.value, 'calendar')
  state.close()
})

test('Запрет хранилища не мешает открыть и изменить расписание', () => {
  const blocked = {
    getItem() { throw new Error('Хранилище недоступно.') },
    setItem() { throw new Error('Хранилище недоступно.') }
  }
  globalThis.window = { sessionStorage: blocked, localStorage: blocked }
  const state = openState()
  assert.equal(state.selectedDate.value, today)
  assert.doesNotThrow(() => { state.selectedDate.value = '2026-10-26' })
  state.close()
})

test('Предпочтение вида другого пользователя не подставляется в новое состояние', () => {
  globalThis.window = { sessionStorage: storage(), localStorage: storage(new Map([[key + ':preferred-mode', 'slots']])) }
  const other = openState('opencrm-workspace:user-2:org-1:scheduler:view-state:v1')
  assert.equal(other.viewMode.value, 'day')
  other.close()
})

test('Слишком большой сохранённый период не ломает открытие списка', () => {
  const saved = JSON.stringify({ viewMode: 'list', listDays: Number.MAX_SAFE_INTEGER })
  globalThis.window = { sessionStorage: storage(new Map([[key, saved]])), localStorage: storage() }
  const state = openState()
  assert.equal(state.viewMode.value, 'list')
  assert.equal(state.listDays.value, 7)
  const end = new Date(state.selectedDate.value + 'T00:00:00Z')
  end.setUTCDate(end.getUTCDate() + state.listDays.value - 1)
  assert.doesNotThrow(() => end.toISOString())
  state.close()
})

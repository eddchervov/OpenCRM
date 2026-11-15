import test from 'node:test'
import assert from 'node:assert/strict'
import { createMemberSelectOptions } from '../packages/workspace/src/utils/memberSelectOptions.ts'

test('Связанный сотрудник отображается по имени, без имени используется почта, логин или идентификатор', () => {
  const members = [
    { value: 'named', employeeName: '  Анна Иванова  ', email: 'contact@example.invalid' },
    { value: 'email', email: 'contact@example.invalid' },
    { value: 'blank', employeeName: '  ', email: 'contact@example.invalid' },
    { value: 'login', userName: 'login' },
    { value: 'id' }
  ]

  assert.deepEqual(createMemberSelectOptions(members), [
    { value: 'named', label: 'Анна Иванова' },
    { value: 'email', label: 'contact@example.invalid' },
    { value: 'blank', label: 'contact@example.invalid' },
    { value: 'login', label: 'login' },
    { value: 'id', label: 'id' }
  ])
  assert.equal(members[0].employeeName, '  Анна Иванова  ')
})

test('Почта добавляется только при совпадении полного имени без учёта регистра и лишних пробелов', () => {
  assert.deepEqual(createMemberSelectOptions([
    { value: 'one', employeeName: 'Иван Петров', email: 'contact@example.invalid' },
    { value: 'two', employeeName: ' иван  петров ', email: 'contact@example.invalid' },
    { value: 'three', employeeName: 'Пётр Петров', email: 'contact@example.invalid' }
  ]), [
    { value: 'one', label: 'Иван Петров (contact@example.invalid)' },
    { value: 'two', label: 'иван петров (contact@example.invalid)' },
    { value: 'three', label: 'Пётр Петров' }
  ])
})

test('При исчезновении дубликата почта убирается, совпадения без почты различаются по логину или идентификатору', () => {
  assert.deepEqual(createMemberSelectOptions([
    { value: 'one', employeeName: 'Иван', userName: 'first' },
    { value: 'two', employeeName: 'Иван' }
  ]), [
    { value: 'one', label: 'Иван (first)' },
    { value: 'two', label: 'Иван (two)' }
  ])
  assert.deepEqual(createMemberSelectOptions([
    { value: 'one', employeeName: 'Иван', email: 'contact@example.invalid' }
  ]), [{ value: 'one', label: 'Иван' }])
  assert.deepEqual(createMemberSelectOptions([]), [])
})

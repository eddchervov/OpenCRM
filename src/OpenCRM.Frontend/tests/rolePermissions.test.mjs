import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceRolePermissions.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'roles' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
function setup() {
  const permissions = [
    { code: 'workspace.view', name: 'Вход', description: 'Открытие CRM', groupName: 'Общее', isEnabled: true },
    { code: 'finance.view', name: 'Просмотр финансов', description: 'Платежи', groupName: 'Финансы', isEnabled: false }
  ]
  const props = vue.reactive({ roles: [{ roleCode: 'owner', roleName: 'Владелец', canEdit: false, permissions }, { roleCode: 'performer', roleName: 'Исполнитель', canEdit: true, permissions }], canManage: true, isEditing: false, isSaving: false, drafts: { performer: ['workspace.view'] } })
  const events = []
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => id === 'vue' ? vue : {}, module, module.exports)
  const state = module.exports.default.setup(props, { expose() {}, emit: (...args) => events.push(args) })
  return { state, props, events }
}

test('Поиск по описанию и разделу не меняет права и общие счётчики', () => {
  const { state, props } = setup()
  assert.equal(state.selectedRole.value.roleCode, 'performer')
  state.search.value = 'ПЛАТЕЖИ'
  assert.deepEqual(state.groups.value.map(group => group.name), ['Финансы'])
  assert.equal(state.enabledCount.value, 1)
  assert.equal(props.drafts.performer.length, 1)
  state.search.value = 'Нет результатов'
  assert.equal(state.groups.value.length, 0)
})

test('Редактор показывает изменения черновика, просмотр — сохранённые права', () => {
  const { state, props } = setup()
  props.drafts.performer.push('finance.view')
  assert.equal(state.changedCount.value, 0)
  props.isEditing = true
  assert.equal(state.enabledCount.value, 2)
  assert.equal(state.changedCount.value, 1)
  state.save()
  props.isEditing = false
  assert.equal(state.enabledCount.value, 1)
})

test('Нельзя переключить роль в редактировании или сохранить системную роль', () => {
  const { state, props, events } = setup()
  props.isEditing = true
  state.selectRole('owner')
  assert.equal(state.selectedRole.value.roleCode, 'performer')
  props.isEditing = false
  state.selectRole('owner')
  props.isEditing = true
  state.toggle('finance.view', true)
  state.save()
  assert.equal(events.length, 0)
})

test('Обязательное право, неизвестный код, чтение и отправка блокируют изменения', () => {
  const { state, props, events } = setup()
  props.isEditing = true
  state.toggle('workspace.view', false)
  state.toggle('unknown', true)
  props.canManage = false
  state.toggle('finance.view', true)
  props.canManage = true
  props.isSaving = true
  state.toggle('finance.view', true)
  state.save()
  assert.equal(events.length, 0)
})

test('Изменение передаёт родителю точную роль и код; поиск не исключает права из сохранения', () => {
  const { state, props, events } = setup()
  props.isEditing = true
  state.toggle('finance.view', true)
  assert.deepEqual(events[0], ['toggle', 'performer', 'finance.view', true])
  props.drafts.performer.push('finance.view')
  state.search.value = 'ничего'
  state.save()
  assert.deepEqual(events[1], ['save', 'performer'])
})

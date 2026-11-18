import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import { i18n } from '@opencrm/ui/i18n'
import '../packages/workspace/src/permissionMessages.ts'
import '../packages/workspace/src/employeeMessages.ts'

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
  new Function('require', 'module', 'exports', code)(id => regionalTestDependencies[id] ?? (id === 'vue' ? vue : {}), module, module.exports)
  const state = module.exports.default.setup(props, { expose() {}, emit: (...args) => events.push(args) })
  return { state, props, events }
}

test('Поиск по описанию и разделу не меняет права и общие счётчики', () => {
  const { state, props } = setup()
  assert.equal(state.selectedRole.value.roleCode, 'performer')
  state.search.value = 'ВЫРУЧКУ'
  assert.deepEqual(state.groups.value.map(group => group.name), ['Финансы'])
  assert.equal(state.enabledCount.value, 1)
  assert.equal(props.drafts.performer.length, 1)
  state.search.value = 'Нет результатов'
  assert.equal(state.groups.value.length, 0)
})

test('Все серверные разрешения имеют локализованные названия, описания и группы', () => {
  const source = readFileSync(new URL('../../OpenCRM.Services.Interfaces/Organizations/OrganizationPermissionCodes.cs', import.meta.url), 'utf8')
  const constants = new Map([...source.matchAll(/const string (\w+) = "([^"]+)"/g)].map(match => [match[1], match[2]]))
  const definitions = [...source.matchAll(/Permission\(OrganizationPermissionCodes\.(\w+),/g)]
  const originalLocale = i18n.global.locale.value
  try {
    const { state } = setup()
    for (const locale of ['ru', 'en', 'zh-Hans']) {
      i18n.global.locale.value = locale
      for (const [, symbol] of definitions) {
        const permission = { code: constants.get(symbol), name: 'fallback', description: 'fallback', groupName: 'fallback' }
        for (const field of ['name', 'description', 'group']) {
          assert.notEqual(state.permissionText(permission, field), 'fallback', `${locale}: ${permission.code}.${field}`)
        }
      }
    }
  } finally { i18n.global.locale.value = originalLocale }
})

test('Поиск использует выбранный язык, неизвестные разрешения сохраняют свои подписи', () => {
  const originalLocale = i18n.global.locale.value
  try {
    i18n.global.locale.value = 'en'
    const { state, props } = setup()
    state.search.value = 'revenue'
    assert.equal(state.groups.value[0].name, 'Finance')
    assert.equal(state.groups.value[0].permissions[0].code, 'finance.view')
    const custom = { code: 'custom', name: 'Особое право', description: 'Описание', groupName: 'Другое' }
    assert.equal(state.permissionText(custom, 'name'), 'Особое право')
    assert.equal(state.roleName({ roleCode: 'custom', roleName: 'Собственная роль' }), 'Собственная роль')
    assert.equal(props.roles[1].permissions[1].name, 'Просмотр финансов')
  } finally { i18n.global.locale.value = originalLocale }
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

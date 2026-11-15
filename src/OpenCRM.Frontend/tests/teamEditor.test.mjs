import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceEmployeesSection.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'team' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function setup(readonly = false) {
  const calls = []
  const api = Object.fromEntries(['saveEmployee', 'createEmployeeWithInvitation', 'createEmployeeForMember', 'linkEmployeeMember', 'inviteWorkspaceMember', 'updateWorkspaceMemberRole', 'revokeWorkspaceInvitation', 'removeWorkspaceMember', 'transferWorkspaceOwnership'].map(name => [name, async (...args) => calls.push([name, ...structuredClone(args.map(value => vue.toRaw(value)))])]))
  api.loadEmployees = async () => ({ items: [], members: [], canManage: true, canLinkMembers: true })
  api.loadWorkspaceMembers = async () => ({ organization: { members: [], pendingInvitations: [], currentUserRoleCode: 'owner' }, canRemoveMembers: true, canInviteMembers: true, canManageRoles: true, roleOptions: [] })
  const props = vue.reactive({ context: { organization: { id: 'org' }, workspaceAccess: { isReadOnly: readonly }, shell: { menu: [{ code: 'members', isVisible: true }] } } })
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => id === 'vue' ? { ...vue, onMounted() {} } : id.startsWith('../api/') ? api : {}, module, module.exports)
  const state = module.exports.default.setup(props, { expose() {}, emit() {} })
  state.addDialog.value = { showModal() {}, close() {} }
  state.cardDialog.value = { showModal() {}, close() {} }
  const employee = { id: 'e', name: 'Алексей', phone: '', memberId: null, isActive: true, version: 7 }
  return { state, calls, api, props, employee }
}

test('Три варианта создания используют свои API и контекстные подписи', async () => {
  for (const [mode, method, label] of [['none', 'saveEmployee', 'Добавить сотрудника'], ['invite', 'createEmployeeWithInvitation', 'Добавить и пригласить'], ['existing', 'createEmployeeForMember', 'Добавить и связать']]) {
    const { state, calls } = setup()
    await state.load()
    state.openAdd()
    state.addAccess.value = mode
    Object.assign(state.addDraft, { name: ' Алексей ', email: ' contact@example.invalid ', memberId: 'm' })
    assert.equal(state.addLabel.value, label)
    await state.addPerson()
    assert.equal(calls.length, 1)
    assert.equal(calls[0][0], method)
    assert.equal(calls[0][2].name, 'Алексей')
    if (mode === 'invite') assert.equal(calls[0][2].email, 'contact@example.invalid')
    if (mode === 'existing') assert.equal(calls[0][2].memberId, 'm')
  }
})

test('Приглашение в CRM не создаёт исполнителя', async () => {
  const { state, calls } = setup()
  await state.load()
  state.openAdd()
  state.canManage.value = false
  state.openAdd()
  state.addDraft.email = ' contact@example.invalid '
  await state.addPerson()
  assert.deepEqual(calls[0], ['inviteWorkspaceMember', 'org', { email: 'contact@example.invalid', roleCode: 'employee' }])
})

test('Изменение активности сохраняет версию и привязку без смены роли', async () => {
  const { state, calls, employee } = setup()
  await state.load()
  employee.memberId = 'm'
  state.openCard({ employee, name: employee.name, email: '', role: '', connection: '' })
  state.cardDraft.isActive = false
  await state.saveCard()
  assert.equal(calls.length, 1)
  assert.deepEqual(calls[0][2], { ...employee, isActive: false })
})

test('Владелец защищён от изменения роли, режим чтения блокирует сохранение', async () => {
  for (const readonly of [false, true]) {
    const { state, calls, employee } = setup(readonly)
    await state.load()
    state.openCard({ employee, member: { id: 'owner', isOwner: true }, name: 'Владелец', email: '', role: '', connection: '' })
    await state.saveRole()
    assert.equal(calls.length, 0)
    if (readonly) {
      await state.saveCard()
      state.openAdd()
      state.addDraft.email = 'contact@example.invalid'
      await state.addPerson()
      assert.equal(calls.length, 0)
    }
  }
})

test('Изменение роли и отправка приглашения требуют сначала сохранить данные', async () => {
  const { state, calls, employee } = setup()
  await state.load()
  state.openCard({ employee, member: { id: 'm', isOwner: false }, name: employee.name, email: '', role: '', connection: '' })
  state.cardDraft.name = 'Новое имя'
  await state.saveRole()
  state.cardAction.value = 'invite'
  state.cardDraft.email = 'contact@example.invalid'
  await state.applyCardAction()
  assert.equal(calls.length, 0)
  assert.match(state.formError.value, /Сначала сохраните/)
})

test('Ошибка сохраняет форму; повторное сохранение блокируется', async () => {
  const { state, api, calls } = setup()
  await state.load()
  state.openAdd()
  state.addDraft.name = 'Новое имя'
  api.saveEmployee = async () => { throw new Error('Ошибка сервера') }
  let closed = false
  state.addDialog.value.close = () => { closed = true }
  await state.addPerson()
  assert.equal(closed, false)
  assert.equal(state.formError.value, 'Ошибка сервера')
  assert.equal(state.addDraft.name, 'Новое имя')
  state.saving.value = true
  await state.addPerson()
  assert.equal(calls.length, 0)
})

test('Проверка email применяется только к приглашению, сохранение данных от неё независимо', async () => {
  const { state, calls, employee } = setup()
  await state.load()
  state.openCard({ employee, name: employee.name, email: '', role: '', connection: '' })
  state.cardAction.value = 'invite'
  state.cardDraft.email = 'invalid'
  state.cardDialog.value.querySelector = () => ({ reportValidity: () => false })
  await state.applyCardAction()
  assert.equal(calls.length, 0)
  state.cardDraft.name = 'Новое имя'
  await state.saveCard()
  assert.equal(calls[0][0], 'saveEmployee')
})

test('Назначение на работы передаётся независимо от CRM во всех вариантах создания', async () => {
  for (const mode of ['none', 'invite', 'existing']) {
    const { state, calls } = setup()
    await state.load()
    state.openAdd()
    state.addAccess.value = mode
    Object.assign(state.addDraft, { name: 'Администратор', email: 'contact@example.invalid', memberId: 'm', isActive: false })
    await state.addPerson()
    assert.equal(calls.length, 1)
    assert.equal(calls[0][2].isActive, false)
  }
})

test('В общем списке сохраняются неактивные сотрудники, аккаунты и приглашения без дублей', async () => {
  const { state } = setup()
  await state.load()
  state.employees.value = [
    { id: 'a', name: 'Мастер', isActive: true, memberId: 'm', version: 1 },
    { id: 'b', name: 'Администратор', isActive: false, memberId: null, version: 1 },
    { id: 'c', name: 'Новый сотрудник', isActive: false, memberId: null, version: 1 }
  ]
  state.access.value.organization.members = [{ id: 'm', email: 'contact@example.invalid', roleName: 'Исполнитель' }, { id: 'standalone', email: 'contact@example.invalid', roleName: 'Администратор' }]
  state.access.value.organization.pendingInvitations = [{ id: 'i', employeeId: 'c', email: 'contact@example.invalid', roleName: 'Сотрудник' }]
  assert.equal(state.rows.value.length, 4)
  state.teamFilter.value = 'not-assignable'
  assert.equal(state.rows.value.length, 3)
  state.teamFilter.value = 'invited'
  assert.deepEqual(state.rows.value.map(row => row.employee.id), ['c'])
  assert.equal(state.rows.value[0].connection, 'Ожидает принятия')
  state.teamFilter.value = 'no-access'
  assert.deepEqual(state.rows.value.map(row => row.employee.id), ['b'])
})

test('Удаление доступа и передача владения требуют подтверждения и отдельных прав', async () => {
  for (const [action, method] of [['remove', 'removeWorkspaceMember'], ['owner', 'transferWorkspaceOwnership']]) {
    const { state, calls, employee } = setup()
    await state.load()
    let opened = false
    state.confirmationDialog.value = { showModal() { opened = true }, close() { opened = false } }
    state.openCard({ employee, member: { id: 'm', isOwner: false }, name: employee.name, email: '', role: '', connection: '' })
    state.requestMemberAction(action)
    assert.equal(opened, true)
    assert.equal(calls.length, 0)
    await state.confirmMemberAction()
    assert.deepEqual(calls[0], [method, 'org', 'm'])
    assert.equal(opened, false)
  }
})

test('Нельзя удалить владельца или менять доступ с несохранёнными данными, без прав и при отправке', async () => {
  const { state, calls, employee } = setup()
  await state.load()
  let opened = false
  state.confirmationDialog.value = { showModal() { opened = true }, close() {} }
  state.openCard({ employee, member: { id: 'm', isOwner: true }, name: employee.name, email: '', role: '', connection: '' })
  state.requestMemberAction('remove')
  assert.equal(opened, false)
  state.selected.value.member.isOwner = false
  state.cardDraft.name = 'Изменено'
  state.requestMemberAction('remove')
  assert.equal(opened, false)
  state.cardDraft.name = employee.name
  state.access.value.canRemoveMembers = false
  state.requestMemberAction('remove')
  assert.equal(opened, false)
  state.saving.value = true
  state.requestMemberAction('owner')
  assert.equal(opened, false)
  assert.equal(calls.length, 0)
})

test('Скрытая связь аккаунта не выдаётся за отсутствие доступа к CRM', async () => {
  const { state, props, employee } = setup()
  await state.load()
  props.context.shell.menu = []
  state.employees.value = [employee]
  assert.equal(state.rows.value[0].connection, 'Нет данных')
  assert.equal(state.filterOptions.value.some(option => option.value === 'no-access' || option.value === 'invited'), false)
})


test('Ошибочная привязка владельца отменяется без удаления членства', async () => {
  const { state, calls, api, employee } = setup()
  api.linkEmployeeMember = async (organizationId, item, memberId) => calls.push(['linkEmployeeMember', organizationId, { id: item.id }, memberId])
  await state.load()
  let confirmationOpened = false
  state.confirmationDialog.value = { showModal() { confirmationOpened = true }, close() {} }
  state.openCard({ key: 'e', name: employee.name, email: 'contact@example.invalid', role: 'Владелец', connection: 'Подключён', employee: { ...employee, memberId: 'm' }, member: { id: 'm', isOwner: true, roleCode: 'owner' } })
  state.requestMemberAction('unlink')
  assert.equal(confirmationOpened, true)
  await state.confirmMemberAction()
  assert.deepEqual(calls.map(call => call[0]), ['linkEmployeeMember'])
  assert.equal(calls[0][3], null)
  assert.equal(calls[0][2].id, employee.id)
})

test('Отвязка недоступна при несохранённых изменениях и в режиме чтения', async () => {
  for (const readonly of [false, true]) {
    const { state, calls, employee } = setup(readonly)
    await state.load()
    state.confirmationDialog.value = { showModal() { throw Error('Недоступное действие') }, close() {} }
    state.openCard({ key: 'e', name: employee.name, email: '', role: '', connection: '', employee: { ...employee, memberId: 'm' } })
    if (!readonly) state.cardDraft.name = 'Изменённое имя'
    state.requestMemberAction('unlink')
    state.memberAction.value = 'unlink'
    await state.confirmMemberAction()
    assert.equal(calls.length, 0)
  }
})

test('Выбор владельца включает общее сохранение данных и привязки одним запросом', async () => {
  const { state, calls, employee } = setup()
  await state.load()
  state.openCard({ employee, name: employee.name, email: '', role: '', connection: '' })
  assert.equal(state.canSaveCard.value, false)
  state.cardAction.value = 'link'
  state.cardDraft.memberId = 'owner'
  assert.equal(state.canSaveCard.value, true)
  await state.saveCard()
  assert.deepEqual(calls, [['linkEmployeeMember', 'org', employee, 'owner']])
  assert.equal(state.selected.value, null)
})

test('Изменённые данные и привязка передаются вместе с исходной версией сотрудника', async () => {
  const { state, calls, employee } = setup()
  await state.load()
  state.openCard({ employee, name: employee.name, email: '', role: '', connection: '' })
  state.cardDraft.name = ' Новое имя '
  state.cardAction.value = 'link'
  state.cardDraft.memberId = 'owner'
  await state.saveCard()
  assert.deepEqual(calls, [['saveEmployee', 'org', { ...employee, name: 'Новое имя', memberIdToLink: 'owner' }]])
})

test('Ошибка общего сохранения сохраняет данные и выбор аккаунта для повторной попытки', async () => {
  const { state, calls, api, employee } = setup()
  await state.load()
  state.openCard({ employee, name: employee.name, email: '', role: '', connection: '' })
  state.cardDraft.name = 'Новое имя'
  state.cardAction.value = 'link'
  state.cardDraft.memberId = 'owner'
  api.saveEmployee = async () => { throw new Error('Ошибка данных или привязки') }
  await state.saveCard()
  assert.equal(state.formError.value, 'Ошибка данных или привязки')
  assert.equal(state.cardDraft.memberId, 'owner')
  assert.equal(state.cardDraft.name, 'Новое имя')
  assert.equal(state.canSaveCard.value, true)
  assert.equal(calls.length, 0)
  api.saveEmployee = async (...args) => calls.push(['saveEmployee', ...structuredClone(args)])
  await state.saveCard()
  assert.deepEqual(calls, [['saveEmployee', 'org', { ...employee, name: 'Новое имя', memberIdToLink: 'owner' }]])
  assert.equal(state.selected.value, null)
})

test('Пустой выбор аккаунта, отсутствие прав, отправка и режим чтения не разрешают привязку', async () => {
  for (const mode of ['empty', 'no-link-permission', 'saving', 'readonly']) {
    const { state, calls, employee } = setup(mode === 'readonly')
    await state.load()
    state.openCard({ employee, name: employee.name, email: '', role: '', connection: '' })
    state.cardAction.value = 'link'
    state.cardDraft.memberId = mode === 'empty' ? '' : 'owner'
    if (mode === 'no-link-permission') state.canLinkMembers.value = false
    if (mode === 'saving') state.saving.value = true
    else assert.equal(state.canSaveCard.value, false)
    await state.saveCard()
    assert.equal(calls.length, 0)
  }
})

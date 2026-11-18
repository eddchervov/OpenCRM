import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const filename = new URL('../Admin/admin-client/src/utils/adminSystemLabels.ts', import.meta.url)
const source = readFileSync(filename, 'utf8')
const code = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS }
}).outputText
const module = { exports: {} }
new Function('require', 'module', 'exports', code)(() => ({}), module, module.exports)
const labels = module.exports

test('Подписи организаций переключаются между EN и zh-Hans и сохраняют неизвестный сценарий', () => {
  const active = { isDeleted: false, isArchived: false, isActive: true }
  assert.equal(labels.getOrganizationStatusLabel(active, 'en'), 'Active')
  assert.equal(labels.getOrganizationStatusLabel(active, 'zh-Hans'), '已启用')
  assert.equal(labels.getOrganizationStatusLabel({ ...active, isArchived: true }, 'en'), 'Archived')
  assert.equal(labels.getOrganizationStatusLabel({ ...active, isDeleted: true }, 'zh-Hans'), '已删除')
  assert.equal(labels.getOrganizationScenarioLabel('service-station', 'Автосервис', 'en'), 'Auto repair shop')
  assert.equal(labels.getOrganizationScenarioLabel('new-scenario', 'Новое направление', 'zh-Hans'), 'Новое направление')
})

test('Все 11 видов техники переводятся по числовому коду, неизвестный сохраняет серверную подпись', () => {
  const kinds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 99]
  for (const locale of ['en', 'zh-Hans']) {
    assert.ok(kinds.every(kind => labels.getVehicleKindLabel(kind, `fallback-${kind}`, locale) !== `fallback-${kind}`))
  }

  assert.equal(labels.getVehicleKindLabel(1, 'Легковой автомобиль', 'en'), 'Passenger car')
  assert.equal(labels.getVehicleKindLabel(999, 'Новый вид техники', 'zh-Hans'), 'Новый вид техники')
})

test('Встроенные права переводятся по неизменным кодам, пользовательские права и группы сохраняют подписи', () => {
  const permissions = [
    ['Admin.Access', 'Доступ к административному модулю', 'Общее'],
    ['Admin.Dashboard.View', 'Просмотр дашборда', 'Дашборд'],
    ['Admin.Users.View', 'Просмотр пользователей', 'Пользователи'],
    ['Admin.Users.Manage', 'Управление пользователями', 'Пользователи'],
    ['Admin.Users.Impersonate', 'Вход от имени пользователя', 'Пользователи'],
    ['Admin.Billing.View', 'Просмотр платежей и подписок', 'Платежи'],
    ['Admin.Billing.Manage', 'Управление платежами и подписками', 'Платежи'],
    ['Admin.Content.View', 'Просмотр контента и обращений', 'Контент'],
    ['Admin.Content.Manage', 'Управление контентом и обращениями', 'Контент'],
    ['Admin.Communications.View', 'Просмотр сообщений и рассылок', 'Коммуникации'],
    ['Admin.Communications.Manage', 'Управление сообщениями и рассылками', 'Коммуникации'],
    ['Admin.Configuration.Manage', 'Управление настройками', 'Настройки']
  ].map(([code, name, group]) => ({ code, name, group }))
  const permissionCodes = permissions.map(permission => permission.code)

  for (const locale of ['en', 'zh-Hans']) {
    for (const permission of permissions) {
      assert.notEqual(labels.getPermissionNameLabel(permission, locale), permission.name)
      assert.notEqual(labels.getPermissionGroupLabel(permission, locale), permission.group)
    }
  }
  assert.deepEqual(permissions.map(permission => permission.code), permissionCodes)

  const custom = { code: 'Custom.Export', name: 'Экспорт клиента', group: 'Мои права' }
  assert.equal(labels.getPermissionNameLabel(custom, 'en'), custom.name)
  assert.equal(labels.getPermissionGroupLabel(custom, 'zh-Hans'), custom.group)
})

test('Организации и админские справочники используют локализованные подписи в интерфейсе', () => {
  const views = ['OrganizationsView.vue', 'VehicleCatalogView.vue', 'CatalogSuggestionsView.vue', 'SystemRolesView.vue']
    .map(name => readFileSync(new URL(`../Admin/admin-client/src/views/${name}`, import.meta.url), 'utf8'))
    .join('\n')

  assert.match(views, /statusLabel\(organization\)/)
  assert.match(views, /scenarioLabel\(organization\)/)
  assert.match(views, /kindLabel\(item\.kind, item\.kindText\)/)
  assert.match(views, /permissionNameLabel\(permission\)/)
  assert.doesNotMatch(views, /\{\{\s*organization\.statusName\s*\}\}/)
  assert.doesNotMatch(views, /\{\{\s*item\.kindText\s*\}\}/)
  assert.doesNotMatch(views, /\{\{\s*permission\.name\s*\}\}/)
})

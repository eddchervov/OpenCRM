import assert from 'node:assert/strict'
import test from 'node:test'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { fileURLToPath } from 'node:url'
import { i18n, translate } from '@opencrm/ui/i18n'
import '../packages/workspace/src/employeeMessages.ts'

const sourcePath = fileURLToPath(new URL('../packages/workspace/src/api/employees.ts', import.meta.url))
const source = ts.transpileModule(readFileSync(sourcePath, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const module = { exports: {} }
new Function('require', 'module', 'exports', source)(id => {
  if (id === '@opencrm/ui/i18n') return { translate }
  if (id === '../employeeMessages') return {}
  throw new Error(`Неизвестная зависимость: ${id}`)
}, module, module.exports)
const {
  assignMaintenanceEmployee,
  createEmployeeForMember,
  createEmployeeWithInvitation,
  linkEmployeeMember,
  loadEmployees,
  saveEmployee
} = module.exports

const apiCases = [
  ['loadEmployees', () => loadEmployees('org')],
  ['linkMember', () => linkEmployeeMember('org', { id: 'employee', version: 1, isActive: true, name: 'Employee' }, 'member')],
  ['saveEmployee', () => saveEmployee('org', { id: 'employee', version: 1, isActive: true, name: 'Employee' })],
  ['inviteEmployee', () => createEmployeeWithInvitation('org', { name: 'Employee', email: 'employee@example.invalid', roleCode: 'employee' })],
  ['createPerformer', () => createEmployeeForMember('org', { memberId: 'member', name: 'Employee' })],
  ['assignEmployee', () => assignMaintenanceEmployee('org', 'maintenance', 'employee')]
]

test('API сотрудников использует локализованные fallback errors и сохраняет серверную ошибку', async () => {
  const originalFetch = globalThis.fetch
  const originalDocument = globalThis.document
  globalThis.document = { documentElement: {}, querySelector: () => null }
  globalThis.fetch = async () => ({ ok: false, status: 503, json: async () => null })

  try {
    for (const locale of ['ru', 'en', 'zh-Hans']) {
      i18n.global.locale.value = locale
      for (const [key, invoke] of apiCases) {
        await assert.rejects(invoke(), error => {
          assert.equal(error.message, translate(`employeeApi.${key}`))
          if (key === 'loadEmployees') assert.equal(error.status, 503)
          return true
        })
      }
    }

    globalThis.fetch = async () => ({ ok: false, status: 400, json: async () => ({ message: 'Server supplied message' }) })
    i18n.global.locale.value = 'en'
    await assert.rejects(saveEmployee('org', { id: 'employee', version: 1, isActive: true, name: 'Employee' }), {
      message: 'Server supplied message'
    })
  } finally {
    globalThis.fetch = originalFetch
    if (originalDocument === undefined) delete globalThis.document
    else globalThis.document = originalDocument
    i18n.global.locale.value = 'ru'
  }
})

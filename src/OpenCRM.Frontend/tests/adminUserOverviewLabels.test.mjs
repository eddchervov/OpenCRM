import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import ts from 'typescript'

const source = readFileSync(new URL('../Admin/admin-client/src/utils/adminRecordLabels.ts', import.meta.url), 'utf8')
const code = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText
const module = { exports: {} }
new Function('module', 'exports', code)(module, module.exports)
const { getAdminRecordLabel, getAdminPaymentStatusLabel } = module.exports

test('Карточка переводит встроенные подписи по кодам и сохраняет пользовательские названия и неизвестные значения', () => {
  assert.equal(getAdminRecordLabel('timeline', 'vehicle', 'Техника', 'en'), 'Vehicle')
  assert.equal(getAdminRecordLabel('title', 'registration', 'Пользователь зарегистрировался', 'zh-Hans'), '用户已注册')
  assert.equal(getAdminRecordLabel('title', 'organization', 'Моя мастерская', 'en'), 'Моя мастерская')
  assert.equal(getAdminRecordLabel('maintenanceStatus', 2, 'InProgress', 'zh-Hans'), '进行中')
  assert.equal(getAdminRecordLabel('maintenanceStatus', undefined, 'Completed', 'en'), 'Completed')
  assert.equal(getAdminRecordLabel('maintenanceStatus', 999, 'Неизвестный статус', 'en'), 'Неизвестный статус')
  assert.equal(getAdminRecordLabel('paymentStatus', 'succeeded', 'Успешно в YooKassa', 'en'), 'Succeeded in YooKassa')
  assert.equal(getAdminRecordLabel('paymentMethod', 'workspace_extension:example', 'Продление организации', 'zh-Hans'), '组织续订')
  assert.equal(getAdminRecordLabel('supportType', 'question', 'Вопрос', 'en'), 'Question')
  assert.equal(getAdminRecordLabel('supportStatus', 'in_progress', 'В работе', 'zh-Hans'), '处理中')
  assert.equal(getAdminRecordLabel('role', 'owner', 'Владелец', 'en'), 'Owner')
  assert.equal(getAdminRecordLabel('role', undefined, 'Сотрудник', 'zh-Hans'), '员工')
  assert.equal(getAdminRecordLabel('role', 'custom', 'Своя роль', 'en'), 'Своя роль')
  assert.equal(getAdminRecordLabel('paymentMethod', 'yookassa_purchase', 'YooKassa: покупка', 'en'), 'YooKassa: purchase')
  assert.equal(getAdminRecordLabel('paymentMethod', 'yookassa_workspace_extension:example', 'YooKassa: продление организации', 'zh-Hans'), 'YooKassa：组织续订')
  assert.equal(getAdminPaymentStatusLabel({ paymentStatus: 'completed', applicationStatus: 'needs_attention', statusName: 'Требует проверки' }, 'en'), 'Needs review')
  assert.equal(getAdminPaymentStatusLabel({ paymentStatus: 'custom', statusName: 'Исходный статус' }, 'zh-Hans'), 'Исходный статус')
})

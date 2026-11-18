import assert from 'node:assert/strict'
import test from 'node:test'
import { i18n } from '@opencrm/ui/i18n'
import { translateWorkspaceAccessMessage } from '../packages/workspace/src/accessMessages.ts'

test('Сообщение ограничения доступа переводится, неизвестное сообщение сохраняется', () => {
  const originalLocale = i18n.global.locale.value
  try {
    for (const [locale, expected] of [
      ['en', 'You have read-only access to this organization.'],
      ['zh-Hans', '您对此组织仅有查看权限。']
    ]) {
      i18n.global.locale.value = locale
      assert.equal(translateWorkspaceAccessMessage('В этой организации у вас доступ только на просмотр.'), expected)
      assert.equal(translateWorkspaceAccessMessage('User supplied message'), 'User supplied message')
    }
  } finally {
    i18n.global.locale.value = originalLocale
  }
})

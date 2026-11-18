import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'

const components = [
  'WorkspaceNotificationSettings',
  'WorkspaceNotificationChannel',
  'WorkspaceVisitReminderSettings',
  'WorkspaceSettingsChoices'
]

test('Настройки уведомлений используют словарь и компилируют шаблоны', () => {
  for (const name of components) {
    const path = `../packages/workspace/src/components/${name}.vue`
    const { descriptor } = parse(readFileSync(new URL(path, import.meta.url), 'utf8'))
    compileScript(descriptor, { id: `notification-${name}` })
    const template = compileTemplate({ source: descriptor.template?.content ?? '', id: `notification-${name}`, filename: path })
    assert.deepEqual(template.errors, [], name)
    assert.doesNotMatch(descriptor.template?.content ?? '', /[А-Яа-яЁё]/, `${name}: видимый текст должен идти через словарь`)
  }
})

import assert from 'node:assert/strict'
import test from 'node:test'
import { getAssistantPreviewUrl, getAssistantUrl, getAssistantFrameUrl, isAssistantScenario } from '../packages/workspace/src/api/assistantAccess.mjs'

test('Страница предпросмотра помощника сохраняет префикс приложения и организацию', () => {
  assert.equal(getAssistantPreviewUrl('org-1', '/detailing/org-1'), '/assistant/org-1')
  assert.equal(getAssistantPreviewUrl('org-1', '/opencrm/autoservice/org-1/'), '/opencrm/assistant/org-1')
  assert.equal(getAssistantPreviewUrl('org one', '/opencrm/detailing/org%20one#billing'), '/opencrm/assistant/org%20one')
  const availability = { visible: true, enabled: true, organizationId: 'org-1', url: '/opencrm/assistant/org-1' }
  assert.equal(getAssistantUrl(availability, 'org-1', 'https://example.test', '/opencrm/detailing/org-1'), '/opencrm/assistant/org-1')
  assert.equal(getAssistantUrl({ ...availability, url: '/other/assistant/org-1' }, 'org-1', 'https://example.test', '/opencrm/detailing/org-1'), null)
})

test('Кнопка помощника доступна только для детейлинга и СТО', () => {
  assert.equal(isAssistantScenario('detailing'), true)
  assert.equal(isAssistantScenario('service-station'), true)
  assert.equal(isAssistantScenario('autoservice'), true)
  assert.equal(isAssistantScenario('fleet'), false)
  assert.equal(isAssistantScenario(null), false)
})

test('Ссылка появляется только при включённом доступе к той же организации', () => {
  const availability = {
    visible: true,
    enabled: true,
    organizationId: 'org-1',
    url: '/assistant/org-1'
  }

  assert.equal(getAssistantUrl(availability, 'org-1', 'https://example.test'), '/assistant/org-1')
  assert.equal(getAssistantUrl({ ...availability, enabled: false }, 'org-1', 'https://example.test'), null)
  assert.equal(getAssistantUrl({ ...availability, visible: false }, 'org-1', 'https://example.test'), null)
  assert.equal(getAssistantUrl(availability, 'org-2', 'https://example.test'), null)
})

test('Ссылка помощника не допускает внешний адрес или другой путь', () => {
  const availability = { visible: true, enabled: true, organizationId: 'org-1', url: 'https://other.test/assistant/org-1' }
  assert.equal(getAssistantUrl(availability, 'org-1', 'https://example.test'), null)
  assert.equal(getAssistantUrl({ ...availability, url: '/admin' }, 'org-1', 'https://example.test'), null)
})

test('Встроенная форма использует только проверенный адрес той же организации', () => {
  const availability = { visible: true, enabled: true, organizationId: 'org-1', url: '/assistant/org-1' }
  assert.equal(getAssistantFrameUrl(availability, 'org-1', 'https://example.test'), '/assistant/org-1/embedded')
  assert.equal(getAssistantFrameUrl({ ...availability, enabled: false }, 'org-1', 'https://example.test'), null)
  assert.equal(getAssistantFrameUrl({ ...availability, url: 'https://other.test/assistant/org-1' }, 'org-1', 'https://example.test'), null)
  assert.equal(getAssistantFrameUrl(availability, 'org-2', 'https://example.test'), null)
  assert.equal(getAssistantFrameUrl({ ...availability, url: '/other/assistant/org-1' }, 'org-1', 'https://example.test', '/opencrm/detailing/org-1'), null)
  const prefixed = { ...availability, url: '/opencrm/assistant/org-1' }
  assert.equal(getAssistantUrl(prefixed, 'org-1', 'https://example.test', '/opencrm/detailing/org-1'), '/opencrm/assistant/org-1')
  assert.equal(getAssistantFrameUrl(prefixed, 'org-1', 'https://example.test', '/opencrm/detailing/org-1'), '/opencrm/assistant/org-1/embedded')
})

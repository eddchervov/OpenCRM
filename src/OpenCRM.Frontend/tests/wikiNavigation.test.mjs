import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'
import test from 'node:test'
import { resolveWorkspaceWikiSlug, resolveWorkspaceWikiHref, workspaceCommonSectionCodes } from '../packages/workspace/src/commonSections.ts'

const scenarioSections = ['detailing', 'autoservice', 'fleet'].flatMap(scenario => {
  const source = readFileSync(new URL(`../Scenarios/${scenario}-client/src/sections.ts`, import.meta.url), 'utf8')
  return [...source.matchAll(/code: '([^']+)'/g)].map(match => match[1])
})

test('Справка всех разделов и форм ведёт на определённые адреса статей', () => {
  const sections = new Set([...scenarioSections, ...workspaceCommonSectionCodes, 'maintenance-journal', 'defects', 'counters'])
  for (const section of sections) {
    const slug = resolveWorkspaceWikiSlug(section)
    assert.ok(typeof slug === 'string' && slug.length > 0, `адрес статьи не определён: ${section}`)
  }

  for (const section of ['vehicles', 'maintenance']) {
    for (const [entityId, action] of [['new', ''], ['vehicle-id', 'edit'], ['vehicle-id', '']]) {
      const slug = resolveWorkspaceWikiSlug(section, entityId, action)
      assert.ok(typeof slug === 'string' && slug.length > 0, `${section}/${entityId}/${action}: адрес статьи не определён`)
      assert.equal(resolveWorkspaceWikiHref(section, entityId, action), `#wiki?slug=${encodeURIComponent(slug)}`)
    }
  }
})

test('Разделы рабочего места ведут на разные статьи, псевдоним журнала сохраняется', () => {
  const distinctSections = ['work-journal', 'statistics', 'finance', 'visualization', 'my-work', 'shifts', 'employees', 'billing', 'messages', 'assistant', 'inquiries']
  const articleSlugs = distinctSections.map(section => resolveWorkspaceWikiSlug(section))
  assert.equal(new Set(articleSlugs).size, distinctSections.length)
  assert.ok(!articleSlugs.includes('start'))
  assert.equal(resolveWorkspaceWikiSlug('maintenance-journal'), resolveWorkspaceWikiSlug('work-journal'))
})

test('Базовая поставка не содержит встроенных статей справки', () => {
  assert.ok(
    !existsSync(new URL('../../OpenCRM.WebApp/Models/Organizations/OrganizationWikiCatalog.cs', import.meta.url)),
    'статьи владельца не должны входить в поставку'
  )

  const provider = readFileSync(
    new URL('../../OpenCRM.WebApp/Models/Organizations/EmptyOrganizationWikiContentProvider.cs', import.meta.url),
    'utf8'
  )
  assert.match(provider, /s_empty/, 'поставщик содержимого по умолчанию должен быть пустым')
})

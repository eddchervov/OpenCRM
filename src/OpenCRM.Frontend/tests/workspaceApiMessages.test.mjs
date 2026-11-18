import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import '../packages/workspace/src/workspaceApiMessages.ts'
import { i18n, translate } from '@opencrm/ui/i18n'

const apiSources = [
  '../packages/workspace/src/api/workspaceAdmin.ts',
  '../Scenarios/autoservice-client/src/api/autoserviceBff.ts',
  '../Scenarios/detailing-client/src/api/detailingBff.ts',
  '../Scenarios/fleet-client/src/api/fleetBff.ts'
].map(path => readFileSync(new URL(path, import.meta.url), 'utf8'))

test('Каждый используемый fallback API ключ переведён на всех поддерживаемых языках', () => {
  const keys = [...new Set(apiSources.flatMap(source =>
    [...source.matchAll(/translate\('(workspaceApi\.[A-Za-z]+)'/g)].map(match => match[1])
  ))]
  assert.ok(keys.length >= 20)

  for (const locale of ['ru', 'en', 'zh-Hans']) {
    i18n.global.locale.value = locale
    for (const key of keys) {
      assert.notEqual(translate(key), key, `${key} отсутствует для ${locale}`)
    }
  }
})

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import vm from 'node:vm'

for (const name of ['vehicle-registration-plate', 'personal-currency']) {
  test(`${name}: собранный browser bundle запускается без Node process`, () => {
    const source = readFileSync(new URL(`../../OpenCRM.WebApp/wwwroot/app/${name}.bundle.js`, import.meta.url), 'utf8')
    const context = {
      Intl, console, setTimeout, clearTimeout,
      document: {
        cookie: '', documentElement: { lang: 'en' }, createElement: () => ({}),
        querySelectorAll: () => [], querySelector: () => null
      },
      window: {}, navigator: { language: 'en-US' }
    }

    assert.doesNotThrow(() => vm.runInNewContext(source, context, { timeout: 10_000 }))
  })
}

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const webRoot = new URL('../../OpenCRM.WebApp/', import.meta.url)
const list = readFileSync(new URL('Views/Vehicle/List.cshtml', webRoot), 'utf8')
const script = readFileSync(new URL('wwwroot/app/vehicles.js', webRoot), 'utf8')
const keyPrefix = 'vehicle.list.'

function resources(language) {
  const filename = language === 'ru'
    ? 'LocalizationStrings.resx'
    : `LocalizationStrings.${language}.resx`
  const xml = readFileSync(new URL(`Resources/Infrastructure/Localization/${filename}`, webRoot), 'utf8')
  return new Set([...xml.matchAll(/<data name="([^"]+)"[^>]*>/g)].map(([, key]) => key))
}

test('Vehicle list strings and script fallbacks use the three resource sets', () => {
  const languages = ['ru', 'en-US', 'zh-Hans']
  const resourceSets = languages.map(resources)
  const listKeysByLanguage = resourceSets.map(keys => [...keys].filter(key => key.startsWith(keyPrefix)).sort())
  assert.deepEqual(listKeysByLanguage[1], listKeysByLanguage[0])
  assert.deepEqual(listKeysByLanguage[2], listKeysByLanguage[0])

  for (const keys of resourceSets) {
    for (const [, key] of list.matchAll(/Text\["([^"]+)"\]/g)) {
      assert.ok(keys.has(key), `missing resource ${key}`)
    }
  }

  const dataAttributes = new Set(
    [...list.matchAll(/data-([\w-]+)="@Text\["([^"]+)"\]"/g)]
      .map(([, attribute]) => attribute.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase()))
  )
  for (const [, message] of script.matchAll(/messages\.([a-zA-Z]\w*)/g)) {
    assert.ok(dataAttributes.has(message), `List.cshtml is missing data-${message}`)
  }

  assert.doesNotMatch(list, /[А-Яа-яЁё]/)
  const scriptWithoutCommentLines = script.replace(/^\s*\/\/.*$/gm, '')
  assert.doesNotMatch(scriptWithoutCommentLines, /[А-Яа-яЁё]/)
})

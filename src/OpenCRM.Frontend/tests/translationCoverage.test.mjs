import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import test from 'node:test'
import vm from 'node:vm'
import ts from 'typescript'
import { localeCultures, messages } from '@opencrm/ui/i18n'

const directory = new URL('../packages/workspace/src/', import.meta.url)
const files = readdirSync(directory).filter(name => name.endsWith('Messages.ts'))
files.push('localization.ts', '../../../Admin/admin-client/src/localization.ts')
for (const name of readdirSync(new URL('../../../Admin/admin-client/src/', directory)).filter(name => name.endsWith('Messages.ts'))) {
  files.push(`../../../Admin/admin-client/src/${name}`)
}

function flatten(value, prefix = '', result = {}) {
  for (const [key, item] of Object.entries(value)) {
    const path = prefix ? `${prefix}.${key}` : key
    if (typeof item === 'string') result[path] = item
    else flatten(item, path, result)
  }
  return result
}

function parameters(value) {
  return [...new Set([...value.matchAll(/\{([\w]+)\}/g)].map(match => match[1]))].sort()
}

test('Общие компоненты содержат переводы всех ключей и параметров', () => {
  const russian = flatten(messages.ru)
  for (const locale of Object.keys(localeCultures)) {
    const translated = flatten(messages[locale])
    assert.deepEqual(Object.keys(translated).sort(), Object.keys(russian).sort(), locale)
    for (const key of Object.keys(russian)) {
      assert.ok(translated[key].trim(), `${locale}: ${key}`)
      assert.deepEqual(parameters(translated[key]), parameters(russian[key]), `${locale}: ${key}`)
    }
  }
})

function loadTranslations(file) {
  const locales = {}
  const source = readFileSync(new URL(file, directory), 'utf8')
  const { outputText } = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } })
  vm.runInNewContext(outputText, {
    exports: {},
    require: () => ({ registerMessages: (locale, messages) => { locales[locale] = flatten(messages) } })
  })

  return locales
}

for (const file of files) {
  test(`${file}: переводы содержат одинаковые ключи и параметры`, () => {
    const locales = loadTranslations(file)
    assert.deepEqual(Object.keys(locales).sort(), Object.keys(localeCultures).sort())
    const keys = Object.keys(locales.ru).sort()
    for (const locale of Object.keys(localeCultures).filter(locale => locale !== 'ru')) {
      assert.deepEqual(Object.keys(locales[locale]).sort(), keys, locale)
      for (const key of keys) {
        assert.ok(locales[locale][key].trim(), `${locale}: ${key}`)
        assert.deepEqual(parameters(locales[locale][key]), parameters(locales.ru[key]), `${locale}: ${key}`)
      }
    }
  })
}

test('Явные ключи перевода в интерфейсах существуют в словарях', () => {
  const keys = new Set(Object.keys(flatten(messages.ru)))
  for (const file of files) {
    for (const key of Object.keys(loadTranslations(file).ru)) keys.add(key)
  }

  function* sources(directory) {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = new URL(entry.name + (entry.isDirectory() ? '/' : ''), directory)
      if (entry.isDirectory()) yield* sources(path)
      else if (/\.(ts|vue)$/.test(entry.name) && !entry.name.endsWith('Messages.ts')) yield path
    }
  }

  const roots = ['packages/ui', 'packages/workspace', 'Scenarios/autoservice-client', 'Scenarios/detailing-client', 'Scenarios/fleet-client', 'Admin/admin-client']
  const missing = []
  for (const root of roots) {
    for (const path of sources(new URL(`../${root}/src/`, import.meta.url))) {
      const source = readFileSync(path, 'utf8')
      for (const [, key] of source.matchAll(/\b(?:t|translate)\(\s*['"]([^'"]+)['"]/g)) {
        if (!keys.has(key)) missing.push(`${path.pathname}: ${key}`)
      }
    }
  }

  assert.deepEqual(missing, [])
})

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { baseParse } from '@vue/compiler-dom'
import vm from 'node:vm'

const webRoot = new URL('../../OpenCRM.WebApp/', import.meta.url)
const view = readFileSync(new URL('Views/Vehicle/VehiclePersonal.cshtml', webRoot), 'utf8')
const script = readFileSync(new URL('wwwroot/app/vehicle-card.personal.js', webRoot), 'utf8')
const organizationView = readFileSync(new URL('Views/Vehicle/Index.cshtml', webRoot), 'utf8')
const organizationScript = readFileSync(new URL('wwwroot/app/vehicle-card.organization.js', webRoot), 'utf8')
const organizationPageScript = readFileSync(new URL('wwwroot/app/vehicle-card.js', webRoot), 'utf8')

test('Старые карточки форматируют пробег и компактные суммы по выбранной культуре', () => {
  for (const source of [script, organizationScript, organizationPageScript]) {
    let options
    function Vue(configuration) { options = configuration }
    Vue.component = () => {}
    Vue.options = { components: {} }
    const regional = { culture: 'en-GB' }
    const dataset = { cardUnitShortKilometers: 'km', cardUnitShortHours: 'h', cardPriceMillion: 'million' }
    vm.runInNewContext(source, {
      Vue, VueSelect: {}, window: { OpenCRMRegional: regional },
      getVehicleDisplayName: () => '',
      document: { getElementById: () => ({ dataset }) }
    })
    assert.equal(options.methods.formatCounterValue(12345.5, 1), '12,345.5 km')
    assert.equal(options.methods.formatCompactPrice(1250000), '1.3 million')
    regional.culture = 'ru-RU'
    assert.match(options.methods.formatCounterValue(12345.5, 1), /345,5 km$/)
    assert.equal(options.methods.formatCompactPrice(1250000), '1,3 million')
  }
})

test('Все JS-подписи карточек находятся внутри открывающего HTML-тега без дубликатов', () => {
  for (const [markup, scripts] of [[view, [script]], [organizationView, [organizationScript, organizationPageScript]]]) {
    const start = markup.indexOf('<div id="vehicle-form"')
    assert.ok(start >= 0)
    const end = markup.indexOf('>', start)
    const openingTag = markup.slice(start, end + 1).replace(/@Text\["([^"]+)"\]/g, '$1')
    const element = baseParse(`${openingTag}</div>`).children[0]
    const dataset = new Set()
    for (const attribute of element.props.filter(property => property.type === 6 && property.name.startsWith('data-'))) {
      assert.equal(attribute.name, attribute.name.toLowerCase(), attribute.name)
      dataset.add(attribute.name.slice(5).replace(/-([a-z])/g, (_, letter) => letter.toUpperCase()))
    }
    for (const source of scripts) {
      for (const [, property] of source.matchAll(/vehicleText\.([a-zA-Z]\w*)/g)) {
        assert.ok(dataset.has(property), `Missing dataset.${property} inside card element`)
      }
    }
  }
})

function resources(language) {
  const filename = language === 'ru'
    ? 'LocalizationStrings.resx'
    : `LocalizationStrings.${language}.resx`
  const xml = readFileSync(new URL(`Resources/Infrastructure/Localization/${filename}`, webRoot), 'utf8')
  return new Set([...xml.matchAll(/<data name="([^"]+)"[^>]*>/g)].map(([, key]) => key))
}

test('Personal vehicle card resource references exist in RU, EN, and zh-Hans', () => {
  const languageResources = ['ru', 'en-US', 'zh-Hans'].map(resources)
  const referencedKeys = new Set([...view.matchAll(/Text\["([^"]+)"\]/g)].map(([, key]) => key))
  for (const keys of languageResources) {
    for (const key of referencedKeys) {
      assert.ok(keys.has(key), `missing resource ${key}`)
    }
  }

  const resourceKeys = languageResources.map(keys => [...keys].filter(key => key.startsWith('vehicle.card.')).sort())
  assert.deepEqual(resourceKeys[1], resourceKeys[0])
  assert.deepEqual(resourceKeys[2], resourceKeys[0])
})

test('Organization vehicle card uses the same localized resource set', () => {
  const languageResources = ['ru', 'en-US', 'zh-Hans'].map(resources)
  const referencedKeys = new Set([...organizationView.matchAll(/Text\["([^"]+)"\]/g)].map(([, key]) => key))
  for (const keys of languageResources) {
    for (const key of referencedKeys) {
      assert.ok(keys.has(key), `missing resource ${key}`)
    }
  }

  const attributes = new Set([...organizationView.matchAll(/data-card-([\w-]+)="@Text\["[^"]+"\]"/g)]
    .map(([, name]) => {
      const property = name.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase())
      return `card${property[0].toUpperCase()}${property.slice(1)}`
    }))
  for (const [, property] of organizationScript.matchAll(/vehicleText\.([a-zA-Z]\w*)/g)) {
    assert.ok(attributes.has(property), `Index.cshtml is missing data attribute for ${property}`)
  }
  for (const [, property] of organizationPageScript.matchAll(/vehicleText\.([a-zA-Z]\w*)/g)) {
    assert.ok(attributes.has(property), `Index.cshtml is missing data attribute for ${property}`)
  }
})

test('Personal vehicle card script reads its labels and fallbacks from the Razor dataset', () => {
  const attributes = new Set([...view.matchAll(/data-card-([\w-]+)="@Text\["[^"]+"\]"/g)]
    .map(([, name]) => {
      const property = name.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase())
      return `card${property[0].toUpperCase()}${property.slice(1)}`
    }))
  for (const [, property] of script.matchAll(/vehicleText\.([a-zA-Z]\w*)/g)) {
    assert.ok(attributes.has(property), `VehiclePersonal.cshtml is missing data attribute for ${property}`)
  }

  const scriptWithoutCommentsAndConsole = script
    .replace(/^\s*\/\/.*$/gm, '')
    .replace(/console\.error\([^;]*\);/g, '')
  assert.doesNotMatch(scriptWithoutCommentsAndConsole, /[А-Яа-яЁё]/)
})

test('Organization vehicle card script has no Russian UI fallbacks', () => {
  for (const source of [organizationScript, organizationPageScript]) {
    const scriptWithoutCommentsAndConsole = source
    .replace(/^\s*\/\/.*$/gm, '')
    .replace(/console\.error\([^;]*\);/g, '')
    assert.doesNotMatch(scriptWithoutCommentsAndConsole, /[А-Яа-яЁё]/)
  }
})

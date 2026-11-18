import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import vm from 'node:vm'

const webRoot = new URL('../../OpenCRM.WebApp/', import.meta.url)
const script = readFileSync(new URL('wwwroot/app/vehicle-edit.js', webRoot), 'utf8')
const form = readFileSync(new URL('Views/Vehicle/_VehicleEditForm.cshtml', webRoot), 'utf8')
const pageFiles = ['VehicleCreatePersonal.cshtml', 'VehicleEditPersonal.cshtml']
const keyPrefix = 'vehicle.edit.'

function resources(language) {
  const filename = language === 'ru'
    ? 'LocalizationStrings.resx'
    : `LocalizationStrings.${language}.resx`
  const xml = readFileSync(new URL(`Resources/Infrastructure/Localization/${filename}`, webRoot), 'utf8')
  return new Map([...xml.matchAll(/<data name="([^"]+)"[^>]*>\s*<value>([\s\S]*?)<\/value>/g)]
    .map(([, key, value]) => [key, value.replaceAll('&quot;', '"').replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&amp;', '&')]))
}

function setup(language) {
  const localized = resources(language)
  const dataset = {}
  for (const [, attribute, key] of form.matchAll(/data-([\w-]+)="@Text\["([^"]+)"\]"/g)) {
    dataset[attribute.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase())] = localized.get(key)
  }

  let component
  function Vue(options) {
    component = options
  }
  Vue.component = () => {}
  Vue.options = { components: {} }

  vm.runInNewContext(script, {
    Vue,
    VueSelect: { VueSelect: {} },
    window: { vehicleEditPage: { mode: 'create' }, scrollTo() {} },
    document: {
      getElementById: () => ({ querySelector: () => ({ dataset }) })
    },
    getVehicleDisplayName() {},
    console: { error() {} }
  })

  return { component, dataset, localized }
}

test('Vehicle edit form labels and fallback errors come from all three resource sets', () => {
  const languages = ['ru', 'en-US', 'zh-Hans']
  const languageResources = languages.map(resources)
  const keysByLanguage = languageResources.map(items => [...items.keys()].filter(key => key.startsWith(keyPrefix)).sort())
  assert.deepEqual(keysByLanguage[1], keysByLanguage[0])
  assert.deepEqual(keysByLanguage[2], keysByLanguage[0])

  for (const language of languages) {
    const { component, dataset, localized } = setup(language)
    const data = component.data()
    assert.deepEqual(Array.from(data.vehicleKindOptions, option => option.value), [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 99])
    assert.equal(data.vehicleKindOptions[0].text, dataset.kindPassenger)
    assert.equal(data.mileageUnitOptions[0].text, dataset.mileageKilometers)
    for (const [, key] of form.matchAll(/["'](vehicle\.edit\.[\w.]+)["']/g)) {
      assert.ok(localized.has(key), `${language}: missing resource ${key}`)
    }

    const state = { errorMessage: '' }
    component.methods.handleApiError.call(state, null, dataset.errorCreate)
    assert.equal(state.errorMessage, dataset.errorCreate)
  }

  assert.equal(setup('en-US').dataset.kindPassenger, 'Passenger car')
  assert.equal(setup('zh-Hans').dataset.kindPassenger, '乘用车')
  const dataAttributes = new Set([...form.matchAll(/data-([\w-]+)=/g)].map(([, attribute]) => attribute.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase())))
  for (const [, message] of script.matchAll(/messages\.([a-zA-Z]\w*)/g)) {
    assert.ok(dataAttributes.has(message), `JS fallback is missing data-${message}`)
  }
})

test('Personal create and edit wrappers localize their titles and keep the shared plate mask bundle', () => {
  for (const pageFile of pageFiles) {
    const source = readFileSync(new URL(`Views/Vehicle/${pageFile}`, webRoot), 'utf8')
    assert.match(source, /Text\["vehicle\.edit\.pageTitle\./)
    assert.match(source, /vehicle-registration-plate\.bundle\.js[\s\S]*vehicle-edit\.js/)
  }
  assert.match(form, /v-registration-plate="vehicle"/)
  assert.match(form, /locale="@Text\["vehicle\.edit\.datePickerLocale"\]"/)
  assert.doesNotMatch(script, /[А-Яа-яЁё]/)
})

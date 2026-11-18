import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const filename = new URL('../packages/workspace/src/components/MaintenancePhotoComparison.vue', import.meta.url)
const { descriptor } = parse(readFileSync(filename, 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'maintenance-photo-comparison-test' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function createComparison() {
  const module = { exports: {} }
  const locale = vue.ref('ru')
  const translations = {
    'photoComparison.before': { ru: 'До', en: 'Before' },
    'photoComparison.after': { ru: 'После', en: 'After' }
  }
  const translate = key => translations[key]?.[locale.value] ?? key
  const resolve = id => {
    if (id === 'vue') return { ...vue, onMounted() {} }
    if (id === '@opencrm/ui') return { UiButton: {}, UiImagePreview: {}, UiLoader: {}, useLocalization: () => ({ t: translate }) }
    if (id === '../maintenanceMediaMessages') return {}
    if (id === '../api/maintenance') return { loadMaintenancePhotoContent: async () => new Blob() }
    throw new Error(`Unexpected dependency: ${id}`)
  }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const state = module.exports.default.setup({ photos: [], organizationId: 'org-1', organizationName: 'Organization', maintenanceId: 'maintenance-1' }, { expose() {}, emit() {} })
  return { state, locale }
}

test('Подписи до и после меняются вместе с языком интерфейса', async () => {
  const { state, locale } = createComparison()

  assert.deepEqual(state.choices.value.map(choice => choice.title), ['До', 'После'])
  locale.value = 'en'
  await vue.nextTick()
  assert.deepEqual(state.choices.value.map(choice => choice.title), ['Before', 'After'])
})

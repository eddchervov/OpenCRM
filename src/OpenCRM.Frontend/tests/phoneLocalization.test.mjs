import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const source = readFileSync(new URL('../packages/ui/src/components/UiPhoneInput.vue', import.meta.url), 'utf8')
const { descriptor } = parse(source)
const code = ts.transpileModule(compileScript(descriptor, { id: 'phone-localization' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

for (const value of ['+86 138 001', '+44 7700 900123']) {
  test(`Смена языка телефонного поля сохраняет введённый ${value}, страну, фокус и запрет редактирования`, async () => {
    const locale = vue.ref('ru')
    const props = vue.reactive({ modelValue: value, initialCountry: 'us', disabled: true })
    const instances = []
    const listeners = new Map()
    const emitted = []
    const hooks = {}
    const scope = vue.effectScope()
    const originalDocument = globalThis.document
    const input = vue.markRaw({
      value, selectionStart: 4, selectionEnd: 7, focusCount: 0,
      addEventListener(name, handler) {
        if (!listeners.has(name)) listeners.set(name, new Set())
        listeners.get(name).add(handler)
      },
      removeEventListener(name, handler) { listeners.get(name)?.delete(handler) },
      focus() { this.focusCount++ },
      setSelectionRange(start, end) { this.selectionStart = start; this.selectionEnd = end }
    })
    globalThis.document = { activeElement: input }
    const dependencies = {
      vue: {
        ...vue, useId: () => 'phone',
        useModel: () => vue.computed({ get: () => props.modelValue, set: value => { props.modelValue = value } }),
        onMounted: fn => { hooks.mount = fn }, onBeforeUnmount: fn => { hooks.unmount = fn }
      },
      '../i18n': {
        useLocalization: () => ({ t: key => key, locale }),
        localeCultures: { ru: 'ru-RU', en: 'en-US', 'zh-Hans': 'zh-CN' }
      },
      'intl-tel-input/i18n': { ru: { language: 'ru' }, en: { language: 'en' }, zh: { language: 'zh-Hans' } },
      'intl-tel-input/intlTelInputWithUtils': {
        default: (element, options) => {
          const instance = {
            options, country: options.initialCountry, destroyed: false,
            setNumber(number) { element.value = `formatted ${number}` },
            setDisabled(disabled) { this.disabled = disabled },
            getSelectedCountryData() { return { iso2: this.country } },
            getNumber() { return props.modelValue }, isValidNumber() { return false },
            destroy() { this.destroyed = true; element.value = '' }
          }
          instances.push(instance)
          return instance
        }
      }
    }
    try {
      const module = { exports: {} }
      new Function('require', 'module', 'exports', code)(id => dependencies[id] ?? {}, module, module.exports)
      const state = scope.run(() => module.exports.default.setup(props, { expose() {}, emit: (...args) => emitted.push(args) }))
      state.inputRef.value = input
      hooks.mount()
      instances[0].country = 'cn'
      input.value = value

      for (const language of ['en', 'zh-Hans', 'ru']) {
        const previous = instances.at(-1)
        locale.value = language
        await vue.nextTick()
        const current = instances.at(-1)
        assert.equal(previous.destroyed, true)
        assert.equal(current.options.i18n.language, language)
        assert.equal(current.options.initialCountry, 'cn')
        assert.equal(current.disabled, true)
        assert.equal(input.value, value)
        assert.equal(props.modelValue, value)
        assert.equal(input.selectionStart, 4)
        assert.equal(input.selectionEnd, 7)
        assert.equal(listeners.get('input').size, 1)
        assert.equal(listeners.get('countrychange').size, 1)
      }
      assert.equal(input.focusCount, 3)
      assert.deepEqual(emitted, [])
      hooks.unmount()
      assert.equal(instances.at(-1).destroyed, true)
      assert.equal(listeners.get('input').size, 0)
      assert.equal(listeners.get('countrychange').size, 0)
    } finally {
      scope.stop()
      globalThis.document = originalDocument
    }
  })
}

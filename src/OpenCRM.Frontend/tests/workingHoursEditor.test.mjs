import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { renderToString } from '@vue/server-renderer'
import { workingWeek, workingScheduleError } from '../packages/workspace/src/workingHours.ts'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/OrganizationWorkingHoursEditor.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'hours-editor', inlineTemplate: true }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

async function render(disabled = false) {
  const pickers = []
  const picker = vue.defineComponent({
    props: ['modelValue', 'label', 'disabled', 'required', 'step'],
    setup(props, { emit }) {
      pickers.push({ props, update: value => emit('update:modelValue', value) })
      return () => vue.h('input', { value: props.modelValue, disabled: props.disabled })
    }
  })
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => {
    if (id === 'vue') return vue
    if (id === '@opencrm/ui') return { UiTimePicker: picker, UiButton: { render() { return vue.h('button') } } }
    if (id === '../workingHours') return { workingWeek }
    return {}
  }, module, module.exports)
  const schedule = vue.reactive([
    { dayOfWeek: 1, isWorking: true, startTime: '08:00:00', endTime: '18:00:00', breaks: [{ startTime: '12:07:00', endTime: '13:08:00' }] },
    { dayOfWeek: 0, isWorking: false, startTime: null, endTime: null, breaks: [] }
  ])
  const html = await renderToString(vue.createSSRApp({
    render: () => vue.h(module.exports.default, { modelValue: schedule, disabled })
  }))
  return { pickers, schedule, html }
}

test('График использует общий picker для часов и перерывов с точностью до минуты', async () => {
  const { pickers, schedule } = await render()
  assert.equal(pickers.length, 4)
  assert.deepEqual(pickers.map(item => item.props.modelValue), ['08:00', '18:00', '12:07', '13:08'])
  assert.ok(pickers.every(item => item.props.step === 60 && item.props.required !== undefined))
  assert.equal(new Set(pickers.map(item => item.props.label)).size, 4)
  pickers[0].update('08:07')
  pickers[2].update('12:09')
  assert.equal(schedule[0].startTime, '08:07')
  assert.equal(schedule[0].breaks[0].startTime, '12:09')
  assert.equal(workingScheduleError(schedule), '')
  pickers[2].update('')
  assert.match(workingScheduleError(schedule), /перерывы/)
  assert.doesNotMatch(descriptor.template.content, /type="time"/)
})

test('Блокировка графика передаётся всем picker, выходной не создаёт редактируемых полей', async () => {
  const { pickers, html } = await render(true)
  assert.equal(pickers.length, 4)
  assert.ok(pickers.every(item => item.props.disabled === true))
  assert.match(html, /Выходной/)
})

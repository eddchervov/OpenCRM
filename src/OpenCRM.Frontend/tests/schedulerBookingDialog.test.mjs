import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

function createDialog({ dirty = true, busy = false } = {}) {
  const path = '../packages/workspace/src/components/SchedulerBookingDialog.vue'
  const { descriptor } = parse(readFileSync(new URL(path, import.meta.url), 'utf8'))
  assert.deepEqual(compileTemplate({ source: descriptor.template?.content ?? '', id: 'booking-dialog-test', filename: path }).errors, [])
  const code = ts.transpileModule(compileScript(descriptor, { id: 'booking-dialog-test' }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const module = { exports: {} }
  new Function('require', 'module', 'exports', 'document', 'HTMLElement', code)(
    id => {
      if (id === 'vue') return { ...vue, watch() {}, onBeforeUnmount() {}, useId: () => 'booking-dialog' }
      if (id === '@opencrm/ui') return { useLocalization: () => ({ t: key => key }) }
      if (id === '../schedulerControlMessages') return {}
      throw new Error('Не задана зависимость: ' + id)
    },
    module, module.exports, { activeElement: null }, class HTMLElement {}
  )
  const state = module.exports.default.setup({ open: true, busy, dirty }, { expose() {}, emit() {} })
  let closed = 0
  let focused = 0
  state.dialog.value = { close() { closed++ } }
  state.keepEditingButton.value = { focus() { focused++ } }
  state.editor.value = { querySelector: () => ({ focus() { focused++ } }) }
  return { state, closed: () => closed, focused: () => focused }
}

test('Изменённая запись требует подтверждения, продолжение возвращает фокус в редактор', async () => {
  const dialog = createDialog()
  assert.equal(dialog.state.dialogTitle.value, 'schedulerControl.newAppointment')
  await dialog.state.close()
  assert.equal(dialog.closed(), 0)
  assert.equal(dialog.state.discardPending.value, true)
  assert.equal(dialog.focused(), 1)
  await dialog.state.keepEditing()
  assert.equal(dialog.state.discardPending.value, false)
  assert.equal(dialog.closed(), 0)
  assert.equal(dialog.focused(), 2)
  await dialog.state.close()
  dialog.state.discard()
  assert.equal(dialog.closed(), 1)
})

test('Пустая запись закрывается сразу, сохранение блокирует закрытие', async () => {
  const clean = createDialog({ dirty: false })
  await clean.state.close()
  assert.equal(clean.closed(), 1)
  const saving = createDialog({ busy: true })
  await saving.state.close()
  assert.equal(saving.closed(), 0)
  assert.equal(saving.state.discardPending.value, false)
})

test('Escape отменяет подтверждение, не закрывая черновик', async () => {
  const dialog = createDialog()
  let prevented = false
  await dialog.state.close()
  dialog.state.onCancel({ preventDefault() { prevented = true } })
  await vue.nextTick()
  assert.equal(prevented, true)
  assert.equal(dialog.state.discardPending.value, false)
  assert.equal(dialog.closed(), 0)
})

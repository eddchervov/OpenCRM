import * as localization from '@opencrm/ui/i18n'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { bookingDayOffset, bookingEndMinute, describeOccupiedSlot, findBookingConflicts, timeMinutes } from '../packages/workspace/src/api/bookingSlots.ts'
import { formatServiceDuration, getUnknownServiceDurationSaveMessage } from '../packages/workspace/src/serviceDuration.ts'
import { createRouteScopedNotice } from '../packages/workspace/src/routeScopedNotice.ts'

const filename = new URL('../packages/ui/src/components/UiContextHint.vue', import.meta.url)
const source = readFileSync(filename, 'utf8')
const { descriptor } = parse(source, { filename: filename.pathname })
const compiled = ts.transpileModule(compileScript(descriptor, { id: 'context-hint' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function createHint(active = false) {
  const listeners = new Map()
  const document = {
    addEventListener: (name, listener) => listeners.set(name, listener),
    removeEventListener: name => listeners.delete(name)
  }
  const module = { exports: {} }
  const resolve = name => {
    if (name === '@opencrm/ui/i18n') return localization
    if (name === '../i18n') return { useLocalization: () => ({ t: localization.translate }) }
    if (name === '../bookingTimeMessages') return {}
    if (name === '@opencrm/ui/i18n') return localization
    if (name === '../bookingTimeMessages') return {}
    if (name === 'vue') return { ...vue, onBeforeUnmount() {} }
    if (name === '@floating-ui/vue') {
      return {
        autoUpdate() {},
        flip() {},
        offset() {},
        shift() {},
        useFloating: () => ({ floatingStyles: vue.ref({}) })
      }
    }
    throw new Error(`Неизвестная зависимость: ${name}`)
  }
  new Function('require', 'module', 'exports', 'document', compiled)(resolve, module, module.exports, document)
  const props = vue.reactive({ active, text: 'Контекстная подсказка', label: 'Почему это важно' })
  const state = module.exports.default.setup(props, { expose() {} })
  return { document, listeners, props, state }
}

const bookingFilename = new URL('../packages/workspace/src/components/BookingTimeFields.vue', import.meta.url)
const bookingSource = readFileSync(bookingFilename, 'utf8')
const { descriptor: bookingDescriptor } = parse(bookingSource, { filename: bookingFilename.pathname })
const bookingScript = compileScript(bookingDescriptor, { id: 'booking-time-fields-test' })
const bookingCode = ts.transpileModule(bookingScript.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const bookingRender = new Function('Vue', compileTemplate({
  source: bookingDescriptor.template.content,
  filename: bookingFilename.pathname,
  id: 'booking-time-fields-test',
  compilerOptions: { mode: 'function', bindingMetadata: bookingScript.bindings }
}).code)(vue)

function createHostNode(type, text = '') {
  return { type, text, props: {}, children: [], parent: null, focus() { this.focused = true } }
}

const renderer = vue.createRenderer({
  createElement: type => createHostNode(type),
  createText: text => createHostNode('#text', text),
  createComment: text => createHostNode('#comment', text),
  setText: (node, text) => { node.text = text },
  setElementText: (node, text) => { node.text = text; node.children = [] },
  patchProp: (node, key, _previous, value) => { node.props[key] = value },
  insert(node, parent, anchor = null) {
    if (node.parent) {
      const previousIndex = node.parent.children.indexOf(node)
      if (previousIndex >= 0) node.parent.children.splice(previousIndex, 1)
    }
    node.parent = parent
    const index = anchor ? parent.children.indexOf(anchor) : -1
    parent.children.splice(index < 0 ? parent.children.length : index, 0, node)
  },
  remove(node) {
    const index = node.parent?.children.indexOf(node) ?? -1
    if (index >= 0) node.parent.children.splice(index, 1)
    node.parent = null
  },
  parentNode: node => node.parent,
  nextSibling(node) {
    const siblings = node.parent?.children ?? []
    return siblings[siblings.indexOf(node) + 1] ?? null
  }
})

function createBooking({ duration, start = '', end = '' }) {
  const module = { exports: {} }
  const UiTimePicker = vue.defineComponent({
    props: ['modelValue', 'label', 'disabled', 'unavailableReason'],
    setup: () => () => vue.h('span')
  })
  const UiContextHint = vue.defineComponent({
    props: ['active', 'text', 'label'],
    setup: props => () => props.active ? vue.h('aside', { 'data-context-hint': 'true' }, props.text) : null
  })
  const resolve = name => {
    if (name === '@opencrm/ui/i18n') return localization
    if (name === '../i18n') return { useLocalization: () => ({ t: localization.translate }) }
    if (name === '../bookingTimeMessages') return {}
    if (name === 'vue') return vue
    if (name === '@opencrm/ui') return { UiTimePicker, UiContextHint }
    if (name === '../serviceDuration') return { formatServiceDuration }
    if (name === '../api/bookingSlots') return {
      bookingDayOffset, bookingEndMinute, describeOccupiedSlot, findBookingConflicts,
      loadOccupiedBookingSlots: async () => [], timeMinutes
    }
    throw new Error(`Неизвестная зависимость формы: ${name}`)
  }
  new Function('require', 'module', 'exports', 'setTimeout', 'clearTimeout', bookingCode)(resolve, module, module.exports, () => 1, () => {})
  module.exports.default.render = bookingRender

  const models = {
    start: vue.ref(start),
    end: vue.ref(end),
    endDate: vue.ref('2026-10-20'),
    duration: vue.ref(duration),
    component: vue.ref(null)
  }
  const Harness = vue.defineComponent({
    setup: () => () => vue.h(module.exports.default, {
      ref: models.component,
      organizationId: 'organization-1',
      date: '2026-10-20',
      durationMinutes: models.duration.value,
      location: '',
      autoDuration: true,
      start: models.start.value,
      'onUpdate:start': value => { models.start.value = value },
      end: models.end.value,
      'onUpdate:end': value => { models.end.value = value },
      endDate: models.endDate.value,
      'onUpdate:endDate': value => { models.endDate.value = value }
    })
  })
  const root = createHostNode('root')
  const app = renderer.createApp(Harness)
  app.mount(root)
  return { app, models, root }
}

function renderedText(node) {
  return node.text + node.children.map(renderedText).join('')
}

test('Подсказка открывается при появлении условия и не всплывает снова после закрытия', async () => {
  const { props, state } = createHint()
  assert.equal(state.isOpen.value, false)

  props.active = true
  await vue.nextTick()
  assert.equal(state.isOpen.value, true)

  state.dismiss()
  assert.equal(state.isOpen.value, false)
  props.active = true
  await vue.nextTick()
  assert.equal(state.isOpen.value, false)

  props.active = false
  await vue.nextTick()
  props.active = true
  await vue.nextTick()
  assert.equal(state.isOpen.value, true)
})

test('Повторный показ доступен кнопкой, Escape и клик снаружи закрывают подсказку', async () => {
  const { listeners, state } = createHint(true)
  assert.equal(state.isOpen.value, true)
  await vue.nextTick()
  assert.equal(listeners.has('keydown'), true)

  let focusCount = 0
  state.reference.value = { contains: () => false, focus: () => focusCount++ }
  const escape = { key: 'Escape', preventDefault() { this.prevented = true } }
  listeners.get('keydown')(escape)
  await vue.nextTick()
  assert.equal(escape.prevented, true)
  assert.equal(state.isOpen.value, false)
  assert.equal(focusCount, 1)

  state.toggle()
  await vue.nextTick()
  assert.equal(state.isOpen.value, true)
  listeners.get('pointerdown')({ target: {} })
  await vue.nextTick()
  assert.equal(state.isOpen.value, false)
  assert.equal(listeners.has('pointerdown'), false)
})

test('Пустая форма и неизвестная длительность сохраняются без лишних действий', async () => {
  const unknown = createBooking({ duration: null })
  await vue.nextTick()
  assert.ok(renderedText(unknown.root).includes('Время начала и окончания можно указать вручную; автоматический расчёт недоступен.'))
  assert.equal(await unknown.models.component.value.validate(), true)
  unknown.app.unmount()

  const empty = createBooking({ duration: undefined })
  await vue.nextTick()
  assert.doesNotMatch(renderedText(empty.root), /длительность не задана/)
  assert.equal(await empty.models.component.value.validate(), true)
  empty.app.unmount()
})

test('Подсказка отражает окончание и 60-минутную проверку при одном времени', async () => {
  const startOnly = createBooking({ duration: null, start: '10:00' })
  await vue.nextTick()
  assert.ok(renderedText(startOnly.root).includes('Для проверки занятости поста учитывается 60 минут от указанного начала; время окончания можно оставить пустым.'))
  assert.equal(await startOnly.models.component.value.validate(), true)
  startOnly.app.unmount()

  const manualEnd = createBooking({ duration: null, start: '10:00', end: '12:30' })
  await vue.nextTick()
  assert.ok(renderedText(manualEnd.root).includes('Указанное окончание сохранится без автоматического пересчёта.'))
  assert.equal(await manualEnd.models.component.value.validate(), true)
  manualEnd.app.unmount()
})

test('Многодневная запись требует оба времени и не обещает сохранение без окончания', async () => {
  const booking = createBooking({ duration: null })
  booking.models.endDate.value = '2026-10-21'
  await vue.nextTick()

  assert.ok(renderedText(booking.root).includes('Для многодневной записи необходимо вручную указать время начала и окончания.'))
  assert.equal(await booking.models.component.value.validate(), false)
  assert.equal(booking.models.component.value.getValidationError(), 'Укажите время начала и окончания многодневной записи.')
  booking.app.unmount()
})

test('Реальная ошибка интервала блокирует сохранение при неизвестной длительности', async () => {
  const booking = createBooking({ duration: null, start: '11:00', end: '10:00' })
  await vue.nextTick()

  assert.equal(await booking.models.component.value.validate(), false)
  assert.match(booking.models.component.value.getValidationError(), /Окончание должно быть позже начала/)
  booking.app.unmount()
})

test('Ручное окончание сохраняется, когда длительность выбранных услуг неизвестна', async () => {
  const booking = createBooking({ duration: 60, start: '10:00' })
  await vue.nextTick()
  assert.equal(booking.models.end.value, '11:00')

  booking.models.component.value.useManualEnd()
  booking.models.end.value = '13:30'
  booking.models.duration.value = null
  await vue.nextTick()
  await vue.nextTick()

  assert.equal(booking.models.end.value, '13:30')
  assert.equal(await booking.models.component.value.validate(), true)
  booking.app.unmount()
})

test('Сообщение после сохранения учитывает ручное окончание и не требует исправлять запись', () => {
  assert.match(getUnknownServiceDurationSaveMessage(true), /указанное время окончания сохранено/)
  assert.match(getUnknownServiceDurationSaveMessage(false), /автоматический расчёт окончания пропущен/)
  assert.doesNotMatch(getUnknownServiceDurationSaveMessage(false), /укажите|добавьте|исправьте/i)
})

test('Сообщение о сохранении переживает переход к записи и очищается при следующей навигации', () => {
  const notice = createRouteScopedNotice()
  const savedRoute = { section: 'maintenance', entityId: '42' }

  notice.show(savedRoute, 'Сохранено. Длительность не задана.')
  notice.navigate(savedRoute)
  assert.equal(notice.message.value, 'Сохранено. Длительность не задана.')

  notice.navigate({ section: 'clients' })
  assert.equal(notice.message.value, '')
})

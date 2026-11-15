import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const editorPath = new URL('../packages/workspace/src/components/MaintenanceServicesEditor.vue', import.meta.url)
const editorSource = readFileSync(editorPath, 'utf8')
const { descriptor } = parse(editorSource)
const editorCode = ts.transpileModule(compileScript(descriptor, { id: 'booking-validation-test' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function servicesEditor({ disabled = false, canAssignEmployees = true, service, services, defaultVatRateCode = 'without_vat' } = {}) {
  const items = services ?? [service ?? { name: 'Полировка кузова', catalogItemId: 'service-1', price: null, quantity: 1, vatRateCode: 'without_vat' }]
  const rows = items.map(() => ({ className: 'service-editor__row' }))
  const input = value => ({ value, disabled, scrolled: false, focused: false, scrollIntoView() { this.scrolled = true }, focus() { this.focused = true } })
  const fields = {
    '.service-editor__name': items.map(() => ({ scrolled: false, focused: false, scrollIntoView() { this.scrolled = true }, focus() { this.focused = true } })),
    '.service-editor__name input': items.filter(item => !item.catalogItemId).map(() => ({ scrolled: false, focused: false, scrollIntoView() { this.scrolled = true }, focus() { this.focused = true } })),
    '.service-editor__quantity input': items.map(item => input(String(item.quantity ?? 1))),
    '.service-editor__price input': items.map(item => input(item.price == null ? '' : String(Math.round(item.price / (item.quantity || 1) * 100) / 100))),
    '.service-editor__vat select': items.map(() => ({ scrolled: false, focused: false, scrollIntoView() { this.scrolled = true }, focus() { this.focused = true } }))
  }
  let invalid = null
  const root = {
    querySelector() { return invalid },
    querySelectorAll(selector) { return selector === '.service-editor__row' ? rows.slice(0, props.modelValue.length) : fields[selector] ?? [] }
  }
  const props = vue.reactive({ organizationId: 'org', modelValue: items, disabled, canAssignEmployees, defaultVatRateCode })
  const events = []
  const module = { exports: {} }
  const vueApi = { ...vue, useId: () => 'booking-validation-test', onMounted() {}, watch(source, callback, options) { if (options?.flush === 'sync') return vue.watch(source, callback, options) } }
  new Function('require', 'module', 'exports', editorCode)(id => {
    if (id === 'vue') return vueApi
    if (id === './EmployeePicker.vue' || id === '@opencrm/ui') return {}
    if (id === '../api/employees') return { loadEmployees: async () => ({ items: [] }) }
    if (id === '../api/scenarioCatalog') return { loadScenarioCatalog: async () => ({ services: [], serviceBundles: [] }) }
    if (id === '../serviceDuration') return { calculateServiceDuration: () => null, formatServiceDuration: () => '' }
    return {}
  }, module, module.exports)
  const state = module.exports.default.setup(props, { expose() {}, emit(event, value) { events.push([event, value]); if (event === 'update:modelValue') props.modelValue = value } })
  state.root.value = root
  function renderNumericInputs() {
    for (const [index, item] of props.modelValue.entries()) {
      fields['.service-editor__quantity input'][index] ??= input('')
      fields['.service-editor__price input'][index] ??= input('')
      fields['.service-editor__quantity input'][index].value = state.quantityDrafts.value.get(item) ?? String(item.quantity ?? 1)
      fields['.service-editor__price input'][index].value = state.priceDrafts.value.get(item)
        ?? (item.price == null ? '' : String(Math.round(item.price / (item.quantity || 1) * 100) / 100))
    }
  }
  return { state, props, root, events, fields, renderNumericInputs, setInvalid(value) { invalid = value } }
}

test('Пустая цена показывает сообщение у поля, называет услугу и прокручивает к ней', () => {
  const subject = servicesEditor()
  const result = subject.state.validate()

  assert.equal(result, 'Укажите цену услуги «Полировка кузова».')
  assert.equal(subject.state.priceErrors.value[0], 'Укажите цену услуги «Полировка кузова»')
  assert.equal(subject.fields['.service-editor__price input'][0].scrolled, true)
  assert.equal(subject.fields['.service-editor__price input'][0].focused, true)
  assert.match(editorSource, /:aria-invalid="priceErrors\[index\] \? 'true' : undefined"/)
  assert.match(editorSource, /:aria-describedby="priceErrors\[index\] \? `\$\{componentId\}-price-error-\$\{index\}` : undefined"/)
})

test('Повторная проверка очищает ошибку после исправления и не меняет данные при disabled', () => {
  const item = { name: 'Защитное покрытие', catalogItemId: 'service-2', price: null, quantity: 2, vatRateCode: 'without_vat' }
  const subject = servicesEditor({ disabled: true, service: item })
  assert.match(subject.state.validate(), /Защитное покрытие/)
  assert.equal(subject.state.priceErrors.value[0], 'Укажите цену услуги «Защитное покрытие»')
  assert.deepEqual(subject.props.modelValue[0], item)

  subject.props.modelValue[0].price = 5000
  assert.equal(subject.state.validate(), null)
  assert.deepEqual(subject.state.priceErrors.value, {})
  assert.equal(subject.props.modelValue[0].price, 5000)
})

test('Проверка disabled не подставляет ставку НДС в исходные данные', () => {
  const item = { name: 'Химчистка', catalogItemId: 'service-9', price: 100, quantity: 1, vatRateCode: null }
  const subject = servicesEditor({ disabled: true, service: item })

  assert.equal(subject.state.validate(), null)
  assert.deepEqual(subject.props.modelValue[0], item)
  assert.equal(subject.events.filter(([event]) => event === 'update:modelValue').length, 0)
})

test('Невалидная цена фокусируется на первом неверном поле при повторной проверке', () => {
  const subject = servicesEditor({ service: { name: 'Мойка', catalogItemId: 'service-3', price: -1, quantity: 1, vatRateCode: 'without_vat' } })
  const invalid = {
    closest(selector) { return selector === '.service-editor__row' ? subject.root.querySelectorAll('.service-editor__row')[0] : selector === '.service-editor__price' ? {} : null },
    scrolled: false,
    focused: false,
    scrollIntoView() { this.scrolled = true },
    focus() { this.focused = true }
  }
  subject.setInvalid(invalid)

  assert.equal(subject.state.validate(), 'Укажите корректную цену услуги «Мойка».')
  assert.equal(subject.state.priceErrors.value[0], 'Укажите корректную цену услуги «Мойка»')
  assert.equal(invalid.scrolled, true)
  assert.equal(invalid.focused, true)
})

test('Ошибки названия, количества и НДС остаются у полей и фокусируют первое неверное', () => {
  const cases = [
    { item: { name: '', catalogItemId: null, price: 100, quantity: 1, vatRateCode: 'without_vat' }, key: 'nameErrors', selector: '.service-editor__name input', message: /название работы/ },
    { item: { name: 'Мойка', catalogItemId: 'service-4', price: 100, quantity: 0, vatRateCode: 'without_vat' }, key: 'quantityErrors', selector: '.service-editor__quantity input', message: /количество/ },
    { item: { name: 'Химчистка', catalogItemId: 'service-5', price: 100, quantity: 1, vatRateCode: null }, key: 'vatErrors', selector: '.service-editor__vat select', message: /ставку НДС/ }
  ]

  for (const scenario of cases) {
    const subject = servicesEditor({ service: scenario.item, defaultVatRateCode: null })
    assert.match(subject.state.validate(), scenario.message)
    assert.ok(subject.state[scenario.key].value[0])
    assert.equal(subject.fields[scenario.selector][0].focused, true)
  }

  assert.match(editorSource, /:aria-invalid="nameErrors\[index\] \? 'true' : undefined"/)
  assert.match(editorSource, /:aria-invalid="quantityErrors\[index\] \? 'true' : undefined"/)
  assert.match(editorSource, /:aria-invalid="vatErrors\[index\] \? 'true' : undefined"/)
  assert.match(editorSource, /:aria-describedby="nameErrors\[index\] \? `\$\{componentId\}-name-error-\$\{index\}` : undefined"/)
  assert.match(editorSource, /:aria-describedby="quantityErrors\[index\] \? `\$\{componentId\}-quantity-error-\$\{index\}` : undefined"/)
  assert.match(editorSource, /:aria-describedby="vatErrors\[index\] \? `\$\{componentId\}-vat-error-\$\{index\}` : undefined"/)
  assert.match(editorSource, /\.service-editor input\[aria-invalid=true\]:focus, \.service-editor select\[aria-invalid=true\]:focus \{ outline:2px solid var\(--crm-danger-text/)
})

test('Удаление соседней строки очищает ошибки и сохраняет числовые черновики оставшейся работы', () => {
  const subject = servicesEditor({ services: [
    { name: 'Первая услуга', catalogItemId: 'service-6', price: null, quantity: 1, vatRateCode: 'without_vat' },
    { name: 'Вторая услуга', catalogItemId: 'service-7', price: null, quantity: 1, vatRateCode: 'without_vat' }
  ] })

  assert.match(subject.state.validate(), /Первая услуга/)
  assert.ok(subject.state.priceErrors.value[0])
  subject.state.updatePriceDraft(1, { target: { value: '1800' } })
  subject.state.updateQuantityDraft(1, { target: { value: '2' } })
  subject.state.removeItem(0)
  assert.equal(subject.props.modelValue.length, 1)
  assert.equal(subject.props.modelValue[0].name, 'Вторая услуга')
  assert.deepEqual(subject.state.priceErrors.value, {})
  subject.renderNumericInputs()
  assert.equal(subject.fields['.service-editor__price input'][0].value, '1800')
  assert.equal(subject.fields['.service-editor__quantity input'][0].value, '2')
  assert.equal(subject.state.validate(), null)
  assert.equal(subject.props.modelValue[0].price, 3600)
  assert.equal(subject.props.modelValue[0].quantity, 2)
})

test('Добавление работы сохраняет неверные числовые черновики и не позволяет отправить прежние значения', async () => {
  const subject = servicesEditor({ service: { name: 'Мойка', price: 100, quantity: 1, vatRateCode: 'without_vat' } })
  subject.state.updateQuantityDraft(0, { target: { value: '0' } })
  subject.state.updatePriceDraft(0, { target: { value: '-10' } })

  await subject.state.addCustom()
  subject.renderNumericInputs()

  assert.equal(subject.fields['.service-editor__quantity input'][0].value, '0')
  assert.equal(subject.fields['.service-editor__price input'][0].value, '-10')
  assert.match(subject.state.validate(), /количество/)
  assert.equal(subject.props.modelValue[0].price, 100)
})

test('Добавление услуги из каталога не сбрасывает введённую цену другой работы', () => {
  const subject = servicesEditor()
  subject.state.updatePriceDraft(0, { target: { value: '1700.25' } })
  subject.state.services.value = [{ id: 'new', name: 'Новая услуга', isActive: true, unitPrice: 200 }]
  subject.state.selectedServiceIds.value = ['new']
  subject.state.loading.value = false

  subject.state.addSelected()
  subject.renderNumericInputs()

  assert.equal(subject.fields['.service-editor__price input'][0].value, '1700.25')
  assert.equal(subject.state.validate(), null)
  assert.equal(subject.props.modelValue[0].price, 1700.25)
})

test('Замена заказа с тем же числом работ сбрасывает числовые черновики предыдущего заказа', () => {
  const subject = servicesEditor({ service: { name: 'Первый заказ', price: 100, quantity: 1, vatRateCode: 'without_vat' } })
  subject.state.updatePriceDraft(0, { target: { value: '777' } })
  subject.state.updateQuantityDraft(0, { target: { value: '2' } })

  subject.props.modelValue = [{ name: 'Другой заказ', price: 500, quantity: 1, vatRateCode: 'without_vat' }]
  subject.renderNumericInputs()

  assert.equal(subject.fields['.service-editor__price input'][0].value, '500')
  assert.equal(subject.fields['.service-editor__quantity input'][0].value, '1')
  assert.equal(subject.state.validate(), null)
  assert.equal(subject.props.modelValue[0].price, 500)
})

test('Изменение названия и общего НДС сохраняет числовой черновик именно своей строки', () => {
  const subject = servicesEditor({ services: [
    { name: 'Первая работа', price: 100, quantity: 1, vatRateCode: 'without_vat' },
    { name: 'Вторая работа', price: 200, quantity: 1, vatRateCode: 'without_vat' }
  ] })
  subject.state.updatePriceDraft(0, { target: { value: '-10' } })
  subject.state.updateQuantityDraft(1, { target: { value: '0' } })
  subject.state.patch(0, { name: 'Изменённая работа' })
  subject.state.allVatRate.value = '22'
  subject.state.setAllVat()
  subject.props.modelValue = [subject.props.modelValue[1], subject.props.modelValue[0]]
  subject.renderNumericInputs()

  assert.equal(subject.fields['.service-editor__quantity input'][0].value, '0')
  assert.equal(subject.fields['.service-editor__price input'][0].value, '200')
  assert.equal(subject.fields['.service-editor__quantity input'][1].value, '1')
  assert.equal(subject.fields['.service-editor__price input'][1].value, '-10')
  assert.equal(subject.props.modelValue[1].name, 'Изменённая работа')
  assert.ok(subject.props.modelValue.every(item => item.vatRateCode === '22'))
  assert.match(subject.state.validate(), /количество/)
})

test('Подтверждение цены сохраняет неверное количество, подтверждение количества сохраняет неверную цену', () => {
  for (const field of ['price', 'quantity']) {
    const subject = servicesEditor({ service: { name: 'Мойка', price: 100, quantity: 1, vatRateCode: 'without_vat' } })
    subject.state.updatePriceDraft(0, { target: { value: '-10' } })
    subject.state.updateQuantityDraft(0, { target: { value: '0' } })
    if (field === 'price') subject.state.setPrice(0, { target: { value: '1500.25', validity: { valid: true } } })
    else subject.state.setQuantity(0, { target: { value: '2', validity: { valid: true } } })
    subject.renderNumericInputs()

    assert.equal(subject.fields['.service-editor__price input'][0].value, field === 'price' ? '1500.25' : '-10')
    assert.equal(subject.fields['.service-editor__quantity input'][0].value, field === 'quantity' ? '2' : '0')
    assert.equal(subject.state[field === 'price' ? 'priceDrafts' : 'quantityDrafts'].value.size, 0)
    assert.match(subject.state.validate(), field === 'price' ? /количество/ : /цену/)
  }
})

test('Удаление самой редактируемой работы не переносит её числа на следующую', () => {
  const subject = servicesEditor({ services: [
    { name: 'Удаляемая', price: 100, quantity: 1, vatRateCode: 'without_vat' },
    { name: 'Оставшаяся', price: 200, quantity: 1, vatRateCode: 'without_vat' }
  ] })
  subject.state.updatePriceDraft(0, { target: { value: '777' } })
  subject.state.updateQuantityDraft(0, { target: { value: '2' } })
  subject.state.removeItem(0)
  subject.renderNumericInputs()

  assert.equal(subject.fields['.service-editor__price input'][0].value, '200')
  assert.equal(subject.fields['.service-editor__quantity input'][0].value, '1')
  assert.equal(subject.state.validate(), null)
  assert.equal(subject.props.modelValue[0].price, 200)
})

test('Правка поля сообщает форме об исправлении ошибки только если она была показана', () => {
  const subject = servicesEditor()
  subject.state.priceErrors.value = { 0: 'Укажите цену услуги «Полировка кузова»' }

  subject.state.clearFieldError(0, 'price')
  assert.deepEqual(subject.events.map(([event]) => event), ['validation-cleared'])
  subject.state.clearFieldError(0, 'price')
  assert.deepEqual(subject.events.map(([event]) => event), ['validation-cleared'])
})

test('Enter без blur сохраняет введённую дробную цену и количество в одной синхронной модели', () => {
  const subject = servicesEditor({ service: { name: 'Диагностика', catalogItemId: 'service-8', price: 1500, quantity: 1, vatRateCode: 'without_vat' } })
  subject.fields['.service-editor__price input'][0].value = '1700.25'
  subject.fields['.service-editor__quantity input'][0].value = '2'

  assert.equal(subject.state.validate(), null)
  assert.equal(subject.props.modelValue[0].price, 3400.5)
  assert.equal(subject.props.modelValue[0].quantity, 2)
  assert.equal(subject.events.filter(([event]) => event === 'update:modelValue').length, 1)
})

test('Ввод цены и количества переживает очистку ошибки и rerender до Enter', () => {
  const subject = servicesEditor({ service: { name: 'Диагностика', catalogItemId: 'service-11', price: null, quantity: 1, vatRateCode: 'without_vat' } })
  assert.match(subject.state.validate(), /Диагностика/)

  const priceInput = subject.fields['.service-editor__price input'][0]
  priceInput.value = '1700.25'
  subject.state.updatePriceDraft(0, { target: priceInput })
  const quantityInput = subject.fields['.service-editor__quantity input'][0]
  quantityInput.value = '2'
  subject.state.updateQuantityDraft(0, { target: quantityInput })
  subject.renderNumericInputs()

  assert.equal(priceInput.value, '1700.25')
  assert.equal(quantityInput.value, '2')
  assert.equal(subject.state.validate(), null)
  assert.equal(subject.props.modelValue[0].price, 3400.5)
  assert.equal(subject.props.modelValue[0].quantity, 2)
  assert.equal(subject.state.priceDrafts.value.size, 0)
  assert.equal(subject.state.quantityDrafts.value.size, 0)
  assert.match(editorSource, /:value="priceDrafts\.get\(item\) \?\? unitPrice\(item\)"[\s\S]*?@input="updatePriceDraft\(index, \$event\)"/)
  assert.match(editorSource, /:value="quantityDrafts\.get\(item\) \?\? item\.quantity \?\? 1"[\s\S]*?@input="updateQuantityDraft\(index, \$event\)"/)
})

test('Enter сохраняет введённую нулевую цену для строки с пустой исходной ценой', () => {
  const subject = servicesEditor({ service: { name: 'Диагностика', catalogItemId: 'service-10', price: null, quantity: 1, vatRateCode: 'without_vat' } })
  subject.fields['.service-editor__price input'][0].value = '0'

  assert.equal(subject.state.validate(), null)
  assert.equal(subject.props.modelValue[0].price, 0)
  assert.equal(subject.events.filter(([event]) => event === 'update:modelValue').length, 1)
})

test('Редактирование цены на Free сохраняет прежнее назначение сотрудника', () => {
  const subject = servicesEditor({
    canAssignEmployees: false,
    service: { name: 'Полировка', price: 1500, quantity: 1, vatRateCode: 'without_vat', employeeId: 'employee-1', employeeName: 'Тестовый сотрудник' }
  })
  subject.fields['.service-editor__price input'][0].value = '1600'

  assert.equal(subject.state.validate(), null)
  assert.equal(subject.props.modelValue[0].price, 1600)
  assert.equal(subject.props.modelValue[0].employeeId, 'employee-1')
  assert.equal(subject.props.modelValue[0].employeeName, 'Тестовый сотрудник')
})

test('Массовый НДС меняется только по подтверждению и сохраняет остальные данные услуг', () => {
  const services = [
    { name: 'Полировка', price: 1500, quantity: 1, vatRateCode: 'without_vat', employeeId: 'employee-1' },
    { name: 'Мойка', price: 500, quantity: 2, vatRateCode: '20', description: 'Тестовое описание' }
  ]
  const subject = servicesEditor({ services })
  let opened = 0
  let closed = 0
  subject.state.vatDialog.value = vue.markRaw({ showModal() { opened++ }, close() { closed++ } })
  subject.state.allVatRate.value = '5'
  subject.state.openVatDialog()
  assert.equal(opened, 1)
  assert.equal(subject.state.allVatRate.value, '')
  subject.state.allVatRate.value = '0'
  assert.deepEqual(subject.props.modelValue, services)
  assert.equal(subject.events.length, 0)

  subject.state.setAllVat()

  assert.deepEqual(subject.props.modelValue, services.map(item => ({ ...item, vatRateCode: '0' })))
  assert.equal(closed, 1)
})

test('Массовая смена НДС не применяется без ставки или в режиме чтения', () => {
  const services = [
    { name: 'Полировка', price: 1500, quantity: 1, vatRateCode: 'without_vat' },
    { name: 'Мойка', price: 500, quantity: 1, vatRateCode: '20' }
  ]
  const subject = servicesEditor({ services })
  subject.state.setAllVat()
  assert.equal(subject.events.length, 0)
  subject.props.disabled = true
  subject.state.allVatRate.value = '5'
  subject.state.setAllVat()
  assert.deepEqual(subject.props.modelValue, services)
  assert.equal(subject.events.length, 0)
})

test('Быстрые формы связывают ошибки госномера и телефона с соответствующими полями', () => {
  for (const scenario of ['detailing-client', 'autoservice-client']) {
    const source = readFileSync(new URL(`../Scenarios/${scenario}/src/components/QuickBookingSection.vue`, import.meta.url), 'utf8')
    assert.match(source, /:id="`\$\{formId\}-registration-plate`"[\s\S]*?:error="registrationPlateError"/)
    assert.match(source, /showFormError\(serviceValidationError, false, 'service'\)/)
    assert.match(source, /showFormError\(bookingTimes\.value\.getValidationError\(\), false, 'time'\)/)
    assert.match(source, /watch\(\(\) => \[form\.plannedDate, form\.plannedEndDate, form\.plannedStartTime, form\.plannedEndTime, form\.location\]/)
    assert.match(source, /watch\(selectedServices, \(\) => clearClientValidationSummary\('service'\)\)/)
    assert.match(source, /@validation-cleared="clearClientValidationSummary\('service'\)"/)
    assert.match(source, /formErrorSource\.value !== source/)
    assert.match(source, /clearClientValidationSummary\('date'\)/)
    assert.match(source, /function clearClientValidationSummary\([\s\S]*?if \(formErrorSource\.value !== source\) return[\s\S]*?errorMessage\.value = ''/)
    assert.match(source, /:id="`\$\{formId\}-planned-date`"[\s\S]*?:error="plannedDateError"/)
    assert.match(source, /focusField\(`\$\{formId\}-planned-date`\)/)
  }

  const autoServiceSource = readFileSync(new URL('../Scenarios/autoservice-client/src/components/QuickBookingSection.vue', import.meta.url), 'utf8')
  assert.match(autoServiceSource, /:id="`\$\{formId\}-client-phone`"[\s\S]*?:error="clientPhoneError"/)
  const phoneInputSource = readFileSync(new URL('../packages/ui/src/components/UiPhoneInput.vue', import.meta.url), 'utf8')
  assert.match(phoneInputSource, /error\?: string/)
  assert.match(phoneInputSource, /:aria-invalid="!!error \|\| undefined"/)
  assert.match(phoneInputSource, /:aria-describedby="error \? `\$\{fieldId\}-help` : undefined"/)
  assert.match(phoneInputSource, /<span v-if="error"[\s\S]*?role="alert">\{\{ error \}\}<\/span>/)
  const datePickerSource = readFileSync(new URL('../packages/ui/src/components/UiDatePicker.vue', import.meta.url), 'utf8')
  assert.match(datePickerSource, /:aria-invalid="!!error \|\| !!manualInputError \|\| undefined"/)
  assert.match(datePickerSource, /:aria-describedby="ariaDescribedBy"/)
  assert.match(datePickerSource, /<span v-if="error"[\s\S]*?role="alert">\{\{ error \}\}<\/span>/)
  const uiStyles = readFileSync(new URL('../packages/ui/src/styles.css', import.meta.url), 'utf8')
  assert.match(uiStyles, /\.ui-field input\[aria-invalid="true"\]/)
  assert.match(uiStyles, /\.ui-field input\[aria-invalid="true"\]:focus[\s\S]*?outline: 2px solid var\(--crm-danger-text/)
  assert.match(editorSource, /\.service-editor input\[aria-invalid=true\], \.service-editor select\[aria-invalid=true\]/)
})

test('Изменённые Vue-шаблоны компилируются', () => {
  const paths = [
    '../packages/workspace/src/components/MaintenanceServicesEditor.vue',
    '../Scenarios/detailing-client/src/components/QuickBookingSection.vue',
    '../Scenarios/autoservice-client/src/components/QuickBookingSection.vue',
    '../packages/ui/src/components/UiPhoneInput.vue',
    '../packages/ui/src/components/UiDatePicker.vue'
  ]

  for (const path of paths) {
    const filename = new URL(path, import.meta.url).pathname
    const source = readFileSync(new URL(path, import.meta.url), 'utf8')
    const { descriptor, errors } = parse(source, { filename })
    assert.deepEqual(errors, [], path)
    const compiled = compileTemplate({ source: descriptor.template.content, filename, id: 'booking-validation-template' })
    assert.deepEqual(compiled.errors, [], path)
  }
})

test('UiDatePicker синхронно очищает модель пустого поля, не подменяя непустой ввод', () => {
  const datePickerPath = new URL('../packages/ui/src/components/UiDatePicker.vue', import.meta.url)
  const { descriptor } = parse(readFileSync(datePickerPath, 'utf8'), { filename: datePickerPath.pathname })
  const datePickerCode = ts.transpileModule(compileScript(descriptor, { id: 'date-picker-test' }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const module = { exports: {} }
  const vueApi = {
    defineComponent: component => component,
    mergeModels: (props, model) => ({ ...props, ...model }),
    useModel(props) { return { get value() { return props.modelValue }, set value(value) { props.modelValue = value } } },
    computed: vue.computed,
    ref: value => ({ value }),
    useId: () => 'date-picker-test'
  }
  new Function('require', 'module', 'exports', datePickerCode)(id => {
    if (id === 'vue') return vueApi
    if (id === 'v-calendar') return { DatePicker: {} }
    if (id === '../dateOnly') return { formatDateOnly: value => value.toISOString(), parseDateOnly: value => new Date(value) }
    if (id === '../workspaceTheme') return { workspaceTheme: 'light' }
    return {}
  }, module, module.exports)
  const props = { modelValue: '2026-10-23', enableTimePicker: false }
  const state = module.exports.default.setup(props, { expose() {} })

  state.clearEmptyInput({ target: { value: '   ', setCustomValidity() {} } })
  assert.equal(props.modelValue, '')
  props.modelValue = '2026-10-23'
  state.clearEmptyInput({ target: { value: '23.10.2026', setCustomValidity() {} } })
  assert.equal(props.modelValue, '2026-10-23')
  const forwardedValues = []
  const events = state.guardImpossibleDateInput({
    input: event => forwardedValues.push(event.target.value),
    change: event => forwardedValues.push(event.target.value)
  })
  const makeInput = value => ({ value, customError: '', setCustomValidity(message) { this.customError = message } })
  const emptyInput = makeInput('')
  const validInput = makeInput('23.10.2026')
  const invalidInput = makeInput('31.02.2026')
  events.input({ target: emptyInput })
  events.input({ target: validInput })
  events.input({ target: invalidInput })
  assert.deepEqual(forwardedValues, ['', '23.10.2026'])
  assert.equal(props.modelValue, '')
  assert.equal(state.manualInputError.value, 'Введите существующую дату.')
  assert.equal(invalidInput.customError, 'Введите существующую дату.')
  events.change({ target: invalidInput })
  assert.deepEqual(forwardedValues, ['', '23.10.2026'])
  state.dateInput.value = invalidInput
  state.pickerValue.value = new Date(2026, 2, 3)
  assert.equal(state.manualInputError.value, '')
  assert.equal(invalidInput.customError, '')
  assert.equal(props.modelValue, new Date(2026, 2, 3).toISOString())
  const correctedInput = makeInput('28.02.2026')
  events.change({ target: correctedInput })
  assert.equal(state.manualInputError.value, '')
  assert.equal(correctedInput.customError, '')
  assert.match(descriptor.template.content, /@input="clearEmptyInput"[\s\S]*?v-on="guardImpossibleDateInput\(inputEvents\)"/)
})

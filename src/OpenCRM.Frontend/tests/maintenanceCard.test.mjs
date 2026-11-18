import { parseDateOnly } from '../packages/ui/src/dateOnly.ts'
import { formattingCulture } from '@opencrm/ui/i18n'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { getWorkdayProgress } from '../packages/workspace/src/utils/workdayCard.ts'
import { isSimpleMaintenanceWorkflow } from '../packages/workspace/src/utils/maintenanceWorkflow.ts'
import { formatWorkDuration, hasRecordedWorkTime } from '../packages/workspace/src/formatWorkDuration.ts'

const testMessages = {
  'maintenanceCard.completedFeminine': 'Выполнена',
  'maintenanceCard.noData': 'Нет данных',
  'maintenanceCard.completed': 'Выполнено',
  'maintenanceCard.ready': 'Готово',
  'maintenanceCard.maxHistoryError': 'Не удалось обновить историю MAX.',
  'maintenanceCard.noPaymentRequired': 'Оплата не требуется',
  'maintenanceCard.vatNotSpecified': 'Российский НДС не указан',
  'maintenanceCard.withoutVat': 'Без НДС',
  'maintenanceCard.russianVatRate': 'Российский НДС {rate} %',
  'maintenanceCard.historicalCurrencyReadOnly': 'Заказ сохранён в {orderCurrency}. В организации используется {workspaceCurrency}.'
}

function translate(key, parameters = {}) {
  return (testMessages[key] ?? key).replace(/\{([^}]+)\}/g, (_, name) => parameters[name] ?? `{${name}}`)
}

const filename = new URL('../packages/workspace/src/components/MaintenanceCardSection.vue', import.meta.url)
const { descriptor } = parse(readFileSync(filename, 'utf8'))
const script = compileScript(descriptor, { id: 'maintenance-card-test' })
const code = ts.transpileModule(script.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

function card({ saveError, maintenanceOverrides = {}, maxReader = async () => [], telegramReader = async () => [], photoReader = async () => [] } = {}) {
  const module = { exports: {} }
  const savedCommerce = []
  const maintenanceData = {
    id: 'order-1', vehicleId: 'vehicle-1', version: 7, status: 2, currencyCode: 'RUB', canEditServices: true, canManagePayments: true,
    maintenanceDetails: [
      { detailId: 'a', name: 'Мойка', price: 2000, quantity: 1, vatRateCode: '0' },
      { detailId: 'b', name: 'Полировка', price: 12000, quantity: 1, vatRateCode: 'without_vat' },
      { detailId: 'c', name: 'Материал', materialName: 'Шампунь' }
    ],
    ...maintenanceOverrides
  }
  const resolve = id => {
    if (id === '@opencrm/ui') return { parseDateOnly, useLocalization: () => ({ t: translate }) }
    if (id === '../maintenanceCardMessages') return {}
    if (Object.hasOwn(regionalTestDependencies, id)) return regionalTestDependencies[id]
    if (id === 'vue') return { ...vue, onMounted() {}, watch() {}, provide() {}, inject() {} }
    if (id === '../utils/workdayCard') return { getWorkdayProgress }
    if (id === '../utils/maintenanceWorkflow') return { isSimpleMaintenanceWorkflow }
    if (id === '../formatWorkDuration') return { formatWorkDuration, hasRecordedWorkTime }
    if (id === '../utils/vehicleBrandLogo') return { getVehicleBrandLogo: key => key === 'bmw' ? { name: 'BMW' } : null }
    if (id === '../api/maintenance') return {
      loadMaintenance: async () => maintenanceData,
      loadMaintenancePhotos: photoReader,
      saveMaintenanceCommerce: async (...args) => {
        savedCommerce.push(args)
        if (saveError) throw saveError
      }
    }
    if (id === '../api/vehicles') return { loadVehicle: async () => ({}) }
    if (id === '../api/maxNotifications' || id === '../api/telegramNotifications') {
      return { loadMaxMaintenanceNotifications: maxReader, loadTelegramMaintenanceNotifications: telegramReader }
    }
    return {}
  }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const props = vue.reactive({
    context: { organization: { id: 'org-1', currencyCode: 'RUB' }, shell: { scenario: { code: 'detailing' }, menu: [] }, workspaceAccess: {} },
    maintenanceId: 'order-1'
  })
  const state = module.exports.default.setup(props, { expose() {}, emit() {} })
  state.maintenance.value = maintenanceData
  return { state, props, savedCommerce }
}

test('Дата карточки следует выбранному формату и сохраняет календарный день', () => {
  const { state } = card()
  const previousCulture = formattingCulture.value
  try {
    for (const [culture, expected] of [
      ['ru-RU', '16.11.2026'], ['en-GB', '16/11/2026'],
      ['en-US', '11/16/2026'], ['zh-CN', '2026/11/16']
    ]) {
      formattingCulture.value = culture
      assert.equal(state.formatDate('2026-11-16T00:00:00Z'), expected)
    }
  } finally {
    formattingCulture.value = previousCulture
  }
})

test('Документы доступны в Free, общий запрет выгрузок сохраняется', () => {
  const { state, props } = card()
  props.context.workspaceAccess = { isFreePlan: true, exportsEnabled: true }
  state.maintenance.value.status = 3
  assert.equal(state.documentLinks.value.length, 6)
  assert.ok(state.documentLinks.value.every(link => link.isAvailable))
  props.context.workspaceAccess.exportsEnabled = false
  assert.ok(state.documentLinks.value.every(link => !link.isAvailable))
})

test('Free показывает приёмку без запроса фотографий, история загружается только по действию пользователя', async () => {
  const reads = []
  const { state, props } = card({ photoReader: async (...args) => { reads.push(args); return [{ photoId: 'saved', stage: 1 }] } })
  props.context.shell.scenario.modules = { intake: false, photos: false }
  await state.loadCard()
  assert.equal(state.isIntakePreview.value, true)
  assert.equal(reads.length, 0)
  assert.deepEqual(state.photos.value, [])
  await state.toggleIntakeHistory()
  assert.equal(reads.length, 1)
  assert.equal(state.isIntakePreview.value, false)
  assert.equal(state.photos.value[0].photoId, 'saved')
  await state.toggleIntakeHistory()
  assert.equal(state.isIntakePreview.value, true)
  assert.deepEqual(state.photos.value, [])
  assert.equal(reads.length, 1)
})

test('Запоздавшая загрузка истории приёмки не открывает данные другой карточки', async () => {
  let release
  const pendingPhotos = new Promise(resolve => { release = resolve })
  const { state, props } = card({ photoReader: () => pendingPhotos })
  props.context.shell.scenario.modules = { intake: false, photos: false }
  const pending = state.toggleIntakeHistory()
  props.maintenanceId = 'other-order'
  release([{ photoId: 'previous-card' }])
  await pending
  assert.equal(state.showIntakeHistory.value, false)
  assert.deepEqual(state.photos.value, [])
})

test('Free не загружает уведомления автоматически, сохранённая история открывается отдельно', async () => {
  const reads = []
  const { state, props } = card({
    maxReader: async () => { reads.push('max'); return [{ id: 'saved' }] },
    telegramReader: async () => { reads.push('telegram'); return [] }
  })
  props.context.shell.scenario.modules = { messaging: false }
  await state.loadCard()
  await state.loadNotificationHistory()
  assert.deepEqual(reads, [])
  await state.toggleNotificationHistory()
  assert.deepEqual(reads, ['max', 'telegram'])
  assert.equal(state.maxNotifications.value[0].id, 'saved')
  assert.equal(state.hasLockedNotifications.value, true)
  await state.toggleNotificationHistory()
  assert.deepEqual(state.maxNotifications.value, [])
  assert.equal(state.showNotificationHistory.value, false)
})

test('Закрытие сохранённой истории отменяет применение запоздавшего ответа', async () => {
  let release
  const delayed = new Promise(resolve => { release = resolve })
  const { state, props } = card({ maxReader: () => delayed })
  props.context.shell.scenario.modules = { messaging: false }
  const pending = state.toggleNotificationHistory()
  await state.toggleNotificationHistory()
  release([{ id: 'hidden' }])
  await pending
  assert.deepEqual(state.maxNotifications.value, [])
  assert.equal(state.notificationsLoading.value, false)
})

test('Сбой MAX не скрывает уведомления Telegram, обновление восстанавливает журнал', async () => {
  let unavailable = true
  const { state } = card({
    maxReader: async () => { if (unavailable) throw new Error('offline'); return [{ id: 2, status: 'sent' }] },
    telegramReader: async () => [{ id: 1, status: 'queued' }]
  })
  await state.loadNotificationHistory()
  assert.equal(state.telegramNotifications.value[0].id, 1)
  assert.match(state.maxNotificationError.value, /MAX/)
  assert.equal(state.notificationsLoading.value, false)
  unavailable = false
  await state.loadNotificationHistory()
  assert.equal(state.maxNotifications.value[0].id, 2)
  assert.equal(state.maxNotificationError.value, '')
})

test('Запоздавшая история уведомлений предыдущей организации не попадает в открытую карточку', async () => {
  let release
  const delayed = new Promise(resolve => { release = resolve })
  const { state, props } = card({ maxReader: () => delayed })
  const pending = state.loadNotificationHistory()
  props.context.organization.id = 'other-org'
  release([{ id: 3, body: 'Чужая история' }])
  await pending
  assert.deepEqual(state.maxNotifications.value, [])
})

test('Прогресс учитывает реальные операции услуг, исключая материалы и чужие строки', () => {
  const { state } = card()
  state.workProgress.value = [{ detailId: 'a', status: 4 }, { detailId: 'other', status: 4 }]
  assert.equal(state.progress.value.completed, 1)
  assert.equal(state.progress.value.total, 2)
  assert.equal(state.workStatusName(state.serviceDetails.value[0]), 'Выполнена')
  state.workProgress.value = null
  assert.equal(state.workStatusName(state.serviceDetails.value[0]), 'Нет данных')
  assert.equal(state.progress.value.completed, 0)
})

test('Выданный Free-заказ без отслеживания операций не показывает фиктивный прогресс', () => {
  const { state } = card()
  state.maintenance.value.status = 3
  state.maintenance.value.handedOverAt = '2026-10-29T11:33:00Z'
  state.workProgress.value = [{ detailId: 'a', status: 1, version: 0, hasAssignedEmployee: false }]
  assert.equal(state.showWorkProgress.value, false)
  assert.equal(state.currentStage.value, 3)
  assert.equal(state.workProgress.value[0].status, 1)
})

test('Реальное назначение и сохранённая история работ остаются видимыми на Free', () => {
  const { state, props } = card()
  props.context.shell.scenario.modules = { members: false }
  state.workProgress.value = [{ detailId: 'a', status: 1, version: 0, hasAssignedEmployee: true }]
  assert.equal(state.showWorkProgress.value, true)
  state.workProgress.value = [{ detailId: 'a', status: 4, version: 1, hasAssignedEmployee: false }]
  assert.equal(state.showWorkProgress.value, true)
  assert.equal(state.progress.value.completed, 1)
  state.workProgress.value = [{ detailId: 'other', status: 4, version: 1 }]
  assert.equal(state.showWorkProgress.value, false)
})

test('Простой процесс сохраняет скрытие счётчика даже с историей операций', () => {
  const { state, props } = card()
  props.context.organization.primaryScenarioCode = 'detailing'
  props.context.organization.maintenanceWorkflowMode = 'simple'
  state.workProgress.value = [{ detailId: 'a', status: 4, version: 1 }]
  assert.equal(state.showWorkProgress.value, false)
})

test('Этапы отличают старое завершение, готовность, выдачу и отмену', () => {
  const { state } = card()
  assert.equal(state.currentStage.value, 1)
  state.maintenance.value.status = 3
  assert.equal(state.recordStages.value[2].title, 'Выполнено')
  state.maintenance.value.readyForHandoverAt = '2026-10-17T12:00:00Z'
  assert.equal(state.recordStages.value[2].title, 'Готово')
  state.maintenance.value.handedOverAt = '2026-10-17T13:00:00Z'
  assert.equal(state.currentStage.value, 3)
  state.maintenance.value.status = 4
  assert.equal(state.currentStage.value, -1)
})

test('Нулевой оплачиваемый итог имеет отдельную подпись', () => {
  const { state } = card()
  assert.equal(state.getPaymentStatusName('no_payment_required'), 'Оплата не требуется')
})

test('Логотип использует ключ марки, а режим чтения сохраняет запрет редактирования', () => {
  const { state, props } = card()
  state.vehicle.value = { brandName: 'BMW', modelName: 'X3' }
  assert.equal(state.brandLogo.value, null)
  state.vehicle.value.brandLogoKey = 'bmw'
  assert.deepEqual(state.brandLogo.value, { name: 'BMW' })
  assert.equal(state.canEditServices.value, true)
  props.context.workspaceAccess.isReadOnly = true
  assert.equal(state.canEditServices.value, false)
})

test('Историческая валюта закрывает редактирование услуг и НДС до API-запроса', async () => {
  const { state, props, savedCommerce } = card({ maintenanceOverrides: { canChangeFinancialData: false } })
  props.context.organization.currencyCode = 'CNY'
  await state.loadCard()

  assert.equal(state.canWrite.value, false)
  assert.equal(state.canEditServices.value, false)
  assert.equal(state.canManageVat.value, false)
  assert.match(state.readOnlyMessage.value, /RUB.*CNY/)

  state.editServices()
  state.editVat()
  await state.saveServices()
  await state.saveVat()

  assert.equal(state.editingServices.value, false)
  assert.equal(state.editingVat.value, false)
  assert.equal(savedCommerce.length, 0)
})

test('Сохранение валидных услуг в существующей записи доходит до API', async () => {
  const { state, savedCommerce } = card()
  await state.loadCard()
  let closed = 0
  state.servicesDialog.value = { showModal() {}, close() { closed++ } }
  state.editServices()
  state.serviceEditor.value = { validate: () => null }

  await state.saveServices()

  assert.equal(savedCommerce.length, 1)
  assert.equal(savedCommerce[0][2], 'services')
  assert.deepEqual(savedCommerce[0][3].details.map(detail => [detail.detailId, detail.vatRateCode]), [
    ['a', '0'],
    ['b', 'without_vat']
  ])
  assert.equal(closed, 1)
  assert.equal(state.editingServices.value, false)
})

test('Отмена услуг закрывает окно без сохранения, повторное открытие восстанавливает данные', () => {
  const { state, savedCommerce } = card()
  let opened = 0
  let closed = 0
  state.servicesDialog.value = { showModal() { opened++ }, close() { closed++ } }
  state.editServices()
  state.serviceDraft.value[0].name = 'Несохранённая услуга'

  state.closeServices()

  assert.equal(state.editingServices.value, false)
  assert.equal(closed, 1)
  assert.equal(savedCommerce.length, 0)
  assert.equal(state.maintenance.value.maintenanceDetails[0].name, 'Мойка')
  state.editServices()
  assert.equal(opened, 2)
  assert.equal(state.serviceDraft.value[0].name, 'Мойка')
})

test('Окно услуг не закрывается во время отправки, ошибка сохраняет черновик', async () => {
  const { state } = card({ saveError: new Error('Не удалось сохранить') })
  let closed = 0
  state.servicesDialog.value = { showModal() {}, close() { closed++ } }
  state.editServices()
  state.serviceDraft.value[0].name = 'Изменённая услуга'
  state.savingServices.value = true
  state.closeServices()
  assert.equal(closed, 0)
  assert.equal(state.editingServices.value, true)
  state.savingServices.value = false
  state.serviceEditor.value = { validate: () => null }

  await state.saveServices()

  assert.equal(closed, 0)
  assert.equal(state.editingServices.value, true)
  assert.equal(state.serviceDraft.value[0].name, 'Изменённая услуга')
  assert.equal(state.serviceError.value, 'Не удалось сохранить')
  assert.equal(state.savingServices.value, false)
})

test('Неизвестный НДС не превращается в процент или освобождение от налога', async () => {
  const { state, savedCommerce } = card()
  assert.equal(state.formatVatRate('none'), 'Российский НДС не указан')
  assert.equal(state.formatVatRate(null), 'Российский НДС не указан')
  assert.equal(state.formatVatRate('without_vat'), 'Без НДС')
  assert.equal(state.formatVatRate('0'), 'Российский НДС 0 %')
  state.maintenance.value.maintenanceDetails[0].vatRateCode = 'none'
  state.editVat()
  assert.equal(state.vatDraft.value[0].vatRateCode, '')

  await state.saveVat()

  assert.equal(savedCommerce.length, 0)
  assert.ok(state.vatError.value)
  assert.equal(state.editingVat.value, true)
})

test('Общая ставка заполняет все платные позиции и сохраняет индивидуальные исключения', async () => {
  const { state, savedCommerce } = card()
  state.maintenance.value.maintenanceDetails.push({ name: 'Расходный материал', price: 300, materialName: 'Состав', vatRateCode: '22' })
  state.editVat()
  assert.equal(state.commonVatRate.value, '')
  state.commonVatRate.value = '22'
  state.applyCommonVatRate()
  assert.deepEqual(state.vatDraft.value.map(line => line.vatRateCode), ['22', '22', '22'])
  state.vatDraft.value[1].vatRateCode = '5'

  await state.saveVat()

  assert.equal(savedCommerce.length, 1)
  assert.equal(savedCommerce[0][2], 'vat-rates')
  assert.deepEqual(savedCommerce[0][3], { version: 7, lines: [
    { lineIndex: 0, vatRateCode: '22' },
    { lineIndex: 1, vatRateCode: '5' },
    { lineIndex: 2, vatRateCode: '22' }
  ] })
  assert.equal(state.editingVat.value, false)
})

test('Отмена НДС не меняет обслуживание и повторное открытие сбрасывает черновик', () => {
  const { state, savedCommerce } = card()
  state.editVat()
  state.commonVatRate.value = '22'
  state.applyCommonVatRate()
  state.closeVat()
  assert.equal(state.editingVat.value, false)
  assert.equal(savedCommerce.length, 0)
  state.editVat()
  assert.deepEqual(state.vatDraft.value.map(line => line.vatRateCode), ['0', 'without_vat'])
})

test('Нулевой или некорректный счётчик не отображается как учтённое время', () => {
  for (const value of [undefined, null, '', '00:00:00', '0.00:00:00.0000000', '00:00:00.9', '-00:01:00', '00:60:00', 'неизвестно']) {
    assert.equal(hasRecordedWorkTime(value), false)
  }
  for (const value of ['00:00:01', '00:05:00', '01:00:00', '1.00:00:00']) {
    assert.equal(hasRecordedWorkTime(value), true)
  }
})


test('Отмена обнуляет итог карточки, сохраняя смету и оплату для повторного открытия', () => {
  const { state } = card()
  Object.assign(state.maintenance.value, { status: 4, grossAmount: 1000, discountAmount: 100, paidAmount: 200 })
  assert.equal(state.netAmount.value, 0)
  assert.equal(state.maintenance.value.grossAmount, 1000)
  assert.equal(state.maintenance.value.paidAmount, 200)
  state.maintenance.value.status = 1
  assert.equal(state.netAmount.value, 900)
})

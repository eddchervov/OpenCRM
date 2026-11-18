import test from 'node:test'
import assert from 'node:assert/strict'
import { i18n } from '../packages/ui/src/i18n.ts'
import { importRowMessage, knownMessageCodes } from '../packages/workspace/src/importRowMessage.ts'
import { knownImportBusinessMessageCodes } from '../packages/workspace/src/importBusinessMessages.ts'

test('import row messages follow the active locale and interpolate parameters', () => {
  const previousLocale = i18n.global.locale.value
  try {
    const row = {
      message: 'Проверьте название и год.',
      messageCode: 'vehicleInvalid',
      messageParameters: { maxYear: 2026 }
    }

    i18n.global.locale.value = 'en'
    assert.equal(importRowMessage(row), 'Check the name, make and model, VIN, country code, plate, and year (1900–2026). HTML is not allowed.')
    i18n.global.locale.value = 'zh-Hans'
    assert.equal(importRowMessage(row), '请检查名称、品牌和型号、VIN、国家代码、车牌及年份（1900–2026）。不允许 HTML。')
  } finally {
    i18n.global.locale.value = previousLocale
  }
})

test('legacy rows map exact known Russian messages and preserve unknown content', () => {
  const previousLocale = i18n.global.locale.value
  try {
    i18n.global.locale.value = 'en'
    assert.equal(importRowMessage({ message: 'Создано.' }), 'Created.')
    assert.equal(importRowMessage({ message: 'server says: invalid row' }), 'server says: invalid row')
    assert.equal(importRowMessage({ message: 'custom message', messageCode: 'futureCode' }), 'custom message')
    assert.equal(importRowMessage({ message: '' }), '')
  } finally {
    i18n.global.locale.value = previousLocale
  }
})

test('every known row message code has Russian, English, and Simplified Chinese translations', () => {
  for (const locale of ['ru', 'en', 'zh-Hans']) {
    const rowMessages = i18n.global.getLocaleMessage(locale).importSection.rowMessages
    assert.deepEqual(Object.keys(rowMessages).sort(), [...knownMessageCodes].sort(), `${locale} row message coverage`)
  }
})

test('business validation details translate independently and preserve unknown reasons', () => {
  const previousLocale = i18n.global.locale.value
  try {
    const row = {
      message: 'Старое полное сообщение на русском.',
      messageDetails: [
        { message: 'Укажите дату заказа в формате ГГГГ-ММ-ДД или ISO 8601.', messageCode: 'orderDateInvalid' },
        { message: 'Укажите внешний идентификатор заказа.', messageCode: 'orderExternalIdRequired' },
        { message: 'Пользовательская ошибка импорта.' }
      ]
    }

    i18n.global.locale.value = 'en'
    assert.equal(importRowMessage(row), 'Enter the order date as YYYY-MM-DD or ISO 8601. · Enter the external order identifier. · Пользовательская ошибка импорта.')
    i18n.global.locale.value = 'zh-Hans'
    assert.equal(importRowMessage(row), '请输入 YYYY-MM-DD 或 ISO 8601 格式的订单日期。 · 请输入订单外部 ID。 · Пользовательская ошибка импорта.')
  } finally {
    i18n.global.locale.value = previousLocale
  }
})

test('business validation catalog covers every known code in all locales', () => {
  for (const locale of ['ru', 'en', 'zh-Hans']) {
    const businessMessages = i18n.global.getLocaleMessage(locale).importBusiness
    assert.deepEqual(Object.keys(businessMessages).sort(), [...knownImportBusinessMessageCodes].sort(), `${locale} business message coverage`)
  }
})

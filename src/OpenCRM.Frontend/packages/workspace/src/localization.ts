import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const regionalMessages = {
  ru: {
    waitFormats: 'Дождитесь загрузки форматов госномеров.', registrationCountry: 'Страна регистрации', unknownCountry: 'Не указана — прежние правила номера',
    plate: 'Регистрационный номер', freePlate: 'Номер в свободном формате', otherPlate: 'Другой формат номера',
    plateLength: 'Номер не должен быть длиннее 24 символов.', plateCharacters: 'Используйте буквы, цифры, пробелы, дефисы и среднюю точку.',
    plateShort: 'Номер должен содержать не менее двух букв или цифр.',
    platePaste: 'Вставленный номер не подходит к маске. Проверьте его или включите «Другой формат».',
    plateMask: 'Номер не подходит к маске. Проверьте его или снова включите «Другой формат».',
    plateLoad: 'Не удалось загрузить форматы номеров.', notConfigured: 'Не настроен'
  },
  en: {
    waitFormats: 'Wait for registration plate formats to load.', registrationCountry: 'Country of registration', unknownCountry: 'Not specified — existing plate rules',
    plate: 'Registration plate', freePlate: 'Enter a registration plate', otherPlate: 'Other plate format',
    plateLength: 'The plate must not exceed 24 characters.', plateCharacters: 'Use letters, numbers, spaces, hyphens and middle dots.',
    plateShort: 'The plate must contain at least two letters or numbers.',
    platePaste: 'The pasted plate does not match the format. Check it or enable “Other plate format”.',
    plateMask: 'The plate does not match the format. Check it or enable “Other plate format” again.',
    plateLoad: 'Could not load plate formats.', notConfigured: 'Not configured'
  },
  'zh-Hans': {
    waitFormats: '请等待车牌格式加载完成。', registrationCountry: '车辆注册国家或地区', unknownCountry: '未指定 — 使用原有车牌规则',
    plate: '车牌号码', freePlate: '请输入车牌号码', otherPlate: '其他车牌格式',
    plateLength: '车牌号码不能超过24个字符。', plateCharacters: '请使用字母、汉字、数字、空格、连字符和间隔号。',
    plateShort: '车牌号码必须至少包含两个字母、汉字或数字。',
    platePaste: '粘贴的车牌号码不符合格式。请检查或启用“其他车牌格式”。',
    plateMask: '车牌号码不符合格式。请检查或重新启用“其他车牌格式”。',
    plateLoad: '无法加载车牌格式。', notConfigured: '未设置'
  }
}

for (const [locale, messages] of Object.entries(regionalMessages)) {
  registerMessages(locale as UiLocale, { regional: messages })
}

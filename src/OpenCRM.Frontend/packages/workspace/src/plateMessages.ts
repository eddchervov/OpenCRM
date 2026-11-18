import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: {
    signIn: 'Войдите в аккаунт, чтобы загрузить формат госномера.', loadError: 'Не удалось загрузить форматы госномеров.',
    invalidConfig: 'Сервер вернул некорректные форматы госномеров.', length: 'Номер не должен быть длиннее {max} символов.',
    short: 'Номер должен содержать не менее двух символов.', characters: 'Используйте в номере только буквы, цифры, пробелы и дефис.',
    format: 'Проверьте госномер. Для этого вида техники пример: {example}.', validationError: 'Не удалось проверить формат госномера. Обновите страницу и попробуйте снова.'
  },
  en: {
    signIn: 'Sign in to load registration plate formats.', loadError: 'Could not load registration plate formats.',
    invalidConfig: 'The server returned invalid plate formats.', length: 'The plate must not exceed {max} characters.',
    short: 'The plate must contain at least two characters.', characters: 'Use only letters, numbers, spaces and hyphens in the plate.',
    format: 'Check the registration plate. An example for this equipment type is {example}.', validationError: 'Could not validate the plate format. Refresh the page and try again.'
  },
  'zh-Hans': {
    signIn: '请登录以加载车牌格式。', loadError: '无法加载车牌格式。', invalidConfig: '服务器返回了无效的车牌格式。',
    length: '车牌号码不能超过{max}个字符。', short: '车牌号码必须至少包含两个字符。', characters: '车牌号码只能包含字母、汉字、数字、空格和连字符。',
    format: '请检查车牌号码。此设备类型的示例为：{example}。', validationError: '无法验证车牌格式。请刷新页面后重试。'
  }
}

for (const [locale, plate] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, { plate })
}

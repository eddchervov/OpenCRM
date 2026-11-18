import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: { adminApi: {
    tokenError: 'Не удалось получить защитный токен запроса.',
    tokenMissing: 'Сервер не вернул защитный токен запроса.',
    requestError: 'Не удалось выполнить запрос (HTTP {status}).'
  } },
  en: { adminApi: {
    tokenError: 'Could not obtain the request verification token.',
    tokenMissing: 'The server did not return a request verification token.',
    requestError: 'The request failed (HTTP {status}).'
  } },
  'zh-Hans': { adminApi: {
    tokenError: '无法获取请求验证令牌。',
    tokenMissing: '服务器未返回请求验证令牌。',
    requestError: '请求失败（HTTP {status}）。'
  } }
} satisfies Record<UiLocale, unknown>

for (const [locale, localeMessages] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, localeMessages)
}

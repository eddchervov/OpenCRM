import { createI18n, useI18n as useVueI18n } from 'vue-i18n'
import { computed, ref, watch } from 'vue'

export const uiLanguages = [
  { code: 'ru', name: 'Русский' },
  { code: 'en', name: 'English' },
  { code: 'zh-Hans', name: '简体中文' }
] as const

export type UiLocale = typeof uiLanguages[number]['code']

export const formattingCultures = ['ru-RU', 'en-US', 'en-GB', 'zh-CN'] as const

export const localeCultures: Record<UiLocale, string> = {
  ru: 'ru-RU',
  en: 'en-US',
  'zh-Hans': 'zh-CN'
}

export const messages = {
  ru: {
    common: {
      profile: 'Профиль',
      mainMenu: 'Главное меню',
      breadcrumbs: 'Навигационная цепочка',
      language: 'Язык',
      numberFormat: 'Формат дат и чисел',
      languageSaveError: 'Не удалось сохранить настройки языка.',
      languageRussian: 'Русский',
      languageEnglish: 'English',
      languageChinese: '简体中文',
      basedOn: 'На базе',
      project: 'Проект',
      home: 'Главная',
      github: 'GitHub',
      workspace: 'Рабочее пространство',
      support: 'Обращения',
      vehicles: 'Личная техника',
      logout: 'Выйти',
      close: 'Закрыть',
      dialog: 'Диалоговое окно',
      help: 'Подсказка: {label}',
      closeHint: 'Закрыть: {label}',
      previousMonth: 'Предыдущий месяц',
      nextMonth: 'Следующий месяц',
      date: 'Дата',
      price: 'Стоимость',
      selectValue: 'Выберите значение',
      emptyList: 'Список пуст',
      nothingFound: 'Ничего не найдено',
      time: 'Время',
      hint: 'Подсказка',
      chooseDate: 'Выберите дату',
      invalidDate: 'Введите существующую дату.',
      phone: 'Телефон',
      pagination: 'Страницы',
      loading: 'Загружаем...',
      message: 'Сообщение',
      understood: 'Понятно',
      confirmation: 'Подтверждение',
      confirm: 'Подтвердить',
      cancel: 'Отмена',
      saving: 'Выполняем...',
      chooseTime: 'Выбрать время',
      timePlaceholder: 'Выберите время',
      hours: 'Часы',
      minutes: 'Минуты',
      clear: 'Очистить',
      done: 'Готово',
      photo: 'Фото записи',
      formatDate: 'dd.MM.yyyy'
    }
  },
  en: {
    common: {
      profile: 'Profile',
      mainMenu: 'Main menu',
      breadcrumbs: 'Breadcrumbs',
      language: 'Language',
      numberFormat: 'Date and number format',
      languageSaveError: 'Could not save the language settings.',
      languageRussian: 'Русский',
      languageEnglish: 'English',
      languageChinese: '简体中文',
      basedOn: 'Powered by',
      project: 'Project',
      home: 'Home',
      github: 'GitHub',
      workspace: 'Workspace',
      support: 'Support requests',
      vehicles: 'My vehicles',
      logout: 'Sign out',
      close: 'Close',
      dialog: 'Dialog',
      help: 'Help: {label}',
      closeHint: 'Close: {label}',
      previousMonth: 'Previous month',
      nextMonth: 'Next month',
      date: 'Date',
      price: 'Price',
      selectValue: 'Select a value',
      emptyList: 'List is empty',
      nothingFound: 'Nothing found',
      time: 'Time',
      hint: 'Hint',
      chooseDate: 'Choose a date',
      invalidDate: 'Enter a valid date.',
      phone: 'Phone',
      pagination: 'Pagination',
      loading: 'Loading...',
      message: 'Message',
      understood: 'Got it',
      confirmation: 'Confirmation',
      confirm: 'Confirm',
      cancel: 'Cancel',
      saving: 'Working...',
      chooseTime: 'Choose time',
      timePlaceholder: 'Choose a time',
      hours: 'Hours',
      minutes: 'Minutes',
      clear: 'Clear',
      done: 'Done',
      photo: 'Visit photo',
      formatDate: 'dd/MM/yyyy'
    }
  },
  'zh-Hans': {
    common: {
      profile: '个人资料',
      mainMenu: '主菜单',
      breadcrumbs: '面包屑导航',
      language: '语言',
      numberFormat: '日期和数字格式',
      languageSaveError: '无法保存语言设置。',
      languageRussian: 'Русский',
      languageEnglish: 'English',
      languageChinese: '简体中文',
      basedOn: '基于',
      project: '项目',
      home: '主页',
      github: 'GitHub',
      workspace: '工作区',
      support: '支持请求',
      vehicles: '我的车辆',
      logout: '退出登录',
      close: '关闭',
      dialog: '对话框',
      help: '帮助：{label}',
      closeHint: '关闭：{label}',
      previousMonth: '上个月',
      nextMonth: '下个月',
      date: '日期',
      price: '价格',
      selectValue: '请选择',
      emptyList: '列表为空',
      nothingFound: '未找到结果',
      time: '时间',
      hint: '提示',
      chooseDate: '选择日期',
      invalidDate: '请输入有效日期。',
      phone: '电话',
      pagination: '分页',
      loading: '正在加载…',
      message: '消息',
      understood: '知道了',
      confirmation: '确认',
      confirm: '确认',
      cancel: '取消',
      saving: '正在处理…',
      chooseTime: '选择时间',
      timePlaceholder: '请选择时间',
      hours: '小时',
      minutes: '分钟',
      clear: '清除',
      done: '完成',
      photo: '预约照片',
      formatDate: 'yyyy/MM/dd'
    }
  }
}

function resolveLocale(value: string | null | undefined): UiLocale | undefined {
  if (!value) return undefined

  const normalized = value.trim().toLowerCase()
  const exact = uiLanguages.find(language => language.code.toLowerCase() === normalized
    || localeCultures[language.code].toLowerCase() === normalized)
  if (exact) return exact.code

  const baseLanguage = normalized.split('-')[0]

  return uiLanguages.find(language => language.code.split('-')[0] === baseLanguage)?.code
}

function getCookieLocale(): UiLocale | undefined {
  if (typeof document === 'undefined') return undefined

  const cookie = document.cookie.split('; ').find(value => value.startsWith('.AspNetCore.Culture='))
  if (!cookie) return undefined

  try {
    const culture = decodeURIComponent(cookie.slice(cookie.indexOf('=') + 1)).match(/(?:^|\|)c=([^|]+)/)?.[1]
    return resolveLocale(culture)
  } catch {
    return undefined
  }
}

function detectLocale(): UiLocale {
  if (typeof document === 'undefined') return 'ru'

  return getCookieLocale()
    ?? resolveLocale(document.documentElement.lang)
    ?? resolveLocale(navigator.languages?.[0] || navigator.language)
    ?? 'ru'
}

const initialLocale = detectLocale()

export const i18n = createI18n({
  legacy: false,
  locale: initialLocale,
  fallbackLocale: 'ru',
  messages
})
const requestVerificationToken = ref('')
export const formattingCulture = ref(localeCultures[initialLocale])

watch(i18n.global.locale, locale => {
  if (typeof document !== 'undefined') document.documentElement.lang = locale
})

export function useLocalization() {
  const { t, te, locale } = useVueI18n({ useScope: 'global' })
  const supportedLocale = computed(() => locale.value as UiLocale)

  return { t, te, locale: supportedLocale }
}

export async function initializeLocalization(app: { use: (plugin: typeof i18n) => unknown }): Promise<void> {
  app.use(i18n)
  if (typeof document !== 'undefined') document.documentElement.lang = initialLocale

  const metaToken = typeof document === 'undefined'
    ? ''
    : document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
  requestVerificationToken.value = metaToken

  if (typeof fetch !== 'undefined') {
    try {
      const pathBase = document.querySelector<HTMLMetaElement>('meta[name="opencrm-path-base"]')?.content.replace(/\/$/, '') ?? ''
      const response = await fetch(`${pathBase}/localization/context`, { credentials: 'same-origin', headers: { Accept: 'application/json' } })
      if (response.ok) {
        const context = await response.json() as { locale?: string; requestVerificationToken?: string; formattingCulture?: string }
        if (context.requestVerificationToken) requestVerificationToken.value = context.requestVerificationToken
        if (context.locale) {
          const resolved = resolveLocale(context.locale)
          if (resolved) {
            i18n.global.locale.value = resolved
            document.documentElement.lang = context.locale
          }
        }

        if (context.formattingCulture) setFormattingCulture(context.formattingCulture)
      }
    } catch {
      // При недоступном контексте остаётся язык документа или браузера.
    }
  }
}

export function registerMessages(locale: UiLocale, localeMessages: Record<string, unknown>): void {
  i18n.global.mergeLocaleMessage(locale, localeMessages)
}

export function translate(key: string, parameters?: Record<string, string | number>): string {
  return i18n.global.t(key as never, parameters as never) as string
}

export async function setUiLocale(locale: UiLocale, selectedFormattingCulture = formattingCulture.value): Promise<boolean> {
  if (!Object.hasOwn(messages, locale)) return false

  if (typeof document === 'undefined') return false

  let token = requestVerificationToken.value || (document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? '')
  if (!token && typeof fetch !== 'undefined') {
    try {
      const pathBase = document.querySelector<HTMLMetaElement>('meta[name="opencrm-path-base"]')?.content.replace(/\/$/, '') ?? ''
      const response = await fetch(`${pathBase}/localization/context`, { credentials: 'same-origin', headers: { Accept: 'application/json' } })
      if (response.ok) {
        const context = await response.json() as { requestVerificationToken?: string }
        token = context.requestVerificationToken ?? ''
        requestVerificationToken.value = token
      }
    } catch {
      return false
    }
  }

  if (!token) return false

  const form = document.createElement('form')
  form.method = 'post'
  const pathBase = document.querySelector<HTMLMetaElement>('meta[name="opencrm-path-base"]')?.content.replace(/\/$/, '') ?? ''
  form.action = `${pathBase}/localization/language`
  for (const [name, value] of Object.entries({
    locale: localeCultures[locale],
    formattingCulture: selectedFormattingCulture,
    returnUrl: `${window.location.pathname}${window.location.search}${window.location.hash}`,
    __RequestVerificationToken: token
  })) {
    const input = document.createElement('input')
    input.type = 'hidden'
    input.name = name
    input.value = value
    form.append(input)
  }

  document.body.append(form)
  form.submit()
  return true
}

export function setFormattingCulture(culture: string): void {
  try {
    formattingCulture.value = Intl.getCanonicalLocales(culture)[0] ?? localeCultures[i18n.global.locale.value as UiLocale]
  } catch {
    formattingCulture.value = localeCultures[i18n.global.locale.value as UiLocale]
  }
}

export function formatLocaleDate(value: Date | number | string, options: Intl.DateTimeFormatOptions = {}, culture = formattingCulture.value): string {
  return new Intl.DateTimeFormat(culture, options).format(new Date(value))
}

export function formatLocaleNumber(value: number, options: Intl.NumberFormatOptions = {}, culture = formattingCulture.value): string {
  return new Intl.NumberFormat(culture, options).format(value)
}

export function formatLocaleCurrency(value: number, currency: string, options: Intl.NumberFormatOptions = {}, culture = formattingCulture.value): string {
  return new Intl.NumberFormat(culture, {
    style: 'currency',
    currency,
    ...options
  }).format(value)
}

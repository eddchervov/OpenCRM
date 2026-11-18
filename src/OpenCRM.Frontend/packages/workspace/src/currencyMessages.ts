import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: {
    title: 'Валюта учёта', personalTitle: 'Валюта личного учёта', change: 'Изменить валюту',
    save: 'Сохранить', cancel: 'Отмена', saving: 'Сохраняем…', loading: 'Загружаем…', retry: 'Повторить',
    locked: 'Валюта зафиксирована: уже созданы заказы, цены или складские записи. Изменение валюты не пересчитывает суммы.',
    hint: 'Валюта применяется к новым ценам, заказам и оплатам. Язык интерфейса и валюта подписки OpenCRM не меняются.',
    loadError: 'Не удалось загрузить валюту учёта.', saveError: 'Не удалось сохранить валюту учёта.',
    conflict: 'Настройки изменились или уже появились денежные записи. Обновите данные перед повторной попыткой.',
    signedOut: 'Войдите снова, чтобы открыть настройки валюты.', forbidden: 'Недостаточно прав для изменения валюты.',
    networkError: 'Нет связи с CRM. Проверьте соединение и повторите попытку.', saved: 'Валюта учёта сохранена.'
  },
  en: {
    title: 'Accounting currency', personalTitle: 'Personal accounting currency', change: 'Change currency',
    save: 'Save', cancel: 'Cancel', saving: 'Saving…', loading: 'Loading…', retry: 'Try again',
    locked: 'The currency is fixed because orders, prices or stock records already exist. Changing currency does not convert amounts.',
    hint: 'The currency applies to new prices, orders and payments. The interface language and OpenCRM subscription currency stay the same.',
    loadError: 'Could not load the accounting currency.', saveError: 'Could not save the accounting currency.',
    conflict: 'Settings changed or financial records were created. Refresh before trying again.',
    signedOut: 'Sign in again to open currency settings.', forbidden: 'You do not have permission to change the currency.',
    networkError: 'Could not connect to CRM. Check your connection and try again.', saved: 'Accounting currency saved.'
  },
  'zh-Hans': {
    title: '记账币种', personalTitle: '个人记账币种', change: '更改币种',
    save: '保存', cancel: '取消', saving: '正在保存…', loading: '正在加载…', retry: '重试',
    locked: '已创建订单、价格或库存记录，币种已固定。更改币种不会换算金额。',
    hint: '此币种用于新价格、订单和付款。界面语言和OpenCRM订阅币种保持不变。',
    loadError: '无法加载记账币种。', saveError: '无法保存记账币种。',
    conflict: '设置已更改或已创建财务记录。请刷新后重试。', signedOut: '请重新登录以打开币种设置。',
    forbidden: '您没有更改币种的权限。', networkError: '无法连接到CRM。请检查网络连接后重试。', saved: '记账币种已保存。'
  }
}

for (const [locale, currency] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, { currency })
}

import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: {
    networkError: "Нет связи с CRM. Проверьте соединение и повторите попытку.",
    signIn: "Войдите снова, чтобы открыть настройки часового пояса.",
    forbidden: "Недостаточно прав для настройки часового пояса организации.",
    conflict: "Часовой пояс уже изменил другой сотрудник. Обновите данные и повторите изменение.",
    requestError: "Не удалось загрузить или сохранить часовой пояс организации.",
    locked: "Часовой пояс нельзя изменить после создания записей со временем или смен сотрудников.",
    loadError: 'Не удалось загрузить часовой пояс.', chooseRequired: 'Выберите часовой пояс организации.',
    saved: 'Часовой пояс сохранён.', saveError: 'Не удалось сохранить часовой пояс.',
    organizationZone: 'Часовой пояс организации', timeZone: 'Часовой пояс',
    changeNotice: 'Новые записи, смены и напоминания будут использовать выбранный часовой пояс.',
    refresh: 'Обновить данные', cancel: 'Отмена', saving: 'Сохраняем…', save: 'Сохранить',
    changeZone: 'Изменить часовой пояс', change: 'Изменить', choose: 'Выбрать', loading: 'Загружаем…',
    utcDefault: 'До настройки используется UTC.', retry: 'Повторить', notConfigured: 'Не настроен'
  },
  en: {
    networkError: "Cannot connect to CRM. Check your connection and try again.",
    signIn: "Sign in again to open time zone settings.",
    forbidden: "You do not have permission to change the organization time zone.",
    conflict: "Another employee has changed the time zone. Refresh the settings and try again.",
    requestError: "Could not load or save the organization time zone.",
    locked: "The time zone cannot be changed after timed bookings or employee shifts have been created.",
    loadError: 'Could not load the time zone.', chooseRequired: 'Select the organization time zone.',
    saved: 'Time zone saved.', saveError: 'Could not save the time zone.',
    organizationZone: 'Organization time zone', timeZone: 'Time zone',
    changeNotice: 'New bookings, shifts, and reminders will use the selected time zone.',
    refresh: 'Refresh', cancel: 'Cancel', saving: 'Saving…', save: 'Save',
    changeZone: 'Change time zone', change: 'Change', choose: 'Select', loading: 'Loading…',
    utcDefault: 'UTC is used until a time zone is configured.', retry: 'Try again', notConfigured: 'Not configured'
  },
  'zh-Hans': {
    networkError: "无法连接CRM。请检查网络连接后重试。",
    signIn: "请重新登录以打开时区设置。",
    forbidden: "您没有更改组织时区的权限。",
    conflict: "其他员工已更改时区。请刷新设置后重试。",
    requestError: "无法加载或保存组织时区。",
    locked: "创建了带时间的预约或员工班次后，无法更改时区。",
    loadError: '无法加载时区。', chooseRequired: '请选择组织时区。', saved: '时区已保存。', saveError: '无法保存时区。',
    organizationZone: '组织时区', timeZone: '时区',
    changeNotice: '新预约、班次和提醒将使用所选时区。',
    refresh: '刷新', cancel: '取消', saving: '正在保存…', save: '保存',
    changeZone: '更改时区', change: '更改', choose: '选择', loading: '正在加载…',
    utcDefault: '设置时区前使用UTC。', retry: '重试', notConfigured: '未设置'
  }
}

for (const [locale, timeZone] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, { timeZone })
}

import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  "ru": {
    "scenarioApp": {
      "autoservice": "СТО / Автосервис",
      "detailing": "Детейлинг",
      "fleet": "Автопарк",
      "profile": "Профиль",
      "admin": "Админ-панель",
      "organizations": "Организации",
      "personalVehicles": "Личная техника",
      "logout": "Выйти",
      "accountingExport": "Выгрузка для бухгалтерии",
      "newWorkOrder": "Новый заказ-наряд",
      "create": "Создание",
      "edit": "Редактирование",
      "photos": "Фото",
      "album": "Альбом",
      "albums": "Альбомы",
      "profileCard": "Карточка",
      "autoserviceError": "Не удалось открыть клиент автосервиса.",
      "detailingError": "Не удалось открыть клиент детейлинга.",
      "fleetError": "Не удалось открыть клиент автопарка.",
      "closeMessage": "Закрыть сообщение",
      "opening": "Открываем рабочий стол...",
      "noSections": "Нет доступных разделов организации.",
      "newBooking": "Новая запись",
      "section": "Раздел"
    }
  },
  "en": {
    "scenarioApp": {
      "autoservice": "Auto service",
      "detailing": "Detailing",
      "fleet": "Fleet",
      "profile": "Profile",
      "admin": "Admin panel",
      "organizations": "Organizations",
      "personalVehicles": "Personal vehicles",
      "logout": "Sign out",
      "accountingExport": "Accounting export",
      "newWorkOrder": "New work order",
      "create": "Create",
      "edit": "Edit",
      "photos": "Photos",
      "album": "Album",
      "albums": "Albums",
      "profileCard": "Details",
      "autoserviceError": "Could not open the auto service workspace.",
      "detailingError": "Could not open the detailing workspace.",
      "fleetError": "Could not open the fleet workspace.",
      "closeMessage": "Close message",
      "opening": "Opening workspace…",
      "noSections": "No organization sections are available.",
      "newBooking": "New booking",
      "section": "Section"
    }
  },
  "zh-Hans": {
    "scenarioApp": {
      "autoservice": "汽车维修",
      "detailing": "汽车美容",
      "fleet": "车队",
      "profile": "个人资料",
      "admin": "管理面板",
      "organizations": "组织",
      "personalVehicles": "个人车辆",
      "logout": "退出登录",
      "accountingExport": "财务导出",
      "newWorkOrder": "新建工单",
      "create": "创建",
      "edit": "编辑",
      "photos": "照片",
      "album": "相册",
      "albums": "相册",
      "profileCard": "详情",
      "autoserviceError": "无法打开汽车维修工作区。",
      "detailingError": "无法打开汽车美容工作区。",
      "fleetError": "无法打开车队工作区。",
      "closeMessage": "关闭消息",
      "opening": "正在打开工作区…",
      "noSections": "此组织暂无可用功能区。",
      "newBooking": "新建预约",
      "section": "功能区"
    }
  }
} satisfies Record<UiLocale, unknown>

for (const [locale, localeMessages] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, localeMessages)
}

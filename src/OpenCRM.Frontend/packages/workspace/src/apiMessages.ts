import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  "ru": {
    "apiErrors": {
      "maintenanceHistory": "Не удалось загрузить историю обслуживания.",
      "maintenanceCard": "Не удалось загрузить карточку записи.",
      "photos": "Не удалось загрузить фотографии.",
      "uploadServicePhoto": "Не удалось загрузить фото услуги.",
      "deleteServicePhoto": "Не удалось удалить фото услуги.",
      "saveService": "Не удалось сохранить услугу.",
      "changeStatus": "Не удалось изменить статус записи.",
      "saveIntake": "Не удалось сохранить приёмку.",
      "photoStage": "Не удалось изменить этап фотографии.",
      "photosRetry": "Не удалось загрузить фотографии. Повторите попытку или обновите карточку записи.",
      "payments": "Не удалось загрузить оплаты.",
      "saveChanges": "Не удалось сохранить изменения.",
      "signIn": "Требуется авторизация.",
      "finance": "Не удалось загрузить финансы."
    }
  },
  "en": {
    "apiErrors": {
      "maintenanceHistory": "Could not load service history.",
      "maintenanceCard": "Could not load the booking.",
      "photos": "Could not load photos.",
      "uploadServicePhoto": "Could not upload the service photo.",
      "deleteServicePhoto": "Could not delete the service photo.",
      "saveService": "Could not save the service.",
      "changeStatus": "Could not change the booking status.",
      "saveIntake": "Could not save the intake.",
      "photoStage": "Could not change the photo stage.",
      "photosRetry": "Could not load photos. Try again or refresh the booking.",
      "payments": "Could not load payments.",
      "saveChanges": "Could not save changes.",
      "signIn": "Sign in to continue.",
      "finance": "Could not load financial data."
    }
  },
  "zh-Hans": {
    "apiErrors": {
      "maintenanceHistory": "无法加载服务记录。",
      "maintenanceCard": "无法加载预约。",
      "photos": "无法加载照片。",
      "uploadServicePhoto": "无法上传服务照片。",
      "deleteServicePhoto": "无法删除服务照片。",
      "saveService": "无法保存服务。",
      "changeStatus": "无法更改预约状态。",
      "saveIntake": "无法保存接车记录。",
      "photoStage": "无法更改照片阶段。",
      "photosRetry": "无法加载照片。请重试或刷新预约。",
      "payments": "无法加载付款记录。",
      "saveChanges": "无法保存更改。",
      "signIn": "请登录后继续。",
      "finance": "无法加载财务数据。"
    }
  }
}

for (const [locale, value] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, value)
}

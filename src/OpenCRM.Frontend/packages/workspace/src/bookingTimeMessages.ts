import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  "ru": {
    "bookingTime": {
      "endDate": "Дата окончания не может быть раньше даты начала.",
      "multiTimes": "Укажите время начала и окончания многодневной записи.",
      "startRequired": "Укажите время начала.",
      "endAfter": "Окончание должно быть позже начала.",
      "endAfterShort": "Окончание должно быть позже начала",
      "multiManual": "Для многодневной записи необходимо вручную указать время начала и окончания. Автоматический расчёт по услугам недоступен.",
      "unknownEnd": "Длительность выбранных услуг неизвестна. Указанное окончание сохранится без автоматического пересчёта.",
      "unknownDefault": "Длительность выбранных услуг неизвестна. Для проверки занятости поста учитывается 60 минут от указанного начала; время окончания можно оставить пустым.",
      "unknownManual": "Длительность выбранных услуг неизвестна. Время начала и окончания можно указать вручную; автоматический расчёт недоступен.",
      "checking": "Проверяем занятость поста…",
      "unchecked": "Занятость поста не проверена",
      "loadError": "Не удалось проверить занятость поста.",
      "conflict": "В выбранном интервале пост занят: {slots}",
      "checkInterval": "Проверьте выбранный интервал времени.",
      "start": "Начало",
      "end": "Окончание",
      "why": "Почему окончание не рассчитывается автоматически",
      "duration": "По услугам: {duration}",
      "recalculate": "Рассчитать окончание",
      "retry": "Повторить",
      "occupied": "В выбранном интервале пост занят:",
      "free": "Выбранное время свободно",
      "slots": "Занято на посту · {count}",
      "periodFree": "На выбранный период пост свободен",
      "savedManual": "Сохранено. Длительность части услуг не задана; указанное время окончания сохранено.",
      "savedAuto": "Сохранено. Длительность части услуг не задана; автоматический расчёт окончания пропущен."
    }
  },
  "en": {
    "bookingTime": {
      "endDate": "The end date cannot be before the start date.",
      "multiTimes": "Enter start and end times for a booking spanning multiple days.",
      "startRequired": "Enter a start time.",
      "endAfter": "The end must be after the start.",
      "endAfterShort": "The end must be after the start",
      "multiManual": "For bookings spanning multiple days, enter start and end times manually. Automatic calculation from services is unavailable.",
      "unknownEnd": "The duration of the selected services is unknown. The entered end time will be saved without recalculation.",
      "unknownDefault": "The duration of the selected services is unknown. Availability is checked for 60 minutes from the entered start time; the end time may be left blank.",
      "unknownManual": "The duration of the selected services is unknown. Enter start and end times manually; automatic calculation is unavailable.",
      "checking": "Checking work bay availability…",
      "unchecked": "Work bay availability has not been checked",
      "loadError": "Could not check work bay availability.",
      "conflict": "The work bay is occupied during the selected time: {slots}",
      "checkInterval": "Check the selected time interval.",
      "start": "Start",
      "end": "End",
      "why": "Why the end time is not calculated automatically",
      "duration": "Service duration: {duration}",
      "recalculate": "Calculate end time",
      "retry": "Retry",
      "occupied": "The work bay is occupied during the selected time:",
      "free": "The selected time is available",
      "slots": "Occupied work bay slots · {count}",
      "periodFree": "The work bay is available during the selected period",
      "savedManual": "Saved. Some services have no duration; the entered end time was preserved.",
      "savedAuto": "Saved. Some services have no duration; automatic end time calculation was skipped."
    }
  },
  "zh-Hans": {
    "bookingTime": {
      "endDate": "结束日期不能早于开始日期。",
      "multiTimes": "跨天预约必须填写开始和结束时间。",
      "startRequired": "请填写开始时间。",
      "endAfter": "结束时间必须晚于开始时间。",
      "endAfterShort": "结束时间必须晚于开始时间",
      "multiManual": "跨天预约必须手动填写开始和结束时间，无法按服务自动计算。",
      "unknownEnd": "所选服务的时长未知。填写的结束时间将保存，不会自动重新计算。",
      "unknownDefault": "所选服务的时长未知。将从填写的开始时间起按60分钟检查工位占用情况；结束时间可留空。",
      "unknownManual": "所选服务的时长未知。您可以手动填写开始和结束时间，无法自动计算。",
      "checking": "正在检查工位占用情况…",
      "unchecked": "尚未检查工位占用情况",
      "loadError": "无法检查工位占用情况。",
      "conflict": "所选时段的工位已被占用：{slots}",
      "checkInterval": "请检查所选时段。",
      "start": "开始",
      "end": "结束",
      "why": "为什么结束时间无法自动计算",
      "duration": "服务时长：{duration}",
      "recalculate": "计算结束时间",
      "retry": "重试",
      "occupied": "所选时段的工位已被占用：",
      "free": "所选时段可用",
      "slots": "工位已占用时段 · {count}",
      "periodFree": "所选时段的工位可用",
      "savedManual": "已保存。部分服务未设置时长；已保留填写的结束时间。",
      "savedAuto": "已保存。部分服务未设置时长；已跳过结束时间的自动计算。"
    }
  }
}

for (const [locale, value] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, value)
}

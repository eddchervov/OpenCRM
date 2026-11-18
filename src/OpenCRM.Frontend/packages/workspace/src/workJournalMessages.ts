import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  "ru": {
    "workJournal": {
      "journalTitle": "Журнал работ",
      "journalView": "Вид журнала работ",
      "journalError": "Не удалось загрузить журнал работ.",
      "completed": "Выполнено",
      "clients": "Клиентов",
      "loadError": "Не удалось загрузить историю работ.",
      "title": "История работ",
      "from": "С",
      "to": "По",
      "view": "Вид истории работ",
      "list": "Список",
      "calendar": "Календарь",
      "empty": "Нет работ на выбранный период",
      "loading": "Загружаем историю...",
      "noCompleted": "За выбранный период выполненных работ нет.",
      "section": "Раздел журнала",
      "maintenances": "Обслуживания",
      "events": "События и календарь"
    }
  },
  "en": {
    "workJournal": {
      "journalTitle": "Work log",
      "journalView": "Work log view",
      "journalError": "Could not load the work log.",
      "completed": "Completed",
      "clients": "Customers",
      "loadError": "Could not load work history.",
      "title": "Work history",
      "from": "From",
      "to": "To",
      "view": "Work history view",
      "list": "List",
      "calendar": "Calendar",
      "empty": "No jobs in the selected period",
      "loading": "Loading history…",
      "noCompleted": "No completed jobs in the selected period.",
      "section": "Journal section",
      "maintenances": "Service records",
      "events": "Events and calendar"
    }
  },
  "zh-Hans": {
    "workJournal": {
      "journalTitle": "工作日志",
      "journalView": "工作日志视图",
      "journalError": "无法加载工作日志。",
      "completed": "已完成",
      "clients": "客户数",
      "loadError": "无法加载工作历史记录。",
      "title": "工作历史记录",
      "from": "开始日期",
      "to": "结束日期",
      "view": "工作历史记录视图",
      "list": "列表",
      "calendar": "日历",
      "empty": "所选期间暂无工作",
      "loading": "正在加载历史记录…",
      "noCompleted": "所选期间暂无已完成的工作。",
      "section": "日志分区",
      "maintenances": "服务记录",
      "events": "事件和日历"
    }
  }
}

for (const [locale, value] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, value)
}

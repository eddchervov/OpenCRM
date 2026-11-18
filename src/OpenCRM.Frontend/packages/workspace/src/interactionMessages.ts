import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages: Record<UiLocale, Record<string, unknown>> = {
  "ru": {
    "interaction": {
      "viewFeatures": "Посмотреть возможности",
      "openChatError": "Не удалось открыть переписку.",
      "openingChat": "Открываем переписку…",
      "retry": "Повторить",
      "backToClients": "← К списку клиентов",
      "clientMessenger": "Мессенджер клиента",
      "chooseMessenger": "Выберите мессенджер для ответа клиенту.",
      "disconnectedHistory": "История отключённых каналов",
      "readOnly": "Только просмотр",
      "noMessengers": "Нет подключённых мессенджеров",
      "messengerHelp": "Создайте приглашение в карточке клиента. После подключения Telegram или MAX здесь появится переписка со студией.",
      "openClient": "Открыть клиента",
      "botLink": "Связь с ботом",
      "botTitle": "Связь клиента с ботом",
      "clientMessengers": "Мессенджеры клиента",
      "pro": "доступно в PRO",
      "botHelp": "Выберите мессенджер и передайте клиенту персональную ссылку. Подключение появится после запуска бота клиентом.",
      "close": "Закрыть",
      "loadRecordError": "Не удалось загрузить запись.",
      "refreshRecords": "Обновите список записей.",
      "loading": "Загружаем…",
      "actions": "Действия",
      "assistant": "ИИ помощник",
      "assistantPro": "ИИ помощник · Доступно в PRO",
      "unread": "{count} новых",
      "lockedInfo": "Подключение клиента и отправка сообщений — {pro}. Историю можно просматривать.",
      "botLinkPro": "Связь с ботом · PRO"
    }
  },
  "en": {
    "interaction": {
      "viewFeatures": "View features",
      "openChatError": "Could not open the conversation.",
      "openingChat": "Opening conversation…",
      "retry": "Retry",
      "backToClients": "← Back to clients",
      "clientMessenger": "Client messenger",
      "chooseMessenger": "Choose a messenger to reply to the client.",
      "disconnectedHistory": "Disconnected channel history",
      "readOnly": "Read only",
      "noMessengers": "No connected messengers",
      "messengerHelp": "Create an invitation in the client profile. After Telegram or MAX is connected, conversations with your business will appear here.",
      "openClient": "Open client",
      "botLink": "Bot connection",
      "botTitle": "Client bot connection",
      "clientMessengers": "Client messengers",
      "pro": "available in PRO",
      "botHelp": "Choose a messenger and share the personal link with the client. The connection appears after the client starts the bot.",
      "close": "Close",
      "loadRecordError": "Could not load the record.",
      "refreshRecords": "Refresh the record list.",
      "loading": "Loading…",
      "actions": "Actions",
      "assistant": "AI assistant",
      "assistantPro": "AI assistant · Available in PRO",
      "unread": "{count} new",
      "lockedInfo": "Client connections and messaging are {pro}. History remains available to read.",
      "botLinkPro": "Bot connection · PRO"
    }
  },
  "zh-Hans": {
    "interaction": {
      "viewFeatures": "查看功能",
      "openChatError": "无法打开对话。",
      "openingChat": "正在打开对话…",
      "retry": "重试",
      "backToClients": "← 返回客户列表",
      "clientMessenger": "客户即时通讯",
      "chooseMessenger": "请选择即时通讯渠道以回复客户。",
      "disconnectedHistory": "已断开渠道的历史",
      "readOnly": "仅查看",
      "noMessengers": "暂无已连接的即时通讯渠道",
      "messengerHelp": "在客户档案中创建邀请。连接 Telegram 或 MAX 后，与您企业的对话将显示在此处。",
      "openClient": "打开客户",
      "botLink": "机器人连接",
      "botTitle": "客户机器人连接",
      "clientMessengers": "客户即时通讯渠道",
      "pro": "仅在 PRO 中可用",
      "botHelp": "请选择即时通讯渠道，并将专属链接发送给客户。客户启动机器人后，连接将会显示。",
      "close": "关闭",
      "loadRecordError": "无法加载记录。",
      "refreshRecords": "请刷新记录列表。",
      "loading": "正在加载…",
      "actions": "操作",
      "assistant": "AI 助手",
      "assistantPro": "AI 助手 · PRO 可用",
      "unread": "{count} 条新消息",
      "lockedInfo": "连接客户和发送消息功能{pro}。仍可查看历史记录。",
      "botLinkPro": "机器人连接 · PRO"
    }
  }
}

for (const [locale, values] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, values)
}

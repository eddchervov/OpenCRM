import { registerMessages, translate, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  "ru": {
    "access": {
      "choosePlan": "Выбрать тариф",
      "readOnly": "Режим просмотра",
      "viewer": "В этой организации у вас доступ только на просмотр.",
      "freeMember": "Бесплатный тариф позволяет изменять данные владельцу организации. Работа команды доступна в PRO.",
      "expiredManager": "Срок бизнес-тарифа закончился. Подключите новый тариф, чтобы снова изменять данные.",
      "noPlanManager": "Данные доступны для просмотра. Добавление и изменение записей — на демо или бизнес-тарифе.",
      "noPlanDemo": "Данные доступны для просмотра. Добавление и изменение записей — на демо или бизнес-тарифе. Можно подключить демо.",
      "expiredMember": "Срок тарифа закончился. Данные доступны только для просмотра.",
      "noPlanMember": "В организации не подключён тариф. Данные доступны только для просмотра.",
      "expiresToday": "Оплаченный период заканчивается сегодня.",
      "daysRemaining": "Дней до окончания: {count}.",
      "expiresTitle": "Срок тарифа заканчивается",
      "expires": "{name} действует до {date}.",
      "baseMode": "После этой даты рабочее пространство перейдёт в базовый режим.",
      "renew": "Продлить тариф"
    }
  },
  "en": {
    "access": {
      "choosePlan": "Choose plan",
      "readOnly": "Read-only mode",
      "viewer": "You have read-only access to this organization.",
      "freeMember": "The free plan allows the organization owner to edit data. Team access is available with PRO.",
      "expiredManager": "The business plan has expired. Activate a new plan to edit data again.",
      "noPlanManager": "Data is available for viewing. Adding and editing records requires a demo or business plan.",
      "noPlanDemo": "Data is available for viewing. Adding and editing records requires a demo or business plan. You can activate a demo.",
      "expiredMember": "The plan has expired. Data is read-only.",
      "noPlanMember": "This organization has no active plan. Data is read-only.",
      "expiresToday": "The paid period ends today.",
      "daysRemaining": "Days remaining: {count}.",
      "expiresTitle": "Your plan is expiring",
      "expires": "{name} is active until {date}.",
      "baseMode": "After this date, the workspace will switch to basic mode.",
      "renew": "Renew plan"
    }
  },
  "zh-Hans": {
    "access": {
      "choosePlan": "选择套餐",
      "readOnly": "只读模式",
      "viewer": "您对此组织仅有查看权限。",
      "freeMember": "免费套餐仅允许组织所有者修改数据。团队协作功能需使用 PRO 套餐。",
      "expiredManager": "商务套餐已到期。请启用新套餐以恢复数据编辑。",
      "noPlanManager": "可以查看数据。添加和修改记录需要试用或商务套餐。",
      "noPlanDemo": "可以查看数据。添加和修改记录需要试用或商务套餐。您可以启用试用。",
      "expiredMember": "套餐已到期，数据仅可查看。",
      "noPlanMember": "组织尚未启用套餐，数据仅可查看。",
      "expiresToday": "付费期将于今天结束。",
      "daysRemaining": "剩余天数：{count}。",
      "expiresTitle": "套餐即将到期",
      "expires": "{name} 有效期至 {date}。",
      "baseMode": "此日期之后，工作空间将切换到基础模式。",
      "renew": "续订套餐"
    }
  }
}

for (const [locale, value] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, value)
}

export function translateWorkspaceAccessMessage(message: string): string {
  const match = Object.entries(messages.ru.access).find(([, value]) => value === message)

  return match ? translate('access.' + match[0]) : message
}

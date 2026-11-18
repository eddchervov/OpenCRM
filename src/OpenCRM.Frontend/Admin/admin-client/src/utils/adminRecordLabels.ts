import type { UiLocale } from '@opencrm/ui/i18n'

const labels: Record<UiLocale, Record<string, string>> = {
  ru: {
    'paymentStatus.needs_attention': 'Требует проверки',
    'paymentMethod.yookassa_purchase': 'YooKassa: покупка', 'paymentMethod.yookassa_workspace_purchase': 'YooKassa: покупка организации', 'paymentMethod.yookassa_workspace_extension': 'YooKassa: продление организации',
    'role.owner': 'Владелец', 'role.admin': 'Администратор', 'role.employee': 'Сотрудник', 'role.performer': 'Исполнитель', 'role.viewer': 'Наблюдатель',
    'timeline.vehicle': 'Техника', 'timeline.payment': 'Оплата', 'timeline.subscription': 'Подписка',
    'timeline.support': 'Обращение', 'timeline.organization': 'Организация', 'timeline.registration': 'Регистрация',
    'timeline.login': 'Вход', 'title.registration': 'Пользователь зарегистрировался', 'title.login': 'Последний вход пользователя',
    'maintenanceStatus.1': 'Запланировано', 'maintenanceStatus.2': 'В работе', 'maintenanceStatus.3': 'Завершено', 'maintenanceStatus.4': 'Отменено',
    'maintenanceType.1': 'Обслуживание', 'maintenanceType.2': 'Ремонт', 'maintenanceType.3': 'Заправка', 'maintenanceType.100': 'Прочее',
    'paymentStatus.completed': 'Оплачено', 'paymentStatus.succeeded': 'Успешно в YooKassa', 'paymentStatus.pending': 'Ожидание',
    'paymentStatus.processing': 'Обработка', 'paymentStatus.waiting_for_capture': 'Ждёт подтверждения',
    'paymentStatus.paid_waiting_confirmation': 'Оплачен, ждёт подтверждения', 'paymentStatus.canceled': 'Отменено',
    'paymentStatus.failed': 'Ошибка', 'paymentStatus.refunded': 'Возврат', 'paymentStatus.': 'Без статуса',
    'paymentMethod.yookassa': 'YooKassa', 'paymentMethod.yookassa_extension': 'YooKassa: продление',
    'paymentMethod.workspace_purchase': 'Покупка организации', 'paymentMethod.workspace_extension': 'Продление организации', 'paymentMethod.': 'Не указан',
    'supportType.question': 'Вопрос', 'supportType.suggestion': 'Предложение', 'supportType.bug': 'Ошибка', 'supportType.billing': 'Биллинг', 'supportType.cooperation': 'Сотрудничество',
    'supportStatus.new': 'Новое', 'supportStatus.in_progress': 'В работе', 'supportStatus.closed': 'Закрыто', 'supportStatus.spam': 'Спам'
  },
  en: {
    'paymentStatus.needs_attention': 'Needs review',
    'paymentMethod.yookassa_purchase': 'YooKassa: purchase', 'paymentMethod.yookassa_workspace_purchase': 'YooKassa: organization purchase', 'paymentMethod.yookassa_workspace_extension': 'YooKassa: organization renewal',
    'role.owner': 'Owner', 'role.admin': 'Administrator', 'role.employee': 'Employee', 'role.performer': 'Assignee', 'role.viewer': 'Viewer',
    'timeline.vehicle': 'Vehicle', 'timeline.payment': 'Payment', 'timeline.subscription': 'Subscription',
    'timeline.support': 'Support request', 'timeline.organization': 'Organization', 'timeline.registration': 'Registration',
    'timeline.login': 'Sign-in', 'title.registration': 'User registered', 'title.login': 'Last user sign-in',
    'maintenanceStatus.1': 'Planned', 'maintenanceStatus.2': 'In progress', 'maintenanceStatus.3': 'Completed', 'maintenanceStatus.4': 'Cancelled',
    'maintenanceType.1': 'Maintenance', 'maintenanceType.2': 'Repair', 'maintenanceType.3': 'Refuelling', 'maintenanceType.100': 'Other',
    'paymentStatus.completed': 'Paid', 'paymentStatus.succeeded': 'Succeeded in YooKassa', 'paymentStatus.pending': 'Pending',
    'paymentStatus.processing': 'Processing', 'paymentStatus.waiting_for_capture': 'Awaiting confirmation',
    'paymentStatus.paid_waiting_confirmation': 'Paid, awaiting confirmation', 'paymentStatus.canceled': 'Cancelled',
    'paymentStatus.failed': 'Failed', 'paymentStatus.refunded': 'Refunded', 'paymentStatus.': 'No status',
    'paymentMethod.yookassa': 'YooKassa', 'paymentMethod.yookassa_extension': 'YooKassa: renewal',
    'paymentMethod.workspace_purchase': 'Organization purchase', 'paymentMethod.workspace_extension': 'Organization renewal', 'paymentMethod.': 'Not specified',
    'supportType.question': 'Question', 'supportType.suggestion': 'Suggestion', 'supportType.bug': 'Bug', 'supportType.billing': 'Billing', 'supportType.cooperation': 'Cooperation',
    'supportStatus.new': 'New', 'supportStatus.in_progress': 'In progress', 'supportStatus.closed': 'Closed', 'supportStatus.spam': 'Spam'
  },
  'zh-Hans': {
    'paymentStatus.needs_attention': '需要检查',
    'paymentMethod.yookassa_purchase': 'YooKassa：购买', 'paymentMethod.yookassa_workspace_purchase': 'YooKassa：组织购买', 'paymentMethod.yookassa_workspace_extension': 'YooKassa：组织续订',
    'role.owner': '所有者', 'role.admin': '管理员', 'role.employee': '员工', 'role.performer': '执行人', 'role.viewer': '查看者',
    'timeline.vehicle': '车辆', 'timeline.payment': '付款', 'timeline.subscription': '订阅',
    'timeline.support': '支持请求', 'timeline.organization': '组织', 'timeline.registration': '注册',
    'timeline.login': '登录', 'title.registration': '用户已注册', 'title.login': '用户上次登录',
    'maintenanceStatus.1': '已计划', 'maintenanceStatus.2': '进行中', 'maintenanceStatus.3': '已完成', 'maintenanceStatus.4': '已取消',
    'maintenanceType.1': '保养', 'maintenanceType.2': '维修', 'maintenanceType.3': '加油', 'maintenanceType.100': '其他',
    'paymentStatus.completed': '已付款', 'paymentStatus.succeeded': 'YooKassa 付款成功', 'paymentStatus.pending': '等待中',
    'paymentStatus.processing': '处理中', 'paymentStatus.waiting_for_capture': '等待确认',
    'paymentStatus.paid_waiting_confirmation': '已付款，等待确认', 'paymentStatus.canceled': '已取消',
    'paymentStatus.failed': '失败', 'paymentStatus.refunded': '已退款', 'paymentStatus.': '无状态',
    'paymentMethod.yookassa': 'YooKassa', 'paymentMethod.yookassa_extension': 'YooKassa：续订',
    'paymentMethod.workspace_purchase': '组织购买', 'paymentMethod.workspace_extension': '组织续订', 'paymentMethod.': '未指定',
    'supportType.question': '问题咨询', 'supportType.suggestion': '建议', 'supportType.bug': '故障', 'supportType.billing': '账单', 'supportType.cooperation': '合作',
    'supportStatus.new': '新建', 'supportStatus.in_progress': '处理中', 'supportStatus.closed': '已关闭', 'supportStatus.spam': '垃圾信息'
  }
}

export function getAdminRecordLabel(group: string, code: string | number | undefined, fallback: string, locale: UiLocale): string {
  if (code === undefined && group === 'maintenanceStatus') {
    code = ({ Planned: 1, InProgress: 2, Completed: 3, Cancelled: 4 } as Record<string, number>)[fallback]
  }
  if (code === undefined && group === 'role') {
    code = ({ Владелец: 'owner', Администратор: 'admin', Сотрудник: 'employee', Исполнитель: 'performer', Наблюдатель: 'viewer' } as Record<string, string>)[fallback]
  }
  if (code === undefined) return fallback
  let normalized = String(code).trim().toLowerCase()
  if (group === 'paymentMethod' && /^(?:yookassa_)?(?:workspace_purchase|workspace_extension):/.test(normalized)) {
    normalized = normalized.split(':')[0]
  }

  return labels[locale][`${group}.${normalized}`] ?? fallback
}

export function getAdminPaymentStatusLabel(payment: { applicationStatus?: string | null; paymentStatus?: string | null; statusName: string }, locale: UiLocale): string {
  return getAdminRecordLabel('paymentStatus', payment.applicationStatus === 'needs_attention' ? 'needs_attention' : payment.paymentStatus ?? '', payment.statusName, locale)
}

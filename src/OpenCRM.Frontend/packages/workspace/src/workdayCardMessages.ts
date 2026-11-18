import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: { workdayCard: {
    today: 'Сегодня', tomorrow: 'Завтра', noTime: 'Без времени', inProgress: 'В работе',
    ready: 'Готов к выдаче', handedOver: 'Выдан', logo: 'Логотип {name}', noServices: 'Услуги не указаны',
    more: 'ещё {count}', progress: 'Выполнено {completed} из {total}',
    progressLabel: 'Выполнение работ: {completed} из {total}', progressValue: 'Выполнено {completed} из {total} работ',
    awaitingReview: 'Работы завершены · ждёт проверки', paused: 'На паузе: {count}', client: 'Клиент',
    call: 'Позвонить: {phone}', callClient: 'Позвонить клиенту', plannedUntil: 'План до {time}',
    past: 'Прошлая запись', endsToday: 'Плановое окончание сегодня', continuing: 'Продолжается',
    clarify: 'Уточнить запись', openBooking: 'Открыть запись', openWork: 'Открыть работу'
  } },
  en: { workdayCard: {
    today: 'Today', tomorrow: 'Tomorrow', noTime: 'No time set', inProgress: 'In progress',
    ready: 'Ready for handover', handedOver: 'Handed over', logo: '{name} logo', noServices: 'No services specified',
    more: '{count} more', progress: '{completed} of {total} completed',
    progressLabel: 'Work progress: {completed} of {total}', progressValue: '{completed} of {total} tasks completed',
    awaitingReview: 'Work completed · awaiting review', paused: 'Paused: {count}', client: 'Customer',
    call: 'Call: {phone}', callClient: 'Call customer', plannedUntil: 'Scheduled until {time}',
    past: 'Past appointment', endsToday: 'Scheduled to finish today', continuing: 'Continuing',
    clarify: 'Review appointment', openBooking: 'Open appointment', openWork: 'Open job'
  } },
  'zh-Hans': { workdayCard: {
    today: '今天', tomorrow: '明天', noTime: '未指定时间', inProgress: '进行中',
    ready: '已准备交付', handedOver: '已交付', logo: '{name} 标志', noServices: '未指定服务',
    more: '另有 {count} 项', progress: '已完成 {completed} / {total} 项',
    progressLabel: '工作进度：{completed} / {total}', progressValue: '已完成 {completed} / {total} 项工作',
    awaitingReview: '工作已完成 · 等待审核', paused: '已暂停：{count}', client: '客户',
    call: '拨打：{phone}', callClient: '致电客户', plannedUntil: '计划完成时间：{time}',
    past: '已过期预约', endsToday: '计划今天完成', continuing: '持续进行',
    clarify: '核实预约', openBooking: '打开预约', openWork: '打开工作'
  } }
}

for (const [locale, value] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, value)
}

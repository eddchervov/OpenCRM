import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: { schedulerControl: {
    listLabel: 'Список записей', searchLabel: 'Поиск по автомобилю, номеру, услуге или сотруднику', searchPlaceholder: 'Автомобиль, номер, услуга, сотрудник…',
    filterLocation: 'Фильтр по посту', filterStatus: 'Фильтр по статусу', allLocations: 'Все посты', allStatuses: 'Все статусы', noPost: 'Без поста',
    rowsCount: 'Записей: {count}', emptyFiltered: 'По выбранным условиям записей нет.', emptyList: 'Записей пока нет.', time: 'Время', vehicle: 'Автомобиль',
    work: 'Работы', locationAssignee: 'Пост / исполнитель', status: 'Статус', cost: 'Стоимость', actions: 'Действия', logoAlt: 'Логотип {name}',
    noWorks: 'Работы не указаны', unassigned: 'Исполнитель не назначен', noTime: 'Без времени', statusNotSpecified: 'Статус не указан',
    scheduled: 'Запланировано', inProgress: 'В работе', completed: 'Выполнено', ready: 'Готово к выдаче', handedOver: 'Выдано', cancelled: 'Отменено',
    viewSchedule: 'Вид расписания', interval: 'Интервал записей', from: 'С', to: 'По', today: 'Сегодня', previousDay: 'Предыдущий день', date: 'Дата', nextDay: 'Следующий день',
    posts: 'Посты', slots: 'Слоты', list: 'Список', calendar: 'Календарь',
    scheduleNotConfigured: 'График работы не настроен', closedDay: 'Выходной день', setupSchedule: 'Настройте график работы', scheduleMissing: 'График работы не настроен',
    todayClosed: 'Сегодня выходной', selectedDayClosed: 'В этот день выходной', organizationHoursRequired: 'Укажите рабочие дни и часы организации, чтобы планировать записи по графику.',
    askAdministrator: 'Попросите администратора организации указать рабочие дни и часы.', closedOnDate: '{date} — выходной по графику организации.', nearestWorkDay: 'К ближайшему рабочему дню',
    nextWorkDayMissing: 'Ближайший рабочий день в графике не указан.', configureSchedule: 'Настроить график',
    newAppointment: 'Новая запись', closeDialog: 'Закрыть', unsavedAppointment: 'Несохранённая запись', unsavedPrompt: 'Изменения не сохранены. Закрыть запись без сохранения?',
    continueEditing: 'Продолжить редактирование', closeWithoutSaving: 'Закрыть без сохранения'
  } },
  en: { schedulerControl: {
    listLabel: 'Appointment list', searchLabel: 'Search vehicles, registration plates, services, or staff', searchPlaceholder: 'Vehicle, plate, service, or staff…',
    filterLocation: 'Filter by bay', filterStatus: 'Filter by status', allLocations: 'All bays', allStatuses: 'All statuses', noPost: 'No bay',
    rowsCount: 'Appointments: {count}', emptyFiltered: 'No appointments match these filters.', emptyList: 'There are no appointments yet.', time: 'Time', vehicle: 'Vehicle',
    work: 'Services', locationAssignee: 'Bay / technician', status: 'Status', cost: 'Cost', actions: 'Actions', logoAlt: '{name} logo',
    noWorks: 'No services listed', unassigned: 'No technician assigned', noTime: 'No time set', statusNotSpecified: 'Status not specified',
    scheduled: 'Scheduled', inProgress: 'In progress', completed: 'Completed', ready: 'Ready for handover', handedOver: 'Handed over', cancelled: 'Cancelled',
    viewSchedule: 'Schedule view', interval: 'Appointment date range', from: 'From', to: 'To', today: 'Today', previousDay: 'Previous day', date: 'Date', nextDay: 'Next day',
    posts: 'Bays', slots: 'Time slots', list: 'List', calendar: 'Calendar',
    scheduleNotConfigured: 'Working hours are not configured', closedDay: 'Closed day', setupSchedule: 'Set up working hours', scheduleMissing: 'Working hours are not configured',
    todayClosed: 'Closed today', selectedDayClosed: 'Closed on this day', organizationHoursRequired: 'Set the organization’s working days and hours to schedule appointments.',
    askAdministrator: 'Ask your organization administrator to set working days and hours.', closedOnDate: '{date} is closed according to the organization schedule.', nearestWorkDay: 'Go to the next working day',
    nextWorkDayMissing: 'The next working day is not set in the schedule.', configureSchedule: 'Configure schedule',
    newAppointment: 'New appointment', closeDialog: 'Close', unsavedAppointment: 'Unsaved appointment', unsavedPrompt: 'Your changes are not saved. Close this appointment without saving?',
    continueEditing: 'Continue editing', closeWithoutSaving: 'Close without saving'
  } },
  'zh-Hans': { schedulerControl: {
    listLabel: '预约列表', searchLabel: '搜索车辆、车牌、服务或员工', searchPlaceholder: '车辆、车牌、服务或员工…',
    filterLocation: '按工位筛选', filterStatus: '按状态筛选', allLocations: '所有工位', allStatuses: '所有状态', noPost: '无工位',
    rowsCount: '预约数：{count}', emptyFiltered: '没有符合筛选条件的预约。', emptyList: '暂无预约。', time: '时间', vehicle: '车辆',
    work: '服务', locationAssignee: '工位 / 维修人员', status: '状态', cost: '费用', actions: '操作', logoAlt: '{name} 标志',
    noWorks: '未列出服务', unassigned: '未指派维修人员', noTime: '未指定时间', statusNotSpecified: '未指定状态',
    scheduled: '已计划', inProgress: '进行中', completed: '已完成', ready: '已准备交付', handedOver: '已交付', cancelled: '已取消',
    viewSchedule: '日程视图', interval: '预约日期范围', from: '从', to: '到', today: '今天', previousDay: '前一天', date: '日期', nextDay: '后一天',
    posts: '工位', slots: '时间段', list: '列表', calendar: '日历',
    scheduleNotConfigured: '尚未设置工作时间', closedDay: '休息日', setupSchedule: '设置工作时间', scheduleMissing: '尚未设置工作时间',
    todayClosed: '今天休息', selectedDayClosed: '当天休息', organizationHoursRequired: '请设置组织的工作日和工作时间，以便按日程安排预约。',
    askAdministrator: '请联系组织管理员设置工作日和工作时间。', closedOnDate: '{date} 按组织日程安排为休息日。', nearestWorkDay: '前往下一个工作日',
    nextWorkDayMissing: '日程中尚未设置下一个工作日。', configureSchedule: '设置日程',
    newAppointment: '新建预约', closeDialog: '关闭', unsavedAppointment: '未保存的预约', unsavedPrompt: '更改尚未保存。要关闭预约而不保存吗？',
    continueEditing: '继续编辑', closeWithoutSaving: '不保存并关闭'
  } }
}

for (const [locale, localeMessages] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, localeMessages)
}

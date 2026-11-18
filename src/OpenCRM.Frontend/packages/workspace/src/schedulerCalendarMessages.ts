import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: {
    schedulerCalendar: {
      dayAria: 'Расписание по постам', postColumnAria: 'Пост {post}', searchLabel: 'Поиск по записям', searchPlaceholder: 'Автомобиль, услуга, сотрудник…',
      filterPostLabel: 'Показать пост', timeAxis: 'Время', createAt: 'Создать запись: {post}, {time}', create: '＋ Записать',
      unknownTime: 'Время не указано', closed: 'Закрыт', reschedule: 'Перенести', noMatch: 'Ничего не найдено', noRecords: 'Записей нет',
      noRecordsForQuery: 'По запросу «{query}» записей нет.', continuing: 'Продолжается', until: 'До {time}', endOfDay: 'конца дня', legend: 'Статусы записей', greenStatus: 'Готово / выполнено',
      slotsAria: 'Свободное время по постам', duration: 'Длительность', workDuration: 'Длительность работы',
      scrollPosts: 'Прокрутка постов', showPostsLeft: 'Показать посты слева', showPostsRight: 'Показать посты справа',
      noWorkingIntervals: 'В этот день нет рабочих интервалов.', postClosed: 'Пост закрыт', nearest: 'Ближайшее: {time}', noFreeTime: 'Свободного времени нет',
      morning: 'Утро', day: 'День', evening: 'Вечер', slotAria: 'Записать на {post}, {time}', noFreeIntervals: 'Нет свободных интервалов',
      createHint: 'Нажмите на свободный интервал, чтобы создать запись', viewOnly: 'Расписание доступно для просмотра', free: 'Свободно', busy: 'Занято',
      duration30: '30 минут', duration60: '1 час', duration90: '1 час 30 минут', duration120: '2 часа', duration180: '3 часа', duration240: '4 часа',
      previewTitle: 'Просмотр записи', record: 'Запись', status: 'Статус', start: 'Начало', end: 'Окончание', noTime: 'Без времени',
      location: 'Пост / место', unassigned: 'Не назначен', employees: 'Сотрудники', employeesUnassigned: 'Не назначены', services: 'Услуги',
      servicesUnspecified: 'Услуги не указаны.', close: 'Закрыть', openMaintenance: 'Открыть обслуживание',
      dayRecordsTitle: 'Записи на {date}', openDay: 'Открыть день по постам', newRecord: 'Новая запись'
    }
  },
  en: {
    schedulerCalendar: {
      dayAria: 'Schedule by bay', postColumnAria: 'Bay {post}', searchLabel: 'Search appointments', searchPlaceholder: 'Vehicle, service, or staff…',
      filterPostLabel: 'Show bay', timeAxis: 'Time', createAt: 'Create appointment: {post}, {time}', create: '＋ Book',
      unknownTime: 'Time not specified', closed: 'Closed', reschedule: 'Reschedule', noMatch: 'No matches found', noRecords: 'No appointments',
      noRecordsForQuery: 'No appointments found for “{query}”.', continuing: 'Continues', until: 'Until {time}', endOfDay: 'the end of the day', legend: 'Appointment statuses', greenStatus: 'Ready / completed',
      slotsAria: 'Available time by bay', duration: 'Duration', workDuration: 'Appointment duration',
      scrollPosts: 'Scroll through bays', showPostsLeft: 'Show bays to the left', showPostsRight: 'Show bays to the right',
      noWorkingIntervals: 'There are no working hours on this day.', postClosed: 'Bay closed', nearest: 'Next available: {time}', noFreeTime: 'No available time',
      morning: 'Morning', day: 'Day', evening: 'Evening', slotAria: 'Book at {post}, {time}', noFreeIntervals: 'No available time slots',
      createHint: 'Select an available time slot to create an appointment', viewOnly: 'Schedule is available for viewing', free: 'Available', busy: 'Busy',
      duration30: '30 minutes', duration60: '1 hour', duration90: '1 hour 30 minutes', duration120: '2 hours', duration180: '3 hours', duration240: '4 hours',
      previewTitle: 'Appointment details', record: 'Appointment', status: 'Status', start: 'Start', end: 'End', noTime: 'Time not specified',
      location: 'Bay / location', unassigned: 'Not assigned', employees: 'Staff', employeesUnassigned: 'No staff assigned', services: 'Services',
      servicesUnspecified: 'No services specified.', close: 'Close', openMaintenance: 'Open service record',
      dayRecordsTitle: 'Appointments for {date}', openDay: 'Open bay schedule', newRecord: 'New appointment'
    }
  },
  'zh-Hans': {
    schedulerCalendar: {
      dayAria: '工位日程', postColumnAria: '工位 {post}', searchLabel: '搜索预约', searchPlaceholder: '车辆、服务或员工…',
      filterPostLabel: '显示工位', timeAxis: '时间', createAt: '新建预约：{post}，{time}', create: '＋ 预约',
      unknownTime: '未指定时间', closed: '已关闭', reschedule: '改期', noMatch: '没有匹配项', noRecords: '暂无预约',
      noRecordsForQuery: '没有找到与“{query}”匹配的预约。', continuing: '继续中', until: '至 {time}', endOfDay: '当天结束', legend: '预约状态', greenStatus: '待交付 / 已完成',
      slotsAria: '工位可用时间', duration: '时长', workDuration: '预约时长',
      scrollPosts: '滚动查看工位', showPostsLeft: '显示左侧工位', showPostsRight: '显示右侧工位',
      noWorkingIntervals: '当天没有工作时间段。', postClosed: '工位已关闭', nearest: '最近可用时间：{time}', noFreeTime: '没有可用时间',
      morning: '上午', day: '下午', evening: '晚上', slotAria: '预约工位 {post}，时间 {time}', noFreeIntervals: '没有可用时段',
      createHint: '点击可用时段以创建预约', viewOnly: '日程仅供查看', free: '可用', busy: '已占用',
      duration30: '30 分钟', duration60: '1 小时', duration90: '1 小时 30 分钟', duration120: '2 小时', duration180: '3 小时', duration240: '4 小时',
      previewTitle: '预约详情', record: '预约', status: '状态', start: '开始', end: '结束', noTime: '未指定时间',
      location: '工位 / 位置', unassigned: '未分配', employees: '员工', employeesUnassigned: '未分配员工', services: '服务',
      servicesUnspecified: '未指定服务。', close: '关闭', openMaintenance: '打开维修记录',
      dayRecordsTitle: '{date} 的预约', openDay: '打开工位日程', newRecord: '新建预约'
    }
  }
}

for (const [locale, localeMessages] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, localeMessages)
}

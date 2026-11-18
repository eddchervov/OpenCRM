import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: {
    shifts: {
      previousWeek: 'Предыдущая неделя', nextWeek: 'Следующая неделя', repeatPreviousWeek: 'Повторить прошлую неделю', addShift: 'Добавить смену',
      schedule: 'График смен', load: 'Загрузка', organizationTime: 'Время организации',
      organizationTimeZone: 'Время организации: {timeZone}', timeZoneSettings: 'Настройки часового пояса',
      teamRequiredLoad: 'Добавьте исполнителей в разделе «Команда», чтобы посмотреть загрузку.',
      teamRequiredSchedule: 'Добавьте исполнителей в разделе «Команда», чтобы составить график.',
      employee: 'Сотрудник', archived: ' · архив', start: 'Начало: {date}', unpublished: 'Не опубликована · нажмите, чтобы опубликовать',
      addShiftFor: 'Добавить смену: {employee}, {day}', noShifts: 'На этой неделе смен пока нет.',
      timeZoneNotConfigured: 'Часовой пояс организации пока не настроен.', openGeneralSettings: 'Открыть общие настройки',
      editShift: 'Изменить смену', employeeLabel: 'Сотрудник', startDate: 'Начало · дата', startTime: 'Начало · время',
      endDate: 'Конец · дата', endTime: 'Конец · время', breakMinutes: 'Перерыв, мин', changeReason: 'Причина изменения',
      cancelShift: 'Отменить смену', close: 'Закрыть', saveChanges: 'Сохранить изменения', saveAndPublish: 'Сохранить и опубликовать',
      weekCopyResult: 'Добавлено смен: {count}.', noWeekToCopy: 'В прошлой неделе нет смен для копирования.',
      loadError: 'Не удалось загрузить смены.', saveError: 'Не удалось сохранить смены.'
    },
    shiftLoad: {
      viewMode: 'Режим просмотра загрузки', oneEmployee: 'Один сотрудник', allEmployees: 'Все сотрудники', employee: 'Сотрудник', day: 'День',
      assignmentsAccess: 'Для просмотра назначенных обслуживаний нужен доступ к записям.', planned: 'Запланировано:',
      freeInShift: 'Без записей в смене:', outsideShift: 'Вне смены:', shiftTitle: 'Смена {start}–{end}',
      outsideShiftTitle: ' · вне смены', noTime: 'Без времени:',
      note: 'Время записей показывает план, а не фактически отработанные часы. Перерыв из смены на шкале не размещён.',
      hours: '{hours} ч {minutes} мин'
    },
    shiftsApi: { actionError: 'Не удалось выполнить действие со сменами.' }
  },
  en: {
    shifts: {
      previousWeek: 'Previous week', nextWeek: 'Next week', repeatPreviousWeek: 'Copy previous week', addShift: 'Add shift',
      schedule: 'Shift schedule', load: 'Workload', organizationTime: 'Organization time',
      organizationTimeZone: 'Organization time: {timeZone}', timeZoneSettings: 'Time zone settings',
      teamRequiredLoad: 'Add employees in the Team section to view workload.',
      teamRequiredSchedule: 'Add employees in the Team section to create a schedule.',
      employee: 'Employee', archived: ' · archived', start: 'Starts: {date}', unpublished: 'Not published · select to publish',
      addShiftFor: 'Add shift: {employee}, {day}', noShifts: 'There are no shifts this week yet.',
      timeZoneNotConfigured: 'The organization time zone is not configured yet.', openGeneralSettings: 'Open general settings',
      editShift: 'Edit shift', employeeLabel: 'Employee', startDate: 'Start · date', startTime: 'Start · time',
      endDate: 'End · date', endTime: 'End · time', breakMinutes: 'Break, minutes', changeReason: 'Reason for change',
      cancelShift: 'Cancel shift', close: 'Close', saveChanges: 'Save changes', saveAndPublish: 'Save and publish',
      weekCopyResult: 'Shifts added: {count}.', noWeekToCopy: 'There are no shifts to copy from the previous week.',
      loadError: 'Could not load shifts.', saveError: 'Could not save shifts.'
    },
    shiftLoad: {
      viewMode: 'Workload view mode', oneEmployee: 'One employee', allEmployees: 'All employees', employee: 'Employee', day: 'Day',
      assignmentsAccess: 'Access to work records is required to view assigned jobs.', planned: 'Planned:',
      freeInShift: 'Unassigned time during shift:', outsideShift: 'Outside shift:', shiftTitle: 'Shift {start}–{end}',
      outsideShiftTitle: ' · outside shift', noTime: 'No scheduled time:',
      note: 'Scheduled work times are estimates, not actual hours worked. Break time is not shown on the timeline.',
      hours: '{hours} hr {minutes} min'
    },
    shiftsApi: { actionError: 'Could not complete the shift action.' }
  },
  'zh-Hans': {
    shifts: {
      previousWeek: '上一周', nextWeek: '下一周', repeatPreviousWeek: '复制上一周', addShift: '添加班次',
      schedule: '班次安排', load: '工作量', organizationTime: '组织时间',
      organizationTimeZone: '组织时间：{timeZone}', timeZoneSettings: '时区设置',
      teamRequiredLoad: '请在“团队”中添加员工以查看工作量。',
      teamRequiredSchedule: '请在“团队”中添加员工以制定班次安排。',
      employee: '员工', archived: ' · 已归档', start: '开始：{date}', unpublished: '尚未发布 · 点击以发布',
      addShiftFor: '为 {employee} 添加班次，日期：{day}', noShifts: '本周暂无班次。',
      timeZoneNotConfigured: '尚未设置组织时区。', openGeneralSettings: '打开常规设置',
      editShift: '编辑班次', employeeLabel: '员工', startDate: '开始 · 日期', startTime: '开始 · 时间',
      endDate: '结束 · 日期', endTime: '结束 · 时间', breakMinutes: '休息时间，分钟', changeReason: '更改原因',
      cancelShift: '取消班次', close: '关闭', saveChanges: '保存更改', saveAndPublish: '保存并发布',
      weekCopyResult: '已添加班次：{count}。', noWeekToCopy: '上一周没有可复制的班次。',
      loadError: '无法加载班次。', saveError: '无法保存班次。'
    },
    shiftLoad: {
      viewMode: '工作量查看模式', oneEmployee: '单个员工', allEmployees: '所有员工', employee: '员工', day: '日期',
      assignmentsAccess: '需要有权查看工单才能查看已分配的工作。', planned: '计划：',
      freeInShift: '班次内未安排工单：', outsideShift: '班次外：', shiftTitle: '班次 {start}–{end}',
      outsideShiftTitle: ' · 班次外', noTime: '未安排时间：',
      note: '工单时间表示计划时长，不代表实际工时。时间轴不显示班次中的休息时间。',
      hours: '{hours} 小时 {minutes} 分钟'
    },
    shiftsApi: { actionError: '无法完成班次操作。' }
  }
}

type ShiftMessageKey = keyof typeof messages.ru.shifts
type ShiftLoadMessageKey = keyof typeof messages.ru.shiftLoad
type ShiftApiMessageKey = keyof typeof messages.ru.shiftsApi
const messagesByLocale: Record<UiLocale, {
  shifts: Record<ShiftMessageKey, string>
  shiftLoad: Record<ShiftLoadMessageKey, string>
  shiftsApi: Record<ShiftApiMessageKey, string>
}> = messages

for (const [locale, localeMessages] of Object.entries(messagesByLocale)) {
  registerMessages(locale as UiLocale, localeMessages)
}

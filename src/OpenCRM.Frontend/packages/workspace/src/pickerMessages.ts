import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  "ru": {
    "pickers": {
      "unassigned": "Не назначен",
      "archived": "{name} · архив",
      "assignedEmployee": "Назначенный сотрудник",
      "employeesError": "Не удалось загрузить сотрудников.",
      "employee": "Сотрудник",
      "offShift": "По графику сотрудник вне смены. Назначить можно.",
      "offShiftDetails": "Сотрудник вне смены: подробности",
      "scheduleError": "Не удалось проверить график. Назначить можно.",
      "retry": "Повторить",
      "clientSelected": "Клиент выбран",
      "clientsError": "Не удалось найти клиентов.",
      "chooseOrCreate": "Выберите клиента из списка или нажмите «Новый клиент».",
      "chooseClient": "Выберите клиента из списка.",
      "newClientRequired": "Укажите телефон или имя нового клиента.",
      "phoneInvalid": "Проверьте номер телефона нового клиента.",
      "client": "Клиент",
      "change": "Изменить",
      "newClient": "Новый клиент",
      "cancel": "Отмена",
      "phone": "Телефон",
      "name": "Имя",
      "optional": "Необязательно",
      "namePhone": "Имя или телефон",
      "searching": "Ищем клиентов…",
      "enterQuery": "Введите имя или телефон",
      "notFound": "Клиенты не найдены",
      "foundClients": "Найденные клиенты",
      "more": "Показать ещё",
      "createClient": "＋ Новый клиент"
    }
  },
  "en": {
    "pickers": {
      "unassigned": "Unassigned",
      "archived": "{name} · archived",
      "assignedEmployee": "Assigned employee",
      "employeesError": "Could not load employees.",
      "employee": "Employee",
      "offShift": "The employee is off shift according to the schedule. You can still assign them.",
      "offShiftDetails": "Employee off shift: details",
      "scheduleError": "Could not check the schedule. You can still assign the employee.",
      "retry": "Retry",
      "clientSelected": "Client selected",
      "clientsError": "Could not find clients.",
      "chooseOrCreate": "Choose a client from the list or select “New client”.",
      "chooseClient": "Choose a client from the list.",
      "newClientRequired": "Enter a phone number or name for the new client.",
      "phoneInvalid": "Check the new client’s phone number.",
      "client": "Client",
      "change": "Change",
      "newClient": "New client",
      "cancel": "Cancel",
      "phone": "Phone",
      "name": "Name",
      "optional": "Optional",
      "namePhone": "Name or phone",
      "searching": "Searching for clients…",
      "enterQuery": "Enter a name or phone number",
      "notFound": "No clients found",
      "foundClients": "Matching clients",
      "more": "Show more",
      "createClient": "＋ New client"
    }
  },
  "zh-Hans": {
    "pickers": {
      "unassigned": "未分配",
      "archived": "{name} · 已归档",
      "assignedEmployee": "已分配员工",
      "employeesError": "无法加载员工。",
      "employee": "员工",
      "offShift": "根据排班，该员工当前不在班。您仍可分配此员工。",
      "offShiftDetails": "员工不在班：详情",
      "scheduleError": "无法检查排班。您仍可分配此员工。",
      "retry": "重试",
      "clientSelected": "已选择客户",
      "clientsError": "无法查找客户。",
      "chooseOrCreate": "从列表中选择客户，或点击“新建客户”。",
      "chooseClient": "请从列表中选择客户。",
      "newClientRequired": "请填写新客户的电话或姓名。",
      "phoneInvalid": "请检查新客户的电话号码。",
      "client": "客户",
      "change": "更改",
      "newClient": "新建客户",
      "cancel": "取消",
      "phone": "电话",
      "name": "姓名",
      "optional": "选填",
      "namePhone": "姓名或电话",
      "searching": "正在搜索客户…",
      "enterQuery": "请输入姓名或电话",
      "notFound": "未找到客户",
      "foundClients": "匹配的客户",
      "more": "显示更多",
      "createClient": "＋ 新建客户"
    }
  }
}

for (const [locale, value] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, value)
}

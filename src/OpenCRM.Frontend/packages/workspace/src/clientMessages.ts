import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: {
    clients: {
      comment: 'Комментарий',
      title: 'Клиенты', vehicles: 'Автомобили', services: 'Услуги', noRecords: 'Нет записей',
      sortNameAsc: 'ФИО: А-Я', sortNameDesc: 'ФИО: Я-А', sortVehicles: 'Больше техники', sortServices: 'Больше обслуживаний', sortRecent: 'Недавно обслуживались',
      allStatuses: 'Все статусы', active: 'Активный', lead: 'Лид', attention: 'Требует внимания', paused: 'Пауза', lost: 'Потерян',
      allTypes: 'Все типы', person: 'Физлицо', company: 'Организация', allResponsible: 'Все ответственные', noResponsible: 'Без ответственного', allTags: 'Все теги',
      none: 'нет', unknownType: 'Тип не указан', loadError: 'Не удалось загрузить клиентов.', add: 'Добавить клиента', summary: 'Сводка по клиентам', allClients: 'Всего клиентов',
      searchPlaceholder: 'Имя, телефон или заметка', search: 'Поиск клиентов', clientType: 'Тип клиента', responsible: 'Ответственный за клиента', filters: 'Фильтры',
      clientStatus: 'Статус клиентов', activeList: 'Активные', archive: 'Архив', sorting: 'Сортировка', status: 'Статус', tag: 'Тег', segments: 'Сегменты клиентов', reset: 'Сбросить фильтры',
      listView: 'Вид списка клиентов', list: 'Список', tiles: 'Плитка', loading: 'Загружаем клиентов…', refreshing: 'Обновляем список…', retry: 'Повторить',
      client: 'Клиент', phone: 'Телефон', lastVisit: 'Последний визит', action: 'Действие', notSpecified: 'Не указан', unassigned: 'Не назначен', never: 'Ещё не было', open: 'Открыть',
      last: 'Последнее', archiveEmpty: 'В архиве ничего не найдено', notFound: 'Клиенты не найдены', firstClient: 'Добавьте первого клиента', noClients: 'Клиентов пока нет',
      changeSearch: 'Измените поиск или сбросьте фильтры.', firstClientHint: 'Сохраните имя и телефон — автомобили и историю можно добавить позже.', organizationClients: 'Здесь появятся клиенты организации.',
      onPage: 'На странице', pageSize: 'Размер страницы', pageNavigation: 'Навигация по страницам клиентов', createClient: '＋ Добавить клиента',
      partyPerson: 'Физлицо', partyCompany: 'Организация', vehiclesEmpty: 'Пока ничего не добавлено.', noPhoto: 'Нет фото',
      allVehicles: 'Все автомобили', allItems: 'Весь список', chooseVehicle: 'Выберите автомобиль для записи', searchVehicle: 'Поиск автомобиля',
      vehicleSearchPlaceholder: 'Марка, модель или госномер…', allClientVehicles: 'Все автомобили клиента', vehicleNotFound: 'Ничего не найдено. Попробуйте другой запрос.',
      foundCount: 'Найдено: {found} из {total}', close: 'Закрыть', openVehicle: 'Открыть автомобиль: {name}', lookupError: 'Не удалось найти клиентов.', linkError: 'Не удалось изменить связь с клиентом.', initial: 'К',
      commentLimit: 'Комментарий не должен превышать 4000 символов.', readOnly: 'Рабочее пространство сейчас доступно только для просмотра.', historyTitle: 'История услуг', defectsTitle: 'Неисправности', createWork: 'Создать работу', createAppointment: 'Записать',
      zeroRecords: '0 записей', rangeOf: '{from}–{to} из {total}', phoneCopied: 'Телефон скопирован', copyPhoneError: 'Не удалось скопировать телефон', cancelled: 'Отменено', handedOver: 'Выдано', readyForHandover: 'Готово к выдаче',
      legalEntity: 'Юридическое лицо (ООО, АО и др.)', entrepreneur: 'ИП', historyLoadError: 'Не удалось загрузить историю работ клиента.', cardLoadError: 'Не удалось загрузить карточку клиента.', commentSaveError: 'Не удалось сохранить комментарий.', clientSaveError: 'Не удалось сохранить клиента.', clientTypeRequired: 'Выберите тип клиента.',
      firstName: 'Имя', lastName: 'Фамилия', middleName: 'Отчество', lengthHint: '{current} / {limit} символов', repair: 'Ремонт', refueling: 'Дозаправка', other: 'Другое', service: 'Обслуживание', planned: 'Запланировано', inProgress: 'В работе', completed: 'Выполнено', unknown: 'Неизвестно', maintenance: 'Техническое обслуживание', newClient: 'Новый клиент', editClient: 'Редактирование клиента', cardTitle: 'Карточка клиента', edit: 'Редактировать', newAppointment: 'Новая запись', newWork: 'Новая работа', loadingCard: 'Загружаем карточку клиента…', contactsAndDetails: 'Контакты и реквизиты', maxCharacters: 'До {count} символов', organizationType: 'Вид организации', businessName: 'Название / ФИО ИП', organizationName: 'Название организации', invalidPhone: 'Проверьте номер телефона.', companyDetails: 'Реквизиты', taxId10Or12: '10 или 12 цифр', digits12: '12 цифр', digits10: '10 цифр', taxIdHelp: 'ИНН юрлица: 10 цифр; ИНН ИП: 12 цифр', taxIdEntrepreneurHelp: 'ИНН ИП: 12 цифр', taxIdLegalHelp: 'ИНН юрлица: 10 цифр', characters9: '9 знаков', registrationCodeHelp: 'КПП: 9 знаков', legalAddress: 'Юридический адрес', inn: 'Российский ИНН', kpp: 'Российский КПП', clientManagement: 'Работа с клиентом', tags: 'Теги', removeTag: 'Удалить тег {tag}', addTag: 'Добавить тег…', tagHelp: 'Введите теги через запятую или Enter.', saveChanges: 'Сохранить изменения', appointments: 'Записи', workOrders: 'Работы', paid: 'Оплачено', debt: 'Долг', lastWork: 'Последняя работа', clientInformation: 'Информация о клиенте', contacts: 'Контакты', copyPhone: 'Скопировать телефон', clientSince: 'Клиент с', archivedSince: 'В архиве с', commentEmpty: 'Комментарий не добавлен.', editComment: 'Изменить комментарий', addComment: 'Добавить комментарий', nextAppointment: 'Ближайшая запись', openAppointment: 'Открыть запись', clientCardSections: 'Раздел карточки клиента', conversations: 'Переписка', visitHistory: 'История посещений', workFilter: 'Фильтр работ', all: 'Все', upcomingAndCurrent: 'Предстоящие и текущие', searchWorkHistory: 'Поиск по истории работ', vehicleOrWork: 'Автомобиль или работа…', loadingWorkHistory: 'Загружаем историю работ…', noWorkMatches: 'По заданным условиям работы не найдены.', emptyWorkHistory: 'История работ пока пустая.', dateVehicle: 'Дата / автомобиль', work: 'Работы', amount: 'Сумма', workHistoryPages: 'Страницы истории работ', saveComment: 'Сохранить комментарий', validation: {
        nameLength: '{label}: не более {limit} символов.', firstNameRequired: 'Укажите имя клиента.', fullNameLength: 'ФИО не должно превышать {limit} символов.',
        companyNameRequired: 'Укажите название организации или ИП.', companyNameLength: 'Название не должно превышать {limit} символов.', taxCodeLength: '{label}: не более {limit} символов.',
        innDigits: 'Российский ИНН: нужно {count} цифр.', kppFormat: 'Проверьте формат российского КПП.', kppLength: '{label}: нужно 9 знаков.', addressLength: 'Юридический адрес: не более {limit} символов.', phoneLength: 'Телефон: не более {limit} символов.'
      },
      linkTitle: 'Связь с клиентом', currentlyLinked: 'Сейчас связан', unlinkConfirm: 'Связь будет удалена, а ожидающие сообщения отменены. Продолжить?', unlink: 'Отвязать',
      keepLink: 'Оставить связь', confirmUnlink: 'Подтвердить отвязку', linkWarning: 'При сохранении связи неотправленные сообщения этого диалога будут отменены.',
      findClient: 'Найти клиента', findClientPlaceholder: 'Имя или телефон клиента', minimumSearch: 'Введите не менее двух символов, чтобы найти клиента.', searchingClients: 'Ищем клиентов…',
      lookupEmpty: 'Клиенты по этому запросу не найдены.', searchResults: 'Результаты поиска клиентов', phoneMissing: 'Телефон не указан', currentLink: 'Текущая связь',
      linked: 'Связан', selected: 'Выбран', select: 'Выбрать', foundTotal: 'Найдено: {total}', searchResultPages: 'Страницы результатов поиска', cancel: 'Отмена', saving: 'Сохраняем…', saveLink: 'Сохранить связь', linkClient: 'Связать с клиентом',
    }
  },
  en: {
    clients: {
      comment: 'Comment',
      title: 'Clients', vehicles: 'Vehicles', services: 'Services', noRecords: 'No records',
      sortNameAsc: 'Name: A–Z', sortNameDesc: 'Name: Z–A', sortVehicles: 'Most vehicles', sortServices: 'Most services', sortRecent: 'Recently serviced',
      allStatuses: 'All statuses', active: 'Active', lead: 'Lead', attention: 'Needs attention', paused: 'Paused', lost: 'Lost',
      allTypes: 'All types', person: 'Individual', company: 'Company', allResponsible: 'All assignees', noResponsible: 'Unassigned', allTags: 'All tags',
      none: 'None', unknownType: 'Type not specified', loadError: 'Could not load clients.', add: 'Add client', summary: 'Client summary', allClients: 'All clients',
      searchPlaceholder: 'Name, phone or note', search: 'Search clients', clientType: 'Client type', responsible: 'Client assignee', filters: 'Filters',
      clientStatus: 'Client status', activeList: 'Active', archive: 'Archive', sorting: 'Sort by', status: 'Status', tag: 'Tag', segments: 'Client segments', reset: 'Clear filters',
      listView: 'Client list view', list: 'List', tiles: 'Cards', loading: 'Loading clients…', refreshing: 'Refreshing list…', retry: 'Try again',
      client: 'Client', phone: 'Phone', lastVisit: 'Last visit', action: 'Action', notSpecified: 'Not provided', unassigned: 'Unassigned', never: 'Never', open: 'Open',
      last: 'Last', archiveEmpty: 'No clients found in the archive', notFound: 'No clients found', firstClient: 'Add your first client', noClients: 'There are no clients yet',
      changeSearch: 'Change your search or clear the filters.', firstClientHint: 'Save a name and phone number. You can add vehicles and history later.', organizationClients: 'Organization clients will appear here.',
      onPage: 'Per page', pageSize: 'Page size', pageNavigation: 'Client pages', createClient: '＋ Add client',
      partyPerson: 'Individual', partyCompany: 'Company', vehiclesEmpty: 'Nothing has been added yet.', noPhoto: 'No photo',
      allVehicles: 'All vehicles', allItems: 'View all', chooseVehicle: 'Choose a vehicle for the appointment', searchVehicle: 'Search vehicles',
      vehicleSearchPlaceholder: 'Make, model or registration plate…', allClientVehicles: 'All client vehicles', vehicleNotFound: 'No results. Try another search.',
      foundCount: 'Found: {found} of {total}', close: 'Close', openVehicle: 'Open vehicle: {name}', lookupError: 'Could not find clients.', linkError: 'Could not update the client link.', initial: 'C',
      commentLimit: 'Comments must be 4,000 characters or fewer.', readOnly: 'This workspace is currently read-only.', historyTitle: 'Service history', defectsTitle: 'Defects', createWork: 'Create work order', createAppointment: 'Book',
      zeroRecords: '0 records', rangeOf: '{from}–{to} of {total}', phoneCopied: 'Phone number copied', copyPhoneError: 'Could not copy phone number', cancelled: 'Cancelled', handedOver: 'Handed over', readyForHandover: 'Ready for handover',
      legalEntity: 'Legal entity (LLC, JSC, etc.)', entrepreneur: 'Sole proprietor', historyLoadError: 'Could not load the client work history.', cardLoadError: 'Could not load the client profile.', commentSaveError: 'Could not save the comment.', clientSaveError: 'Could not save the client.', clientTypeRequired: 'Select a client type.',
      firstName: 'First name', lastName: 'Last name', middleName: 'Middle name', lengthHint: '{current} / {limit} characters', repair: 'Repair', refueling: 'Refueling', other: 'Other', service: 'Service', planned: 'Planned', inProgress: 'In progress', completed: 'Completed', unknown: 'Unknown', maintenance: 'Maintenance', newClient: 'New client', editClient: 'Edit client', cardTitle: 'Client profile', edit: 'Edit', newAppointment: 'New appointment', newWork: 'New work order', loadingCard: 'Loading client profile…', contactsAndDetails: 'Contact and business details', maxCharacters: 'Up to {count} characters', organizationType: 'Organization type', businessName: 'Business or sole proprietor name', organizationName: 'Organization name', invalidPhone: 'Check the phone number.', companyDetails: 'Business details', taxId10Or12: '10 or 12 digits', digits12: '12 digits', digits10: '10 digits', taxIdHelp: 'Legal entity tax ID: 10 digits; sole proprietor: 12 digits', taxIdEntrepreneurHelp: 'Sole proprietor tax ID: 12 digits', taxIdLegalHelp: 'Legal entity tax ID: 10 digits', characters9: '9 characters', registrationCodeHelp: 'Registration code: 9 characters', legalAddress: 'Legal address', inn: 'Russian tax ID (INN)', kpp: 'Russian tax registration code (KPP)', clientManagement: 'Client management', tags: 'Tags', removeTag: 'Remove tag {tag}', addTag: 'Add a tag…', tagHelp: 'Enter tags separated by commas or press Enter.', saveChanges: 'Save changes', appointments: 'Appointments', workOrders: 'Work orders', paid: 'Paid', debt: 'Debt', lastWork: 'Last work order', clientInformation: 'Client information', contacts: 'Contacts', copyPhone: 'Copy phone number', clientSince: 'Client since', archivedSince: 'Archived since', commentEmpty: 'No comment added.', editComment: 'Edit comment', addComment: 'Add comment', nextAppointment: 'Upcoming appointment', openAppointment: 'Open appointment', clientCardSections: 'Client profile sections', conversations: 'Conversations', visitHistory: 'Visit history', workFilter: 'Work filter', all: 'All', upcomingAndCurrent: 'Upcoming and current', searchWorkHistory: 'Search work history', vehicleOrWork: 'Vehicle or work…', loadingWorkHistory: 'Loading work history…', noWorkMatches: 'No work orders match these filters.', emptyWorkHistory: 'There is no work history yet.', dateVehicle: 'Date / vehicle', work: 'Work orders', amount: 'Amount', workHistoryPages: 'Work history pages', saveComment: 'Save comment', validation: {
        nameLength: '{label}: no more than {limit} characters.', firstNameRequired: 'Enter the client’s first name.', fullNameLength: 'The full name must be {limit} characters or fewer.',
        companyNameRequired: 'Enter the company or sole proprietor name.', companyNameLength: 'The name must be {limit} characters or fewer.', taxCodeLength: '{label}: no more than {limit} characters.',
        innDigits: 'Russian tax ID (INN) must contain {count} digits.', kppFormat: 'Check the Russian tax registration code (KPP) format.', kppLength: '{label} must contain 9 characters.', addressLength: 'The legal address must be {limit} characters or fewer.', phoneLength: 'The phone number must be {limit} characters or fewer.'
      },
      linkTitle: 'Link a client', currentlyLinked: 'Currently linked', unlinkConfirm: 'This will remove the link and cancel pending messages. Continue?', unlink: 'Unlink',
      keepLink: 'Keep link', confirmUnlink: 'Confirm unlink', linkWarning: 'Saving the link will cancel unsent messages in this conversation.',
      findClient: 'Find a client', findClientPlaceholder: 'Client name or phone', minimumSearch: 'Enter at least two characters to find a client.', searchingClients: 'Searching clients…',
      lookupEmpty: 'No clients found for this search.', searchResults: 'Client search results', phoneMissing: 'No phone provided', currentLink: 'Current link',
      linked: 'Linked', selected: 'Selected', select: 'Select', foundTotal: 'Found: {total}', searchResultPages: 'Search result pages', cancel: 'Cancel', saving: 'Saving…', saveLink: 'Save link', linkClient: 'Link client',
    }
  },
  'zh-Hans': {
    clients: {
      comment: '备注',
      title: '客户', vehicles: '车辆', services: '服务', noRecords: '没有记录',
      sortNameAsc: '姓名：A–Z', sortNameDesc: '姓名：Z–A', sortVehicles: '车辆数最多', sortServices: '服务次数最多', sortRecent: '最近服务',
      allStatuses: '所有状态', active: '活跃', lead: '潜在客户', attention: '需要关注', paused: '已暂停', lost: '已流失',
      allTypes: '所有类型', person: '个人', company: '公司', allResponsible: '所有负责人', noResponsible: '未分配', allTags: '所有标签',
      none: '无', unknownType: '未指定类型', loadError: '无法加载客户。', add: '添加客户', summary: '客户摘要', allClients: '客户总数',
      searchPlaceholder: '姓名、电话或备注', search: '搜索客户', clientType: '客户类型', responsible: '客户负责人', filters: '筛选',
      clientStatus: '客户状态', activeList: '活跃', archive: '归档', sorting: '排序', status: '状态', tag: '标签', segments: '客户分组', reset: '清除筛选',
      listView: '客户列表视图', list: '列表', tiles: '卡片', loading: '正在加载客户…', refreshing: '正在刷新列表…', retry: '重试',
      client: '客户', phone: '电话', lastVisit: '最近到访', action: '操作', notSpecified: '未提供', unassigned: '未分配', never: '尚无记录', open: '打开',
      last: '最近', archiveEmpty: '归档中没有找到客户', notFound: '未找到客户', firstClient: '添加第一位客户', noClients: '暂无客户',
      changeSearch: '请修改搜索内容或清除筛选。', firstClientHint: '保存姓名和电话后，可稍后添加车辆和历史记录。', organizationClients: '组织客户将在此显示。',
      onPage: '每页', pageSize: '每页数量', pageNavigation: '客户分页', createClient: '＋ 添加客户',
      partyPerson: '个人', partyCompany: '公司', vehiclesEmpty: '尚未添加任何内容。', noPhoto: '无照片',
      allVehicles: '所有车辆', allItems: '查看全部', chooseVehicle: '选择要预约的车辆', searchVehicle: '搜索车辆',
      vehicleSearchPlaceholder: '品牌、型号或车牌号…', allClientVehicles: '客户的所有车辆', vehicleNotFound: '未找到结果，请尝试其他搜索内容。',
      foundCount: '找到 {found} / {total} 项', close: '关闭', openVehicle: '打开车辆：{name}', lookupError: '无法查找客户。', linkError: '无法更新客户关联。', initial: '客',
      commentLimit: '备注不能超过 4000 个字符。', readOnly: '此工作区当前为只读状态。', historyTitle: '服务记录', defectsTitle: '故障', createWork: '创建工单', createAppointment: '预约',
      zeroRecords: '0 条记录', rangeOf: '{from}–{to} / {total}', phoneCopied: '电话号码已复制', copyPhoneError: '无法复制电话号码', cancelled: '已取消', handedOver: '已交付', readyForHandover: '已准备交付',
      legalEntity: '法人实体（有限公司、股份公司等）', entrepreneur: '个体经营者', historyLoadError: '无法加载客户工单记录。', cardLoadError: '无法加载客户资料。', commentSaveError: '无法保存备注。', clientSaveError: '无法保存客户。', clientTypeRequired: '请选择客户类型。',
      firstName: '名字', lastName: '姓氏', middleName: '中间名', lengthHint: '{current} / {limit} 个字符', repair: '维修', refueling: '补充燃料', other: '其他', service: '服务', planned: '已计划', inProgress: '进行中', completed: '已完成', unknown: '未知', maintenance: '保养', newClient: '新客户', editClient: '编辑客户', cardTitle: '客户资料', edit: '编辑', newAppointment: '新预约', newWork: '新工单', loadingCard: '正在加载客户资料…', contactsAndDetails: '联系方式和业务信息', maxCharacters: '最多 {count} 个字符', organizationType: '组织类型', businessName: '企业或个体经营者名称', organizationName: '组织名称', invalidPhone: '请检查电话号码。', companyDetails: '业务信息', taxId10Or12: '10 或 12 位数字', digits12: '12 位数字', digits10: '10 位数字', taxIdHelp: '法人税号：10 位；个体经营者税号：12 位', taxIdEntrepreneurHelp: '个体经营者税号：12 位', taxIdLegalHelp: '法人税号：10 位', characters9: '9 个字符', registrationCodeHelp: '登记代码：9 个字符', legalAddress: '法定地址', inn: '俄罗斯税号（INN）', kpp: '俄罗斯税务登记代码（KPP）', clientManagement: '客户管理', tags: '标签', removeTag: '删除标签 {tag}', addTag: '添加标签…', tagHelp: '输入标签后按逗号或 Enter 键分隔。', saveChanges: '保存更改', appointments: '预约', workOrders: '工单', paid: '已付款', debt: '欠款', lastWork: '最近工单', clientInformation: '客户信息', contacts: '联系方式', copyPhone: '复制电话号码', clientSince: '客户注册时间', archivedSince: '归档时间', commentEmpty: '尚未添加备注。', editComment: '编辑备注', addComment: '添加备注', nextAppointment: '下次预约', openAppointment: '打开预约', clientCardSections: '客户资料分区', conversations: '对话', visitHistory: '到访记录', workFilter: '工单筛选', all: '全部', upcomingAndCurrent: '即将进行和进行中', searchWorkHistory: '搜索工单记录', vehicleOrWork: '车辆或工单…', loadingWorkHistory: '正在加载工单记录…', noWorkMatches: '没有符合条件的工单。', emptyWorkHistory: '暂无工单记录。', dateVehicle: '日期 / 车辆', work: '工单', amount: '金额', workHistoryPages: '工单记录分页', saveComment: '保存备注', validation: {
        nameLength: '{label}不能超过 {limit} 个字符。', firstNameRequired: '请输入客户名字。', fullNameLength: '姓名不能超过 {limit} 个字符。',
        companyNameRequired: '请输入公司或个体经营者名称。', companyNameLength: '名称不能超过 {limit} 个字符。', taxCodeLength: '{label}不能超过 {limit} 个字符。',
        innDigits: '俄罗斯税号（INN）必须为 {count} 位数字。', kppFormat: '请检查俄罗斯税务登记代码（KPP）格式。', kppLength: '{label}必须为 9 个字符。', addressLength: '法定地址不能超过 {limit} 个字符。', phoneLength: '电话号码不能超过 {limit} 个字符。'
      },
      linkTitle: '关联客户', currentlyLinked: '当前关联', unlinkConfirm: '此操作将解除关联并取消待发送的消息。是否继续？', unlink: '解除关联',
      keepLink: '保留关联', confirmUnlink: '确认解除关联', linkWarning: '保存关联后，此对话中尚未发送的消息将被取消。',
      findClient: '查找客户', findClientPlaceholder: '客户姓名或电话', minimumSearch: '请输入至少两个字符以查找客户。', searchingClients: '正在搜索客户…',
      lookupEmpty: '未找到符合条件的客户。', searchResults: '客户搜索结果', phoneMissing: '未提供电话', currentLink: '当前关联',
      linked: '已关联', selected: '已选择', select: '选择', foundTotal: '找到 {total} 项', searchResultPages: '搜索结果分页', cancel: '取消', saving: '正在保存…', saveLink: '保存关联', linkClient: '关联客户',
    }
  }
}

for (const [locale, localeMessages] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, localeMessages)
}

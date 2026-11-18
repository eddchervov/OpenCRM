import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: {
    work: {
      materialCurrencyMismatch: "Заказ сохранён в {orderCurrency}, организация использует {workspaceCurrency}. Выдача и возврат материалов недоступны.",
      tabs: { today: 'Сегодня', active: 'Активные', assigned: 'Назначенные', available: 'Свободные', completed: 'Завершённые' },
      empty: {
        todayTitle: 'На сегодня работ нет', todayDescription: 'Назначенные на сегодня и текущие работы появятся здесь.',
        activeTitle: 'Нет активных работ', activeDescription: 'Здесь будут начатые и приостановленные работы.',
        assignedTitle: 'Нет назначенных работ', assignedDescription: 'Здесь появятся работы, назначенные вам.',
        availableTitle: 'Нет свободных работ', availableDescription: 'Доступные для самостоятельного назначения работы появятся здесь.',
        completedTitle: 'Сегодня пока нет завершённых работ', completedDescription: 'Здесь будут работы, которые вы завершили сегодня.'
      },
      summary: 'Сводка моих работ', nextShift: 'Ближайшая смена', activeCount: 'В работе', completedToday: 'Завершено сегодня',
      load: 'Загружаем работы…', retry: 'Повторить', workFallback: 'Техника без названия', notSpecified: 'Не указана',
      noScheduledTime: 'Время не назначено', unnamedLocation: 'Не назначен', plan: 'План', location: 'Пост', price: 'Стоимость работы', quantity: 'Количество',
      consider: 'Что учесть', rework: 'Возврат на исправление', intake: 'Приёмка', completeness: 'Комплектность', damage: 'Повреждения', workComment: 'Комментарий к заказу',
      started: 'Начата', activeTime: 'Активное время', takeWork: 'Взять работу', start: 'Начать', pauseReason: 'Причина паузы', pause: 'Пауза', complete: 'Завершить', resume: 'Продолжить',
      materials: 'Материалы', hideMaterials: 'Скрыть материалы', loadMaterials: 'Загружаем материалы…', issuedMaterials: 'Списано на работу', noIssuedMaterials: 'Материалы ещё не списаны.',
      return: 'Вернуть', returnReason: 'Причина возврата', materialReturnLabel: 'Причина возврата материала', confirm: 'Подтвердить', cancel: 'Отмена',
      continueBeforeIssue: 'Чтобы списать материалы, продолжите работу.', noStock: 'На складе нет доступных материалов.', material: 'Материал', chooseMaterial: 'Выберите материал',
      materialQuantity: 'Количество материала', unit: 'Единица количества', packageEquals: '1 уп = {quantity} {unit}', issue: 'Списать', returned: 'Возврат', issued: 'Списано',
      reports: 'Замечания и допработы', hideReports: 'Скрыть замечания', loadReports: 'Загружаем замечания…', noReports: 'Сообщений пока нет.',
      reportType: 'Тип сообщения', comment: 'Комментарий', defect: 'Дефект', extraWorkRequest: 'Запрос дополнительной работы', reportText: 'Текст сообщения', send: 'Отправить',
      photos: 'Фото', hidePhotos: 'Скрыть фото', loadPhotos: 'Загружаем фото…', noPhotos: 'Фотографий пока нет.', beforeWork: 'До работы', afterWork: 'После работы', workPhoto: 'Фото работы',
      photoStage: 'Этап съёмки', uploadPhotos: 'Загрузить фотографии', before: 'До', after: 'После', photo: 'Фото',
      status: { assigned: 'Назначена', active: 'В работе', paused: 'Пауза', completed: 'Выполнена' },
      reportKind: { defect: 'Дефект', extra: 'Дополнительная работа', time: 'Корректировка времени', comment: 'Комментарий' },
      decision: { approved: 'Согласована: {price}', rejected: 'Отклонена: {reason}', pending: 'Ожидает согласования' },
      units: { g: 'г', kg: 'кг', ml: 'мл', l: 'л', package: 'уп' },
      errors: {
        load: 'Не удалось загрузить работы.', claim: 'Не удалось взять работу.', transition: 'Не удалось изменить состояние работы.',
        loadMaterials: 'Не удалось загрузить материалы.', issueMaterial: 'Не удалось списать материал.', returnMaterial: 'Не удалось вернуть материал.',
        loadReports: 'Не удалось загрузить замечания.', saveReport: 'Не удалось сохранить сообщение.', loadPhotos: 'Не удалось загрузить фотографии.'
      },
      validation: {
        pauseReason: 'Укажите причину паузы.', materialRequired: 'Выберите материал и укажите положительное количество.',
        returnReason: 'Укажите причину возврата материала.', reportText: 'Опишите замечание или дополнительную работу.',
        photoLimit: 'Выберите до 10 изображений общим размером не более 100 МБ.'
      }
    },
    workApi: {
      loadProductionReport: 'Не удалось загрузить производственный отчёт.', loadPhotos: 'Не удалось загрузить фотографии.',
      uploadPhotos: 'Не удалось загрузить фотографии.', loadReports: 'Не удалось загрузить замечания.', saveReport: 'Не удалось сохранить сообщение.',
      saveDecision: 'Не удалось сохранить решение.', adjustTime: 'Не удалось исправить время работы.', loadMaterials: 'Не удалось загрузить материалы.',
      loadIssuedMaterials: 'Не удалось загрузить списанные материалы.', issueMaterial: 'Не удалось списать материал.', returnMaterial: 'Не удалось вернуть материал.',
      loadProgress: 'Не удалось загрузить состояние работ.', completeWork: 'Не удалось завершить работу.', returnWork: 'Не удалось вернуть работу.',
      loadAssigned: 'Не удалось загрузить работы.', loadAvailable: 'Не удалось загрузить свободные работы.', claim: 'Не удалось взять работу.',
      transition: 'Не удалось изменить состояние работы.'
    }
  },
  en: {
    work: {
      materialCurrencyMismatch: "The order uses {orderCurrency}; the organization uses {workspaceCurrency}. Material issues and returns are unavailable.",
      tabs: { today: 'Today', active: 'Active', assigned: 'Assigned', available: 'Available', completed: 'Completed' },
      empty: {
        todayTitle: 'No work for today', todayDescription: 'Work scheduled for today and ongoing work will appear here.',
        activeTitle: 'No active work', activeDescription: 'Work that has been started or paused will appear here.',
        assignedTitle: 'No assigned work', assignedDescription: 'Work assigned to you will appear here.',
        availableTitle: 'No available work', availableDescription: 'Work available for self-assignment will appear here.',
        completedTitle: 'No work completed today yet', completedDescription: 'Work you complete today will appear here.'
      },
      summary: 'My work summary', nextShift: 'Next shift', activeCount: 'In progress', completedToday: 'Completed today',
      load: 'Loading work…', retry: 'Try again', workFallback: 'Unnamed vehicle', notSpecified: 'Not specified',
      noScheduledTime: 'No time scheduled', unnamedLocation: 'Not assigned', plan: 'Schedule', location: 'Bay', price: 'Work price', quantity: 'Quantity',
      consider: 'Notes to consider', rework: 'Returned for rework', intake: 'Intake', completeness: 'Items received', damage: 'Visible damage', workComment: 'Order comment',
      started: 'Started', activeTime: 'Active time', takeWork: 'Claim work', start: 'Start', pauseReason: 'Reason for pause', pause: 'Pause', complete: 'Complete', resume: 'Resume',
      materials: 'Materials', hideMaterials: 'Hide materials', loadMaterials: 'Loading materials…', issuedMaterials: 'Materials issued to this job', noIssuedMaterials: 'No materials have been issued yet.',
      return: 'Return', returnReason: 'Reason for return', materialReturnLabel: 'Material return reason', confirm: 'Confirm', cancel: 'Cancel',
      continueBeforeIssue: 'Resume the work before issuing materials.', noStock: 'No materials are available in stock.', material: 'Material', chooseMaterial: 'Choose a material',
      materialQuantity: 'Material quantity', unit: 'Quantity unit', packageEquals: '1 package = {quantity} {unit}', issue: 'Issue', returned: 'Returned', issued: 'Issued',
      reports: 'Issues and extra work', hideReports: 'Hide issues', loadReports: 'Loading issues…', noReports: 'No messages yet.',
      reportType: 'Message type', comment: 'Comment', defect: 'Defect', extraWorkRequest: 'Request extra work', reportText: 'Message text', send: 'Send',
      photos: 'Photos', hidePhotos: 'Hide photos', loadPhotos: 'Loading photos…', noPhotos: 'No photos yet.', beforeWork: 'Before work', afterWork: 'After work', workPhoto: 'Work photo',
      photoStage: 'Photo stage', uploadPhotos: 'Upload photos', before: 'Before', after: 'After', photo: 'Photo',
      status: { assigned: 'Assigned', active: 'In progress', paused: 'Paused', completed: 'Completed' },
      reportKind: { defect: 'Defect', extra: 'Extra work', time: 'Time adjustment', comment: 'Comment' },
      decision: { approved: 'Approved: {price}', rejected: 'Rejected: {reason}', pending: 'Awaiting approval' },
      units: { g: 'g', kg: 'kg', ml: 'mL', l: 'L', package: 'package' },
      errors: {
        load: 'Could not load work.', claim: 'Could not claim the work.', transition: 'Could not update the work status.',
        loadMaterials: 'Could not load materials.', issueMaterial: 'Could not issue the material.', returnMaterial: 'Could not return the material.',
        loadReports: 'Could not load issues.', saveReport: 'Could not save the message.', loadPhotos: 'Could not load photos.'
      },
      validation: {
        pauseReason: 'Enter a reason for pausing.', materialRequired: 'Choose a material and enter a quantity greater than zero.',
        returnReason: 'Enter a reason for returning the material.', reportText: 'Describe the issue or extra work.',
        photoLimit: 'Choose up to 10 images with a combined size of no more than 100 MB.'
      }
    },
    workApi: {
      loadProductionReport: 'Could not load the production report.', loadPhotos: 'Could not load photos.',
      uploadPhotos: 'Could not upload photos.', loadReports: 'Could not load issues.', saveReport: 'Could not save the message.',
      saveDecision: 'Could not save the decision.', adjustTime: 'Could not adjust work time.', loadMaterials: 'Could not load materials.',
      loadIssuedMaterials: 'Could not load issued materials.', issueMaterial: 'Could not issue the material.', returnMaterial: 'Could not return the material.',
      loadProgress: 'Could not load work progress.', completeWork: 'Could not complete the work.', returnWork: 'Could not return the work.',
      loadAssigned: 'Could not load assigned work.', loadAvailable: 'Could not load available work.', claim: 'Could not claim the work.',
      transition: 'Could not update the work status.'
    }
  },
  'zh-Hans': {
    work: {
      materialCurrencyMismatch: "订单使用 {orderCurrency}，组织使用 {workspaceCurrency}。无法领用或退回材料。",
      tabs: { today: '今日', active: '进行中', assigned: '已分配', available: '可领取', completed: '已完成' },
      empty: {
        todayTitle: '今日暂无工作', todayDescription: '今日安排的工作和正在进行的工作会显示在这里。',
        activeTitle: '暂无进行中的工作', activeDescription: '已开始或已暂停的工作会显示在这里。',
        assignedTitle: '暂无已分配的工作', assignedDescription: '分配给您的工作会显示在这里。',
        availableTitle: '暂无可领取的工作', availableDescription: '可自行领取的工作会显示在这里。',
        completedTitle: '今日尚无已完成的工作', completedDescription: '您今日完成的工作会显示在这里。'
      },
      summary: '我的工作概览', nextShift: '下一班次', activeCount: '进行中', completedToday: '今日已完成',
      load: '正在加载工作…', retry: '重试', workFallback: '未命名的车辆', notSpecified: '未注明',
      noScheduledTime: '未安排时间', unnamedLocation: '未分配', plan: '计划', location: '工位', price: '工作价格', quantity: '数量',
      consider: '注意事项', rework: '退回返工', intake: '接车信息', completeness: '随车物品', damage: '可见损伤', workComment: '订单备注',
      started: '开始时间', activeTime: '有效工时', takeWork: '领取工作', start: '开始', pauseReason: '暂停原因', pause: '暂停', complete: '完成', resume: '继续',
      materials: '材料', hideMaterials: '隐藏材料', loadMaterials: '正在加载材料…', issuedMaterials: '已领用材料', noIssuedMaterials: '尚未领用材料。',
      return: '退回', returnReason: '退回原因', materialReturnLabel: '材料退回原因', confirm: '确认', cancel: '取消',
      continueBeforeIssue: '请先继续工作，然后再领用材料。', noStock: '库存中没有可用材料。', material: '材料', chooseMaterial: '选择材料',
      materialQuantity: '材料数量', unit: '数量单位', packageEquals: '1 包 = {quantity} {unit}', issue: '领用', returned: '已退回', issued: '已领用',
      reports: '问题与额外工作', hideReports: '隐藏问题', loadReports: '正在加载问题…', noReports: '暂无消息。',
      reportType: '消息类型', comment: '备注', defect: '缺陷', extraWorkRequest: '申请额外工作', reportText: '消息内容', send: '发送',
      photos: '照片', hidePhotos: '隐藏照片', loadPhotos: '正在加载照片…', noPhotos: '暂无照片。', beforeWork: '工作前', afterWork: '工作后', workPhoto: '工作照片',
      photoStage: '拍摄阶段', uploadPhotos: '上传照片', before: '之前', after: '之后', photo: '照片',
      status: { assigned: '已分配', active: '进行中', paused: '已暂停', completed: '已完成' },
      reportKind: { defect: '缺陷', extra: '额外工作', time: '工时调整', comment: '备注' },
      decision: { approved: '已批准：{price}', rejected: '已拒绝：{reason}', pending: '等待审批' },
      units: { g: '克', kg: '千克', ml: '毫升', l: '升', package: '包' },
      errors: {
        load: '无法加载工作。', claim: '无法领取工作。', transition: '无法更改工作状态。',
        loadMaterials: '无法加载材料。', issueMaterial: '无法领用材料。', returnMaterial: '无法退回材料。',
        loadReports: '无法加载问题。', saveReport: '无法保存消息。', loadPhotos: '无法加载照片。'
      },
      validation: {
        pauseReason: '请填写暂停原因。', materialRequired: '请选择材料并输入大于零的数量。',
        returnReason: '请填写材料退回原因。', reportText: '请描述问题或额外工作。',
        photoLimit: '最多选择 10 张图片，且总大小不超过 100 MB。'
      }
    },
    workApi: {
      loadProductionReport: '无法加载生产报告。', loadPhotos: '无法加载照片。',
      uploadPhotos: '无法上传照片。', loadReports: '无法加载问题。', saveReport: '无法保存消息。',
      saveDecision: '无法保存审批结果。', adjustTime: '无法调整工作时间。', loadMaterials: '无法加载材料。',
      loadIssuedMaterials: '无法加载已领用材料。', issueMaterial: '无法领用材料。', returnMaterial: '无法退回材料。',
      loadProgress: '无法加载工作进度。', completeWork: '无法完成工作。', returnWork: '无法退回工作。',
      loadAssigned: '无法加载已分配的工作。', loadAvailable: '无法加载可领取的工作。', claim: '无法领取工作。',
      transition: '无法更改工作状态。'
    }
  }
}

for (const [locale, localeMessages] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, localeMessages)
}

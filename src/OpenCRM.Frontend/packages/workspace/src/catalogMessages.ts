import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: { catalogEditor: {
    type: 'Тип справочника', search: 'Поиск по справочнику', searchPlaceholder: 'Название или описание…', noSearchResults: 'По вашему запросу ничего не найдено.',
    activeCount: '{active} активных из {total}', serviceCount: 'Услуг: {count}', order: 'Порядок', active: 'Активен', serviceActive: 'Активна',
    archived: 'В архиве', disabled: 'Отключен', usedInData: 'Используется в данных', details: 'Подробнее', edit: 'Редактировать', closePost: 'Закрыть пост', restorePost: 'Вернуть в работу',
    close: 'Закрыть', status: 'Статус', price: 'Цена', duration: 'Длительность', description: 'Описание', bundleServices: 'Услуги набора', noSelectedServices: 'Услуги не выбраны.',
    usedReadOnly: 'Используется в данных.', usedCannotEdit: 'Используется в данных. Редактирование недоступно.', itemName: 'Название', itemDescription: 'Описание',
    createService: 'Новая услуга', editService: 'Редактирование услуги', createBundle: 'Новый набор', editBundle: 'Редактирование набора', createPost: 'Новый пост / бокс', editPost: 'Редактирование поста / бокса', createMaterial: 'Новый материал', editMaterial: 'Редактирование материала', addItem: 'Добавить элемент', editItem: 'Редактирование элемента', addService: 'Добавить услугу', saveService: 'Сохранить услугу', addBundle: 'Добавить набор', saveBundle: 'Сохранить набор', addPost: 'Добавить пост / бокс', savePost: 'Сохранить пост / бокс', addMaterial: 'Добавить материал', saveMaterial: 'Сохранить материал',
    priceCurrency: 'Цена, {currency}', bundleDuration: 'Длительность набора, мин', serviceDuration: 'Длительность, мин', durationHelp: 'Время занятости поста, включая ожидание и сушку. У набора пустое поле означает сумму длительностей услуг.',
    durationFromServices: 'По услугам', durationUnset: 'Не задана', activeCheckbox: 'Активна', inactiveCheckbox: 'Активен', bundleContents: 'Состав набора',
    serviceSearch: 'Поиск услуг', findService: 'Найти услугу…', service: 'Услуга', serviceDurationColumn: 'Длительность', servicePriceColumn: 'Цена',
    unavailableService: 'Недоступная услуга', noAvailableServices: 'Нет доступных услуг.', noServicesFound: 'По вашему запросу услуги не найдены.',
    selectedServices: 'Выбрано услуг', priceFromServices: 'Стоимость по услугам', unknownPrices: 'Не все цены заданы', bundleDurationSummary: 'Длительность набора', bundleSummary: 'Итог набора',
    durationIncomplete: 'Не задана полностью', durationCustom: 'Задана для набора', durationSum: 'Сумма длительностей услуг', unavailableBundleWarning: 'В составе есть архивные или недоступные услуги. Проверьте состав перед сохранением.',
    archive: 'В архив', deactivate: 'Отключить', delete: 'Удалить', cancel: 'Отмена', add: 'Добавить', saving: 'Сохраняем…', save: 'Сохранить',
    validationServices: 'Выберите от {min} до {max} услуг для набора.',
    groupServices: 'Услуги', groupBundles: 'Наборы услуг', groupPosts: 'Посты / боксы', groupMaterials: 'Материалы', groupWorkTypes: 'Типы работ', groupDocuments: 'Документы', groupPlanningRules: 'Правила планирования', groupOther: 'Справочник',
    emptyServices: 'Услуги еще не настроены.', emptyBundles: 'Наборы ещё не созданы.', emptyPosts: 'Посты еще не настроены.', emptyMaterials: 'Материалы еще не настроены.', emptyWorkTypes: 'Типы работ еще не настроены.', emptyDocuments: 'Документы еще не настроены.', emptyPlanningRules: 'Правила планирования еще не настроены.', emptyOther: 'Элементы справочника не настроены.',
    serviceNameHelp: 'Название услуги, которое будет видно в записи обслуживания и расписании. Например: полировка кузова.', serviceDescriptionHelp: 'Коротко опишите состав работ, длительность или то, что важно администратору.',
    bundleNameHelp: 'Название набора услуг.', bundleDescriptionHelp: 'Состав и назначение набора.',
    postNameHelp: 'Название поста или бокса, где выполняются работы. Например: Бокс 1.', postDescriptionHelp: 'Укажите назначение зоны: мокрая зона, полировка, выдача или другое рабочее место.',
    materialNameHelp: 'Название материала для учета остатков и движений. Например: керамический состав.', materialDescriptionHelp: 'Укажите единицу, зону применения или внутреннее примечание по материалу.',
    genericNameHelp: 'Введите название элемента справочника.', genericDescriptionHelp: 'Добавьте описание элемента справочника.'
  } },
  en: { catalogEditor: {
    type: 'Catalog type', search: 'Search catalog', searchPlaceholder: 'Name or description…', noSearchResults: 'No results match your search.',
    activeCount: '{active} active of {total}', serviceCount: '{count} services', order: 'Order', active: 'Active', serviceActive: 'Active',
    archived: 'Archived', disabled: 'Disabled', usedInData: 'Used in records', details: 'Details', edit: 'Edit', closePost: 'Close bay', restorePost: 'Reopen bay',
    close: 'Close', status: 'Status', price: 'Price', duration: 'Duration', description: 'Description', bundleServices: 'Bundle services', noSelectedServices: 'No services selected.',
    usedReadOnly: 'Used in records.', usedCannotEdit: 'Used in records. Editing is unavailable.', itemName: 'Name', itemDescription: 'Description',
    createService: 'New service', editService: 'Edit service', createBundle: 'New bundle', editBundle: 'Edit bundle', createPost: 'New bay', editPost: 'Edit bay', createMaterial: 'New material', editMaterial: 'Edit material', addItem: 'Add item', editItem: 'Edit item', addService: 'Add service', saveService: 'Save service', addBundle: 'Add bundle', saveBundle: 'Save bundle', addPost: 'Add bay', savePost: 'Save bay', addMaterial: 'Add material', saveMaterial: 'Save material',
    priceCurrency: 'Price, {currency}', bundleDuration: 'Bundle duration, min', serviceDuration: 'Duration, min', durationHelp: 'Time the bay is occupied, including waiting and drying. Leave a bundle duration blank to use the sum of service durations.',
    durationFromServices: 'From services', durationUnset: 'Not set', activeCheckbox: 'Active', inactiveCheckbox: 'Active', bundleContents: 'Bundle contents',
    serviceSearch: 'Search services', findService: 'Find a service…', service: 'Service', serviceDurationColumn: 'Duration', servicePriceColumn: 'Price',
    unavailableService: 'Unavailable service', noAvailableServices: 'No available services.', noServicesFound: 'No services match your search.',
    selectedServices: 'Selected services', priceFromServices: 'Price from services', unknownPrices: 'Some prices are not set', bundleDurationSummary: 'Bundle duration', bundleSummary: 'Bundle summary',
    durationIncomplete: 'Not fully set', durationCustom: 'Set for this bundle', durationSum: 'Sum of service durations', unavailableBundleWarning: 'This bundle contains archived or unavailable services. Review it before saving.',
    archive: 'Archive', deactivate: 'Disable', delete: 'Delete', cancel: 'Cancel', add: 'Add', saving: 'Saving…', save: 'Save',
    validationServices: 'Select between {min} and {max} services for the bundle.',
    groupServices: 'Services', groupBundles: 'Service bundles', groupPosts: 'Bays', groupMaterials: 'Materials', groupWorkTypes: 'Work types', groupDocuments: 'Documents', groupPlanningRules: 'Planning rules', groupOther: 'Catalog',
    emptyServices: 'No services have been set up yet.', emptyBundles: 'No service bundles have been created yet.', emptyPosts: 'No bays have been set up yet.', emptyMaterials: 'No materials have been set up yet.', emptyWorkTypes: 'No work types have been set up yet.', emptyDocuments: 'No documents have been set up yet.', emptyPlanningRules: 'No planning rules have been set up yet.', emptyOther: 'No catalog items have been set up.',
    serviceNameHelp: 'The service name shown on maintenance records and schedules. For example: body polishing.', serviceDescriptionHelp: 'Briefly describe the work, duration, or details that matter to the administrator.',
    bundleNameHelp: 'Name of the service bundle.', bundleDescriptionHelp: 'Bundle contents and purpose.',
    postNameHelp: 'Name of the bay where the work is done. For example: Bay 1.', postDescriptionHelp: 'Describe the area, such as a wet bay, polishing area, handover area, or another workspace.',
    materialNameHelp: 'Name of the material used to track stock and movements. For example: ceramic coating.', materialDescriptionHelp: 'Specify the unit, use area, or an internal note about the material.',
    genericNameHelp: 'Enter a catalog item name.', genericDescriptionHelp: 'Add a description for the catalog item.'
  } },
  'zh-Hans': { catalogEditor: {
    type: '目录类型', search: '搜索目录', searchPlaceholder: '名称或描述…', noSearchResults: '没有符合搜索条件的结果。',
    activeCount: '已启用 {active} 项，共 {total} 项', serviceCount: '{count} 项服务', order: '排序', active: '已启用', serviceActive: '已启用',
    archived: '已归档', disabled: '已停用', usedInData: '已用于记录', details: '详情', edit: '编辑', closePost: '关闭工位', restorePost: '重新开放工位',
    close: '关闭', status: '状态', price: '价格', duration: '时长', description: '描述', bundleServices: '组合服务', noSelectedServices: '未选择服务。',
    usedReadOnly: '已用于记录。', usedCannotEdit: '已用于记录，无法编辑。', itemName: '名称', itemDescription: '描述',
    createService: '新建服务', editService: '编辑服务', createBundle: '新建组合', editBundle: '编辑组合', createPost: '新建工位', editPost: '编辑工位', createMaterial: '新建材料', editMaterial: '编辑材料', addItem: '添加项目', editItem: '编辑项目', addService: '添加服务', saveService: '保存服务', addBundle: '添加组合', saveBundle: '保存组合', addPost: '添加工位', savePost: '保存工位', addMaterial: '添加材料', saveMaterial: '保存材料',
    priceCurrency: '价格（{currency}）', bundleDuration: '组合时长（分钟）', serviceDuration: '时长（分钟）', durationHelp: '工位占用时间，包括等待和干燥时间。组合的时长留空时，将使用各项服务时长之和。',
    durationFromServices: '按服务计算', durationUnset: '未设置', activeCheckbox: '已启用', inactiveCheckbox: '已启用', bundleContents: '组合内容',
    serviceSearch: '搜索服务', findService: '查找服务…', service: '服务', serviceDurationColumn: '时长', servicePriceColumn: '价格',
    unavailableService: '服务不可用', noAvailableServices: '没有可用服务。', noServicesFound: '没有符合搜索条件的服务。',
    selectedServices: '已选服务', priceFromServices: '服务价格合计', unknownPrices: '部分价格未设置', bundleDurationSummary: '组合时长', bundleSummary: '组合汇总',
    durationIncomplete: '尚未完整设置', durationCustom: '为此组合设置', durationSum: '服务时长合计', unavailableBundleWarning: '组合中包含已归档或不可用的服务。保存前请检查组合内容。',
    archive: '归档', deactivate: '停用', delete: '删除', cancel: '取消', add: '添加', saving: '正在保存…', save: '保存',
    validationServices: '请为组合选择 {min} 至 {max} 项服务。',
    groupServices: '服务', groupBundles: '服务组合', groupPosts: '工位', groupMaterials: '材料', groupWorkTypes: '工作类型', groupDocuments: '文档', groupPlanningRules: '计划规则', groupOther: '目录',
    emptyServices: '尚未设置服务。', emptyBundles: '尚未创建服务组合。', emptyPosts: '尚未设置工位。', emptyMaterials: '尚未设置材料。', emptyWorkTypes: '尚未设置工作类型。', emptyDocuments: '尚未设置文档。', emptyPlanningRules: '尚未设置计划规则。', emptyOther: '尚未设置目录项目。',
    serviceNameHelp: '此服务名称会显示在维修记录和日程中。例如：车身抛光。', serviceDescriptionHelp: '简要说明工作内容、时长或管理员需要了解的信息。',
    bundleNameHelp: '服务组合名称。', bundleDescriptionHelp: '组合内容和用途。',
    postNameHelp: '执行工作的工位名称。例如：1 号工位。', postDescriptionHelp: '说明区域用途，例如湿作业区、抛光区、交车区或其他工作区域。',
    materialNameHelp: '用于跟踪库存和变动的材料名称。例如：陶瓷涂层剂。', materialDescriptionHelp: '填写计量单位、使用区域或材料内部备注。',
    genericNameHelp: '输入目录项目名称。', genericDescriptionHelp: '添加目录项目描述。'
  } }
}

for (const [locale, localeMessages] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, localeMessages)
}

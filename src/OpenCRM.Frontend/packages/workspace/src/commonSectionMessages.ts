import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: {
    commonSection: {
      sections: {
        organization: 'Организация', statistics: 'Статистика', billing: 'Тарифы', wiki: 'Вики', shifts: 'Смены',
        employees: 'Команда', 'my-work': 'Мои работы', members: 'Роли и права', catalog: 'Справочники', settings: 'Настройки организации'
      },
      scenarios: { autoservice: 'Автосервис', detailing: 'Детейлинг-центр', fleet: 'Автопарк' },
      workspace: 'Рабочее пространство', promoHistory: 'История промокодов', employeeCount: '{title} ({count})',
      settings: { detailing: 'Настройки детейлинг-центра', organization: 'Настройки организации' },
      loading: 'Загружаем раздел…', loadError: 'Не удалось загрузить раздел.', error: 'Ошибка', done: 'Готово', confirm: 'Подтвердить',
      promoRefreshError: 'Промокод применён, но тарифы не удалось обновить. Обновите страницу.',
      payment: {
        received: 'Оплата получена, активируем подписку.',
        manualReview: 'Оплата требует проверки. Обратитесь в поддержку и не оплачивайте повторно.',
        preparing: 'Готовим оплату.', completeError: 'Не удалось завершить оплату.',
        refreshCatalogError: 'Изменения сохранены, но список справочника не удалось обновить. Обновите страницу, чтобы увидеть актуальные данные.',
        ready: 'Готово.', actionError: 'Действие не выполнено.', canceled: 'Проверка оплаты отменена.',
        chooseBuyer: 'Выберите, кто покупает подписку.', enterBuyerName: 'Укажите наименование организации или ФИО ИП.',
        invalidInn: 'ИНН организации или ИП должен содержать 10 или 12 цифр.', buyerNameTooLong: 'Наименование или ФИО должно содержать не более 200 символов.',
        title: 'Данные покупателя', description: 'Укажите, кто оплачивает подписку. Эти данные сохранятся вместе с платежом, чтобы оформить чек НПД с правильными данными покупателя.',
        buyer: 'Покупатель', individual: 'Физическое лицо', business: 'Организация или ИП',
        businessName: 'Наименование организации или ФИО ИП', inn: 'Российский ИНН (INN)',
        innHelp: 'Укажите 10 цифр для организации или 12 цифр для индивидуального предпринимателя.',
        close: 'Закрыть', cancel: 'Отмена', prepare: 'Готовим оплату…', checkStatus: 'Проверить статус', continue: 'Продолжить к оплате'
      },
      catalog: {
        modeNavigation: 'Разделы справочников', workspaceMode: 'Услуги и ресурсы', templatesMode: 'Шаблоны сообщений',
        templatesPro: 'Шаблоны сообщений доступны в PRO', nameRequired: 'Укажите название элемента справочника.',
        emptyName: 'Название элемента справочника не должно быть пустым.', refreshed: 'Справочник обновлён.',
        kind: {
          service: {
            title: 'Услуги', empty: 'Услуги ещё не настроены.',
            nameHelp: 'Название услуги, которое будет видно в записи обслуживания и расписании. Например: полировка кузова.',
            descriptionHelp: 'Коротко опишите состав работ, длительность или то, что важно администратору.'
          },
          service_bundle: {
            title: 'Наборы услуг', empty: 'Наборы ещё не созданы.', nameHelp: 'Название набора услуг.', descriptionHelp: 'Состав и назначение набора.'
          },
          work_post: {
            title: 'Посты / боксы', empty: 'Посты ещё не настроены.',
            nameHelp: 'Название поста или бокса, где выполняются работы. Например: Бокс 1.',
            descriptionHelp: 'Укажите назначение зоны: мокрая зона, полировка, выдача или другое рабочее место.'
          },
          material: {
            title: 'Материалы', empty: 'Материалы ещё не настроены.',
            nameHelp: 'Название материала для учёта остатков и движений. Например: керамический состав.',
            descriptionHelp: 'Укажите единицу, зону применения или внутреннее примечание по материалу.'
          }
        },
        archiveServiceTitle: 'Перенести услугу в архив', disableTitle: 'Отключить элемент', deleteTitle: 'Удалить элемент',
        archiveServiceMessage: 'Услуга останется в существующих записях, но будет недоступна для новых. Её можно будет восстановить из архива.',
        usedMessage: 'Элемент уже используется в записях или складских движениях. Удалить его нельзя: он будет скрыт из новых форм, а история останется корректной.',
        systemMessage: 'Системный элемент будет отключён в этом справочнике.', deleteMessage: 'Элемент будет удалён из справочника.',
        archiveAction: 'В архив', disableAction: 'Отключить', deleteAction: 'Удалить'
      },
      postActivity: {
        closeTitle: 'Закрыть пост', reopenTitle: 'Вернуть пост в работу',
        closeQuestion: 'Закрыть пост «{name}»?', reopenQuestion: 'Вернуть пост «{name}» в работу?',
        closedMessage: 'Пост станет недоступен для новых назначений. История и существующие записи сохранятся. Перенос записей выполняется вручную. Вернуть пост в работу можно здесь же.',
        reopenedMessage: 'Пост снова станет доступен для новых назначений.', loading: 'Проверяем назначения…',
        noViewPermission: 'У вас нет права просмотра работ. Проверьте текущие и будущие назначения с ответственным сотрудником: при закрытии поста они сохранятся без автоматического переноса.',
        unfinished: 'Незавершённых записей на посту: {count}.', shownNearest: 'Показаны ближайшие {shown}. Остальные записи доступны в календаре.',
        vehicleFallback: 'Автомобиль', save: 'Сохраняем…', reopenAction: 'Вернуть в работу', closeAction: 'Закрыть пост',
        checkAssignmentsError: 'Не удалось проверить назначения.', closed: 'Пост закрыт.', reopened: 'Пост возвращён в работу.'
      },
      archiveOrganization: {
        title: 'Архивировать организацию', message: 'Организация исчезнет из рабочих разделов. Её можно будет восстановить из списка организаций в течение срока хранения архива.', action: 'Архивировать'
      },
      wikiLoadError: 'Не удалось загрузить статью.', catalogRefreshTitle: 'Изменения сохранены', actionErrorTitle: 'Не удалось выполнить действие'
    }
  },
  en: {
    commonSection: {
      sections: {
        organization: 'Organization', statistics: 'Statistics', billing: 'Plans', wiki: 'Wiki', shifts: 'Shifts',
        employees: 'Team', 'my-work': 'My work', members: 'Roles and permissions', catalog: 'Catalogs', settings: 'Organization settings'
      },
      scenarios: { autoservice: 'Auto service', detailing: 'Detailing center', fleet: 'Fleet' },
      workspace: 'Workspace', promoHistory: 'Promo code history', employeeCount: '{title} ({count})',
      settings: { detailing: 'Detailing center settings', organization: 'Organization settings' },
      loading: 'Loading section…', loadError: 'Could not load the section.', error: 'Error', done: 'Done', confirm: 'Confirm',
      promoRefreshError: 'The promo code was applied, but plans could not be refreshed. Reload the page.',
      payment: {
        received: 'Payment received. Activating the subscription.',
        manualReview: 'The payment needs review. Contact support and do not pay again.',
        preparing: 'Preparing payment.', completeError: 'Could not complete the payment.',
        refreshCatalogError: 'Changes were saved, but the catalog could not be refreshed. Reload the page to see the latest data.',
        ready: 'Done.', actionError: 'The action could not be completed.', canceled: 'Payment verification was canceled.',
        chooseBuyer: 'Select who is purchasing the subscription.', enterBuyerName: 'Enter the organization name or sole proprietor’s full name.',
        invalidInn: 'A Russian organization or sole proprietor INN must contain 10 or 12 digits.', buyerNameTooLong: 'The name must be no longer than 200 characters.',
        title: 'Buyer details', description: 'Specify who is paying for the subscription. These details are saved with the payment so the self-employment receipt uses the correct buyer information.',
        buyer: 'Buyer', individual: 'Individual', business: 'Organization or sole proprietor',
        businessName: 'Organization name or sole proprietor’s full name', inn: 'Russian tax ID (INN)',
        innHelp: 'Enter 10 digits for an organization or 12 digits for a sole proprietor.',
        close: 'Close', cancel: 'Cancel', prepare: 'Preparing payment…', checkStatus: 'Check status', continue: 'Continue to payment'
      },
      catalog: {
        modeNavigation: 'Catalog sections', workspaceMode: 'Services and resources', templatesMode: 'Message templates',
        templatesPro: 'Message templates are available in PRO', nameRequired: 'Enter a catalog item name.',
        emptyName: 'The catalog item name cannot be empty.', refreshed: 'Catalog updated.',
        kind: {
          service: {
            title: 'Services', empty: 'No services have been set up yet.',
            nameHelp: 'The service name shown in maintenance records and the schedule. For example, exterior polishing.',
            descriptionHelp: 'Briefly describe the work, duration, or details the administrator should know.'
          },
          service_bundle: {
            title: 'Service bundles', empty: 'No bundles have been created yet.', nameHelp: 'Service bundle name.', descriptionHelp: 'Bundle contents and purpose.'
          },
          work_post: {
            title: 'Work bays', empty: 'No work bays have been set up yet.',
            nameHelp: 'Name of the bay where work is done. For example, Bay 1.',
            descriptionHelp: 'Describe the area: wash bay, polishing, pickup, or another work area.'
          },
          material: {
            title: 'Materials', empty: 'No materials have been set up yet.',
            nameHelp: 'Material name used to track stock and movements. For example, ceramic coating.',
            descriptionHelp: 'Add a unit, area of use, or internal note about the material.'
          }
        },
        archiveServiceTitle: 'Archive service', disableTitle: 'Disable item', deleteTitle: 'Delete item',
        archiveServiceMessage: 'The service will remain on existing records but will no longer be available for new ones. You can restore it from the archive.',
        usedMessage: 'This item is used in records or stock movements. It cannot be deleted; it will be hidden from new forms while history remains intact.',
        systemMessage: 'The system item will be disabled in this catalog.', deleteMessage: 'The item will be deleted from the catalog.',
        archiveAction: 'Archive', disableAction: 'Disable', deleteAction: 'Delete'
      },
      postActivity: {
        closeTitle: 'Close work bay', reopenTitle: 'Reopen work bay',
        closeQuestion: 'Close work bay “{name}”?', reopenQuestion: 'Reopen work bay “{name}”?',
        closedMessage: 'The bay will no longer be available for new assignments. History and existing records will remain. Records must be moved manually. You can reopen the bay here.',
        reopenedMessage: 'The bay will be available for new assignments again.', loading: 'Checking assignments…',
        noViewPermission: 'You cannot view work records. Check current and future assignments with the responsible employee. Closing the bay will leave them in place without moving them automatically.',
        unfinished: 'Unfinished records in this bay: {count}.', shownNearest: 'Showing the next {shown}. Other records are available in the calendar.',
        vehicleFallback: 'Vehicle', save: 'Saving…', reopenAction: 'Reopen bay', closeAction: 'Close bay',
        checkAssignmentsError: 'Could not check assignments.', closed: 'Work bay closed.', reopened: 'Work bay reopened.'
      },
      archiveOrganization: {
        title: 'Archive organization', message: 'The organization will disappear from work sections. You can restore it from the organization list during the archive retention period.', action: 'Archive'
      },
      wikiLoadError: 'Could not load the article.', catalogRefreshTitle: 'Changes saved', actionErrorTitle: 'Could not complete the action'
    }
  },
  'zh-Hans': {
    commonSection: {
      sections: {
        organization: '组织', statistics: '统计', billing: '套餐', wiki: '知识库', shifts: '班次',
        employees: '团队', 'my-work': '我的工作', members: '角色与权限', catalog: '目录', settings: '组织设置'
      },
      scenarios: { autoservice: '汽车维修', detailing: '汽车美容', fleet: '车队管理' },
      workspace: '工作空间', promoHistory: '优惠码历史', employeeCount: '{title}（{count}）',
      settings: { detailing: '汽车美容中心设置', organization: '组织设置' },
      loading: '正在加载栏目…', loadError: '无法加载栏目。', error: '错误', done: '完成', confirm: '确认',
      promoRefreshError: '优惠码已应用，但无法刷新套餐。请重新加载页面。',
      payment: {
        received: '已收到付款，正在激活订阅。',
        manualReview: '付款需要审核。请联系支持人员，请勿重复付款。',
        preparing: '正在准备付款。', completeError: '无法完成付款。',
        refreshCatalogError: '已保存更改，但无法刷新目录。请重新加载页面以查看最新数据。',
        ready: '已完成。', actionError: '无法完成此操作。', canceled: '付款验证已取消。',
        chooseBuyer: '请选择订阅购买方。', enterBuyerName: '请输入组织名称或个体经营者姓名。',
        invalidInn: '俄罗斯组织或个体经营者的 INN 必须为 10 位或 12 位数字。', buyerNameTooLong: '名称最多可输入 200 个字符。',
        title: '购买方信息', description: '请指定订阅付款方。这些信息将与付款一同保存，以便使用正确的购买方信息开具自雇收入收据。',
        buyer: '购买方', individual: '个人', business: '组织或个体经营者',
        businessName: '组织名称或个体经营者姓名', inn: '俄罗斯税号（INN）',
        innHelp: '组织请输入 10 位数字，个体经营者请输入 12 位数字。',
        close: '关闭', cancel: '取消', prepare: '正在准备付款…', checkStatus: '检查状态', continue: '继续付款'
      },
      catalog: {
        modeNavigation: '目录栏目', workspaceMode: '服务与资源', templatesMode: '消息模板',
        templatesPro: '消息模板仅在 PRO 版中提供', nameRequired: '请输入目录项目名称。',
        emptyName: '目录项目名称不能为空。', refreshed: '目录已更新。',
        kind: {
          service: {
            title: '服务', empty: '尚未设置服务。',
            nameHelp: '显示在维修记录和日程中的服务名称。例如：车身抛光。',
            descriptionHelp: '简要说明工作内容、时长或管理员需要了解的信息。'
          },
          service_bundle: {
            title: '服务套餐', empty: '尚未创建套餐。', nameHelp: '服务套餐名称。', descriptionHelp: '套餐内容与用途。'
          },
          work_post: {
            title: '工位 / 车间', empty: '尚未设置工位。',
            nameHelp: '执行工作的工位名称。例如：1 号工位。',
            descriptionHelp: '说明区域用途：清洗区、抛光区、交车区或其他工作区域。'
          },
          material: {
            title: '材料', empty: '尚未设置材料。',
            nameHelp: '用于跟踪库存和变动的材料名称。例如：陶瓷涂层。',
            descriptionHelp: '填写单位、用途区域或材料内部备注。'
          }
        },
        archiveServiceTitle: '归档服务', disableTitle: '停用项目', deleteTitle: '删除项目',
        archiveServiceMessage: '此服务仍会保留在现有记录中，但不能用于新记录。您可以从归档中恢复。',
        usedMessage: '此项目已用于记录或库存变动，无法删除。它会从新表单中隐藏，历史记录将保留。',
        systemMessage: '系统项目将在此目录中停用。', deleteMessage: '此项目将从目录中删除。',
        archiveAction: '归档', disableAction: '停用', deleteAction: '删除'
      },
      postActivity: {
        closeTitle: '关闭工位', reopenTitle: '重新开放工位',
        closeQuestion: '关闭“{name}”工位？', reopenQuestion: '重新开放“{name}”工位？',
        closedMessage: '此工位将不再接受新任务。历史记录和现有记录会保留，记录需手动转移。您可以在此重新开放工位。',
        reopenedMessage: '此工位将重新接受新任务。', loading: '正在检查任务…',
        noViewPermission: '您没有查看工作记录的权限。请与负责员工确认当前及未来任务。关闭工位后，这些记录会保留，不会自动转移。',
        unfinished: '此工位有 {count} 条未完成记录。', shownNearest: '显示接下来的 {shown} 条。其他记录可在日历中查看。',
        vehicleFallback: '车辆', save: '正在保存…', reopenAction: '重新开放工位', closeAction: '关闭工位',
        checkAssignmentsError: '无法检查任务。', closed: '工位已关闭。', reopened: '工位已重新开放。'
      },
      archiveOrganization: {
        title: '归档组织', message: '此组织将从工作栏目中移除。在归档保留期内，您可以从组织列表中恢复。', action: '归档'
      },
      wikiLoadError: '无法加载文章。', catalogRefreshTitle: '更改已保存', actionErrorTitle: '无法完成此操作'
    }
  }
}

for (const [locale, value] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, value)
}

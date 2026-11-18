import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: {
    adminUsers: {
      dashboard: {
        title: 'Дашборд', refresh: 'Обновить', refreshing: 'Обновляем…', updatedAt: 'Обновлено: {time}', refreshInterval: 'Автообновление',
        refreshMinute: '1 минута', refreshSeconds: '{seconds} секунд', loading: 'Загрузка данных', newSection: 'Что нового', today: 'Сегодня',
        sevenDays: '7 дней', month: 'Месяц', newUsers: 'Новые пользователи', newOrganizations: 'Новые организации', payments: 'Платежи',
        email: 'Email', support: 'Обращения', catalogSuggestions: 'Предложения в каталог',
        todayByHour: 'Сегодня по часам', forSevenDays: 'За 7 дней', forMonth: 'За месяц',
        refreshError: 'Не удалось обновить данные. Показаны ранее полученные значения.', loadError: 'Не удалось загрузить дашборд.',
        users: 'Пользователи', organizations: 'Организации', total: 'Всего', newToday: 'Новых сегодня', newSevenDays: 'Новых за 7 дней',
        active: 'Активных', members: 'Участников', activeSubscriptions: 'Активных подписок', equipmentAndMaintenance: 'Техника и обслуживание',
        vehicles: 'Техники', maintenances: 'Обслуживаний', planned: 'Запланировано', documents: 'Документов', openDefects: 'Открытых дефектов',
        finance: 'Финансы', amountThirtyDays: 'Сумма за 30 дней', paymentsThirtyDays: 'Платежей за 30 дней', problemPayments: 'Проблемных платежей',
        communications: 'Коммуникации', emailToday: 'Email сегодня', emailSevenDays: 'Email за 7 дней', sentSevenDays: 'Отправлено за 7 дней',
        errorsSevenDays: 'Ошибок за 7 дней', pendingSend: 'Ожидают отправки', attention: 'Требует внимания', newRequests: 'Новые обращения',
        suggestions: 'Предложения в каталог', pendingInvitations: 'Ожидающие приглашения', overdueTasks: 'Просроченные задачи', lowStock: 'Низкие остатки'
      },
      activityChart: { registrationsAndWork: 'Регистрации и работа', registrations: 'Регистрации', maintenances: 'Обслуживания', date: 'Дата', aria: 'График регистраций и рабочих процессов' },
      paymentsChart: { payments: 'Платежи', date: 'Дата', aria: 'График платежей' },
      users: {
        title: 'Пользователи', searchPlaceholder: 'Email или телефон…', searchLabel: 'Поиск по email или телефону',
        includeDeleted: 'Показывать удалённых', find: 'Найти', reset: 'Сбросить', found: 'Найдено: {count}',
        loading: 'Загружаем пользователей…', loadError: 'Не удалось загрузить пользователей.', empty: 'Пользователи не найдены.',
        sort: 'Сортировка', user: 'Пользователь', plan: 'Тариф', registration: 'Регистрация', vehicles: 'Техника', maintenance: 'ТО', organizations: 'Организации',
        sortPlan: 'Личный тариф · прежняя модель', openCard: 'Открыть карточку', phoneNotProvided: 'Телефон не указан', emailUnconfirmed: 'Email не подтверждён',
        deleted: 'Удалён', notAssigned: 'Не назначен', until: 'до {date}', pagination: 'Страницы пользователей', previous: 'Назад', next: 'Вперёд',
        range: 'Показаны {first}–{last} из {total}', pageSize: 'На странице'
      },
      overview: {
        tabOverview: 'Обзор', tabVehicles: 'Техника и ТО', tabOrganizationsPlans: 'Организации и подписки', tabOrganizations: 'Организации',
        tabPayments: 'Платежи', tabSupport: 'Обращения', tabFeatures: 'Возможности',
        loadError: 'Не удалось загрузить карточку пользователя.', impersonateError: 'Не удалось войти от имени пользователя.',
        plansLoadError: 'Не удалось загрузить доступные тарифы.', contentsLoadError: 'Не удалось загрузить содержимое организации.',
        operationError: 'Не удалось выполнить операцию.', organizationSubscriptionError: 'Не удалось изменить подписку организации.',
        allowed: 'Разрешено', denied: 'Запрещено', noValue: 'Без значения', backToUsers: '← Пользователи', title: 'Карточка 360',
        manage: 'Управление', impersonate: 'Войти как пользователь', refresh: 'Обновить', loading: 'Загружаем карточку пользователя…',
        noEmail: 'Email не указан', deleted: 'Удалён', active: 'Активен', vehicles: 'Техника', maintenances: 'Обслуживания', organizations: 'Организации',
        paymentAmount: 'Сумма платежей', activeSubscriptions: 'Активные подписки', openRequests: 'Открытые обращения',
        basicInfo: 'Основная информация', emailConfirmed: 'Email подтверждён', yes: 'Да', no: 'Нет', phone: 'Телефон', phoneConfirmed: 'Телефон подтверждён',
        registration: 'Регистрация', password: 'Пароль', passwordSet: 'Установлен', passwordNotSet: 'Не установлен',
        securityConsent: 'Безопасность и согласия', lockoutAllowed: 'Блокировка разрешена', lockedUntil: 'Заблокирован до', failedLogins: 'Неудачных входов',
        dataProcessing: 'Обработка данных', accepted: 'Принято', notAccepted: 'Не принято', consentDate: 'Дата согласия',
        testPlans: 'Тестовые тарифы', allowedPlural: 'Разрешены', notAllowed: 'Не разрешены', history: 'История', noEvents: 'Событий нет.',
        equipment: 'Техника', archived: 'Архив', equipmentActive: 'Активна', plateVin: 'Номер / VIN', organization: 'Организация', personal: 'Личная',
        openDefects: 'Открытые дефекты', noVehicles: 'Техники нет.', recentMaintenances: 'Последние обслуживания', date: 'Дата', type: 'Тип', status: 'Статус',
        cost: 'Стоимость', noMaintenances: 'Обслуживаний нет.', organizationsAndPlans: 'Организации и тарифы', freeProInfo: 'Free и PRO относятся к конкретной организации. Личная техника доступна каждому пользователю. Подарочный PRO подключается промокодом; автоматического демо нет.',
        noName: 'Без названия', demoActive: 'Демо активно', proActive: 'PRO активен', planActive: 'Тариф активен', freeForever: 'Free · бессрочно',
        freeProFallback: 'После завершения PRO владелец продолжит базовую работу на Free. История сохранится; новые платные действия будут закрыты.',
        otherScenarioFallback: 'После завершения подписки действуют прежние условия этого направления.',
        basePlan: 'Базовый тариф', inactive: 'Неактивна', deletedOrganization: 'Удалена', archivedStatus: 'В архиве', activeOrganization: 'Активна',
        participants: 'Участники', clients: 'Клиенты', joined: 'Вступил', currentPlan: 'Текущий тариф', validUntil: 'Действует до',
        hideContents: 'Скрыть состав', showContents: 'Показать состав', grantDemo: 'Выдать демо', grantPro: 'Выдать PRO без оплаты', grantPlan: 'Выдать тариф без оплаты',
        extend: 'Продлить', revokeDemo: 'Отозвать демо', stopAll: 'Остановить все действующие', organizationSubscriptions: 'Подписки организации',
        expired: 'Истекла', loadingContents: 'Загрузка состава…', noParticipants: 'Нет участников.', noEquipment: 'Нет техники.', noClients: 'Нет клиентов.',
        clientVehicles: '{name} · техники: {count}', lastTwenty: 'Показаны 20 последних.', noOrganizationMaintenances: 'Обслуживаний нет.', noOrganizations: 'Организаций нет.',
        invitations: 'Приглашения', role: 'Роль', invitedBy: 'Пригласил', created: 'Создано', noInvitations: 'Приглашений нет.',
        legacySubscriptions: 'Личные подписки · прежняя модель', start: 'Начало', end: 'Окончание', daysLeft: 'Осталось дней', autoRenewal: 'Автопродление',
        noSubscriptions: 'Подписок нет.', subscriptionHistory: 'История пользовательских подписок', comment: 'Комментарий', noHistory: 'Истории нет.',
        payment: 'Платежи', amount: 'Сумма', method: 'Способ', transaction: 'Транзакция', noPayments: 'Платежей нет.', support: 'Обращения',
        noSubject: 'Без темы', noSupport: 'Обращений нет.', individualFeatures: 'Индивидуальные возможности', value: 'Значение', source: 'Источник',
        manualGrant: 'Ручная выдача', noFeatures: 'Индивидуальных возможностей нет.',
        impersonationTitle: 'Войти как пользователь', signingIn: 'Входим…', continue: 'Продолжить', cancel: 'Отмена',
        orgGrantPlanTitle: 'Выдать тариф организации без оплаты', orgGrantDemoTitle: 'Выдать демо организации', orgExtendTitle: 'Продлить тариф организации',
        orgRevokeTitle: 'Отозвать демо организации', orgStopTitle: 'Остановить тариф организации',
        stopOrgWarning: 'Все действующие подписки организации будут остановлены сразу. Неиспользованный оплаченный срок не восстанавливается автоматически.',
        revokeOrgWarning: 'Демо организации будет отозвано.', plan: 'Тариф', selectPlan: 'Выберите тариф', termDays: 'Срок в днях',
        planStartsAndEnds: 'Тариф начнётся сейчас и закончится {date}. Платёж не создаётся.', choosePlanAndTerm: 'после выбора тарифа и срока',
        extendByDays: 'Продлить на дней', newEndDate: 'Новый срок до {date}. Платёж не создаётся.', specifyTerm: 'укажите срок', reason: 'Причина',
        saving: 'Сохраняем…', confirm: 'Подтвердить', managingUser: 'Управление пользователем',
        grantPlanQuestion: 'Выдать тариф без оплаты?', grantDemoQuestion: 'Выдать пользователю демо-тариф?', revokeDemoQuestion: 'Отозвать демо-тариф?',
        extendPersonalQuestion: 'Продлить личную подписку?', stopPersonalQuestion: 'Остановить личную подписку?',
        demoDurationInfo: 'Срок демо определяется настройками тарифа.',
        stopPersonalWarning: 'Все действующие личные подписки будут остановлены. Неиспользованный оплаченный срок не восстанавливается автоматически.',
        disableTestPlans: 'Отключить тестовые тарифы', allowTestPlans: 'Разрешить тестовые тарифы',
        legacyPersonalDemo: 'Выдать личное демо · прежняя модель', grantPersonalPlan: 'Выдать личный тариф без оплаты',
        revokePersonalDemo: 'Отозвать демо-тариф', extendPersonal: 'Продлить личную подписку', finishPersonal: 'Завершить личную подписку', close: 'Закрыть'
      }
    }
  },
  en: {
    adminUsers: {
      dashboard: {
        title: 'Dashboard', refresh: 'Refresh', refreshing: 'Refreshing…', updatedAt: 'Updated: {time}', refreshInterval: 'Auto refresh',
        refreshMinute: '1 minute', refreshSeconds: '{seconds} seconds', loading: 'Loading data', newSection: 'What’s new', today: 'Today',
        sevenDays: '7 days', month: 'Month', newUsers: 'New users', newOrganizations: 'New organizations', payments: 'Payments',
        email: 'Email', support: 'Support requests', catalogSuggestions: 'Catalog suggestions',
        todayByHour: 'Today by hour', forSevenDays: 'Last 7 days', forMonth: 'Last month',
        refreshError: 'Could not refresh data. Previously loaded values are shown.', loadError: 'Could not load the dashboard.',
        users: 'Users', organizations: 'Organizations', total: 'Total', newToday: 'New today', newSevenDays: 'New in 7 days',
        active: 'Active', members: 'Members', activeSubscriptions: 'Active subscriptions', equipmentAndMaintenance: 'Vehicles and maintenance',
        vehicles: 'Vehicles', maintenances: 'Maintenance records', planned: 'Planned', documents: 'Documents', openDefects: 'Open defects',
        finance: 'Finance', amountThirtyDays: 'Amount in 30 days', paymentsThirtyDays: 'Payments in 30 days', problemPayments: 'Problem payments',
        communications: 'Communications', emailToday: 'Email today', emailSevenDays: 'Email in 7 days', sentSevenDays: 'Sent in 7 days',
        errorsSevenDays: 'Errors in 7 days', pendingSend: 'Pending delivery', attention: 'Needs attention', newRequests: 'New requests',
        suggestions: 'Catalog suggestions', pendingInvitations: 'Pending invitations', overdueTasks: 'Overdue tasks', lowStock: 'Low stock items'
      },
      activityChart: { registrationsAndWork: 'Registrations and work', registrations: 'Registrations', maintenances: 'Maintenance', date: 'Date', aria: 'Registration and work activity chart' },
      paymentsChart: { payments: 'Payments', date: 'Date', aria: 'Payments chart' },
      users: {
        title: 'Users', searchPlaceholder: 'Email or phone…', searchLabel: 'Search by email or phone', includeDeleted: 'Show deleted users', find: 'Search', reset: 'Reset', found: 'Found: {count}',
        loading: 'Loading users…', loadError: 'Could not load users.', empty: 'No users found.', sort: 'Sort by', user: 'User', plan: 'Plan', registration: 'Registered', vehicles: 'Vehicles', maintenance: 'Maintenance', organizations: 'Organizations',
        sortPlan: 'Personal plan · legacy model', openCard: 'Open profile', phoneNotProvided: 'No phone number', emailUnconfirmed: 'Email not confirmed', deleted: 'Deleted', notAssigned: 'Not assigned', until: 'until {date}', pagination: 'User pages', previous: 'Previous', next: 'Next', range: 'Showing {first}–{last} of {total}', pageSize: 'Per page'
      },
      overview: {
        tabOverview: 'Overview', tabVehicles: 'Vehicles and maintenance', tabOrganizationsPlans: 'Organizations and subscriptions', tabOrganizations: 'Organizations', tabPayments: 'Payments', tabSupport: 'Support requests', tabFeatures: 'Features',
        loadError: 'Could not load the user profile.', impersonateError: 'Could not sign in as the user.', plansLoadError: 'Could not load available plans.', contentsLoadError: 'Could not load organization contents.', operationError: 'Could not complete the operation.', organizationSubscriptionError: 'Could not update the organization subscription.',
        allowed: 'Allowed', denied: 'Denied', noValue: 'No value', backToUsers: '← Users', title: '360° profile', manage: 'Manage', impersonate: 'Sign in as user', refresh: 'Refresh', loading: 'Loading user profile…', noEmail: 'No email provided', deleted: 'Deleted', active: 'Active', vehicles: 'Vehicles', maintenances: 'Maintenance records', organizations: 'Organizations', paymentAmount: 'Payment total', activeSubscriptions: 'Active subscriptions', openRequests: 'Open support requests',
        basicInfo: 'Basic information', emailConfirmed: 'Email confirmed', yes: 'Yes', no: 'No', phone: 'Phone', phoneConfirmed: 'Phone confirmed', registration: 'Registered', password: 'Password', passwordSet: 'Set', passwordNotSet: 'Not set', securityConsent: 'Security and consent', lockoutAllowed: 'Lockout enabled', lockedUntil: 'Locked until', failedLogins: 'Failed sign-ins', dataProcessing: 'Personal data processing', accepted: 'Accepted', notAccepted: 'Not accepted', consentDate: 'Consent date', testPlans: 'Test plans', allowedPlural: 'Allowed', notAllowed: 'Not allowed', history: 'History', noEvents: 'No events.', equipment: 'Vehicles', archived: 'Archived', equipmentActive: 'Active', plateVin: 'Registration / VIN', organization: 'Organization', personal: 'Personal', openDefects: 'Open defects', noVehicles: 'No vehicles.', recentMaintenances: 'Recent maintenance', date: 'Date', type: 'Type', status: 'Status', cost: 'Cost', noMaintenances: 'No maintenance records.', organizationsAndPlans: 'Organizations and plans', freeProInfo: 'Free and PRO apply to a specific organization. Every user can use personal vehicles. Gifted PRO is activated with a promo code; there is no automatic demo.', noName: 'Unnamed', demoActive: 'Demo active', proActive: 'PRO active', planActive: 'Plan active', freeForever: 'Free · no expiry', basePlan: 'Base plan', inactive: 'Inactive', deletedOrganization: 'Deleted', archivedStatus: 'Archived', activeOrganization: 'Active', participants: 'Members', clients: 'Clients', joined: 'Joined', currentPlan: 'Current plan', validUntil: 'Valid until', hideContents: 'Hide contents', showContents: 'Show contents', grantDemo: 'Grant demo', grantPro: 'Grant PRO for free', grantPlan: 'Grant plan for free', extend: 'Extend', revokeDemo: 'Revoke demo', stopAll: 'Stop all active subscriptions', organizationSubscriptions: 'Organization subscriptions', expired: 'Expired', loadingContents: 'Loading contents…', noParticipants: 'No members.', noEquipment: 'No vehicles.', noClients: 'No clients.', clientVehicles: '{name} · vehicles: {count}', lastTwenty: 'Showing the 20 most recent.', noOrganizationMaintenances: 'No maintenance records.', noOrganizations: 'No organizations.', invitations: 'Invitations', role: 'Role', invitedBy: 'Invited by', created: 'Created', noInvitations: 'No invitations.', legacySubscriptions: 'Personal subscriptions · legacy model', start: 'Start', end: 'End', daysLeft: 'Days left', autoRenewal: 'Auto-renewal', noSubscriptions: 'No subscriptions.', subscriptionHistory: 'Personal subscription history', comment: 'Comment', noHistory: 'No history.', payment: 'Payments', amount: 'Amount', method: 'Method', transaction: 'Transaction', noPayments: 'No payments.', support: 'Support requests', noSubject: 'No subject', noSupport: 'No support requests.', individualFeatures: 'Individual features', value: 'Value', source: 'Source', manualGrant: 'Manual grant', noFeatures: 'No individual features.', impersonationTitle: 'Sign in as user', signingIn: 'Signing in…', continue: 'Continue', cancel: 'Cancel',
        orgGrantPlanTitle: 'Grant organization plan for free', orgGrantDemoTitle: 'Grant organization demo', orgExtendTitle: 'Extend organization plan', orgRevokeTitle: 'Revoke organization demo', orgStopTitle: 'Stop organization plan', stopOrgWarning: 'All active organization subscriptions will stop immediately. Unused paid time will not be restored automatically.', revokeOrgWarning: 'The organization demo will be revoked.', plan: 'Plan', selectPlan: 'Select a plan', termDays: 'Term in days', planStartsAndEnds: 'The plan starts now and ends {date}. No payment will be created.', choosePlanAndTerm: 'after selecting a plan and term', extendByDays: 'Extend by days', newEndDate: 'The new end date is {date}. No payment will be created.', specifyTerm: 'specify a term', reason: 'Reason', saving: 'Saving…', confirm: 'Confirm', managingUser: 'Manage user', grantPlanQuestion: 'Grant a plan for free?', grantDemoQuestion: 'Grant a demo plan to this user?', revokeDemoQuestion: 'Revoke the demo plan?', extendPersonalQuestion: 'Extend the personal subscription?', stopPersonalQuestion: 'Stop the personal subscription?', demoDurationInfo: 'The demo duration is defined by the plan settings.', stopPersonalWarning: 'All active personal subscriptions will stop. Unused paid time will not be restored automatically.', disableTestPlans: 'Disable test plans', allowTestPlans: 'Allow test plans', legacyPersonalDemo: 'Grant personal demo · legacy model', grantPersonalPlan: 'Grant personal plan for free', revokePersonalDemo: 'Revoke demo plan', extendPersonal: 'Extend personal subscription', finishPersonal: 'End personal subscription', close: 'Close'
      }
    }
  },
  'zh-Hans': {
    adminUsers: {
      dashboard: {
        title: '仪表盘', refresh: '刷新', refreshing: '正在刷新…', updatedAt: '更新时间：{time}', refreshInterval: '自动刷新', refreshMinute: '1 分钟', refreshSeconds: '{seconds} 秒', loading: '正在加载数据', newSection: '最新动态', today: '今天', sevenDays: '7 天', month: '一个月', newUsers: '新用户', newOrganizations: '新组织', payments: '付款', email: '电子邮件', support: '支持请求', catalogSuggestions: '目录建议', todayByHour: '今天（按小时）', forSevenDays: '最近 7 天', forMonth: '最近一个月', refreshError: '无法刷新数据。当前显示此前加载的内容。', loadError: '无法加载仪表盘。', users: '用户', organizations: '组织', total: '总数', newToday: '今天新增', newSevenDays: '7 天内新增', active: '活跃', members: '成员', activeSubscriptions: '有效订阅', equipmentAndMaintenance: '车辆与维修', vehicles: '车辆', maintenances: '维修记录', planned: '已计划', documents: '文档', openDefects: '未解决缺陷', finance: '财务', amountThirtyDays: '近 30 天金额', paymentsThirtyDays: '近 30 天付款数', problemPayments: '异常付款', communications: '通信', emailToday: '今天的电子邮件', emailSevenDays: '7 天内的电子邮件', sentSevenDays: '7 天内已发送', errorsSevenDays: '7 天内错误', pendingSend: '等待发送', attention: '需要关注', newRequests: '新请求', suggestions: '目录建议', pendingInvitations: '待处理邀请', overdueTasks: '逾期任务', lowStock: '库存不足'
      },
      activityChart: { registrationsAndWork: '注册与工作', registrations: '注册', maintenances: '维修', date: '日期', aria: '注册与工作活动图表' },
      paymentsChart: { payments: '付款', date: '日期', aria: '付款图表' },
      users: {
        title: '用户', searchPlaceholder: '电子邮件或电话…', searchLabel: '按电子邮件或电话搜索', includeDeleted: '显示已删除用户', find: '搜索', reset: '重置', found: '找到：{count}', loading: '正在加载用户…', loadError: '无法加载用户。', empty: '未找到用户。', sort: '排序', user: '用户', plan: '套餐', registration: '注册时间', vehicles: '车辆', maintenance: '维修', organizations: '组织', sortPlan: '个人套餐 · 旧模式', openCard: '打开个人资料', phoneNotProvided: '未提供电话号码', emailUnconfirmed: '电子邮件未确认', deleted: '已删除', notAssigned: '未分配', until: '至 {date}', pagination: '用户分页', previous: '上一页', next: '下一页', range: '显示 {first}–{last}，共 {total} 条', pageSize: '每页显示'
      },
      overview: {
        tabOverview: '概览', tabVehicles: '车辆与维修', tabOrganizationsPlans: '组织与订阅', tabOrganizations: '组织', tabPayments: '付款', tabSupport: '支持请求', tabFeatures: '功能', loadError: '无法加载用户资料。', impersonateError: '无法以该用户身份登录。', plansLoadError: '无法加载可用套餐。', contentsLoadError: '无法加载组织内容。', operationError: '无法完成操作。', organizationSubscriptionError: '无法更新组织订阅。', allowed: '允许', denied: '禁止', noValue: '无值', backToUsers: '← 用户', title: '用户 360° 资料', manage: '管理', impersonate: '以用户身份登录', refresh: '刷新', loading: '正在加载用户资料…', noEmail: '未提供电子邮件', deleted: '已删除', active: '活跃', vehicles: '车辆', maintenances: '维修记录', organizations: '组织', paymentAmount: '付款总额', activeSubscriptions: '有效订阅', openRequests: '未处理支持请求', basicInfo: '基本信息', emailConfirmed: '电子邮件已确认', yes: '是', no: '否', phone: '电话', phoneConfirmed: '电话已确认', registration: '注册时间', password: '密码', passwordSet: '已设置', passwordNotSet: '未设置', securityConsent: '安全与同意', lockoutAllowed: '允许锁定', lockedUntil: '锁定至', failedLogins: '登录失败次数', dataProcessing: '个人数据处理', accepted: '已同意', notAccepted: '未同意', consentDate: '同意日期', testPlans: '测试套餐', allowedPlural: '已允许', notAllowed: '未允许', history: '历史记录', noEvents: '没有事件。', equipment: '车辆', archived: '已归档', equipmentActive: '活跃', plateVin: '车牌 / VIN', organization: '组织', personal: '个人', openDefects: '未解决缺陷', noVehicles: '没有车辆。', recentMaintenances: '最近维修', date: '日期', type: '类型', status: '状态', cost: '费用', noMaintenances: '没有维修记录。', organizationsAndPlans: '组织与套餐', freeProInfo: 'Free 和 PRO 针对特定组织。每位用户都可使用个人车辆。赠送 PRO 通过优惠码激活；不会自动提供演示套餐。', noName: '未命名', demoActive: '演示套餐有效', proActive: 'PRO 有效', planActive: '套餐有效', freeForever: 'Free · 永久有效', basePlan: '基础套餐', inactive: '未启用', deletedOrganization: '已删除', archivedStatus: '已归档', activeOrganization: '活跃', participants: '成员', clients: '客户', joined: '加入时间', currentPlan: '当前套餐', validUntil: '有效期至', hideContents: '隐藏内容', showContents: '显示内容', grantDemo: '授予演示套餐', grantPro: '免费授予 PRO', grantPlan: '免费授予套餐', extend: '延长', revokeDemo: '撤销演示套餐', stopAll: '停止所有有效订阅', organizationSubscriptions: '组织订阅', expired: '已过期', loadingContents: '正在加载内容…', noParticipants: '没有成员。', noEquipment: '没有车辆。', noClients: '没有客户。', clientVehicles: '{name} · 车辆：{count}', lastTwenty: '显示最近 20 条。', noOrganizationMaintenances: '没有维修记录。', noOrganizations: '没有组织。', invitations: '邀请', role: '角色', invitedBy: '邀请人', created: '创建时间', noInvitations: '没有邀请。', legacySubscriptions: '个人订阅 · 旧模式', start: '开始', end: '结束', daysLeft: '剩余天数', autoRenewal: '自动续费', noSubscriptions: '没有订阅。', subscriptionHistory: '个人订阅历史', comment: '备注', noHistory: '没有历史记录。', payment: '付款', amount: '金额', method: '方式', transaction: '交易', noPayments: '没有付款。', support: '支持请求', noSubject: '无主题', noSupport: '没有支持请求。', individualFeatures: '个人功能', value: '值', source: '来源', manualGrant: '手动授予', noFeatures: '没有个人功能。', impersonationTitle: '以用户身份登录', signingIn: '正在登录…', continue: '继续', cancel: '取消', orgGrantPlanTitle: '免费授予组织套餐', orgGrantDemoTitle: '授予组织演示套餐', orgExtendTitle: '延长组织套餐', orgRevokeTitle: '撤销组织演示套餐', orgStopTitle: '停止组织套餐', stopOrgWarning: '所有有效组织订阅将立即停止。未使用的付费期限不会自动恢复。', revokeOrgWarning: '组织演示套餐将被撤销。', plan: '套餐', selectPlan: '选择套餐', termDays: '期限（天）', planStartsAndEnds: '套餐立即开始，将于 {date} 结束。不会创建付款。', choosePlanAndTerm: '选择套餐和期限后', extendByDays: '延长天数', newEndDate: '新期限至 {date}。不会创建付款。', specifyTerm: '指定期限', reason: '原因', saving: '正在保存…', confirm: '确认', managingUser: '管理用户', grantPlanQuestion: '免费授予套餐？', grantDemoQuestion: '向该用户授予演示套餐？', revokeDemoQuestion: '撤销演示套餐？', extendPersonalQuestion: '延长个人订阅？', stopPersonalQuestion: '停止个人订阅？', demoDurationInfo: '演示期限由套餐设置决定。', stopPersonalWarning: '所有有效个人订阅将停止。未使用的付费期限不会自动恢复。', disableTestPlans: '禁用测试套餐', allowTestPlans: '允许测试套餐', legacyPersonalDemo: '授予个人演示套餐 · 旧模式', grantPersonalPlan: '免费授予个人套餐', revokePersonalDemo: '撤销演示套餐', extendPersonal: '延长个人订阅', finishPersonal: '结束个人订阅', close: '关闭'
      }
    }
  }
} as const

const subscriptionMessages = {
  ru: {
    freeProFallback: 'После завершения PRO владелец продолжит базовую работу на Free. История сохранится; новые платные действия будут закрыты.',
    otherScenarioFallback: 'После завершения подписки действуют прежние условия этого направления.'
  },
  en: {
    freeProFallback: 'After PRO ends, the owner can continue basic work on Free. History remains available; new paid actions are restricted.',
    otherScenarioFallback: 'After the subscription ends, the existing terms for this service area apply.'
  },
  'zh-Hans': {
    freeProFallback: 'PRO 到期后，组织所有者仍可使用 Free 进行基础工作。历史记录会保留；新的付费操作将受限。',
    otherScenarioFallback: '订阅结束后，将按照该业务的现有条件执行。'
  }
} satisfies Record<UiLocale, Record<string, string>>

const apiMessages = {
  ru: { requestFailed: 'Запрос к API завершился ошибкой (код {status}).' },
  en: { requestFailed: 'The API request failed (status {status}).' },
  'zh-Hans': { requestFailed: 'API 请求失败（状态码 {status}）。' }
} satisfies Record<UiLocale, Record<string, string>>

for (const [locale, localeMessages] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, {
    adminUsers: {
      ...localeMessages.adminUsers,
      overview: {
        ...localeMessages.adminUsers.overview,
        ...subscriptionMessages[locale as UiLocale]
      },
      api: apiMessages[locale as UiLocale]
    }
  })
}

using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary>
/// Стабильные permission-коды действий внутри рабочего пространства организации.
/// </summary>
public static class OrganizationPermissionCodes
{
    /// <summary>Просмотр рабочего пространства организации.</summary>
    public const string WorkspaceView = "workspace.view";
    /// <summary>Редактирование профиля организации.</summary>
    public const string WorkspaceProfileEdit = "workspace.profile.edit";
    /// <summary>Просмотр справочников организации.</summary>
    public const string WorkspaceCatalogView = "workspace.catalog.view";
    /// <summary>Редактирование юридических реквизитов организации.</summary>
    public const string WorkspaceRequisitesEdit = "workspace.requisites.edit";
    /// <summary>Архивация или восстановление организации.</summary>
    public const string WorkspaceArchive = "workspace.archive";
    /// <summary>Управление тарифом и оплатами организации.</summary>
    public const string BillingManage = "billing.manage";
    /// <summary>Управление сотрудниками без изменения доступа пользователей.</summary>
    public const string EmployeesManage = "employees.manage";
    /// <summary>Просмотр общего графика смен.</summary>
    public const string ShiftsView = "shifts.view";
    /// <summary>Управление графиком смен.</summary>
    public const string ShiftsManage = "shifts.manage";
    /// <summary>Просмотр собственных опубликованных смен.</summary>
    public const string ShiftsSelfView = "shifts.self.view";
    /// <summary>Просмотр участников организации.</summary>
    public const string MembersView = "members.view";
    /// <summary>Приглашение новых участников.</summary>
    public const string MembersInvite = "members.invite";
    /// <summary>Управление ролями и правами участников.</summary>
    public const string MembersRolesManage = "members.roles.manage";
    /// <summary>Удаление участников из организации.</summary>
    public const string MembersRemove = "members.remove";
    /// <summary>Просмотр клиентов организации.</summary>
    public const string ClientsView = "clients.view";
    /// <summary>Создание клиентов организации.</summary>
    public const string ClientsCreate = "clients.create";
    /// <summary>Редактирование карточек клиентов.</summary>
    public const string ClientsEdit = "clients.edit";
    /// <summary>Архивация и восстановление клиентов.</summary>
    public const string ClientsArchive = "clients.archive";
    /// <summary>Управление привязкой техники к клиентам.</summary>
    public const string ClientsVehiclesManage = "clients.vehicles.manage";
    /// <summary> Просмотр обращений организации. </summary>
    public const string MessagesView = "messages.view";
    public const string MessagesSend = "messages.send";
    public const string MessagesManage = "messages.manage";
    public const string MessagesTemplatesManage = "messages.templates.manage";

    public const string InquiriesView = "inquiries.view";
    /// <summary> Управление обращениями организации. </summary>
    public const string InquiriesManage = "inquiries.manage";
    /// <summary>Просмотр техники организации.</summary>
    public const string VehiclesView = "vehicles.view";
    /// <summary>Создание техники организации.</summary>
    public const string VehiclesCreate = "vehicles.create";
    /// <summary>Редактирование техники организации.</summary>
    public const string VehiclesEdit = "vehicles.edit";
    /// <summary>Архивация и восстановление техники.</summary>
    public const string VehiclesArchive = "vehicles.archive";
    /// <summary>Управление счетчиками пробега и моточасов техники.</summary>
    public const string VehiclesCountersManage = "vehicles.counters.manage";
    /// <summary>Просмотр обслуживаний и работ.</summary>
    public const string MaintenanceView = "maintenance.view";
    /// <summary>Создание обслуживаний и работ.</summary>
    public const string MaintenanceCreate = "maintenance.create";
    /// <summary>Редактирование обслуживаний, workflow и состава работ.</summary>
    public const string MaintenanceEdit = "maintenance.edit";
    /// <summary>Удаление обслуживаний.</summary>
    public const string MaintenanceDelete = "maintenance.delete";
    /// <summary>Управление документами обслуживания.</summary>
    public const string MaintenanceDocumentsManage = "maintenance.documents.manage";
    /// <summary>Просмотр назначенных исполнителю операций.</summary>
    public const string MaintenanceWorkView = "maintenance.work.view";
    /// <summary>Выполнение назначенных исполнителю операций.</summary>
    public const string MaintenanceWorkExecute = "maintenance.work.execute";
    /// <summary>Просмотр дефектов и замечаний.</summary>
    public const string DefectsView = "defects.view";
    /// <summary>Создание дефектов и замечаний.</summary>
    public const string DefectsCreate = "defects.create";
    /// <summary>Редактирование дефектов и замечаний.</summary>
    public const string DefectsEdit = "defects.edit";
    /// <summary>Удаление дефектов и замечаний.</summary>
    public const string DefectsDelete = "defects.delete";
    /// <summary>Управление статусами дефектов.</summary>
    public const string DefectsStatusManage = "defects.status.manage";
    /// <summary>Связь дефектов с обслуживаниями.</summary>
    public const string DefectsMaintenanceLink = "defects.maintenance.link";
    /// <summary>Просмотр планировщика.</summary>
    public const string SchedulerView = "scheduler.view";
    /// <summary>Управление задачами планировщика.</summary>
    public const string SchedulerManage = "scheduler.manage";
    /// <summary>Просмотр счетчиков пробега и моточасов.</summary>
    public const string CountersView = "counters.view";
    /// <summary>Управление записями счетчиков.</summary>
    public const string CountersManage = "counters.manage";
    /// <summary>Просмотр склада материалов и запчастей.</summary>
    public const string MaterialsView = "materials.view";
    /// <summary>Управление складом материалов и запчастей.</summary>
    public const string MaterialsManage = "materials.manage";
    /// <summary>Просмотр журнала работ.</summary>
    public const string WorkJournalView = "work_journal.view";
    /// <summary>Просмотр финансового раздела.</summary>
    public const string FinanceView = "finance.view";
    /// <summary>Полное управление финансовым разделом.</summary>
    public const string FinanceManage = "finance.manage";
    /// <summary>Просмотр статистики рабочего пространства.</summary>
    public const string AnalyticsView = "analytics.view";
    /// <summary>Экспорт документов PDF/Word.</summary>
    public const string DocumentsExport = "documents.export";
    /// <summary>Просмотр внутренней wiki организации.</summary>
    public const string WikiView = "wiki.view";
}

/// <summary>
/// Каталог прав организации и дефолтная матрица для фиксированных ролей.
/// </summary>
public static class OrganizationPermissionCatalog
{
    /// <summary> Каталог определений прав организации. </summary>
    private static readonly IReadOnlyList<OrganizationPermissionDefinitionDto> Definitions =
    [
        Permission(OrganizationPermissionCodes.WorkspaceView, "Просмотр рабочего пространства", "Организация", "Открывать разделы организации.", false),
        Permission(OrganizationPermissionCodes.WorkspaceCatalogView, "Просмотр справочников", "Организация", "Видеть услуги, посты, материалы и другие справочники организации.", false),
        Permission(OrganizationPermissionCodes.WorkspaceProfileEdit, "Изменение профиля организации", "Организация", "Менять название, описание и тип организации.", true),
        Permission(OrganizationPermissionCodes.WorkspaceRequisitesEdit, "Изменение реквизитов организации", "Организация", "Менять юридические реквизиты и контакты.", true),
        Permission(OrganizationPermissionCodes.WorkspaceArchive, "Архивация организации", "Организация", "Отправлять организацию в архив и восстанавливать ее.", true),
        Permission(OrganizationPermissionCodes.BillingManage, "Управление тарифом", "Организация", "Покупать, продлевать и активировать тариф организации.", true),
        Permission(OrganizationPermissionCodes.EmployeesManage, "Управление сотрудниками", "Команда", "Добавлять, редактировать и архивировать сотрудников без изменения доступа пользователей.", true),
        Permission(OrganizationPermissionCodes.ShiftsView, "Просмотр смен", "Смены", "Видеть график всей команды.", false),
        Permission(OrganizationPermissionCodes.ShiftsManage, "Управление сменами", "Смены", "Планировать, менять и публиковать смены.", true),
        Permission(OrganizationPermissionCodes.ShiftsSelfView, "Мои смены", "Смены", "Видеть собственные опубликованные смены.", false),
        Permission(OrganizationPermissionCodes.MembersView, "Просмотр участников", "Команда", "Видеть участников и приглашения.", false),
        Permission(OrganizationPermissionCodes.MembersInvite, "Приглашение участников", "Команда", "Приглашать пользователей в CRM.", true),
        Permission(OrganizationPermissionCodes.MembersRolesManage, "Управление ролями участников", "Команда", "Менять роли пользователей.", true),
        Permission(OrganizationPermissionCodes.MembersRemove, "Удаление участников", "Команда", "Удалять доступ пользователей к организации.", true),
        Permission(OrganizationPermissionCodes.ClientsView, "Просмотр клиентов", "Клиенты", "Видеть список и карточки клиентов.", false),
        Permission(OrganizationPermissionCodes.ClientsCreate, "Создание клиентов", "Клиенты", "Добавлять клиентов.", true),
        Permission(OrganizationPermissionCodes.ClientsEdit, "Редактирование клиентов", "Клиенты", "Менять карточки клиентов, статусы, ответственных и теги.", true),
        Permission(OrganizationPermissionCodes.ClientsArchive, "Архивация клиентов", "Клиенты", "Отправлять клиентов в архив и восстанавливать их.", true),
        Permission(OrganizationPermissionCodes.ClientsVehiclesManage, "Управление техникой клиента", "Клиенты", "Привязывать, отвязывать и переносить технику между клиентами.", true),
        Permission(OrganizationPermissionCodes.MessagesTemplatesManage, "Управление шаблонами сообщений", "Сообщения", "Создавать, изменять и архивировать группы и шаблоны ответов организации.", true),
        Permission(OrganizationPermissionCodes.MessagesView, "Просмотр переписки", "Сообщения", "Открывать диалоги организации и отмечать прочитанные сообщения.", false),
        Permission(OrganizationPermissionCodes.MessagesSend, "Отправка сообщений", "Сообщения", "Отвечать клиентам от имени организации.", true),
        Permission(OrganizationPermissionCodes.MessagesManage, "Связь диалогов с клиентами", "Сообщения", "Связывать диалоги с карточками клиентов.", true),
        Permission(OrganizationPermissionCodes.InquiriesView, "Просмотр обращений", "Клиенты", "Видеть обращения клиентов до записи.", false),
        Permission(OrganizationPermissionCodes.InquiriesManage, "Управление обращениями", "Клиенты", "Создавать и изменять обращения, действия и этапы.", true),
        Permission(OrganizationPermissionCodes.VehiclesView, "Просмотр техники", "Техника", "Видеть технику организации.", false),
        Permission(OrganizationPermissionCodes.VehiclesCreate, "Создание техники", "Техника", "Добавлять технику в организацию.", true),
        Permission(OrganizationPermissionCodes.VehiclesEdit, "Редактирование техники", "Техника", "Менять карточки техники.", true),
        Permission(OrganizationPermissionCodes.VehiclesArchive, "Архивация техники", "Техника", "Отправлять технику в архив и восстанавливать ее.", true),
        Permission(OrganizationPermissionCodes.VehiclesCountersManage, "Управление пробегом и наработкой", "Техника", "Добавлять и удалять записи пробега или моточасов.", true),
        Permission(OrganizationPermissionCodes.MaintenanceView, "Просмотр обслуживаний", "Обслуживание", "Видеть обслуживание и историю работ.", false),
        Permission(OrganizationPermissionCodes.MaintenanceCreate, "Создание обслуживаний", "Обслуживание", "Добавлять обслуживание и работы.", true),
        Permission(OrganizationPermissionCodes.MaintenanceEdit, "Редактирование обслуживаний", "Обслуживание", "Менять обслуживание, workflow, даты и состав работ.", true),
        Permission(OrganizationPermissionCodes.MaintenanceDelete, "Удаление обслуживаний", "Обслуживание", "Удалять обслуживание.", true),
        Permission(OrganizationPermissionCodes.MaintenanceDocumentsManage, "Управление документами обслуживания", "Обслуживание", "Загружать и удалять документы обслуживания.", true),
        Permission(OrganizationPermissionCodes.MaintenanceWorkView, "Мои работы", "Обслуживание", "Видеть назначенные операции.", false),
        Permission(OrganizationPermissionCodes.MaintenanceWorkExecute, "Выполнение работ", "Обслуживание", "Начинать, приостанавливать, продолжать и завершать назначенные операции.", true),
        Permission(OrganizationPermissionCodes.DefectsView, "Просмотр неисправностей", "Неисправности", "Видеть неисправности.", false),
        Permission(OrganizationPermissionCodes.DefectsCreate, "Создание неисправностей", "Неисправности", "Добавлять неисправности.", true),
        Permission(OrganizationPermissionCodes.DefectsEdit, "Редактирование неисправностей", "Неисправности", "Менять описание, важность и технику неисправности.", true),
        Permission(OrganizationPermissionCodes.DefectsDelete, "Удаление неисправностей", "Неисправности", "Удалять неисправности.", true),
        Permission(OrganizationPermissionCodes.DefectsStatusManage, "Изменение статуса неисправностей", "Неисправности", "Переводить неисправности в работу, откладывать и закрывать.", true),
        Permission(OrganizationPermissionCodes.DefectsMaintenanceLink, "Связь неисправностей с обслуживанием", "Неисправности", "Привязывать и отвязывать обслуживания от неисправностей.", true),
        Permission(OrganizationPermissionCodes.SchedulerView, "Просмотр планировщика", "Планировщик", "Видеть календарь и список задач.", false),
        Permission(OrganizationPermissionCodes.SchedulerManage, "Управление планировщиком", "Планировщик", "Менять статусы задач, переносить и закрывать задачи.", true),
        Permission(OrganizationPermissionCodes.CountersView, "Просмотр счетчиков", "Счетчики", "Видеть пробег, моточасы, историю и графики.", false),
        Permission(OrganizationPermissionCodes.CountersManage, "Управление счетчиками", "Счетчики", "Добавлять, менять и удалять записи счетчиков.", true),
        Permission(OrganizationPermissionCodes.MaterialsView, "Просмотр склада", "Материалы", "Видеть склад, остатки и движения материалов.", false),
        Permission(OrganizationPermissionCodes.MaterialsManage, "Управление складом", "Материалы", "Добавлять поступления, списания и корректировки материалов.", true),
        Permission(OrganizationPermissionCodes.WorkJournalView, "Просмотр журнала работ", "Журнал работ", "Видеть закрытые работы, события обслуживания и историю изменений.", false),
        Permission(OrganizationPermissionCodes.FinanceView, "Просмотр финансов", "Финансы", "Видеть выручку, долги, себестоимость, прибыль, графики и заказы финансового раздела.", false),
        Permission(OrganizationPermissionCodes.FinanceManage, "Полный доступ к финансам", "Финансы", "Управлять финансовыми действиями, оплатами и будущими настройками финансового раздела.", true),
        Permission(OrganizationPermissionCodes.AnalyticsView, "Просмотр статистики и аналитики", "Статистика", "Открывает раздел «Статистика» и аналитические блоки рабочего стола: KPI, графики, отчеты, выручку и сводные показатели.", false),
        Permission(OrganizationPermissionCodes.DocumentsExport, "Экспорт документов", "Документы", "Формировать PDF и Word документы.", false),
        Permission(OrganizationPermissionCodes.WikiView, "Просмотр wiki", "Wiki", "Открывать внутреннюю wiki организации.", false)
    ];

    /// <summary> Матрица прав по умолчанию для фиксированных ролей. </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Matrix =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [OrganizationRoleCodes.Owner] = Definitions.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase),
            [OrganizationRoleCodes.Admin] = Definitions
                .Where(x => x.Code is not OrganizationPermissionCodes.WorkspaceArchive)
                .Select(x => x.Code)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            [OrganizationRoleCodes.Employee] = new[]
            {
                OrganizationPermissionCodes.WorkspaceView,
                OrganizationPermissionCodes.WorkspaceCatalogView,
                OrganizationPermissionCodes.ClientsView,
                OrganizationPermissionCodes.ClientsCreate,
                OrganizationPermissionCodes.ClientsEdit,
                OrganizationPermissionCodes.MessagesView,
                OrganizationPermissionCodes.InquiriesView,
                OrganizationPermissionCodes.InquiriesManage,
                OrganizationPermissionCodes.MessagesSend,
                OrganizationPermissionCodes.MessagesManage,
                OrganizationPermissionCodes.ClientsVehiclesManage,
                OrganizationPermissionCodes.VehiclesView,
                OrganizationPermissionCodes.VehiclesCreate,
                OrganizationPermissionCodes.VehiclesEdit,
                OrganizationPermissionCodes.VehiclesCountersManage,
                OrganizationPermissionCodes.MaintenanceView,
                OrganizationPermissionCodes.MaintenanceCreate,
                OrganizationPermissionCodes.MaintenanceEdit,
                OrganizationPermissionCodes.MaintenanceDocumentsManage,
                OrganizationPermissionCodes.DefectsView,
                OrganizationPermissionCodes.DefectsCreate,
                OrganizationPermissionCodes.DefectsEdit,
                OrganizationPermissionCodes.DefectsStatusManage,
                OrganizationPermissionCodes.DefectsMaintenanceLink,
                OrganizationPermissionCodes.SchedulerView,
                OrganizationPermissionCodes.SchedulerManage,
                OrganizationPermissionCodes.CountersView,
                OrganizationPermissionCodes.CountersManage,
                OrganizationPermissionCodes.MaterialsView,
                OrganizationPermissionCodes.MaterialsManage,
                OrganizationPermissionCodes.WorkJournalView,
                OrganizationPermissionCodes.FinanceView,
                OrganizationPermissionCodes.AnalyticsView,
                OrganizationPermissionCodes.DocumentsExport,
                OrganizationPermissionCodes.WikiView
            }.ToHashSet(StringComparer.OrdinalIgnoreCase),
            [OrganizationRoleCodes.Performer] = new[]
            {
                OrganizationPermissionCodes.WorkspaceView,
                OrganizationPermissionCodes.MaintenanceWorkView,
                OrganizationPermissionCodes.MaintenanceWorkExecute,
                OrganizationPermissionCodes.ShiftsSelfView
            }.ToHashSet(StringComparer.OrdinalIgnoreCase),
            [OrganizationRoleCodes.Viewer] = new[]
            {
                OrganizationPermissionCodes.WorkspaceView,
                OrganizationPermissionCodes.WorkspaceCatalogView,
                OrganizationPermissionCodes.ClientsView,
                OrganizationPermissionCodes.MessagesView,
                OrganizationPermissionCodes.InquiriesView,
                OrganizationPermissionCodes.VehiclesView,
                OrganizationPermissionCodes.MaintenanceView,
                OrganizationPermissionCodes.DefectsView,
                OrganizationPermissionCodes.SchedulerView,
                OrganizationPermissionCodes.CountersView,
                OrganizationPermissionCodes.MaterialsView,
                OrganizationPermissionCodes.WorkJournalView,
                OrganizationPermissionCodes.FinanceView,
                OrganizationPermissionCodes.AnalyticsView,
                OrganizationPermissionCodes.DocumentsExport,
                OrganizationPermissionCodes.WikiView
            }.ToHashSet(StringComparer.OrdinalIgnoreCase)
        };

    /// <summary>
    /// Возвращает определения всех прав, доступных для настройки ролей.
    /// </summary>
    public static IReadOnlyList<OrganizationPermissionDefinitionDto> GetDefinitions() => Definitions;

    /// <summary>
    /// Возвращает дефолтную матрицу прав по фиксированным ролям.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> GetDefaultRoleMatrix() => Matrix;

    /// <summary>
    /// Проверяет наличие права у роли без учета organization overrides.
    /// </summary>
    public static bool RoleHasPermission(string? roleCode, string permissionCode)
    {
        var normalizedPermissionCode = NormalizePermissionCode(permissionCode);
        return !string.IsNullOrWhiteSpace(roleCode)
            && Matrix.TryGetValue(roleCode, out var permissions)
            && permissions.Contains(normalizedPermissionCode);
    }

    /// <summary>
    /// Возвращает описание права или fallback-описание для неизвестного кода.
    /// </summary>
    public static OrganizationPermissionDefinitionDto GetDefinition(string permissionCode)
    {
        var normalizedPermissionCode = NormalizePermissionCode(permissionCode);
        return Definitions.FirstOrDefault(x => string.Equals(x.Code, normalizedPermissionCode, StringComparison.OrdinalIgnoreCase))
            ?? new OrganizationPermissionDefinitionDto
            {
                Code = normalizedPermissionCode,
                Name = "выполнение действия",
                GroupName = "Другое",
                Description = "Действие в рабочем пространстве.",
                IsWritePermission = true
            };
    }

    /// <summary> Возвращает человекочитаемое название права по коду. </summary>
    public static string GetPermissionName(string permissionCode) => GetDefinition(permissionCode).Name;

    /// <summary> Формирует сообщение об отказе в доступе для права. </summary>
    public static string BuildDeniedMessage(string permissionCode)
    {
        var permissionName = GetPermissionName(permissionCode);
        return $"Недостаточно прав: {permissionName.ToLowerInvariant()}.";
    }

    /// <summary> Создаёт определение права с заданными атрибутами. </summary>
    private static OrganizationPermissionDefinitionDto Permission(
        string code,
        string name,
        string groupName,
        string description,
        bool isWritePermission) =>
        new()
        {
            Code = code,
            Name = name,
            GroupName = groupName,
            Description = description,
            IsWritePermission = isWritePermission
        };

    /// <summary> Приводит код права к каноническому виду. </summary>
    private static string NormalizePermissionCode(string? permissionCode) =>
        string.IsNullOrWhiteSpace(permissionCode)
            ? string.Empty
            : permissionCode.Trim().ToLowerInvariant();
}

using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Defects.Dtos;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.WebApp.Infrastructure.Localization;
using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;

namespace OpenCRM.WebApp.Models.Organizations;

/// <summary> Модель списка организаций с сводкой и приглашениями. </summary>
public class OrganizationIndexViewModel
{
    /// <summary> Сводка по команде и организациям. </summary>
    public TeamOverviewDto Overview { get; set; } = new();

    /// <summary> Список ожидающих приглашений в организации. </summary>
    public IReadOnlyList<PendingOrganizationInvitationDto> Invitations { get; set; } = [];

    /// <summary> Варианты типов организаций с доступностью для владельца. </summary>
    public IReadOnlyList<OrganizationTypeChoiceViewModel> OrganizationTypeOptions { get; set; } = [];

    /// <summary> Доступ к рабочим пространствам по идентификаторам организаций. </summary>
    public IReadOnlyDictionary<Guid, OrganizationWorkspaceAccessDto> WorkspaceAccessByOrganizationId { get; set; }
        = new Dictionary<Guid, OrganizationWorkspaceAccessDto>();

    /// <summary> Признак возможности создания организации. </summary>
    public bool CanCreateOrganization { get; set; } = true;

    /// <summary> Сообщение о создании организации, если есть. </summary>
    public string? CreateOrganizationMessage { get; set; }

    /// <summary> Информационное сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке, если она возникла. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Модель страницы создания организации. </summary>
public class OrganizationCreateViewModel
{
    /// <summary> Доступные варианты типа организации. </summary>
    public IReadOnlyList<OrganizationTypeChoiceViewModel> OrganizationTypeOptions { get; set; } = [];

    /// <summary> Введённые данные для создания организации. </summary>
    public CreateOrganizationInputModel CreateOrganization { get; set; } = new();

    /// <summary> Признак возможности создания организации. </summary>
    public bool CanCreateOrganization { get; set; } = true;

    /// <summary> Сообщение о создании организации, если есть. </summary>
    public string? CreateOrganizationMessage { get; set; }

    /// <summary> Информационное сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке, если она возникла. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Вариант типа организации и доступность его создания. </summary>
public class OrganizationTypeChoiceViewModel
{
    /// <summary> Код типа организации. </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary> Название типа организации. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание типа организации. </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary> Доступно ли создание организации этого типа. </summary>
    public bool IsAvailable { get; set; }

    /// <summary> Есть ли в архиве организация этого типа, принадлежащая владельцу. </summary>
    public bool HasArchivedOrganization { get; set; }

    /// <summary> Пояснение о недоступности создания. </summary>
    public string? UnavailableMessage { get; set; }
}

/// <summary> Модель рабочего пространства организации. </summary>
public class OrganizationWorkspaceViewModel
{
    /// <summary> Данные организации. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Оболочка рабочего пространства, если доступна. </summary>
    public OrganizationWorkspaceShellDto? WorkspaceShell { get; set; }

    /// <summary> Доступ пользователя к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Данные боковой панели рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Текущая подписка организации, если есть. </summary>
    public OrganizationSubscriptionDto? CurrentSubscription { get; set; }

    /// <summary> Действующий тарифный план подписки. </summary>
    public SubscriptionPlanDto? EffectivePlan { get; set; }

    /// <summary> Ограничение рабочего пространства по числу автомобилей. </summary>
    public int WorkspaceVehicleLimit { get; set; }

    /// <summary> Ограничение рабочего пространства по числу участников. </summary>
    public int WorkspaceMembersLimit { get; set; }

    /// <summary> Количество активных автомобилей. </summary>
    public int ActiveVehiclesCount { get; set; }

    /// <summary> Количество архивных автомобилей. </summary>
    public int ArchivedVehiclesCount { get; set; }

    /// <summary> Возможности рабочего пространства с их значениями. </summary>
    public IReadOnlyList<FeatureValueDto> WorkspaceFeatures { get; set; } = [];

    /// <summary> Список активных автомобилей организации. </summary>
    public IReadOnlyList<GetVehicleDto> ActiveVehicles { get; set; } = [];

    /// <summary> Список архивных автомобилей организации. </summary>
    public IReadOnlyList<GetVehicleDto> ArchivedVehicles { get; set; } = [];

    /// <summary> Последние записи об обслуживании организации. </summary>
    public IReadOnlyList<OrganizationRecentMaintenanceViewModel> RecentMaintenances { get; set; } = [];

    /// <summary> Начальная дата фильтра обслуживаний. </summary>
    public DateTime MaintenanceDateFrom { get; set; }

    /// <summary> Конечная дата фильтра обслуживаний. </summary>
    public DateTime MaintenanceDateTo { get; set; }

    /// <summary> Номер страницы списка обслуживаний. </summary>
    public int MaintenancePage { get; set; } = 1;

    /// <summary> Размер страницы списка обслуживаний. </summary>
    public int MaintenancePageSize { get; set; } = 10;

    /// <summary> Общее количество записей об обслуживании. </summary>
    public int MaintenanceTotalCount { get; set; }

    /// <summary> Признак доступности модуля автомобилей. </summary>
    public bool HasVehiclesModule { get; set; }

    /// <summary> Признак доступности модуля клиентов. </summary>
    public bool HasClientsModule { get; set; }

    /// <summary> Признак доступности модуля дефектов. </summary>
    public bool HasDefectsModule { get; set; }

    /// <summary> Признак доступности модуля счётчиков. </summary>
    public bool HasCountersModule { get; set; }

    /// <summary> Признак доступности модуля планировщика. </summary>
    public bool HasSchedulerModule { get; set; }

    /// <summary> Вкладка рабочего пространства по умолчанию. </summary>
    public string DefaultWorkspaceTab { get; set; } = "vehicles";

    /// <summary> Количество просроченных задач планировщика. </summary>
    public int PlannerOverdueCount { get; set; }

    /// <summary> Количество близких по сроку задач планировщика. </summary>
    public int PlannerSoonCount { get; set; }

    /// <summary> Количество задач планировщика, требующих внимания. </summary>
    public int PlannerAttentionCount { get; set; }

    /// <summary> Количество дефектов в планировщике. </summary>
    public int PlannerDefectsCount { get; set; }

    /// <summary> Количество задач первого обслуживания в планировщике. </summary>
    public int PlannerFirstServiceCount { get; set; }

    /// <summary> Количество задач обновления счётчиков в планировщике. </summary>
    public int PlannerCounterUpdateCount { get; set; }

    /// <summary> Количество клиентов организации. </summary>
    public int ClientsCount { get; set; }

    /// <summary> Информационное сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке, если она возникла. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Модель обзорной страницы организации. </summary>
public class OrganizationOverviewViewModel
{
    /// <summary> Данные организации. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Оболочка рабочего пространства, если доступна. </summary>
    public OrganizationWorkspaceShellDto? WorkspaceShell { get; set; }

    /// <summary> Доступ пользователя к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Данные боковой панели рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Текущая подписка организации, если есть. </summary>
    public OrganizationSubscriptionDto? CurrentSubscription { get; set; }

    /// <summary> Действующий тарифный план подписки. </summary>
    public SubscriptionPlanDto? EffectivePlan { get; set; }

    /// <summary> Возможности рабочего пространства с их значениями. </summary>
    public IReadOnlyList<FeatureValueDto> WorkspaceFeatures { get; set; } = [];

    /// <summary> Ограничение рабочего пространства по числу автомобилей. </summary>
    public int WorkspaceVehicleLimit { get; set; }

    /// <summary> Ограничение рабочего пространства по числу участников. </summary>
    public int WorkspaceMembersLimit { get; set; }

    /// <summary> Количество активных автомобилей. </summary>
    public int ActiveVehiclesCount { get; set; }

    /// <summary> Количество архивных автомобилей. </summary>
    public int ArchivedVehiclesCount { get; set; }

    /// <summary> Количество клиентов организации. </summary>
    public int ClientsCount { get; set; }

    /// <summary> Общее количество записей об обслуживании. </summary>
    public int MaintenanceTotalCount { get; set; }

    /// <summary> Количество запланированных обслуживаний. </summary>
    public int MaintenancePlannedCount { get; set; }

    /// <summary> Количество обслуживаний в работе. </summary>
    public int MaintenanceInProgressCount { get; set; }

    /// <summary> Количество завершённых обслуживаний. </summary>
    public int MaintenanceCompletedCount { get; set; }

    /// <summary> Количество открытых дефектов. </summary>
    public int OpenDefectsCount { get; set; }

    /// <summary> Количество устранённых дефектов. </summary>
    public int ResolvedDefectsCount { get; set; }

    /// <summary> Количество записей счётчиков. </summary>
    public int CounterEntriesCount { get; set; }

    /// <summary> Количество документов. </summary>
    public int DocumentsCount { get; set; }

    /// <summary> Количество просроченных задач планировщика. </summary>
    public int PlannerOverdueCount { get; set; }

    /// <summary> Количество близких по сроку задач планировщика. </summary>
    public int PlannerSoonCount { get; set; }

    /// <summary> Количество задач планировщика, требующих внимания. </summary>
    public int PlannerAttentionCount { get; set; }

    /// <summary> Признак доступности модуля автомобилей. </summary>
    public bool HasVehiclesModule { get; set; }

    /// <summary> Признак доступности модуля клиентов. </summary>
    public bool HasClientsModule { get; set; }

    /// <summary> Признак доступности модуля дефектов. </summary>
    public bool HasDefectsModule { get; set; }

    /// <summary> Признак доступности модуля счётчиков. </summary>
    public bool HasCountersModule { get; set; }

    /// <summary> Признак доступности модуля планировщика. </summary>
    public bool HasSchedulerModule { get; set; }

    /// <summary> Информационное сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке, если она возникла. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Модель журнала работ организации. </summary>
public class OrganizationWorkJournalViewModel
{
    /// <summary> Данные организации. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Доступ пользователя к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Данные боковой панели рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Записи текущих работ в журнале. </summary>
    public IReadOnlyList<OrganizationWorkJournalWorkItemViewModel> WorkItems { get; set; } = [];

    /// <summary> Записи истории работ в журнале. </summary>
    public IReadOnlyList<OrganizationWorkJournalItemViewModel> HistoryItems { get; set; } = [];

    /// <summary> Список автомобилей для выбора фильтра. </summary>
    public IReadOnlyList<OrganizationVehicleOptionViewModel> VehicleOptions { get; set; } = [];

    /// <summary> Начальная дата фильтра журнала. </summary>
    public DateTime DateFrom { get; set; }

    /// <summary> Конечная дата фильтра журнала. </summary>
    public DateTime DateTo { get; set; }

    /// <summary> Идентификатор выбранного автомобиля, если фильтр задан. </summary>
    public Guid? SelectedVehicleId { get; set; }

    /// <summary> Выбранный тип записей журнала. </summary>
    public string SelectedKind { get; set; } = string.Empty;

    /// <summary> Выбранная вкладка журнала. </summary>
    public string SelectedTab { get; set; } = "works";

    /// <summary> Количество записей текущих работ. </summary>
    public int WorkItemsCount { get; set; }

    /// <summary> Количество записей истории. </summary>
    public int HistoryItemsCount { get; set; }

    /// <summary> Количество событий обслуживания. </summary>
    public int MaintenanceEventsCount { get; set; }

    /// <summary> Количество созданных дефектов. </summary>
    public int DefectsCreatedCount { get; set; }

    /// <summary> Количество закрытых дефектов. </summary>
    public int DefectsClosedCount { get; set; }

    /// <summary> Количество завершённых обслуживаний. </summary>
    public int CompletedMaintenancesCount { get; set; }

    /// <summary> Количество обслуженных клиентов. </summary>
    public int ServedClientsCount { get; set; }

    /// <summary> Информационное сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке, если она возникла. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Элемент текущих работ в журнале организации. </summary>
public class OrganizationWorkJournalWorkItemViewModel
{
    /// <summary> Время события. </summary>
    public DateTime OccurredAt { get; set; }

    /// <summary> Код типа записи журнала. </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary> Отображаемое название типа записи. </summary>
    public string KindName { get; set; } = string.Empty;

    /// <summary> CSS-класс значка типа записи. </summary>
    public string BadgeCssClass { get; set; } = "text-bg-light";

    /// <summary> Заголовок записи. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Отображаемое название автомобиля. </summary>
    public string VehicleDisplayName { get; set; } = string.Empty;

    /// <summary> Дополнительная подпись автомобиля, если есть. </summary>
    public string? VehicleSubtitle { get; set; }

    /// <summary> Идентификатор клиента, если связан. </summary>
    public Guid? ClientId { get; set; }

    /// <summary> Имя клиента, если связан. </summary>
    public string? ClientName { get; set; }

    /// <summary> Текст результата работы, если есть. </summary>
    public string? ResultText { get; set; }

    /// <summary> Дополнительный текст записи, если есть. </summary>
    public string? MetaText { get; set; }

    /// <summary> Стоимость работы, если указана. </summary>
    public decimal? Price { get; set; }

    /// <summary> Валюта сохранённого обслуживания. </summary>
    public string CurrencyCode { get; set; } = "RUB";

    /// <summary> Ссылка на детали записи, если есть. </summary>
    public string? DetailUrl { get; set; }

    /// <summary> Ссылка на страницу автомобиля, если есть. </summary>
    public string? VehicleUrl { get; set; }
}

/// <summary> Элемент истории в журнале организации. </summary>
public class OrganizationWorkJournalItemViewModel
{
    /// <summary> Время события. </summary>
    public DateTime OccurredAt { get; set; }

    /// <summary> Код типа записи истории. </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary> Отображаемое название типа записи. </summary>
    public string KindName { get; set; } = string.Empty;

    /// <summary> CSS-класс значка для элемента журнала работ. </summary>
    public string BadgeCssClass { get; set; } = "text-bg-light";

    /// <summary> Заголовок элемента журнала работ. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Описание элемента журнала работ. </summary>
    public string? Description { get; set; }

    /// <summary> Отображаемое имя автомобиля. </summary>
    public string VehicleDisplayName { get; set; } = string.Empty;

    /// <summary> Дополнительная подпись автомобиля. </summary>
    public string? VehicleSubtitle { get; set; }

    /// <summary> Имя пользователя, выполнившего действие. </summary>
    public string? ActorDisplayName { get; set; }

    /// <summary> Дополнительный текст с метаданными записи. </summary>
    public string? MetaText { get; set; }

    /// <summary> Ссылка на страницу деталей записи. </summary>
    public string? DetailUrl { get; set; }

    /// <summary> Ссылка на страницу автомобиля. </summary>
    public string? VehicleUrl { get; set; }
}

/// <summary> Модель страницы автомобилей организации. </summary>
public class OrganizationVehiclesViewModel
{
    /// <summary> Данные организации. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Права доступа к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Модель бокового меню рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Признак доступности модуля автомобилей. </summary>
    public bool HasVehiclesModule { get; set; }

    /// <summary> Количество активных автомобилей. </summary>
    public int ActiveVehiclesCount { get; set; }

    /// <summary> Количество архивных автомобилей. </summary>
    public int ArchivedVehiclesCount { get; set; }

    /// <summary> Сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Модель недавней записи обслуживания организации. </summary>
public class OrganizationRecentMaintenanceViewModel
{
    /// <summary> Идентификатор записи обслуживания. </summary>
    public Guid Id { get; set; }

    /// <summary> Версия записи. </summary>
    public uint Version { get; set; }

    /// <summary> Статус обслуживания. </summary>
    public MaintenanceStatus Status { get; set; }

    /// <summary> Признак возможности переноса обслуживания. </summary>
    public bool CanReschedule { get; set; }

    /// <summary> Идентификатор автомобиля. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Отображаемое имя автомобиля. </summary>
    public string VehicleDisplayName { get; set; } = string.Empty;

    /// <summary> Дополнительная подпись автомобиля. </summary>
    public string? VehicleSubtitle { get; set; }

    /// <summary> Государственный номер транспортного средства. </summary>
    public string? RegistrationPlate { get; set; }

    /// <summary> Ключ логотипа справочной марки. </summary>
    public string? BrandLogoKey { get; set; }

    /// <summary> Названия услуг в порядке строк заказа. </summary>
    public IReadOnlyList<string> ServiceNames { get; set; } = [];

    /// <summary> Имена назначенных исполнителей услуг. </summary>
    public IReadOnlyList<string> EmployeeNames { get; set; } = [];

    /// <summary> Дата готовности к выдаче. </summary>
    public DateTime? ReadyForHandoverAt { get; set; }

    /// <summary> Дата выдачи транспортного средства. </summary>
    public DateTime? HandedOverAt { get; set; }

    /// <summary> Название типа обслуживания. </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary> Название статуса обслуживания. </summary>
    public string StatusName { get; set; } = string.Empty;

    /// <summary> CSS-класс значка статуса. </summary>
    public string StatusBadgeClass { get; set; } = "text-bg-light";

    /// <summary> Отображаемая дата обслуживания. </summary>
    public string DateLabel { get; set; } = "Плановая дата";

    /// <summary> Дата обслуживания. </summary>
    public DateTime Date { get; set; }

    /// <summary> Плановое время начала. </summary>
    public TimeSpan? PlannedStartTime { get; set; }

    /// <summary> Плановое время окончания. </summary>
    public TimeSpan? PlannedEndTime { get; set; }

    /// <summary>Плановая дата окончания записи.</summary>
    public DateTime? PlannedEndDate { get; set; }

    /// <summary> Место проведения обслуживания. </summary>
    public string? Location { get; set; }

    /// <summary> Пробег автомобиля. </summary>
    public int? Mileage { get; set; }

    /// <summary> Отображаемый пробег. </summary>
    public string MileageLabel { get; set; } = string.Empty;

    /// <summary> Комментарий к обслуживанию. </summary>
    public string? Comment { get; set; }

    /// <summary> Стоимость обслуживания. </summary>
    public decimal? Price { get; set; }

    /// <summary> Валюта сохранённого обслуживания. </summary>
    public string CurrencyCode { get; set; } = "RUB";

    /// <summary> Ссылка на страницу обслуживания. </summary>
    public string DetailUrl { get; set; } = string.Empty;
}

/// <summary> Модель страницы клиентов организации. </summary>
public class OrganizationClientsViewModel
{
    /// <summary> Данные организации. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Права доступа к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Модель бокового меню рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Список клиентов. </summary>
    public IReadOnlyList<OrganizationClientDto> Clients { get; set; } = [];

    /// <summary> Доступные теги клиентов. </summary>
    public IReadOnlyList<OrganizationClientTagDto> AvailableTags { get; set; } = [];

    /// <summary> Доступные сегменты клиентов. </summary>
    public IReadOnlyList<OrganizationClientSegmentDto> Segments { get; set; } = [];

    /// <summary> Поисковый запрос. </summary>
    public string SearchQuery { get; set; } = string.Empty;

    /// <summary> Выбранный вариант сортировки. </summary>
    public string SelectedSort { get; set; } = "name_asc";

    /// <summary> Номер страницы списка клиентов. </summary>
    public int ClientsPage { get; set; } = 1;

    /// <summary> Размер страницы списка клиентов. </summary>
    public int ClientsPageSize { get; set; } = 10;

    /// <summary> Общее количество клиентов по фильтру. </summary>
    public int ClientsTotalCount { get; set; }

    /// <summary> Общее количество клиентов. </summary>
    public int AllClientsCount { get; set; }

    /// <summary> Общее количество автомобилей. </summary>
    public int TotalVehiclesCount { get; set; }

    /// <summary> Общее количество записей обслуживания. </summary>
    public int TotalMaintenanceCount { get; set; }

    /// <summary> Разрешение на создание клиентов. </summary>
    public bool CanCreateClients { get; set; }

    /// <summary> Разрешение на редактирование клиентов. </summary>
    public bool CanEditClients { get; set; }

    /// <summary> Разрешение на архивирование клиентов. </summary>
    public bool CanArchiveClients { get; set; }

    /// <summary> Сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Модель страницы деталей клиента. </summary>
public class OrganizationClientDetailsViewModel
{
    /// <summary> Данные организации. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Права доступа к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Модель бокового меню рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Данные клиента. </summary>
    public required OrganizationClientDto Client { get; set; }

    /// <summary> Недавние обслуживания клиента. </summary>
    public IReadOnlyList<OrganizationClientMaintenanceItemDto> RecentMaintenances { get; set; } = [];

    /// <summary> Дефекты клиента. </summary>
    public IReadOnlyList<OrganizationClientDefectItemDto> Defects { get; set; } = [];

    /// <summary> События истории клиента. </summary>
    public IReadOnlyList<OrganizationClientHistoryEventDto> HistoryItems { get; set; } = [];

    /// <summary> Номер страницы истории. </summary>
    public int HistoryPage { get; set; } = 1;

    /// <summary> Размер страницы истории. </summary>
    public int HistoryPageSize { get; set; } = 5;

    /// <summary> Общее количество событий истории по фильтру. </summary>
    public int HistoryTotalCount { get; set; }

    /// <summary> Общее количество событий истории. </summary>
    public int HistoryAllCount { get; set; }

    /// <summary> Поисковый запрос по истории. </summary>
    public string HistorySearchQuery { get; set; } = string.Empty;

    /// <summary> Вариант сортировки истории. </summary>
    public string HistorySort { get; set; } = "created_desc";

    /// <summary> Варианты клиентов для переноса. </summary>
    public IReadOnlyList<OrganizationClientOptionDto> TransferClientOptions { get; set; } = [];

    /// <summary> Количество автомобилей клиента. </summary>
    public int VehiclesCount { get; set; }

    /// <summary> Количество записей обслуживания клиента. </summary>
    public int MaintenanceCount { get; set; }

    /// <summary> Количество открытых дефектов. </summary>
    public int OpenDefectsCount { get; set; }

    /// <summary> Количество закрытых дефектов. </summary>
    public int ClosedDefectsCount { get; set; }

    /// <summary> Ссылка на список клиентов. </summary>
    public string ClientsListUrl { get; set; } = string.Empty;

    /// <summary> Ссылка на создание автомобиля. </summary>
    public string CreateVehicleUrl { get; set; } = string.Empty;

    /// <summary> Ссылка на создание обслуживания. </summary>
    public string CreateMaintenanceUrl { get; set; } = string.Empty;

    /// <summary> Ссылка на список дефектов. </summary>
    public string DefectsListUrl { get; set; } = string.Empty;

    /// <summary> Ссылка для возврата. </summary>
    public string ReturnUrl { get; set; } = string.Empty;

    /// <summary> Разрешение на редактирование клиентов. </summary>
    public bool CanEditClients { get; set; }

    /// <summary> Разрешение на управление автомобилями клиента. </summary>
    public bool CanManageClientVehicles { get; set; }

    /// <summary> Разрешение на создание автомобилей. </summary>
    public bool CanCreateVehicles { get; set; }

    /// <summary> Разрешение на создание обслуживания. </summary>
    public bool CanCreateMaintenance { get; set; }

    /// <summary> Разрешение на создание дефектов. </summary>
    public bool CanCreateDefects { get; set; }

    /// <summary> Сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Модель формы создания или редактирования клиента. </summary>
public class OrganizationClientFormViewModel
{
    /// <summary> Данные организации. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Права доступа к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Модель бокового меню рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Данные формы создания клиента. </summary>
    public CreateOrganizationClientInputModel CreateClient { get; set; } = new();

    /// <summary> Данные формы редактирования клиента. </summary>
    public UpdateOrganizationClientInputModel EditClient { get; set; } = new();

    /// <summary> Идентификатор клиента. </summary>
    public Guid? ClientId { get; set; }

    /// <summary> Признак режима редактирования. </summary>
    public bool IsEditMode { get; set; }

    /// <summary> Адрес отправки формы. </summary>
    public string FormActionUrl { get; set; } = string.Empty;

    /// <summary> Ссылка отмены. </summary>
    public string CancelUrl { get; set; } = string.Empty;

    /// <summary> Заголовок формы. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Текст кнопки отправки формы. </summary>
    public string SubmitText { get; set; } = string.Empty;

    /// <summary> Сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Модель ввода записи истории клиента. </summary>
public class CreateOrganizationClientHistoryEntryInputModel
{
    /// <summary> Текст записи истории. </summary>
    [Required(ErrorMessage = "Укажите текст записи.")]
    [MaxLength(1000)]
    public string Text { get; set; } = string.Empty;

    /// <summary> Дата события. </summary>
    public DateTime? OccurredAt { get; set; }
}

/// <summary> Модель страницы дефектов организации. </summary>
public class OrganizationDefectsViewModel
{
    /// <summary> Данные организации. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Права доступа к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Модель бокового меню рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Список дефектов. </summary>
    public IReadOnlyList<VehicleDefectDto> Defects { get; set; } = [];

    /// <summary> Варианты выбора клиента. </summary>
    public IReadOnlyList<OrganizationClientOptionDto> ClientOptions { get; set; } = [];

    /// <summary> Варианты выбора автомобиля. </summary>
    public IReadOnlyList<OrganizationVehicleOptionViewModel> VehicleOptions { get; set; } = [];

    /// <summary> Данные формы создания дефекта. </summary>
    public CreateVehicleDefectInputModel CreateDefect { get; set; } = new();

    /// <summary> Данные формы редактирования дефекта. </summary>
    public UpdateVehicleDefectInputModel EditDefect { get; set; } = new();

    /// <summary> Идентификатор редактируемого дефекта. </summary>
    public Guid? EditDefectId { get; set; }

    /// <summary> Выбранный клиент. </summary>
    public Guid? SelectedClientId { get; set; }

    /// <summary> Выбранный автомобиль. </summary>
    public Guid? SelectedVehicleId { get; set; }

    /// <summary> Автомобиль для возврата к списку. </summary>
    public Guid? ReturnVehicleId { get; set; }

    /// <summary> Выбранный код статуса дефекта. </summary>
    public string SelectedStatusCode { get; set; } = string.Empty;

    /// <summary> Выбранный вариант сортировки. </summary>
    public string SelectedSort { get; set; } = "created_desc";

    /// <summary> Номер страницы списка дефектов. </summary>
    public int DefectsPage { get; set; } = 1;

    /// <summary> Размер страницы списка дефектов. </summary>
    public int DefectsPageSize { get; set; } = 12;

    /// <summary> Общее количество дефектов по фильтру. </summary>
    public int DefectsTotalCount { get; set; }

    /// <summary> Общее количество дефектов. </summary>
    public int AllDefectsCount { get; set; }

    /// <summary> Признак наличия ещё дефектов для загрузки. </summary>
    public bool HasMoreDefects { get; set; }

    /// <summary> Признак открытия формы создания при загрузке страницы. </summary>
    public bool OpenCreateModalOnLoad { get; set; }

    /// <summary> Ссылка для возврата. </summary>
    public string? ReturnUrl { get; set; }

    /// <summary> Поисковый запрос. </summary>
    public string SearchQuery { get; set; } = string.Empty;

    /// <summary> Количество открытых дефектов. </summary>
    public int OpenDefectsCount { get; set; }

    /// <summary> Количество решённых дефектов. </summary>
    public int ResolvedDefectsCount { get; set; }

    /// <summary> Разрешение на создание дефектов. </summary>
    public bool CanCreateDefects { get; set; }

    /// <summary> Разрешение на редактирование дефектов. </summary>
    public bool CanEditDefects { get; set; }

    /// <summary> Разрешение на удаление дефектов. </summary>
    public bool CanDeleteDefects { get; set; }

    /// <summary> Разрешение на управление статусами дефектов. </summary>
    public bool CanManageDefectStatuses { get; set; }

    /// <summary> Разрешение на связывание дефекта с обслуживанием. </summary>
    public bool CanLinkDefectMaintenance { get; set; }

    /// <summary> Разрешение на создание обслуживания. </summary>
    public bool CanCreateMaintenance { get; set; }

    /// <summary> Сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Модель страницы деталей дефекта в организации. </summary>
public class OrganizationDefectDetailsViewModel
{
    /// <summary> Данные организации для страницы. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Сведения о доступах пользователя к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Модель боковой панели рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Данные просматриваемого дефекта. </summary>
    public required VehicleDefectDto Defect { get; set; }

    /// <summary> История изменений дефекта. </summary>
    public IReadOnlyList<VehicleDefectHistoryItemDto> HistoryItems { get; set; } = [];

    /// <summary> Доступные варианты обслуживаний для связи с дефектом. </summary>
    public IReadOnlyList<VehicleDefectMaintenanceOptionDto> MaintenanceOptions { get; set; } = [];

    /// <summary> Адрес страницы создания обслуживания. </summary>
    public string CreateMaintenanceUrl { get; set; } = string.Empty;

    /// <summary> Адрес страницы автомобиля. </summary>
    public string VehicleUrl { get; set; } = string.Empty;

    /// <summary> Адрес списка дефектов. </summary>
    public string DefectsListUrl { get; set; } = string.Empty;

    /// <summary> Признак права управления дефектами. </summary>
    public bool CanManageDefects { get; set; }

    /// <summary> Признак права редактирования дефектов. </summary>
    public bool CanEditDefects { get; set; }

    /// <summary> Признак права удаления дефектов. </summary>
    public bool CanDeleteDefects { get; set; }

    /// <summary> Признак права управления статусами дефектов. </summary>
    public bool CanManageDefectStatuses { get; set; }

    /// <summary> Признак права связывания дефекта с обслуживанием. </summary>
    public bool CanLinkDefectMaintenance { get; set; }

    /// <summary> Признак права создания обслуживания. </summary>
    public bool CanCreateMaintenance { get; set; }

    /// <summary> Сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Модель страницы счётчиков автомобилей организации. </summary>
public class OrganizationCountersViewModel
{
    /// <summary> Данные организации для страницы. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Сведения о доступах пользователя к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Модель боковой панели рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Список клиентов для выбора. </summary>
    public IReadOnlyList<OrganizationClientOptionDto> ClientOptions { get; set; } = [];

    /// <summary> Автомобили со счётчиками на текущей странице. </summary>
    public IReadOnlyList<VehicleCounterVehicleDto> Vehicles { get; set; } = [];

    /// <summary> Записи истории показаний счётчиков. </summary>
    public IReadOnlyList<VehicleCounterEntryDto> History { get; set; } = [];

    /// <summary> Модель ввода для обновления счётчика. </summary>
    public UpdateVehicleCounterInputModel UpdateCounter { get; set; } = new();

    /// <summary> Идентификатор выбранного автомобиля. </summary>
    public Guid? SelectedVehicleId { get; set; }

    /// <summary> Идентификатор выбранного клиента. </summary>
    public Guid? SelectedClientId { get; set; }

    /// <summary> Поисковый запрос по автомобилям. </summary>
    public string SearchQuery { get; set; } = string.Empty;

    /// <summary> Текущая сортировка списка автомобилей. </summary>
    public string SelectedVehiclesSort { get; set; } = "name_asc";

    /// <summary> Текущая сортировка истории показаний. </summary>
    public string SelectedHistorySort { get; set; } = "recorded_desc";

    /// <summary> Номер страницы списка автомобилей. </summary>
    public int VehiclesPage { get; set; } = 1;

    /// <summary> Размер страницы списка автомобилей. </summary>
    public int VehiclesPageSize { get; set; } = 10;

    /// <summary> Общее число автомобилей с счётчиками. </summary>
    public int VehiclesTotalCount { get; set; }

    /// <summary> Признак наличия ещё автомобилей для показа. </summary>
    public bool HasMoreVehicles { get; set; }

    /// <summary> Номер страницы истории показаний. </summary>
    public int HistoryPage { get; set; } = 1;

    /// <summary> Размер страницы истории показаний. </summary>
    public int HistoryPageSize { get; set; } = 10;

    /// <summary> Общее число записей истории с учётом фильтра. </summary>
    public int HistoryTotalCount { get; set; }

    /// <summary> Общее число записей истории без фильтра. </summary>
    public int HistoryAllCount { get; set; }

    /// <summary> Признак наличия ещё записей истории. </summary>
    public bool HasMoreHistory { get; set; }

    /// <summary> Число автомобилей со счётчиками. </summary>
    public int VehiclesWithCountersCount { get; set; }

    /// <summary> Признак права управления счётчиками. </summary>
    public bool CanManageCounters { get; set; }

    /// <summary> Сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Элемент списка для выбора автомобиля организации. </summary>
public class OrganizationVehicleOptionViewModel
{
    /// <summary> Идентификатор автомобиля. </summary>
    public Guid Id { get; set; }

    /// <summary> Идентификатор связи клиента с организацией, если есть. </summary>
    public Guid? OrganizationClientId { get; set; }

    /// <summary> Отображаемое название автомобиля. </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary> Дополнительная подпись к названию автомобиля. </summary>
    public string? Subtitle { get; set; }
}

/// <summary> Модель страницы участников организации. </summary>
public class OrganizationMembersViewModel
{
    /// <summary> Данные организации для страницы. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Сведения о доступах пользователя к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Модель боковой панели рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Модель ввода приглашения участника. </summary>
    public InviteOrganizationMemberInputModel InviteMember { get; set; } = new();

    /// <summary> Матрица прав по ролям организации. </summary>
    public IReadOnlyList<OrganizationRolePermissionMatrixDto> RolePermissionMatrix { get; set; } = [];

    /// <summary> Сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// ViewModel страницы истории изменений организации.
/// </summary>
public class OrganizationActivityViewModel
{
    /// <summary> Данные организации для страницы. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Сведения о доступах пользователя к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Модель боковой панели рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> События журнала активности организации. </summary>
    public IReadOnlyList<OrganizationAuditEventDto> Events { get; set; } = [];

    /// <summary> Сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// ViewModel страницы настроек организации: профиль, тариф и реквизиты.
/// </summary>
public class OrganizationSettingsViewModel
{
    /// <summary> Данные организации для страницы. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Сведения о доступах пользователя к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Модель боковой панели рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Текущая подписка организации. </summary>
    public OrganizationSubscriptionDto? CurrentSubscription { get; set; }

    /// <summary> Действующий тарифный план организации. </summary>
    public SubscriptionPlanDto? EffectivePlan { get; set; }

    /// <summary> Список функций рабочего пространства с их значениями. </summary>
    public IReadOnlyList<FeatureValueDto> WorkspaceFeatures { get; set; } = [];

    /// <summary> Лимит числа автомобилей рабочего пространства. </summary>
    public int WorkspaceVehicleLimit { get; set; }

    /// <summary> Лимит числа участников рабочего пространства. </summary>
    public int WorkspaceMembersLimit { get; set; }

    /// <summary> Признак доступности модуля автомобилей. </summary>
    public bool HasVehiclesModule { get; set; }

    /// <summary> Признак доступности модуля клиентов. </summary>
    public bool HasClientsModule { get; set; }

    /// <summary> Признак доступности модуля дефектов. </summary>
    public bool HasDefectsModule { get; set; }

    /// <summary> Признак доступности модуля счётчиков. </summary>
    public bool HasCountersModule { get; set; }

    /// <summary> Признак доступности модуля планировщика. </summary>
    public bool HasSchedulerModule { get; set; }

    /// <summary> Доступные бизнес-тарифные планы. </summary>
    public IReadOnlyList<SubscriptionPlanDto> AvailableBusinessPlans { get; set; } = [];

    /// <summary> Признак доступности тестовых подписок. </summary>
    public bool CanUseTestSubscriptions { get; set; }

    /// <summary> Признак доступности демо-режима бизнес-подписки. </summary>
    public bool DemoBusinessAvailable { get; set; }

    /// <summary> Пояснение о доступности демо-режима. </summary>
    public string? DemoBusinessAvailabilityMessage { get; set; }

    /// <summary> Признак возможности продления бизнес-подписки. </summary>
    public bool CanExtendBusinessSubscription { get; set; }

    /// <summary> Число дней до окончания бизнес-подписки. </summary>
    public int BusinessDaysUntilExpiry { get; set; }

    /// <summary> Сообщение о продлении бизнес-подписки. </summary>
    public string? BusinessExtensionMessage { get; set; }

    /// <summary> Доступные типы организаций для выбора. </summary>
    public IReadOnlyList<OrganizationTypeOptionDto> OrganizationTypeOptions { get; set; } = [];

    /// <summary> Модель ввода настроек организации. </summary>
    public UpdateOrganizationInputModel Settings { get; set; } = new();

    /// <summary> Справочник сценариев организации. </summary>
    public OrganizationScenarioCatalogDto? ScenarioCatalog { get; set; }

    /// <summary> Модель ввода нового элемента справочника сценариев. </summary>
    public CreateOrganizationScenarioCatalogItemInputModel ScenarioCatalogItem { get; set; } = new();

    /// <summary> Сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// ViewModel отдельной страницы справочников сценария организации.
/// </summary>
public class OrganizationScenarioCatalogViewModel
{
    /// <summary> Данные организации для страницы. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Сведения о доступах пользователя к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Модель боковой панели рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Справочник сценариев организации. </summary>
    public OrganizationScenarioCatalogDto? ScenarioCatalog { get; set; }

    /// <summary> Модель ввода нового элемента справочника сценариев. </summary>
    public CreateOrganizationScenarioCatalogItemInputModel ScenarioCatalogItem { get; set; } = new();

    /// <summary> Признак права управления справочником сценариев. </summary>
    public bool CanManageScenarioCatalog { get; set; }

    /// <summary> Сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// ViewModel отдельной страницы реквизитов и настроек оформления документов организации.
/// </summary>
public class OrganizationRequisitesViewModel
{
    /// <summary> Данные организации для страницы. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Сведения о доступах пользователя к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Модель боковой панели рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Модель ввода реквизитов организации. </summary>
    public UpdateOrganizationRequisitesInputModel Settings { get; set; } = new();

    /// <summary> Признак права управления реквизитами. </summary>
    public bool CanManageRequisites { get; set; }

    /// <summary> Сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// ViewModel отдельной страницы склада материалов организации.
/// </summary>
public class OrganizationMaterialsViewModel
{
    /// <summary> Данные организации для страницы. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Сведения о доступах пользователя к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Модель боковой панели рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Справочник сценариев организации. </summary>
    public OrganizationScenarioCatalogDto? ScenarioCatalog { get; set; }

    /// <summary> Данные складских запасов материалов. </summary>
    public OrganizationMaterialInventoryDto Inventory { get; set; } = new();

    /// <summary> Модель ввода поступления материалов. </summary>
    public CreateOrganizationMaterialReceiptInputModel MaterialReceipt { get; set; } = new();

    /// <summary> Модель ввода выдачи материалов. </summary>
    public CreateOrganizationMaterialIssueInputModel MaterialIssue { get; set; } = new();

    /// <summary> Модель ввода корректировки остатка материала. </summary>
    public AdjustOrganizationMaterialStockInputModel StockAdjustment { get; set; } = new();

    /// <summary> Поисковый запрос по материалам. </summary>
    public string SearchQuery { get; set; } = string.Empty;

    /// <summary> Фильтр по остаткам материалов. </summary>
    public string StockFilter { get; set; } = string.Empty;

    /// <summary> Фильтр по типу движения материалов. </summary>
    public string MovementType { get; set; } = string.Empty;

    /// <summary> Идентификатор позиции склада для фильтра. </summary>
    public Guid? StockId { get; set; }

    /// <summary> Номер страницы списка остатков. </summary>
    public int StocksPage { get; set; } = 1;

    /// <summary> Размер страницы списка остатков. </summary>
    public int StocksPageSize { get; set; } = 20;

    /// <summary> Номер страницы списка движений материалов. </summary>
    public int MovementsPage { get; set; } = 1;

    /// <summary> Размер страницы списка движений. </summary>
    public int MovementsPageSize { get; set; } = 20;

    /// <summary> Признак права управления материалами. </summary>
    public bool CanManageMaterials { get; set; }

    /// <summary> Сообщение о результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Модель ввода создания элемента справочника сценариев организации. </summary>
public class CreateOrganizationScenarioCatalogItemInputModel
{
    /// <summary> Код вида элемента справочника. </summary>
    [Required]
    public string KindCode { get; set; } = string.Empty;

    /// <summary> Название элемента справочника. </summary>
    [Required(ErrorMessage = "Укажите название.")]
    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание элемента справочника. </summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary> Порядок сортировки элемента. </summary>
    public int SortOrder { get; set; }
}

/// <summary> Модель ввода изменения элемента справочника сценариев. </summary>
public class UpdateOrganizationScenarioCatalogItemInputModel
{
    /// <summary> Идентификатор изменяемого элемента справочника. </summary>
    [Required]
    public Guid ItemId { get; set; }

    /// <summary> Новое название элемента. </summary>
    [Required(ErrorMessage = "Укажите название.")]
    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary> Новое описание элемента. </summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary> Новый порядок сортировки элемента. </summary>
    public int SortOrder { get; set; }

    /// <summary> Признак активности элемента справочника. </summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// MVC input-модель формы прихода материала на склад организации.
/// </summary>
public class CreateOrganizationMaterialReceiptInputModel
{
    /// <summary>Материал из scenario-catalog, если пользователь выбрал справочник.</summary>
    public Guid? CatalogItemId { get; set; }

    /// <summary>Ручное название материала, если его еще нет в справочнике.</summary>
    [MaxLength(160)]
    public string? Name { get; set; }

    /// <summary>Единица измерения прихода.</summary>
    [MaxLength(32)]
    public string? Unit { get; set; }

    /// <summary>Количество прихода.</summary>
    [Range(0.001, 999999999)]
    public decimal Quantity { get; set; }

    /// <summary>Комментарий к приходу.</summary>
    [MaxLength(1000)]
    public string? Comment { get; set; }
}

/// <summary>
/// MVC input-модель ручной корректировки складского остатка.
/// </summary>
public class AdjustOrganizationMaterialStockInputModel
{
    /// <summary>Новый фактический остаток материала.</summary>
    [Range(0, 999999999)]
    public decimal QuantityOnHand { get; set; }

    /// <summary>Минимальный остаток для контроля дефицита.</summary>
    [Range(0, 999999999)]
    public decimal? MinQuantity { get; set; }

    /// <summary>Причина или пояснение корректировки.</summary>
    [MaxLength(1000)]
    public string? Comment { get; set; }
}

/// <summary>
/// MVC input-модель ручного списания материала со склада.
/// </summary>
public class CreateOrganizationMaterialIssueInputModel
{
    /// <summary>Складская позиция, с которой списывается материал.</summary>
    [Required]
    public Guid StockId { get; set; }

    /// <summary>Количество списания.</summary>
    [Range(0.001, 999999999)]
    public decimal Quantity { get; set; }

    /// <summary>Причина или комментарий к списанию.</summary>
    [MaxLength(1000)]
    public string? Comment { get; set; }
}

/// <summary> Модель страницы планировщика организации. </summary>
public class OrganizationSchedulerViewModel
{
    /// <summary> Данные организации для страницы. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Сведения о доступах пользователя к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Модель боковой панели рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Элементы планировщика. </summary>
    public IReadOnlyList<OrganizationSchedulerItemViewModel> Items { get; set; } = [];

    /// <summary> Список автомобилей для выбора. </summary>
    public IReadOnlyList<OrganizationVehicleOptionViewModel> VehicleOptions { get; set; } = [];

    /// <summary> Список клиентов для выбора при планировании задач. </summary>
    public IReadOnlyList<OrganizationClientOptionDto> ClientOptions { get; set; } = [];

    /// <summary> Список ответственных пользователей для планировщика. </summary>
    public IReadOnlyList<OrganizationSchedulerMemberOptionDto> ResponsibleOptions { get; set; } = [];

    /// <summary> Список доступных типов задач планировщика. </summary>
    public IReadOnlyList<OrganizationSchedulerOptionDto> TaskTypeOptions { get; set; } = [];

    /// <summary> Список доступных приоритетов планировщика. </summary>
    public IReadOnlyList<OrganizationSchedulerOptionDto> PriorityOptions { get; set; } = [];

    /// <summary> Список дефектов для связывания с задачей. </summary>
    public IReadOnlyList<OrganizationSchedulerLinkedItemOptionDto> LinkedDefectOptions { get; set; } = [];

    /// <summary> Список обслуживаний для связывания с задачей. </summary>
    public IReadOnlyList<OrganizationSchedulerLinkedItemOptionDto> LinkedMaintenanceOptions { get; set; } = [];

    /// <summary> Напоминания, наступившие к текущему моменту. </summary>
    public IReadOnlyList<OrganizationSchedulerReminderViewModel> DueReminders { get; set; } = [];

    /// <summary> Идентификатор задачи, открытой для редактирования. </summary>
    public Guid? EditTaskId { get; set; }

    /// <summary> Задача, открытая для редактирования. </summary>
    public OrganizationSchedulerItemViewModel? EditTask { get; set; }

    /// <summary> Идентификатор выбранного автомобиля в фильтре. </summary>
    public Guid? SelectedVehicleId { get; set; }

    /// <summary> Идентификатор выбранного клиента в фильтре. </summary>
    public Guid? SelectedClientId { get; set; }

    /// <summary> Выбранный статус в фильтре планировщика. </summary>
    public string SelectedStatus { get; set; } = string.Empty;

    /// <summary> Выбранный источник записей в фильтре. </summary>
    public string SelectedSource { get; set; } = string.Empty;

    /// <summary> Выбранный процесс в фильтре. </summary>
    public string SelectedWorkflow { get; set; } = string.Empty;

    /// <summary> Выбранный ответственный пользователь в фильтре. </summary>
    public string SelectedResponsibleUserId { get; set; } = string.Empty;

    /// <summary> Выбранный тип задачи в фильтре. </summary>
    public string SelectedTaskType { get; set; } = string.Empty;

    /// <summary> Выбранный приоритет в фильтре. </summary>
    public string SelectedPriority { get; set; } = string.Empty;

    /// <summary> Поисковый запрос по записям планировщика. </summary>
    public string SearchQuery { get; set; } = string.Empty;

    /// <summary> Общее количество записей планировщика. </summary>
    public int TotalItemsCount { get; set; }

    /// <summary> Количество записей в разделе «Внимание» планировщика. </summary>
    public int FocusItemsCount { get; set; }

    /// <summary> Количество просроченных записей. </summary>
    public int OverdueItemsCount { get; set; }

    /// <summary> Количество записей с приближающимися сроками. </summary>
    public int SoonItemsCount { get; set; }

    /// <summary> Количество записей, требующих внимания. </summary>
    public int AttentionItemsCount { get; set; }

    /// <summary> Количество записей с календарным сроком. </summary>
    public int DateItemsCount { get; set; }

    /// <summary> Количество записей со сроком по пробегу. </summary>
    public int MileageItemsCount { get; set; }

    /// <summary> Количество записей, связанных с дефектами. </summary>
    public int DefectItemsCount { get; set; }

    /// <summary> Количество записей, связанных со счётчиками. </summary>
    public int CounterItemsCount { get; set; }

    /// <summary> Количество записей из настроек справочников. </summary>
    public int SetupItemsCount { get; set; }

    /// <summary> Количество записей, созданных вручную. </summary>
    public int ManualTaskItemsCount { get; set; }

    /// <summary> Количество процессов в работе. </summary>
    public int InProgressWorkflowCount { get; set; }

    /// <summary> Количество отложенных процессов. </summary>
    public int DeferredWorkflowCount { get; set; }

    /// <summary> Количество закрытых процессов. </summary>
    public int ClosedWorkflowCount { get; set; }

    /// <summary> Количество записей о запланированных обслуживаниях. </summary>
    public int PlannedMaintenanceItemsCount { get; set; }

    /// <summary> Количество записей об открытых дефектах. </summary>
    public int OpenDefectItemsCount { get; set; }

    /// <summary> Количество записей, находящихся в работе. </summary>
    public int WorkInProgressItemsCount { get; set; }

    /// <summary> Количество завершённых записей. </summary>
    public int CompletedItemsCount { get; set; }

    /// <summary> Список просроченных записей планировщика. </summary>
    public IReadOnlyList<OrganizationSchedulerItemViewModel> OverdueItems { get; set; } = [];

    /// <summary> Список записей с приближающимися сроками. </summary>
    public IReadOnlyList<OrganizationSchedulerItemViewModel> SoonItems { get; set; } = [];

    /// <summary> Список записей, требующих внимания. </summary>
    public IReadOnlyList<OrganizationSchedulerItemViewModel> AttentionItems { get; set; } = [];

    /// <summary> Сообщение об успешном результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке операции. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Модель напоминания планировщика организации. </summary>
public class OrganizationSchedulerReminderViewModel
{
    /// <summary> Идентификатор задачи, к которой относится напоминание. </summary>
    public Guid TaskId { get; set; }

    /// <summary> Заголовок напоминания. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Комментарий к напоминанию. </summary>
    public string? Comment { get; set; }

    /// <summary> Время напоминания. </summary>
    public DateTime ReminderAt { get; set; }

    /// <summary> Время начала задачи. </summary>
    public DateTime? StartAt { get; set; }

    /// <summary> Отображаемое название автомобиля. </summary>
    public string? VehicleDisplayName { get; set; }

    /// <summary> Имя клиента, связанного с задачей. </summary>
    public string? ClientName { get; set; }

    /// <summary> Ссылка на страницу с деталями задачи. </summary>
    public string DetailUrl { get; set; } = string.Empty;
}

/// <summary> Модель записи планировщика организации. </summary>
public class OrganizationSchedulerItemViewModel
{
    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Идентификатор автомобиля. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Идентификатор связанной задачи, если есть. </summary>
    public Guid? TaskId { get; set; }

    /// <summary> Идентификатор клиента организации. </summary>
    public Guid? OrganizationClientId { get; set; }

    /// <summary> Имя клиента организации. </summary>
    public string? OrganizationClientName { get; set; }

    /// <summary> Идентификатор ответственного пользователя. </summary>
    public string? ResponsibleUserId { get; set; }

    /// <summary> Отображаемое имя ответственного пользователя. </summary>
    public string? ResponsibleDisplayName { get; set; }

    /// <summary> Идентификатор исходной сущности, породившей запись. </summary>
    public Guid? SourceEntityId { get; set; }

    /// <summary> Отображаемое название автомобиля. </summary>
    public string VehicleDisplayName { get; set; } = string.Empty;

    /// <summary> Дополнительная подпись к автомобилю. </summary>
    public string? VehicleSubtitle { get; set; }

    /// <summary> Заголовок записи планировщика. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Описание записи планировщика. </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary> Код статуса записи. </summary>
    public string StatusCode { get; set; } = string.Empty;

    /// <summary> Отображаемое имя статуса записи. </summary>
    public string StatusName { get; set; } = string.Empty;

    /// <summary> Код источника записи. </summary>
    public string SourceCode { get; set; } = string.Empty;

    /// <summary> Отображаемое имя источника записи. </summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary> Код типа задачи. </summary>
    public string TaskTypeCode { get; set; } = string.Empty;

    /// <summary> Отображаемое имя типа задачи. </summary>
    public string TaskTypeName { get; set; } = string.Empty;

    /// <summary> Код приоритета. </summary>
    public string PriorityCode { get; set; } = string.Empty;

    /// <summary> Отображаемое имя приоритета. </summary>
    public string PriorityName { get; set; } = string.Empty;

    /// <summary> Время начала записи. </summary>
    public DateTime? StartAt { get; set; }

    /// <summary> Время окончания записи. </summary>
    public DateTime? EndAt { get; set; }

    /// <summary> Идентификатор связанного дефекта. </summary>
    public Guid? LinkedDefectId { get; set; }

    /// <summary> Идентификатор обслуживания, связанного вручную. </summary>
    public Guid? ManualLinkedMaintenanceId { get; set; }

    /// <summary> Код правила повторения записи. </summary>
    public string RecurrenceCode { get; set; } = string.Empty;

    /// <summary> Отображаемое имя правила повторения. </summary>
    public string RecurrenceName { get; set; } = string.Empty;

    /// <summary> Интервал повторения записи. </summary>
    public int? RecurrenceInterval { get; set; }

    /// <summary> Дата, до которой действует повторение. </summary>
    public DateTime? RecurrenceUntil { get; set; }

    /// <summary> Время напоминания по записи. </summary>
    public DateTime? ReminderAt { get; set; }

    /// <summary> Время отправки напоминания. </summary>
    public DateTime? ReminderSentAt { get; set; }

    /// <summary> Срок выполнения записи. </summary>
    public DateTime? DueDate { get; set; }

    /// <summary> Целевой пробег для выполнения записи. </summary>
    public int? DueMileage { get; set; }

    /// <summary> Текущий пробег автомобиля. </summary>
    public int? CurrentMileage { get; set; }

    /// <summary> Подпись единицы измерения пробега. </summary>
    public string MileageUnitLabel { get; set; } = string.Empty;

    /// <summary> Дополнительный текст со сведениями о записи. </summary>
    public string? MetaText { get; set; }

    /// <summary> Идентификатор связанного обслуживания. </summary>
    public Guid? LinkedMaintenanceId { get; set; }

    /// <summary> Ключ условия, породившего запись. </summary>
    public string TriggerKey { get; set; } = string.Empty;

    /// <summary> Код процесса, связанного с записью. </summary>
    public string ProcessCode { get; set; } = string.Empty;

    /// <summary> Код статуса процесса. </summary>
    public string WorkflowStatusCode { get; set; } = OrganizationSchedulerTaskStatusCodes.New;

    /// <summary> Отображаемое имя статуса процесса. </summary>
    public string WorkflowStatusName { get; set; } = OrganizationSchedulerTaskStatusCodes.GetName(OrganizationSchedulerTaskStatusCodes.New);

    /// <summary> Код этапа процесса. </summary>
    public string WorkflowStageCode { get; set; } = string.Empty;

    /// <summary> Отображаемое имя этапа процесса. </summary>
    public string WorkflowStageName { get; set; } = string.Empty;

    /// <summary> Время, до которого процесс отложен. </summary>
    public DateTime? WorkflowDeferredUntil { get; set; }

    /// <summary> Признак возможности управления процессом текущим пользователем. </summary>
    public bool CanManageWorkflow { get; set; }

    /// <summary> Ссылка на страницу с деталями записи. </summary>
    public string DetailUrl { get; set; } = string.Empty;

    /// <summary> Текст действия перехода к деталям. </summary>
    public string DetailActionText { get; set; } = "Техника";

    /// <summary> Доступные действия по записи планировщика. </summary>
    public IReadOnlyList<OrganizationSchedulerActionViewModel> Actions { get; set; } = [];

    /// <summary> Ссылка основного действия записи. </summary>
    public string? PrimaryActionUrl { get; set; }

    /// <summary> Текст основного действия записи. </summary>
    public string? PrimaryActionText { get; set; }

    /// <summary> Признак приглушённого отображения основного действия. </summary>
    public bool IsPrimaryActionMuted { get; set; }

    /// <summary> Порядок сортировки записи. </summary>
    public int SortRank { get; set; }
}

/// <summary> Модель действия над записью планировщика. </summary>
public class OrganizationSchedulerActionViewModel
{
    /// <summary> Текст действия. </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary> Адрес, на который ведёт действие. </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary> CSS-класс кнопки действия. </summary>
    public string ButtonCssClass { get; set; } = "btn btn-outline-dark btn-sm";

    /// <summary> Признак того, что действие выполняется методом POST. </summary>
    public bool IsPost { get; set; }

    /// <summary> Код источника, к которому относится действие. </summary>
    public string? SourceCode { get; set; }

    /// <summary> Идентификатор автомобиля, связанного с действием. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Идентификатор исходной сущности действия. </summary>
    public Guid? SourceEntityId { get; set; }

    /// <summary> Ключ условия, связанного с действием. </summary>
    public string? TriggerKey { get; set; }

    /// <summary> Текст подтверждения перед выполнением действия. </summary>
    public string? ConfirmMessage { get; set; }
}

/// <summary> Модель страницы wiki-статей организации. </summary>
public class OrganizationWikiViewModel
{
    /// <summary> Данные организации. </summary>
    public required OrganizationDto Organization { get; set; }

    /// <summary> Данные о доступе пользователя к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; set; } = new();

    /// <summary> Данные боковой панели рабочего пространства. </summary>
    public OrganizationWorkspaceSidebarViewModel WorkspaceSidebar { get; set; } = new();

    /// <summary> Список wiki-статей. </summary>
    public IReadOnlyList<OrganizationWikiArticleViewModel> Articles { get; set; } = [];

    /// <summary> Выбранная wiki-статья. </summary>
    public OrganizationWikiArticleViewModel? SelectedArticle { get; set; }

    /// <summary> Поисковый запрос по статьям. </summary>
    public string SearchQuery { get; set; } = string.Empty;

    /// <summary> Признак наличия результатов поиска. </summary>
    public bool HasSearchResults { get; set; } = true;

    /// <summary> Сообщение об успешном результате операции. </summary>
    public string? StatusMessage { get; set; }

    /// <summary> Сообщение об ошибке операции. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Модель wiki-статьи организации. </summary>
public class OrganizationWikiArticleViewModel
{
    /// <summary> Символьный адрес статьи. </summary>
    public required string Slug { get; set; }

    /// <summary> Заголовок статьи. </summary>
    public required string Title { get; set; }

    /// <summary> Краткое содержание статьи. </summary>
    public required string Summary { get; set; }

    /// <summary> Ссылка на скриншот статьи. </summary>
    public string? ScreenshotUrl { get; set; }

    /// <summary> Альтернативный текст скриншота. </summary>
    public string? ScreenshotAlt { get; set; }

    /// <summary> CSS-класс иконки статьи. </summary>
    public string IconCssClass { get; set; } = "fa fa-book";

    /// <summary> Разделы статьи. </summary>
    public IReadOnlyList<OrganizationWikiSectionViewModel> Sections { get; set; } = [];
}

/// <summary> Модель раздела wiki-статьи. </summary>
public class OrganizationWikiSectionViewModel
{
    /// <summary> Заголовок раздела. </summary>
    public required string Title { get; set; }

    /// <summary> Абзацы текста раздела. </summary>
    public IReadOnlyList<string> Paragraphs { get; set; } = [];

    /// <summary> Список пунктов раздела. </summary>
    public IReadOnlyList<string> Bullets { get; set; } = [];

    /// <summary> Примечание к разделу. </summary>
    public string? Note { get; set; }
}

/// <summary> Модель боковой панели рабочего пространства организации. </summary>
public class OrganizationWorkspaceSidebarViewModel
{
    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Данные оболочки рабочего пространства. </summary>
    public OrganizationWorkspaceShellDto? WorkspaceShell { get; set; }

    /// <summary> Название организации. </summary>
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary> Код основного сценария организации. </summary>
    public string PrimaryScenarioCode { get; set; } = string.Empty;

    /// <summary> Код текущего раздела рабочей области организации. </summary>
    public string CurrentSection { get; set; } = string.Empty;

    /// <summary> Признак доступности модуля транспортных средств. </summary>
    public bool HasVehiclesModule { get; set; }

    /// <summary> Признак доступности модуля клиентов. </summary>
    public bool HasClientsModule { get; set; }

    /// <summary> Признак доступности модуля дефектов. </summary>
    public bool HasDefectsModule { get; set; }

    /// <summary> Признак доступности модуля счётчиков. </summary>
    public bool HasCountersModule { get; set; }

    /// <summary> Признак доступности модуля планировщика. </summary>
    public bool HasSchedulerModule { get; set; }

    /// <summary> Признак доступности модуля материалов. </summary>
    public bool HasMaterialsModule { get; set; }

    /// <summary> Признак права управления настройками организации. </summary>
    public bool CanManageSettings { get; set; }

    /// <summary> Количество клиентов организации, если известно. </summary>
    public int? ClientsCount { get; set; }

    /// <summary> Количество участников организации, если известно. </summary>
    public int? MembersCount { get; set; }
}

/// <summary> Модель варианта переключателя организаций. </summary>
public class OrganizationSwitcherOptionViewModel
{
    /// <summary> Идентификатор варианта переключателя. </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary> Отображаемый текст варианта переключателя. </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary> Название организации. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Название сценария организации. </summary>
    public string ScenarioName { get; set; } = string.Empty;

    /// <summary> Название роли текущего пользователя в организации. </summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary> Адрес перехода в рабочую область организации. </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary> Признак текущей организации. </summary>
    public bool IsCurrent { get; set; }
}

/// <summary> Входная модель создания организации. </summary>
public class CreateOrganizationInputModel : IValidatableObject
{
    /// <summary> Название новой организации. </summary>
    [Required(ErrorMessage = "organization.create.name.required")]
    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание новой организации. </summary>
    [MaxLength(600)]
    public string? Description { get; set; }

    /// <summary> Валюта учета новой организации. </summary>
    [Required(ErrorMessage = "organization.create.currency.required")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "organization.create.currency.length")]
    public string CurrencyCode { get; set; } = CurrencyCodes.Rub;

    /// <summary> Часовой пояс новой организации в формате IANA. </summary>
    [Required(ErrorMessage = "organization.create.timezone.required")]
    [MaxLength(100, ErrorMessage = "organization.create.timezone.length")]
    public string TimeZoneId { get; set; } = string.Empty;

    /// <summary> Код стиля документов организации. </summary>
    [MaxLength(40)]
    public string? DocumentStyleCode { get; set; } = "auto";

    /// <summary> Код основного сценария организации. </summary>
    [Required(ErrorMessage = "organization.create.scenario.required")]
    public string PrimaryScenarioCode { get; set; } = string.Empty;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!CurrencyCodes.IsSupported(this.CurrencyCode))
        {
            var localizer = validationContext.GetService(typeof(IStringLocalizer<LocalizationStrings>))
                as IStringLocalizer<LocalizationStrings>;
            var errorMessage = localizer is null
                ? "Выберите валюту из списка."
                : localizer["organization.create.currency.unsupported"].Value;

            yield return new ValidationResult(
                errorMessage,
                [nameof(CurrencyCode)]);
        }
    }
}

/// <summary> Входная модель обновления организации с реквизитами. </summary>
public class UpdateOrganizationInputModel
{
    /// <summary> Идентификатор организации. </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    /// <summary> Название организации. </summary>
    [Required(ErrorMessage = "Укажите название организации.")]
    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание организации. </summary>
    [MaxLength(600)]
    public string? Description { get; set; }

    /// <summary> Юридическое наименование организации. </summary>
    [MaxLength(240)]
    public string? LegalName { get; set; }

    /// <summary> ИНН организации. </summary>
    [MaxLength(12)]
    public string? Inn { get; set; }

    /// <summary> КПП организации. </summary>
    [MaxLength(9)]
    public string? Kpp { get; set; }

    /// <summary> ОГРН организации. </summary>
    [MaxLength(15)]
    public string? Ogrn { get; set; }

    /// <summary> Юридический адрес организации. </summary>
    [MaxLength(400)]
    public string? LegalAddress { get; set; }

    /// <summary> Фактический адрес организации. </summary>
    [MaxLength(400)]
    public string? ActualAddress { get; set; }

    /// <summary> Телефон организации. </summary>
    [MaxLength(40)]
    public string? Phone { get; set; }

    /// <summary> Электронная почта организации. </summary>
    [EmailAddress(ErrorMessage = "Укажите корректный email.")]
    [MaxLength(320)]
    public string? Email { get; set; }

    /// <summary> Должность подписанта. </summary>
    [MaxLength(120)]
    public string? SignatoryPosition { get; set; }

    /// <summary> Имя подписанта. </summary>
    [MaxLength(160)]
    public string? SignatoryName { get; set; }

    /// <summary> Основание полномочий подписанта. </summary>
    [MaxLength(160)]
    public string? SignatoryBasis { get; set; }

    /// <summary> Код стиля документов организации. </summary>
    [MaxLength(40)]
    public string? DocumentStyleCode { get; set; } = "auto";

    /// <summary> Код основного сценария организации. </summary>
    [Required(ErrorMessage = "Выберите тип организации.")]
    public string PrimaryScenarioCode { get; set; } = string.Empty;
}

/// <summary> Входная модель обновления профиля организации. </summary>
public class UpdateOrganizationProfileInputModel
{
    /// <summary> Идентификатор организации. </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    /// <summary> Название организации. </summary>
    [Required(ErrorMessage = "Укажите название организации.")]
    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание организации. </summary>
    [MaxLength(600)]
    public string? Description { get; set; }

    /// <summary> Код стиля документов организации. </summary>
    [MaxLength(40)]
    public string? DocumentStyleCode { get; set; } = "auto";

    /// <summary> Код основного сценария организации. </summary>
    [Required(ErrorMessage = "Выберите тип организации.")]
    public string PrimaryScenarioCode { get; set; } = string.Empty;
}

/// <summary> Входная модель обновления реквизитов организации. </summary>
public class UpdateOrganizationRequisitesInputModel
{
    /// <summary> Идентификатор организации. </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    /// <summary> Юридическое наименование организации. </summary>
    [MaxLength(240)]
    public string? LegalName { get; set; }

    /// <summary> ИНН организации. </summary>
    [MaxLength(12)]
    public string? Inn { get; set; }

    /// <summary> КПП организации. </summary>
    [MaxLength(9)]
    public string? Kpp { get; set; }

    /// <summary> ОГРН организации. </summary>
    [MaxLength(15)]
    public string? Ogrn { get; set; }

    /// <summary> Юридический адрес организации. </summary>
    [MaxLength(400)]
    public string? LegalAddress { get; set; }

    /// <summary> Фактический адрес организации. </summary>
    [MaxLength(400)]
    public string? ActualAddress { get; set; }

    /// <summary> Телефон организации. </summary>
    [MaxLength(40)]
    public string? Phone { get; set; }

    /// <summary> Электронная почта организации. </summary>
    [EmailAddress(ErrorMessage = "Укажите корректный email.")]
    [MaxLength(320)]
    public string? Email { get; set; }

    /// <summary> Должность подписанта. </summary>
    [MaxLength(120)]
    public string? SignatoryPosition { get; set; }

    /// <summary> Имя подписанта. </summary>
    [MaxLength(160)]
    public string? SignatoryName { get; set; }

    /// <summary> Основание полномочий подписанта. </summary>
    [MaxLength(160)]
    public string? SignatoryBasis { get; set; }
}

/// <summary> Входная модель приглашения участника в организацию. </summary>
public class InviteOrganizationMemberInputModel
{
    /// <summary> Идентификатор организации. </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    /// <summary> Электронная почта приглашаемого пользователя. </summary>
    [Required(ErrorMessage = "Укажите email участника.")]
    [EmailAddress(ErrorMessage = "Укажите корректный email.")]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    /// <summary> Код роли приглашаемого участника. </summary>
    [Required]
    public string RoleCode { get; set; } = string.Empty;
}

/// <summary> Входная модель создания клиента организации. </summary>
public class CreateOrganizationClientInputModel
{
    /// <summary> Фамилия клиента. </summary>
    [Required(ErrorMessage = "Укажите фамилию клиента.")]
    [MaxLength(60)]
    [RegularExpression(@"^[A-Za-zА-Яа-яЁё-]+$", ErrorMessage = "Фамилия может содержать только буквы и дефис.")]
    public string LastName { get; set; } = string.Empty;

    /// <summary> Имя клиента. </summary>
    [Required(ErrorMessage = "Укажите имя клиента.")]
    [MaxLength(60)]
    [RegularExpression(@"^[A-Za-zА-Яа-яЁё-]+$", ErrorMessage = "Имя может содержать только буквы и дефис.")]
    public string FirstName { get; set; } = string.Empty;

    /// <summary> Отчество клиента. </summary>
    [MaxLength(60)]
    [RegularExpression(@"^[A-Za-zА-Яа-яЁё-]*$", ErrorMessage = "Отчество может содержать только буквы и дефис.")]
    public string? MiddleName { get; set; }

    /// <summary> Телефон клиента. </summary>
    [MaxLength(18, ErrorMessage = "Телефон не должен быть длиннее 18 символов.")]
    public string? Phone { get; set; }

    /// <summary> Комментарий к клиенту. </summary>
    [MaxLength(600)]
    public string? Comment { get; set; }

    /// <summary> Код статуса клиента. </summary>
    public string? StatusCode { get; set; }

    /// <summary> Идентификатор ответственного пользователя. </summary>
    public string? ResponsibleUserId { get; set; }

    /// <summary> Заметки о клиенте. </summary>
    [MaxLength(2000)]
    public string? Notes { get; set; }

    /// <summary> Текст со названиями тегов клиента. </summary>
    [MaxLength(500)]
    public string? TagNamesText { get; set; }
}

/// <summary> Входная модель обновления клиента организации. </summary>
public class UpdateOrganizationClientInputModel
{
    /// <summary> Идентификатор клиента. </summary>
    [Required]
    public Guid ClientId { get; set; }

    /// <summary> Фамилия клиента. </summary>
    [Required(ErrorMessage = "Укажите фамилию клиента.")]
    [MaxLength(60)]
    [RegularExpression(@"^[A-Za-zА-Яа-яЁё-]+$", ErrorMessage = "Фамилия может содержать только буквы и дефис.")]
    public string LastName { get; set; } = string.Empty;

    /// <summary> Имя клиента. </summary>
    [Required(ErrorMessage = "Укажите имя клиента.")]
    [MaxLength(60)]
    [RegularExpression(@"^[A-Za-zА-Яа-яЁё-]+$", ErrorMessage = "Имя может содержать только буквы и дефис.")]
    public string FirstName { get; set; } = string.Empty;

    /// <summary> Отчество клиента. </summary>
    [MaxLength(60)]
    [RegularExpression(@"^[A-Za-zА-Яа-яЁё-]*$", ErrorMessage = "Отчество может содержать только буквы и дефис.")]
    public string? MiddleName { get; set; }

    /// <summary> Телефон клиента. </summary>
    [MaxLength(18, ErrorMessage = "Телефон не должен быть длиннее 18 символов.")]
    public string? Phone { get; set; }

    /// <summary> Комментарий к клиенту. </summary>
    [MaxLength(600)]
    public string? Comment { get; set; }

    /// <summary> Код статуса клиента. </summary>
    public string? StatusCode { get; set; }

    /// <summary> Идентификатор ответственного пользователя. </summary>
    public string? ResponsibleUserId { get; set; }

    /// <summary> Заметки о клиенте. </summary>
    [MaxLength(2000)]
    public string? Notes { get; set; }

    /// <summary> Текст со названиями тегов клиента. </summary>
    [MaxLength(500)]
    public string? TagNamesText { get; set; }
}

/// <summary> Входная модель создания дефекта транспортного средства. </summary>
public class CreateVehicleDefectInputModel
{
    /// <summary> Идентификатор транспортного средства. </summary>
    [Required(ErrorMessage = "Выберите технику.")]
    public Guid? VehicleId { get; set; }

    /// <summary> Идентификатор транспортного средства для возврата. </summary>
    public Guid? ReturnVehicleId { get; set; }

    /// <summary> Адрес возврата после создания. </summary>
    public string? ReturnUrl { get; set; }

    /// <summary> Заголовок дефекта. </summary>
    [Required(ErrorMessage = "Укажите название неисправности.")]
    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    /// <summary> Описание дефекта. </summary>
    [MaxLength(1500)]
    public string? Description { get; set; }

    /// <summary> Код серьёзности дефекта. </summary>
    [Required]
    public string SeverityCode { get; set; } = string.Empty;

    /// <summary> Код статуса дефекта. </summary>
    [Required]
    public string StatusCode { get; set; } = string.Empty;
}

/// <summary> Входная модель обновления дефекта транспортного средства. </summary>
public class UpdateVehicleDefectInputModel
{
    /// <summary> Идентификатор дефекта. </summary>
    [Required]
    public Guid DefectId { get; set; }

    /// <summary> Идентификатор транспортного средства. </summary>
    [Required(ErrorMessage = "Выберите технику.")]
    public Guid? VehicleId { get; set; }

    /// <summary> Идентификатор транспортного средства для возврата. </summary>
    public Guid? ReturnVehicleId { get; set; }

    /// <summary> Адрес возврата после обновления. </summary>
    public string? ReturnUrl { get; set; }

    /// <summary> Заголовок дефекта. </summary>
    [Required(ErrorMessage = "Укажите название неисправности.")]
    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    /// <summary> Описание дефекта. </summary>
    [MaxLength(1500)]
    public string? Description { get; set; }

    /// <summary> Код серьёзности дефекта. </summary>
    [Required]
    public string SeverityCode { get; set; } = string.Empty;

    /// <summary> Код статуса дефекта. </summary>
    [Required]
    public string StatusCode { get; set; } = string.Empty;
}

/// <summary> Входная модель обновления показания счётчика транспортного средства. </summary>
public class UpdateVehicleCounterInputModel
{
    /// <summary> Идентификатор записи показания. </summary>
    public Guid? EntryId { get; set; }

    /// <summary> Идентификатор транспортного средства. </summary>
    [Required(ErrorMessage = "Выберите технику.")]
    public Guid? VehicleId { get; set; }

    /// <summary> Значение показания счётчика. </summary>
    [Required(ErrorMessage = "Укажите значение счетчика.")]
    [Range(0, int.MaxValue, ErrorMessage = "Значение счетчика не может быть отрицательным.")]
    public int? Value { get; set; }

    /// <summary> Момент фиксации показания. </summary>
    [Required(ErrorMessage = "Укажите дату обновления.")]
    public DateTime? RecordedAt { get; set; }

    /// <summary> Комментарий к показанию. </summary>
    [MaxLength(600)]
    public string? Comment { get; set; }
}

/// <summary> Входная модель изменения роли участника организации. </summary>
public class UpdateOrganizationMemberRoleInputModel
{
    /// <summary> Идентификатор организации. </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    /// <summary> Идентификатор участника. </summary>
    [Required]
    public Guid MemberId { get; set; }

    /// <summary> Код новой роли участника. </summary>
    [Required]
    public string RoleCode { get; set; } = string.Empty;
}

/// <summary> Входная модель обновления разрешений роли организации. </summary>
public class UpdateOrganizationRolePermissionsInputModel
{
    /// <summary> Коды включённых разрешений роли. </summary>
    public List<string> EnabledPermissionCodes { get; set; } = [];
}

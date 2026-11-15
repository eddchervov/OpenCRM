using OpenCRM.Modules.Clients.Contracts.Dtos;
namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Результат запроса страницы планировщика организации. </summary>
public sealed class OrganizationSchedulerPageQueryResult
{
    /// <summary> Записи планировщика на текущей странице. </summary>
    public IReadOnlyList<OrganizationSchedulerItemDto> Items { get; init; } = [];

    /// <summary> Варианты выбора транспортных средств для фильтров. </summary>
    public IReadOnlyList<OrganizationVehicleOptionDto> VehicleOptions { get; init; } = [];

    /// <summary> Варианты выбора клиентов для фильтров. </summary>
    public IReadOnlyList<OrganizationClientOptionDto> ClientOptions { get; init; } = [];

    /// <summary> Варианты выбора ответственных для фильтров. </summary>
    public IReadOnlyList<OrganizationSchedulerMemberOptionDto> ResponsibleOptions { get; init; } = [];

    /// <summary> Варианты выбора типа задачи для фильтров. </summary>
    public IReadOnlyList<OrganizationSchedulerOptionDto> TaskTypeOptions { get; init; } = [];

    /// <summary> Варианты выбора приоритета для фильтров. </summary>
    public IReadOnlyList<OrganizationSchedulerOptionDto> PriorityOptions { get; init; } = [];

    /// <summary> Варианты выбора связанного дефекта для фильтров. </summary>
    public IReadOnlyList<OrganizationSchedulerLinkedItemOptionDto> LinkedDefectOptions { get; init; } = [];

    /// <summary> Варианты выбора связанного обслуживания для фильтров. </summary>
    public IReadOnlyList<OrganizationSchedulerLinkedItemOptionDto> LinkedMaintenanceOptions { get; init; } = [];

    /// <summary> Выбранный фильтр по статусу. </summary>
    public string SelectedStatus { get; init; } = string.Empty;

    /// <summary> Выбранный фильтр по источнику. </summary>
    public string SelectedSource { get; init; } = string.Empty;

    /// <summary> Выбранный фильтр по workflow-процессу. </summary>
    public string SelectedWorkflow { get; init; } = string.Empty;

    /// <summary> Выбранный фильтр по клиенту, если задан. </summary>
    public Guid? SelectedClientId { get; init; }

    /// <summary> Выбранный фильтр по ответственному. </summary>
    public string SelectedResponsibleUserId { get; init; } = string.Empty;

    /// <summary> Выбранный фильтр по типу задачи. </summary>
    public string SelectedTaskType { get; init; } = string.Empty;

    /// <summary> Выбранный фильтр по приоритету. </summary>
    public string SelectedPriority { get; init; } = string.Empty;

    /// <summary> Поисковый запрос. </summary>
    public string SearchQuery { get; init; } = string.Empty;

    /// <summary> Общее количество записей по фильтрам. </summary>
    public int TotalItemsCount { get; init; }

    /// <summary> Количество записей в группе «внимание в ближайшее время». </summary>
    public int FocusItemsCount { get; init; }

    /// <summary> Количество просроченных записей. </summary>
    public int OverdueItemsCount { get; init; }

    /// <summary> Количество записей, приближающихся по сроку. </summary>
    public int SoonItemsCount { get; init; }

    /// <summary> Количество записей, требующих внимания. </summary>
    public int AttentionItemsCount { get; init; }

    /// <summary> Количество записей по дате. </summary>
    public int DateItemsCount { get; init; }

    /// <summary> Количество записей по пробегу. </summary>
    public int MileageItemsCount { get; init; }

    /// <summary> Количество записей по дефектам. </summary>
    public int DefectItemsCount { get; init; }

    /// <summary> Количество записей по счётчикам. </summary>
    public int CounterItemsCount { get; init; }

    /// <summary> Количество записей по настройке. </summary>
    public int SetupItemsCount { get; init; }

    /// <summary> Количество ручных задач. </summary>
    public int ManualTaskItemsCount { get; init; }

    /// <summary> Количество workflow-процессов в работе. </summary>
    public int InProgressWorkflowCount { get; init; }

    /// <summary> Количество отложенных workflow-процессов. </summary>
    public int DeferredWorkflowCount { get; init; }

    /// <summary> Количество закрытых workflow-процессов. </summary>
    public int ClosedWorkflowCount { get; init; }

    /// <summary> Количество запланированных обслуживаний. </summary>
    public int PlannedMaintenanceItemsCount { get; init; }

    /// <summary> Количество открытых дефектов. </summary>
    public int OpenDefectItemsCount { get; init; }

    /// <summary> Количество работ в процессе выполнения. </summary>
    public int WorkInProgressItemsCount { get; init; }

    /// <summary> Количество завершённых записей. </summary>
    public int CompletedItemsCount { get; init; }

    /// <summary> Список просроченных записей. </summary>
    public IReadOnlyList<OrganizationSchedulerItemDto> OverdueItems { get; init; } = [];

    /// <summary> Список записей с приближающимся сроком. </summary>
    public IReadOnlyList<OrganizationSchedulerItemDto> SoonItems { get; init; } = [];

    /// <summary> Список записей, требующих внимания. </summary>
    public IReadOnlyList<OrganizationSchedulerItemDto> AttentionItems { get; init; } = [];
}

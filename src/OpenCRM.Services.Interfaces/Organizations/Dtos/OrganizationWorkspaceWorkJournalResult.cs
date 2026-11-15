namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Результат журнала работ рабочего пространства. </summary>
public sealed class OrganizationWorkspaceWorkJournalResult
{
    /// <summary> Записи выполненных работ. </summary>
    public IReadOnlyList<OrganizationWorkJournalWorkItemDto> WorkItems { get; init; } = [];

    /// <summary> Исторические записи журнала. </summary>
    public IReadOnlyList<OrganizationWorkJournalItemDto> HistoryItems { get; init; } = [];

    /// <summary> Варианты выбора транспортных средств для фильтров. </summary>
    public IReadOnlyList<OrganizationVehicleOptionDto> VehicleOptions { get; init; } = [];

    /// <summary> Выбранный фильтр по виду событий. </summary>
    public string SelectedKind { get; init; } = string.Empty;

    /// <summary> Выбранная вкладка журнала. </summary>
    public string SelectedTab { get; init; } = "works";

    /// <summary> Количество записей выполненных работ. </summary>
    public int WorkItemsCount { get; init; }

    /// <summary> Количество исторических записей. </summary>
    public int HistoryItemsCount { get; init; }

    /// <summary> Количество событий обслуживаний. </summary>
    public int MaintenanceEventsCount { get; init; }

    /// <summary> Количество созданных дефектов. </summary>
    public int DefectsCreatedCount { get; init; }

    /// <summary> Количество закрытых дефектов. </summary>
    public int DefectsClosedCount { get; init; }

    /// <summary> Количество завершённых обслуживаний. </summary>
    public int CompletedMaintenancesCount { get; init; }

    /// <summary> Количество обслуженных клиентов. </summary>
    public int ServedClientsCount { get; init; }
}

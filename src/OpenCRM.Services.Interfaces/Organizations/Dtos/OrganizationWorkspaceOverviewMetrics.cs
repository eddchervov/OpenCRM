namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Метрики обзора рабочего пространства организации. </summary>
public sealed class OrganizationWorkspaceOverviewMetrics
{
    /// <summary> Общее количество обслуживаний. </summary>
    public int MaintenanceTotalCount { get; init; }

    /// <summary> Количество запланированных обслуживаний. </summary>
    public int MaintenancePlannedCount { get; init; }

    /// <summary> Количество обслуживаний в процессе выполнения. </summary>
    public int MaintenanceInProgressCount { get; init; }

    /// <summary> Количество завершённых обслуживаний. </summary>
    public int MaintenanceCompletedCount { get; init; }

    /// <summary> Количество открытых дефектов. </summary>
    public int OpenDefectsCount { get; init; }

    /// <summary> Количество решённых дефектов. </summary>
    public int ResolvedDefectsCount { get; init; }

    /// <summary> Количество записей счётчиков. </summary>
    public int CounterEntriesCount { get; init; }

    /// <summary> Количество документов. </summary>
    public int DocumentsCount { get; init; }
}

namespace OpenCRM.Modules.Catalog.Contracts.Dtos;

/// <summary> Незавершённые записи, назначенные на рабочий пост. </summary>
public sealed class WorkPostAffectedMaintenancesDto
{
    /// <summary> Общее число затронутых записей. </summary>
    public int TotalCount { get; init; }

    /// <summary> До двадцати ближайших затронутых записей. </summary>
    public IReadOnlyList<WorkPostAffectedMaintenanceDto> Items { get; init; } = [];
}

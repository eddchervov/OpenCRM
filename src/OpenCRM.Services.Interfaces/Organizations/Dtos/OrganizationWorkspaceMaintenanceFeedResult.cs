namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Результат ленты обслуживаний рабочего пространства. </summary>
public sealed class OrganizationWorkspaceMaintenanceFeedResult
{
    /// <summary> Записи обслуживаний на текущей странице. </summary>
    public IReadOnlyList<OrganizationRecentMaintenanceDto> Items { get; init; } = [];

    /// <summary> Номер текущей страницы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }

    /// <summary> Общее количество записей. </summary>
    public int TotalCount { get; init; }
}

namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Страница записей журнала с учётом всех фильтров. </summary>
public sealed class OrganizationMaintenanceJournalResult
{
    /// <summary> Элементы журнала обслуживания на текущей странице. </summary>
    public IReadOnlyList<OrganizationMaintenanceJournalItemDto> Items { get; init; } = [];

    /// <summary> Сотрудники организации, доступные для фильтрации исполнителя, включая неактивных. </summary>
    public IReadOnlyList<OrganizationMaintenanceJournalPerformerDto> Performers { get; init; } = [];

    /// <summary> Общее количество записей журнала. </summary>
    public int TotalCount { get; init; }

    /// <summary> Номер текущей страницы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }
}

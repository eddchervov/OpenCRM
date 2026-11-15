namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Страница истории работ клиента. </summary>
public sealed class OrganizationClientMaintenancePageDto
{
    /// <summary> Работы текущей страницы. </summary>
    public IReadOnlyList<OrganizationClientMaintenanceItemDto> Items { get; init; } = [];

    /// <summary> Количество работ после применения фильтров. </summary>
    public int TotalCount { get; init; }

    /// <summary> Номер страницы, начиная с единицы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }
}

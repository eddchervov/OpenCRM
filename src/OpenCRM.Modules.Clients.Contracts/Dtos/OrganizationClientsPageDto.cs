namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Страница списка клиентов организации. </summary>
public class OrganizationClientsPageDto
{
    /// <summary> Клиенты на странице. </summary>
    public IReadOnlyList<OrganizationClientDto> Items { get; init; } = [];

    /// <summary> Метки для фильтрации. </summary>
    public IReadOnlyList<OrganizationClientTagDto> Tags { get; init; } = [];

    /// <summary> Сегменты для фильтрации. </summary>
    public IReadOnlyList<OrganizationClientSegmentDto> Segments { get; init; } = [];

    /// <summary> Номер страницы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }

    /// <summary> Общее количество клиентов по фильтру. </summary>
    public int TotalCount { get; init; }

    /// <summary> Общее количество клиентов без фильтра. </summary>
    public int AllCount { get; init; }

    /// <summary> Общее количество транспортных средств. </summary>
    public int TotalVehiclesCount { get; init; }

    /// <summary> Общее количество обслуживаний. </summary>
    public int TotalMaintenanceCount { get; init; }
}

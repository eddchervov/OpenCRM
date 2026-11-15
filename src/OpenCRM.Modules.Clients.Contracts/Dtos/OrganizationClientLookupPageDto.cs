namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Страница результатов поиска клиентов при выборе. </summary>
public class OrganizationClientLookupPageDto
{
    /// <summary> Найденные клиенты. </summary>
    public IReadOnlyList<OrganizationClientLookupItemDto> Items { get; init; } = [];

    /// <summary> Общее количество клиентов по поиску. </summary>
    public int TotalCount { get; init; }
}

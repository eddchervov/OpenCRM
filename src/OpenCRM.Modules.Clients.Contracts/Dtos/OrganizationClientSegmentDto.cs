namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Сегмент клиентов организации. </summary>
public class OrganizationClientSegmentDto
{
    /// <summary> Код сегмента. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Название сегмента. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Количество клиентов в сегменте. </summary>
    public int Count { get; init; }
}

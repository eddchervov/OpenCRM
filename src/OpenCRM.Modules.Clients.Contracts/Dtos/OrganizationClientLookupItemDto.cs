namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Краткие данные клиента для поиска при выборе. </summary>
public class OrganizationClientLookupItemDto
{
    /// <summary> Идентификатор клиента. </summary>
    public Guid Id { get; init; }

    /// <summary> Наименование клиента. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Телефон клиента. </summary>
    public string? Phone { get; init; }

    /// <summary> Код типа контрагента. </summary>
    public string? PartyTypeCode { get; init; }
}

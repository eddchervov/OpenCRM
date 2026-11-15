namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Краткий вариант клиента для выбора в списках. </summary>
public class OrganizationClientOptionDto
{
    /// <summary> Идентификатор клиента. </summary>
    public Guid Id { get; set; }

    /// <summary> Наименование клиента. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Телефон клиента. </summary>
    public string? Phone { get; set; }

    /// <summary> Название статуса клиента. </summary>
    public string StatusName { get; set; } = string.Empty;

    /// <summary> Количество транспортных средств клиента. </summary>
    public int VehiclesCount { get; set; }

    /// <summary> Дата создания клиента. </summary>
    public DateTime CreatedAt { get; set; }
}

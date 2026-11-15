namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Дефект транспортного средства клиента организации. </summary>
public class OrganizationClientDefectItemDto
{
    /// <summary> Идентификатор дефекта. </summary>
    public Guid Id { get; set; }

    /// <summary> Идентификатор транспортного средства. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Отображаемое название транспортного средства. </summary>
    public string VehicleDisplayName { get; set; } = string.Empty;

    /// <summary> Заголовок дефекта. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Код серьёзности дефекта. </summary>
    public string SeverityCode { get; set; } = string.Empty;

    /// <summary> Название серьёзности дефекта. </summary>
    public string SeverityName { get; set; } = string.Empty;

    /// <summary> Код статуса дефекта. </summary>
    public string StatusCode { get; set; } = string.Empty;

    /// <summary> Название статуса дефекта. </summary>
    public string StatusName { get; set; } = string.Empty;

    /// <summary> Дата и время создания дефекта. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Дата и время закрытия дефекта. </summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary> Ссылка на карточку дефекта. </summary>
    public string DetailUrl { get; set; } = string.Empty;

    /// <summary> Ссылка на транспортное средство. </summary>
    public string VehicleUrl { get; set; } = string.Empty;
}

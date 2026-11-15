namespace OpenCRM.Services.Interfaces.Defects.Dtos;

/// <summary> Информация о дефекте техники. </summary>
public class VehicleDefectDto
{
    /// <summary> Идентификатор дефекта. </summary>
    public Guid Id { get; set; }

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Отображаемое название техники. </summary>
    public string VehicleDisplayName { get; set; } = string.Empty;

    /// <summary> Дополнительное описание техники, если есть. </summary>
    public string? VehicleSubtitle { get; set; }

    /// <summary> Название дефекта. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Описание дефекта, если указано. </summary>
    public string? Description { get; set; }

    /// <summary> Код критичности дефекта. </summary>
    public string SeverityCode { get; set; } = string.Empty;

    /// <summary> Название критичности дефекта. </summary>
    public string SeverityName { get; set; } = string.Empty;

    /// <summary> Код статуса дефекта. </summary>
    public string StatusCode { get; set; } = string.Empty;

    /// <summary> Название статуса дефекта. </summary>
    public string StatusName { get; set; } = string.Empty;

    /// <summary> Признак закрытия дефекта. </summary>
    public bool IsClosed { get; set; }

    /// <summary> Количество связанных обслуживаний. </summary>
    public int LinkedMaintenanceCount { get; set; }

    /// <summary> Единственное связанное обслуживание, если оно одно. </summary>
    public VehicleDefectLinkedMaintenanceDto? LinkedMaintenance { get; set; }

    /// <summary> Список связанных обслуживаний. </summary>
    public IReadOnlyList<VehicleDefectLinkedMaintenanceDto> LinkedMaintenances { get; set; } = [];

    /// <summary> Время создания дефекта. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Время последнего обновления дефекта. </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary> Время закрытия дефекта, если закрыт. </summary>
    public DateTime? ClosedAt { get; set; }
}

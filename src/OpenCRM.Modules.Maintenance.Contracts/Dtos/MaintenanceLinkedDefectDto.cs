namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные дефекта, связанного с обслуживанием. </summary>
public class MaintenanceLinkedDefectDto
{
    /// <summary> Идентификатор дефекта. </summary>
    public required Guid Id { get; set; }

    /// <summary> Заголовок дефекта. </summary>
    public required string Title { get; set; } = string.Empty;

    /// <summary> Описание дефекта. </summary>
    public required string? Description { get; set; }

    /// <summary> Код серьёзности дефекта. </summary>
    public required string SeverityCode { get; set; } = string.Empty;

    /// <summary> Код статуса дефекта. </summary>
    public required string StatusCode { get; set; } = string.Empty;

    /// <summary> Дата создания дефекта. </summary>
    public required DateTime CreatedAt { get; set; }

    /// <summary> Дата закрытия дефекта, если закрыт. </summary>
    public required DateTime? ClosedAt { get; set; }
}

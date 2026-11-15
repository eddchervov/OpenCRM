using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Запись о показании счетчика техники. </summary>
public class VehicleCounterEntryDto
{
    /// <summary> Идентификатор записи показания. </summary>
    public Guid Id { get; set; }

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Идентификатор клиента организации, если применим. </summary>
    public Guid? OrganizationClientId { get; set; }

    /// <summary> Название клиента организации. </summary>
    public string? OrganizationClientName { get; set; }

    /// <summary> Отображаемое название техники. </summary>
    public string VehicleDisplayName { get; set; } = string.Empty;

    /// <summary> Дополнительное описание техники. </summary>
    public string? VehicleSubtitle { get; set; }

    /// <summary> Единица измерения пробега. </summary>
    public VehicleMileageUnit MileageUnit { get; set; }

    /// <summary> Значение показания. </summary>
    public int Value { get; set; }

    /// <summary> Время фиксации показания. </summary>
    public DateTime RecordedAt { get; set; }

    /// <summary> Комментарий к показанию. </summary>
    public string? Comment { get; set; }

    /// <summary> Тип источника записи. </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary> Название источника записи. </summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary> Тип записи в хронологии. </summary>
    public string TimelineType { get; set; } = "counter";

    /// <summary> Тип обслуживания, если связан. </summary>
    public int? MaintenanceType { get; set; }

    /// <summary> Признак возможности управления записью. </summary>
    public bool CanManage { get; set; }
}

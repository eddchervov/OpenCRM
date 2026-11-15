using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Данные показания счетчика на дату. </summary>
public class VehicleCounterLookupDto
{
    /// <summary> Признак наличия записи на дату. </summary>
    public bool HasEntry { get; set; }

    /// <summary> Идентификатор существующей записи. </summary>
    public Guid? EntryId { get; set; }

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

    /// <summary> Значение показания, если есть. </summary>
    public int? Value { get; set; }

    /// <summary> Время фиксации показания. </summary>
    public DateTime RecordedAt { get; set; }

    /// <summary> Комментарий к показанию. </summary>
    public string? Comment { get; set; }

    /// <summary> Тип источника записи. </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary> Название источника записи. </summary>
    public string SourceName { get; set; } = string.Empty;
}

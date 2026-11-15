using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Краткие данные техники со счетчиками. </summary>
public class VehicleCounterVehicleDto
{
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

    /// <summary> Текущее значение счетчика, если есть. </summary>
    public int? CurrentValue { get; set; }

    /// <summary> Время последнего обновления показания. </summary>
    public DateTime? UpdatedAt { get; set; }
}

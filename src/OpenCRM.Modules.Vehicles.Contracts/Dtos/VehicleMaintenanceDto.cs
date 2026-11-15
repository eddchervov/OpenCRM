using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Данные об обслуживании техники. </summary>
public class VehicleMaintenanceDto
{
    /// <summary> Тип обслуживания. </summary>
    public MaintenanceType Type { get; set; }

    /// <summary> Дата обслуживания. </summary>
    public DateTime Date { get; set; }

    /// <summary> Место обслуживания. </summary>
    public string? Location { get; set; }

    /// <summary> Пробег на момент обслуживания. </summary>
    public int? Mileage { get; set; }

    /// <summary> Комментарий к обслуживанию. </summary>
    public string? Comment { get; set; }

}

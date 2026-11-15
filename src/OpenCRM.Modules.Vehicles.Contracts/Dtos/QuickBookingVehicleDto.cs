using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Краткие данные техники для быстрого бронирования. </summary>
public sealed class QuickBookingVehicleDto
{
    /// <summary> Идентификатор техники. </summary>
    public Guid Id { get; set; }

    /// <summary> Идентификатор клиента организации, если применим. </summary>
    public Guid? OrganizationClientId { get; set; }

    /// <summary> Вид техники. </summary>
    public VehicleKind Kind { get; set; }

    /// <summary> Регистрационный номер техники. </summary>
    public string? RegistrationPlate { get; set; }
}

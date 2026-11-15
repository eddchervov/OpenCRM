namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Краткие данные техники для сценариев обслуживания. </summary>
public sealed class MaintenanceVehicleDto
{
    /// <summary> Идентификатор техники. </summary>
    public Guid Id { get; init; }

    /// <summary> Идентификатор клиента организации, если применим. </summary>
    public Guid? OrganizationClientId { get; init; }
}

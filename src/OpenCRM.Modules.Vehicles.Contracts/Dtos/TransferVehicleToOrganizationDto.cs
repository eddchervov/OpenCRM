namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Данные для передачи техники организации. </summary>
public class TransferVehicleToOrganizationDto
{
    /// <summary> Идентификатор организации-получателя. </summary>
    public Guid OrganizationId { get; set; }
}

namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Карточка ручного обращения. </summary>
public sealed class OrganizationInquiryDto
{
    public Guid Id { get; init; }
    public string ContactName { get; init; } = string.Empty;
    public string? ContactPhone { get; init; }
    public string? SourceCode { get; init; }
    public string InterestText { get; init; } = string.Empty;
    public Guid? OrganizationClientId { get; init; }
    public string? OrganizationClientName { get; init; }
    public Guid? VehicleId { get; init; }
    public string? VehicleName { get; init; }
    public string? VehicleDescription { get; init; }
    public string StageCode { get; init; } = string.Empty;
    public string? ResponsibleUserId { get; init; }
    public string? ResponsibleUserName { get; init; }
    public string? NextActionText { get; init; }
    public DateTime? NextActionAt { get; init; }
    public string? CloseReason { get; init; }
    public Guid? MaintenanceId { get; init; }
    public long Version { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public bool CanManage { get; init; }
    public bool CanCreateClient { get; init; }
    public bool CanCreateBooking { get; init; }
    public bool CanViewLinkedMaintenance { get; init; }
    public bool CanViewLinkedClient { get; init; }
    public bool CanViewLinkedVehicle { get; init; }
    public bool CanViewClients { get; init; }
    public bool CanViewVehicles { get; init; }
    public string? TimeZoneId { get; init; }
}

namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Данные создания или изменения обращения. </summary>
public sealed record OrganizationInquirySaveDto
{
    public string ContactName { get; init; } = string.Empty;
    public string? ContactPhone { get; init; }
    public string? SourceCode { get; init; }
    public string InterestText { get; init; } = string.Empty;
    public Guid? OrganizationClientId { get; init; }
    public Guid? VehicleId { get; init; }
    public string? VehicleDescription { get; init; }
    public string? ResponsibleUserId { get; init; }
    public string? NextActionText { get; init; }
    public DateTime? NextActionAt { get; init; }
    public long Version { get; init; }
}

namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Состояние обращения, заблокированного для атомарной конвертации в запись. </summary>
public sealed class OrganizationInquiryBookingLinkDto
{
    public Guid InquiryId { get; init; }
    public Guid OrganizationId { get; init; }
    public long Version { get; init; }
    public string StageCode { get; init; } = string.Empty;
    public Guid? MaintenanceId { get; init; }
}

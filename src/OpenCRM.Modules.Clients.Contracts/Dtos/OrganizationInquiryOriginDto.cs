namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Источник записи обслуживания — связанное с ней обращение. </summary>
public sealed class OrganizationInquiryOriginDto
{
    /// <summary> Идентификатор обращения. </summary>
    public Guid InquiryId { get; init; }
}

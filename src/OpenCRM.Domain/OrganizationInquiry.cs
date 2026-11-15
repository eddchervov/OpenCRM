using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Ручное обращение клиента до создания записи. </summary>
public class OrganizationInquiry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string ContactName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? NormalizedPhone { get; set; }
    public string? SourceCode { get; set; }
    public string Interest { get; set; } = string.Empty;
    public Guid? OrganizationClientId { get; set; }
    public OrganizationClient? OrganizationClient { get; set; }
    public Guid? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public string? VehicleDescription { get; set; }
    public string StageCode { get; set; } = OrganizationInquiryStageCodes.New;
    public string? ResponsibleUserId { get; set; }
    public ApplicationUser? ResponsibleUser { get; set; }
    public string? NextActionText { get; set; }
    public DateTime? NextActionAt { get; set; }
    public string? CloseReason { get; set; }
    public Guid? MaintenanceId { get; set; }
    public Maintenance? Maintenance { get; set; }
    public long Version { get; set; } = 1;
    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<OrganizationInquiryEvent> Events { get; set; } = new HashSet<OrganizationInquiryEvent>();
}

/// <summary> Коды этапов ручного обращения. </summary>
public static class OrganizationInquiryStageCodes
{
    public const string New = "new";
    public const string Clarifying = "clarifying";
    public const string Proposal = "proposal";
    public const string Ready = "ready";
    public const string Booked = "booked";
    public const string Closed = "closed";
    public static readonly IReadOnlySet<string> Open = new HashSet<string>(StringComparer.Ordinal) { New, Clarifying, Proposal, Ready };
}

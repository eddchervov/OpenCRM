using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Необязательная рекомендация повторного визита клиента. </summary>
public sealed class OrganizationRepeatVisit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public Guid OrganizationClientId { get; set; }
    public OrganizationClient OrganizationClient { get; set; } = null!;
    public Guid VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
    public Guid SourceMaintenanceId { get; set; }
    public Maintenance SourceMaintenance { get; set; } = null!;
    public string ServiceName { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateOnly SuggestedVisitDate { get; set; }
    public string? ResponsibleUserId { get; set; }
    public ApplicationUser? ResponsibleUser { get; set; }
    public DateTime? ContactByUtc { get; set; }
    public DateTime? ContactCompletedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? BookedMaintenanceId { get; set; }
    public Maintenance? BookedMaintenance { get; set; }
    public long Version { get; set; } = 1;
    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

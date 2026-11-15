namespace OpenCRM.Domain;

public class MaintenanceWorkReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CommandId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid MaintenanceId { get; set; }
    public Maintenance Maintenance { get; set; } = null!;
    public Guid DetailId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid MemberId { get; set; }
    public string ActorUserId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? Decision { get; set; }
    public string? DecisionReason { get; set; }
    public string? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public Guid? ApprovedDetailId { get; set; }
    public decimal? ApprovedPrice { get; set; }
    public int? TimeAdjustmentMinutes { get; set; }
}

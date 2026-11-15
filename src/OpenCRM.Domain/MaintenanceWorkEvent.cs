using OpenCRM.Domain.Enums;

namespace OpenCRM.Domain;

public class MaintenanceWorkEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperationId { get; set; }
    public MaintenanceWorkOperation Operation { get; set; } = null!;
    public Guid CommandId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid MaintenanceId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid MemberId { get; set; }
    public string ActorUserId { get; set; } = string.Empty;
    public MaintenanceWorkStatus PreviousStatus { get; set; }
    public MaintenanceWorkStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Reason { get; set; }
    public uint PreviousVersion { get; set; }
    public uint ResultVersion { get; set; }
}

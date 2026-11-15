using OpenCRM.Domain.Enums;

namespace OpenCRM.Domain;

public class MaintenanceWorkOperation
{
    public Guid Id { get; set; }
    public Guid MaintenanceId { get; set; }
    public Maintenance Maintenance { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public Guid EmployeeId { get; set; }
    public MaintenanceWorkStatus Status { get; set; } = MaintenanceWorkStatus.Assigned;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public uint Version { get; set; }
}

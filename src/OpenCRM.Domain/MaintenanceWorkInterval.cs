namespace OpenCRM.Domain;

public class MaintenanceWorkInterval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperationId { get; set; }
    public MaintenanceWorkOperation Operation { get; set; } = null!;
    public Guid EmployeeId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}

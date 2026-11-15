using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Связь обслуживания с неисправностью техники. </summary>
public class MaintenanceDefect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MaintenanceId { get; set; }
    public Maintenance Maintenance { get; set; } = null!;
    public Guid VehicleDefectId { get; set; }
    public VehicleDefect VehicleDefect { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

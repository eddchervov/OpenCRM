namespace OpenCRM.Domain;

public class OrganizationAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string ActorUserId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

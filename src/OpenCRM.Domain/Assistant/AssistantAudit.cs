namespace OpenCRM.Domain.Assistant;

public sealed class AssistantAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AdminUserId { get; set; } = string.Empty;
    public Guid? OrganizationId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

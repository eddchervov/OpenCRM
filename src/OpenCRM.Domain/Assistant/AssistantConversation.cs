namespace OpenCRM.Domain.Assistant;

public sealed class AssistantConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Channel { get; set; } = "web";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string EngineStateJson { get; set; } = "{}";
    public string SelectionJson { get; set; } = "{}";
    public Guid? ProcessingToken { get; set; }
    public DateTime? ProcessingUntil { get; set; }
    public bool IsClosed { get; set; }
}

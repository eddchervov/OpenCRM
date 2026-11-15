namespace OpenCRM.Domain.Assistant;

public sealed class AssistantDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public AssistantConversation Conversation { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Channel { get; set; } = "web";
    public string Action { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string CommandJson { get; set; } = "{}";
    public string SelectionJson { get; set; } = "{}";
    public string Snapshot { get; set; } = string.Empty;
    public string PreviewJson { get; set; } = "{}";
    public string? ResultJson { get; set; }
    public string? FailureMessage { get; set; }
    public bool Destructive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

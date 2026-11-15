namespace OpenCRM.Domain.Assistant;

public sealed class AssistantMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public AssistantConversation Conversation { get; set; } = null!;
    public int Sequence { get; set; }
    public string Role { get; set; } = "user";
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

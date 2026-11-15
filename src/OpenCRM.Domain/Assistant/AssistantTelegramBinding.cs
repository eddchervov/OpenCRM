namespace OpenCRM.Domain.Assistant;

public sealed class AssistantTelegramBinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long TelegramUserId { get; set; }
    public long ChatId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid? SelectedOrganizationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public Guid? ConversationId { get; set; }
    public bool SpeechRepliesEnabled { get; set; }
}

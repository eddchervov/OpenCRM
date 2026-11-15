namespace OpenCRM.Domain;

/// <summary> Личный диалог Telegram-клиента с ботом конкретной организации. </summary>
public sealed class OrganizationConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public Guid? ConnectionId { get; set; }
    public OrganizationChannelConnection? Connection { get; set; }
    public string? ExternalConversationId { get; set; }
    public string? DeliveryAddress { get; set; }
    public long? BotId { get; set; }
    public string BotUsername { get; set; } = string.Empty;
    public long? TelegramChatId { get; set; }
    public long? TelegramUserId { get; set; }
    public string? TelegramDisplayName { get; set; }
    public string? TelegramUsername { get; set; }
    public Guid? OrganizationClientId { get; set; }
    public OrganizationClient? OrganizationClient { get; set; }
    public long LastSequence { get; set; }
    public string? LastMessageText { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsChatActive { get; set; } = true;
    public DateTime? ChannelStateChangedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<OrganizationConversationMessage> Messages { get; set; } = new HashSet<OrganizationConversationMessage>();
    public ICollection<OrganizationConversationAttachment> Attachments { get; set; } = new HashSet<OrganizationConversationAttachment>();
}

namespace Notifications.Domain;

/// <summary>Durable inbox для адресных сигналов доставки переписки.</summary>
public sealed class ConversationDeliveryInbox
{
    public ConversationDeliveryInbox()
    {
    }

    public string ProviderCode { get; set; } = string.Empty;
    public Guid MessageId { get; set; }
    public Guid RequestId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset NextAttemptAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

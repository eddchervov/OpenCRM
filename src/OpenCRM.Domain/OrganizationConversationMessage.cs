namespace OpenCRM.Domain;

/// <summary> Сообщение с монотонным номером в диалоге и результатом его доставки. </summary>
public sealed class OrganizationConversationMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public OrganizationConversation Conversation { get; set; } = null!;
    public long Sequence { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Text { get; set; }
    public string? ActorUserId { get; set; }
    public string? ActorDisplayName { get; set; }
    public Guid? MaintenanceId { get; set; }
    public Maintenance? Maintenance { get; set; }
    public long? SenderTelegramUserId { get; set; }
    public string? SenderExternalUserId { get; set; }
    public Guid? RequestId { get; set; }
    public long? TelegramUpdateId { get; set; }
    public string? ProviderUpdateId { get; set; }
    public string? ProviderMessageId { get; set; }
    public Guid? MessageTemplateId { get; set; }
    public int? MessageTemplateVersion { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime? RetryAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool SafeToRetry { get; set; }
    public int DeliveryAttemptCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public ICollection<OrganizationConversationAttachment> Attachments { get; set; } = new HashSet<OrganizationConversationAttachment>();
}

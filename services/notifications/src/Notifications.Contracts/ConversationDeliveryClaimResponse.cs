namespace Notifications.Contracts;

/// <summary> Проверенная сервером команда для одной попытки отправки через канал организации. </summary>
public sealed record ConversationDeliveryClaimResponse
{
    public Guid MessageId { get; init; }
    public Guid LeaseId { get; init; }
    public bool IsRecovery { get; init; }
    public Guid OrganizationId { get; init; }
    public ConversationDeliveryProvider ProviderCode { get; init; }
    public Guid ConnectionId { get; init; }
    public string ProviderBotId { get; init; } = string.Empty;
    public string DeliveryAddress { get; init; } = string.Empty;
    public string ExternalConversationId { get; init; } = string.Empty;
    public string BotUsername { get; init; } = string.Empty;
    public Guid RequestId { get; init; }
    public string Token { get; init; } = string.Empty;
    public string? Text { get; init; }
    public IReadOnlyList<ConversationDeliveryAttachment> Attachments { get; init; } = [];
}

/// <summary> Метаданные единственного допустимого вложения в адресной доставке. </summary>
public sealed record ConversationDeliveryAttachment
{
    public Guid AttachmentId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? ProviderFileId { get; init; }
    public string? StorageReference { get; init; }
    public int SortOrder { get; init; }
}

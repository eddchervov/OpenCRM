namespace Notifications.Contracts;

/// <summary>Проверенная сервером команда для одной попытки отправки в Telegram.</summary>
public sealed record TelegramChatDeliveryClaimResponse
{
    public Guid MessageId { get; init; }
    public Guid LeaseId { get; init; }
    public bool IsRecovery { get; init; }
    public Guid OrganizationId { get; init; }
    public long BotId { get; init; }
    public long ChatId { get; init; }
    public Guid RequestId { get; init; }
    public string BotToken { get; init; } = string.Empty;
    public string? Text { get; init; }
    public IReadOnlyList<TelegramChatAttachment> Attachments { get; init; } = [];
}

/// <summary>Метаданные и допустимая ссылка на вложение для доставки в Telegram.</summary>
public sealed record TelegramChatAttachment
{
    public Guid AttachmentId { get; init; }
    public string Kind { get; init; } = "document";
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? ProviderFileId { get; init; }
    public string? StorageReference { get; init; }
    public int SortOrder { get; init; }
}

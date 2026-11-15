namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Нормализованное входящее сообщение из уже проверенного webhook Telegram. </summary>
public sealed record IngestTelegramMessageDto
{
    public Guid OrganizationId { get; init; }
    public long BotId { get; init; }
    public string BotUsername { get; init; } = string.Empty;
    public long UpdateId { get; init; }
    public long ChatId { get; init; }
    public long ProviderMessageId { get; init; }
    public long? SenderTelegramUserId { get; init; }
    public string? SenderDisplayName { get; init; }
    public string? SenderUsername { get; init; }
    public string? Text { get; init; }
    public DateTime SentAt { get; init; }
    public IReadOnlyList<IngestTelegramAttachmentDto> Attachments { get; init; } = [];
}

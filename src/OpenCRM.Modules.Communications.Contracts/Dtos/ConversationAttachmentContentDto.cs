namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Разрешённое к чтению вложение или ссылка Telegram, требующая защищённого чтения через relay. </summary>
public sealed record ConversationAttachmentContentDto
{
    public Guid OrganizationId { get; init; }
    public long? BotId { get; init; }
    public Guid? ConnectionId { get; init; }
    public string ProviderCode { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? ProviderFileId { get; init; }
    public byte[]? Content { get; init; }
}

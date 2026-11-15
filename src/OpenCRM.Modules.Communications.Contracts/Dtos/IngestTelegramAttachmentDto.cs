namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Ссылка на вложение, полученная от Telegram без его бинарного содержимого. </summary>
public sealed record IngestTelegramAttachmentDto
{
    public string Kind { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string ProviderFileId { get; init; } = string.Empty;
    public string? ProviderFileUniqueId { get; init; }
}

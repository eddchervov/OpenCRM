namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Метаданные вложения входящего сообщения без его байтов. </summary>
public sealed record ChannelAttachmentReceivedDto
{
    public string Kind { get; init; } = string.Empty;
    public string? ProviderFileId { get; init; }
    public string? ProviderFileUniqueId { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
    public long? SizeBytes { get; init; }
}

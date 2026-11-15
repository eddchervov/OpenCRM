namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Метаданные вложения без передачи бинарного содержимого через очередь сообщений. </summary>
public sealed record ConversationAttachmentDto
{
    public Guid Id { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public int SortOrder { get; init; }
}

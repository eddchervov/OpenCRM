namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Внутренние ссылки для транспорта доставки; не возвращаются в пользовательской истории. </summary>
public sealed record ConversationDeliveryAttachmentDto
{
    public Guid Id { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? ProviderFileId { get; init; }
    public string? StorageReference { get; init; }
    public int SortOrder { get; init; }
}

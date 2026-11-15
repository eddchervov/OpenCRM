namespace OpenCRM.Domain;

/// <summary> Метаданные и ссылка на вложение сообщения без хранения байтов в очереди. </summary>
public sealed class OrganizationConversationAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ConversationId { get; set; }
    public OrganizationConversation Conversation { get; set; } = null!;
    public Guid? MessageId { get; set; }
    public OrganizationConversationMessage? Message { get; set; }
    public string? UploadedByUserId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? ProviderFileId { get; set; }
    public string? ProviderFileUniqueId { get; set; }
    public string? StorageReference { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

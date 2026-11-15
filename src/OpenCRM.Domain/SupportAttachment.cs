namespace OpenCRM.Domain;

/// <summary> Вложение исходного обращения или сообщения в переписке поддержки. </summary>
public class SupportAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int SupportMessageId { get; set; }

    public SupportMessage SupportMessage { get; set; } = null!;

    public int? EntryId { get; set; }

    public SupportMessageEntry? Entry { get; set; }

    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string StorageReference { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}

namespace OpenCRM.Domain;

/// <summary> Запись переписки по обращению в поддержку. </summary>
public class SupportMessageEntry
{
    /// <summary> Идентификатор записи переписки. </summary>
    public int Id { get; set; }

    /// <summary> Идентификатор обращения, к которому относится запись. </summary>
    public int SupportMessageId { get; set; }

    /// <summary> Обращение, к которому относится запись. </summary>
    public SupportMessage SupportMessage { get; set; } = null!;

    /// <summary> Идентификатор пользователя — автора записи. </summary>
    public string? AuthorUserId { get; set; }

    /// <summary> Признак записи от сотрудника поддержки. </summary>
    public bool IsStaff { get; set; }

    /// <summary> Вид записи переписки. </summary>
    public string Kind { get; set; } = "message";

    /// <summary> Текст записи переписки. </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary> Дата и время создания записи. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Файлы, отправленные с записью переписки. </summary>
    public ICollection<SupportAttachment> Attachments { get; set; } = new List<SupportAttachment>();
}

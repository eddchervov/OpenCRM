namespace OpenCRM.Services.Interfaces.Home.Dtos;

/// <summary> Сообщение в обращении в поддержку. </summary>
public class SupportConversationEntryDto
{
    /// <summary> Признак сообщения сотрудника поддержки. </summary>
    public bool IsStaff { get; set; }

    /// <summary> Тип сообщения. </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary> Текст сообщения. </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary> Дата и время создания сообщения. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Вложения этого сообщения. </summary>
    public IReadOnlyList<SupportAttachmentDto> Attachments { get; set; } = [];
}

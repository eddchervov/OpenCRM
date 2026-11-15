namespace OpenCRM.Services.Interfaces.Home.Dtos;

/// <summary> Обращение в поддержку пользователя. </summary>
public class SupportConversationDto
{
    /// <summary> Идентификатор обращения. </summary>
    public int Id { get; set; }

    /// <summary> Тема обращения. </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary> Код типа обращения. </summary>
    public string TypeCode { get; set; } = string.Empty;

    /// <summary> Код статуса обращения. </summary>
    public string StatusCode { get; set; } = string.Empty;

    /// <summary> Наименование организации, к которой относится обращение. </summary>
    public string? OrganizationName { get; set; }

    /// <summary> Дата и время последнего обновления обращения. </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary> Сообщения обращения. </summary>
    public IReadOnlyList<SupportConversationEntryDto> Entries { get; set; } = [];
}

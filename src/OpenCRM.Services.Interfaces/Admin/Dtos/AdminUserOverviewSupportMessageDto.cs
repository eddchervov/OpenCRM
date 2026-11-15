namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO сообщения поддержки в обзоре пользователя. </summary>
public sealed class AdminUserOverviewSupportMessageDto
{
    /// <summary> Идентификатор сообщения. </summary>
    public int Id { get; init; }

    /// <summary> Код типа сообщения. </summary>
    public string TypeCode { get; init; } = string.Empty;

    /// <summary> Название типа сообщения. </summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary> Код статуса сообщения. </summary>
    public string StatusCode { get; init; } = string.Empty;

    /// <summary> Название статуса сообщения. </summary>
    public string StatusName { get; init; } = string.Empty;

    /// <summary> Тема сообщения. </summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary> Текст сообщения. </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary> Страница, с которой отправлено сообщение. </summary>
    public string? SourcePage { get; init; }

    /// <summary> Комментарий администратора. </summary>
    public string? AdminComment { get; init; }

    /// <summary> Дата создания сообщения. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Дата обновления сообщения. </summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary> Дата закрытия сообщения. </summary>
    public DateTime? ClosedAt { get; init; }
}

namespace OpenCRM.Admin.Api.Models;

/// <summary> Обращение пользователя в поддержку. </summary>
public sealed class AdminUserOverviewSupportMessage
{
    /// <summary> Идентификатор обращения. </summary>
    public int Id { get; init; }

    /// <summary> Код типа обращения. </summary>
    public string TypeCode { get; init; } = string.Empty;

    /// <summary> Название типа обращения. </summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary> Код статуса обращения. </summary>
    public string StatusCode { get; init; } = string.Empty;

    /// <summary> Название статуса обращения. </summary>
    public string StatusName { get; init; } = string.Empty;

    /// <summary> Тема обращения. </summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary> Текст обращения. </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary> Страница, с которой отправлено обращение. </summary>
    public string? SourcePage { get; init; }

    /// <summary> Комментарий администратора. </summary>
    public string? AdminComment { get; init; }

    /// <summary> Время создания обращения. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Время обновления обращения. </summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary> Время закрытия обращения. </summary>
    public DateTime? ClosedAt { get; init; }
}

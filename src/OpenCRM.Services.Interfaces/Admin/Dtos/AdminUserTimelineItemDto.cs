namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO элемента ленты событий пользователя. </summary>
public sealed class AdminUserTimelineItemDto
{
    /// <summary> Тип события. </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary> Подпись события. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Заголовок события. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Дата события. </summary>
    public DateTime Date { get; init; }

    /// <summary> Ключ события. </summary>
    public string Key { get; init; } = string.Empty;
}

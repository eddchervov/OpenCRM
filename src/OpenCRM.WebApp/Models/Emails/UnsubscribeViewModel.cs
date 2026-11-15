namespace OpenCRM.WebApp.Models.Emails;

/// <summary> Модель страницы отписки от рассылок. </summary>
public sealed class UnsubscribeViewModel
{
    /// <summary> Заголовок страницы отписки. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Сообщение для пользователя. </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary> Email отписавшегося пользователя. </summary>
    public string? Email { get; init; }

    /// <summary> Признак успешной отписки. </summary>
    public bool Succeeded { get; init; }
}

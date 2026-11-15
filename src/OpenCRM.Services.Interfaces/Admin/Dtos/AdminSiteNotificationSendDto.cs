namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO отправки уведомления сайта. </summary>
public sealed class AdminSiteNotificationSendDto
{
    /// <summary> Заголовок уведомления. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Текст уведомления. </summary>
    public string Body { get; init; } = string.Empty;

    /// <summary> Область получателей. </summary>
    public string? RecipientScope { get; init; }

    /// <summary> Список идентификаторов пользователей-получателей. </summary>
    public IReadOnlyCollection<string>? UserIds { get; init; }

    /// <summary> Список аудиторий получателей. </summary>
    public IReadOnlyCollection<string>? Audiences { get; init; }
}

namespace OpenCRM.Services.Interfaces.Notifications.Dtos;

/// <summary> Уведомление сайта для пользователя. </summary>
public sealed record SiteNotificationDto(
    Guid Id,
    string Title,
    string Body,
    DateTime CreatedAt,
    DateTime? ClosedAt);

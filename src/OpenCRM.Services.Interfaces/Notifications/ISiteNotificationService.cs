using OpenCRM.Services.Interfaces.Notifications.Dtos;

namespace OpenCRM.Services.Interfaces.Notifications;

/// <summary>Чтение и закрытие доступных пользователю уведомлений сайта.</summary>
public interface ISiteNotificationService
{
    /// <summary> Возвращает уведомления сайта, доступные пользователю. </summary>
    Task<IReadOnlyList<SiteNotificationDto>> GetAsync(
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken);

    /// <summary> Закрывает уведомление сайта для пользователя. </summary>
    Task<bool> CloseAsync(
        string userId,
        bool isAdmin,
        Guid notificationId,
        CancellationToken cancellationToken);
}

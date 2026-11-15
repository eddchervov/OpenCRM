using System.Text.Json;

using Notifications.Contracts;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>
/// Определяет операции хранения шаблонов с изоляцией по приложению и каналу.
/// </summary>
public interface INotificationTemplateRepository
{
    /// <summary>Возвращает точную версию шаблона по составному бизнес-ключу.</summary>
    Task<NotificationTemplate?> GetAsync(
        string applicationId,
        string key,
        NotificationChannel channel,
        CancellationToken cancellationToken,
        int version = 1);

    /// <summary>Добавляет новый шаблон в текущую единицу работы.</summary>
    void Add(NotificationTemplate notificationTemplate);
}
